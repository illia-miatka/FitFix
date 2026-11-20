using System.Reflection;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;
using FitFix.Core.Tracks;

namespace FitFix.Core.Tests;

/// <summary>
/// AD-10: every tuning threshold lives in <see cref="AlignmentOptions"/> and no public core
/// signature carries a default, so two entry points cannot silently disagree on the same files.
/// The numbers pinned here are the ones the suite produced before the thresholds moved.
/// </summary>
public class AlignmentOptionsTests
{
    /// <summary>Repaired distance of the synthetic run before the thresholds moved, m.</summary>
    private const double ExpectedRepairedDistance = 6499.5036;

    /// <summary>Anchor count of the synthetic run before the thresholds moved.</summary>
    private const int ExpectedAnchorCount = 1129;

    /// <summary>Boundary of the measured zone of the synthetic run before the move, s.</summary>
    private const double ExpectedMeasuredUntil = 1295;

    [Fact]
    public void The_defaults_reproduce_the_numbers_from_before_the_move()
    {
        var run = SyntheticRun.Create();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Equal(ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.01);
        Assert.Equal(SyntheticRun.DurationSeconds, result.Report.ElapsedSeconds, 0.001);
        Assert.Equal(ExpectedAnchorCount, result.Report.AnchorCount);
        Assert.Equal(ExpectedMeasuredUntil, result.Report.MeasuredUntilSeconds, 0.001);
    }

    [Fact]
    public void The_sport_derived_overload_is_the_same_call_as_stating_the_options()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        var derived = RepairService.Repair(run.Original, track);
        var stated = RepairService.Repair(run.Original, track, AlignmentOptions.ForSport(run.Original.Sport));

        Assert.Equal(stated.Report.RepairedDistanceMeters, derived.Report.RepairedDistanceMeters, 0.001);
        Assert.Equal(stated.Report.AnchorCount, derived.Report.AnchorCount);
    }

    [Fact]
    public void The_composition_root_and_a_hand_assembled_pipeline_agree_on_the_distance()
    {
        var run = SyntheticRun.Create();
        var originalBytes = SyntheticFitBuilder.WriteActivity(run.Original);
        var trackBytes = SyntheticFitBuilder.WriteTrack(run.TrackPoints, run.Original.StartTime);

        using var pipelineOriginal = new MemoryStream(originalBytes);
        using var pipelineTrack = new MemoryStream(trackBytes);
        var throughRoot = RepairPipeline.Repair(pipelineOriginal, pipelineTrack);

        using var handOriginal = new MemoryStream(originalBytes);
        using var handTrack = new MemoryStream(trackBytes);
        var original = FitActivityReader.Read(handOriginal);
        var options = AlignmentOptions.ForSport(original.Sport);
        var byHand = RepairService.Repair(original, TrackReader.ReadReferenceTrack(handTrack, options), options);

        // One pipeline, one answer: the API will read the same files through the same root.
        Assert.Equal(byHand.Report.RepairedDistanceMeters, throughRoot.Report.RepairedDistanceMeters, 0.001);
        Assert.Equal(byHand.Report.TrackLengthMeters, throughRoot.Report.TrackLengthMeters, 0.001);
        Assert.Equal(byHand.Report.AnchorCount, throughRoot.Report.AnchorCount);
        Assert.Equal(byHand.Report.MeasuredUntilSeconds, throughRoot.Report.MeasuredUntilSeconds, 0.001);
    }

    [Fact]
    public void The_file_overload_the_cli_calls_gives_the_same_answer_as_the_stream_overload()
    {
        var run = SyntheticRun.Create();
        var directory = Path.Combine(AppContext.BaseDirectory, "options-matrix");
        Directory.CreateDirectory(directory);

        var originalPath = Path.Combine(directory, "original.fit");
        var trackPath = Path.Combine(directory, "track.fit");
        File.WriteAllBytes(originalPath, SyntheticFitBuilder.WriteActivity(run.Original));
        File.WriteAllBytes(trackPath, SyntheticFitBuilder.WriteTrack(run.TrackPoints, run.Original.StartTime));

        var fromFiles = RepairPipeline.RepairFiles(originalPath, trackPath);

        using var original = File.OpenRead(originalPath);
        using var track = File.OpenRead(trackPath);
        var fromStreams = RepairPipeline.Repair(original, track);

        Assert.Equal(fromStreams.Report.RepairedDistanceMeters, fromFiles.Report.RepairedDistanceMeters, 0.001);
        Assert.Equal(fromStreams.Report.AnchorCount, fromFiles.Report.AnchorCount);
    }

    [Fact]
    public void The_de_duplication_spacing_is_read_from_the_options()
    {
        var run = SyntheticRun.Create();

        var fine = ReferenceTrack.FromPoints(run.TrackPoints, new AlignmentOptions());
        var coarse = ReferenceTrack.FromPoints(
            run.TrackPoints,
            new AlignmentOptions { TrackMinSpacingMeters = 50 });

        // The route is sampled every 5 m, so a 50 m spacing has to throw most of it away.
        Assert.True(
            coarse.Points.Count * 2 < fine.Points.Count,
            $"50 m spacing kept {coarse.Points.Count} of {fine.Points.Count} points");
    }

    [Fact]
    public void The_minimum_anchor_count_is_read_from_the_options()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        var withDefault = RepairService.Repair(run.Original, track);
        var demanding = RepairService.Repair(
            run.Original,
            track,
            // Derived from the run's own result, so the test keeps asserting the threshold
            // and not the anchor count a future story may change.
            SyntheticRun.Options with { MinimumAnchorCount = withDefault.Report.AnchorCount + 1 });

        Assert.DoesNotContain(DiagnosticCodes.FewAnchors, withDefault.Report.Warnings);
        Assert.Contains(DiagnosticCodes.FewAnchors, demanding.Report.Warnings);
    }

    [Fact]
    public void Both_device_distance_thresholds_are_read_from_the_options()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        // Right after the GPS freezes, the device's own distance still grew earlier inside the
        // window, so those zero power values are a real reading and are carried over (FR-6).
        var withDefaults = ZeroPowerRecords(RepairService.Repair(run.Original, track));
        var shortWindow = ZeroPowerRecords(RepairService.Repair(
            run.Original, track, SyntheticRun.Options with { DeviceDistanceStallWindowSeconds = 3 }));
        var hugeGrowth = ZeroPowerRecords(RepairService.Repair(
            run.Original, track, SyntheticRun.Options with { DeviceDistanceGrowthMeters = 1_000 }));

        Assert.True(withDefaults > 0, "the defaults should carry some zero power over");
        // A narrower window reaches back over fewer growing samples, so fewer of them survive.
        Assert.True(
            shortWindow > 0 && shortWindow < withDefaults,
            $"a 3 s window kept {shortWindow} zero power records, the default kept {withDefaults}");
        // Growth nothing can satisfy: every zero power counts as derived from the dead GPS.
        Assert.Equal(0, hugeGrowth);
    }

    [Fact]
    public void The_speed_window_is_read_from_the_options()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        var withDefaults = RepairService.Repair(run.Original, track).Records.Select(r => r.Speed).ToArray();
        var wide = RepairService
            .Repair(run.Original, track, SyntheticRun.Options with { SpeedWindowHalfWidthRecords = 60 })
            .Records.Select(r => r.Speed).ToArray();

        // The series has to describe the athlete first (SM-1).
        Assert.Equal(SyntheticRun.SpeedMetersPerSecond, withDefaults.Average(), SyntheticRun.SpeedMetersPerSecond * 0.02);
        Assert.All(withDefaults, speed =>
            Assert.Equal(SyntheticRun.SpeedMetersPerSecond, speed, SyntheticRun.SpeedMetersPerSecond * 0.1));

        // A window twenty times wider averages the GPS jitter away, so the two series differ.
        var biggestDifference = withDefaults.Zip(wide, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(biggestDifference > 0.05, $"the widest difference was only {biggestDifference:F4} m/s");
        Assert.True(
            StandardDeviation(withDefaults) > 2 * StandardDeviation(wide),
            $"spread {StandardDeviation(withDefaults):F4} against {StandardDeviation(wide):F4} m/s");
    }

    [Fact]
    public void A_ride_is_aligned_with_the_cycling_cap_and_not_the_running_one()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        var ride = Ride(track);

        var derived = RepairService.Repair(ride, track);
        var cycling = RepairService.Repair(ride, track, AlignmentOptions.ForSport(SportKind.Cycling));
        var running = RepairService.Repair(ride, track, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(cycling.Report.AnchorCount, derived.Report.AnchorCount);
        Assert.Equal(cycling.Report.MeasuredUntilSeconds, derived.Report.MeasuredUntilSeconds, 0.001);

        // The running cap rejects every 15 m/s step, which is exactly the mistake the
        // sport-derived overload exists to prevent.
        Assert.True(
            running.Report.AnchorCount < derived.Report.AnchorCount,
            $"the running profile kept {running.Report.AnchorCount} anchors, cycling {derived.Report.AnchorCount}");
    }

    [Fact]
    public void The_composition_root_takes_the_sport_from_the_file()
    {
        var run = SyntheticRun.Create();
        var ride = Ride(run.ReferenceTrack);
        var originalBytes = SyntheticFitBuilder.WriteActivity(ride);
        var trackBytes = SyntheticFitBuilder.WriteTrack(run.TrackPoints, ride.StartTime);

        using var pipelineOriginal = new MemoryStream(originalBytes);
        using var pipelineTrack = new MemoryStream(trackBytes);
        var throughRoot = RepairPipeline.Repair(pipelineOriginal, pipelineTrack);

        Assert.Equal(SportKind.Cycling, throughRoot.Sport);
        Assert.Equal(
            Hand(originalBytes, trackBytes, AlignmentOptions.ForSport(SportKind.Cycling)).Report.AnchorCount,
            throughRoot.Report.AnchorCount);
        Assert.True(
            Hand(originalBytes, trackBytes, AlignmentOptions.ForSport(SportKind.Running)).Report.AnchorCount
                < throughRoot.Report.AnchorCount,
            "the root must read the sport from the file rather than assume the defaults");
    }

    [Fact]
    public void No_public_method_in_the_core_declares_an_optional_parameter()
    {
        var offenders = typeof(AlignmentOptions).Assembly.GetExportedTypes()
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OfType<MethodBase>())
            .Where(method => method.GetParameters().Any(parameter => parameter.IsOptional))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // AD-10: an omitted argument must not be able to change the numbers.
        Assert.True(offenders.Length == 0, $"Optional parameters survive on: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_track_entry_points_demand_the_options()
    {
        var run = SyntheticRun.Create();
        using var stream = new MemoryStream(
            SyntheticFitBuilder.WriteTrack(run.TrackPoints, run.Original.StartTime));

        Assert.Throws<ArgumentNullException>(() => ReferenceTrack.FromPoints(run.TrackPoints, null!));
        Assert.Throws<ArgumentNullException>(() => TrackReader.ReadReferenceTrack(stream, null!));
        Assert.Throws<ArgumentNullException>(() => TrackReader.ReadReferenceTrackFile("track.fit", null!));
    }

    [Fact]
    public void Repair_demands_the_options_on_the_explicit_overload()
    {
        var run = SyntheticRun.Create();

        Assert.Throws<ArgumentNullException>(() => RepairService.Repair(run.Original, run.ReferenceTrack, null!));
    }

    /// <summary>
    /// A ride along the same geometry at 15 m/s — above the running cap of 7 m/s and below
    /// cycling's 25, so the two sport profiles cannot produce the same alignment.
    /// </summary>
    private static OriginalActivity Ride(ReferenceTrack track)
    {
        const double speed = 15.0;
        var start = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
        var records = new List<ActivityRecord>();

        for (var second = 0; second * speed <= track.Length; second++)
        {
            var at = track.At(second * speed);
            records.Add(new ActivityRecord
            {
                Timestamp = start.AddSeconds(second),
                Latitude = at.Position.Latitude,
                Longitude = at.Position.Longitude,
                Altitude = at.Altitude,
                Distance = second * speed,
                HeartRate = 140,
                Cadence = 85
            });
        }

        return new OriginalActivity { Records = records, Sport = SportKind.Cycling };
    }

    private static RepairResult Hand(byte[] originalBytes, byte[] trackBytes, AlignmentOptions options)
    {
        using var original = new MemoryStream(originalBytes);
        using var track = new MemoryStream(trackBytes);
        return RepairService.Repair(
            FitActivityReader.Read(original),
            TrackReader.ReadReferenceTrack(track, options),
            options);
    }

    private static int ZeroPowerRecords(RepairResult result) => result.Records.Count(r => r.Power == 0);

    private static double StandardDeviation(double[] values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Select(value => (value - mean) * (value - mean)).Average());
    }
}
