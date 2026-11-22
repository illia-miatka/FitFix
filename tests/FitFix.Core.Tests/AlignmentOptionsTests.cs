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
/// The numbers pinned here are the ones the suite produced before the thresholds moved. They are
/// internal rather than private because <see cref="PauseTests"/> asserts the very same three on
/// the pause-free run: one number, one owner, so neither copy can be updated alone.
/// </summary>
public class AlignmentOptionsTests
{
    /// <summary>Repaired distance of the synthetic run before the thresholds moved, m.</summary>
    internal const double ExpectedRepairedDistance = 6499.5036;

    /// <summary>Anchor count of the synthetic run before the thresholds moved.</summary>
    internal const int ExpectedAnchorCount = 1129;

    /// <summary>Boundary of the measured zone of the synthetic run before the move, s.</summary>
    internal const double ExpectedMeasuredUntil = 1295;

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
        // And the same decision about how much of it to use: two paths that read one track and
        // chose differently would be the disagreement AD-17 forbids.
        Assert.Equal(byHand.Report.UsedTrackLengthMeters, throughRoot.Report.UsedTrackLengthMeters, 0.001);
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
    public void The_judging_window_cleaning_uses_is_read_from_the_options()
    {
        // 20 m steps at 1 Hz: impossible at the running cap over one second, ordinary over five.
        var activity = StraightRun(metersPerSecond: 20, seconds: 300);

        var withDefaults = GpsCleaner.Clean(activity, SyntheticRun.Options);
        var perSecond = GpsCleaner.Clean(
            activity,
            SyntheticRun.Options with { ImpossibleSpeedJudgedOverSeconds = 1 });

        Assert.Equal(0, withDefaults.DiscardedPositionCount);
        // A one-second window is the arithmetic that was measured to delete real data, so the
        // threshold has to be the one doing the work here — not a constant inside the cleaner.
        Assert.True(
            perSecond.DiscardedPositionCount > 100,
            $"a one-second window discarded only {perSecond.DiscardedPositionCount} positions");
    }

    [Fact]
    public void The_number_of_voters_cleaning_asks_is_read_from_the_options()
    {
        // A minute of standing still, then two spikes: the standing positions are one place and
        // cast no vote, so only a window wide enough to reach the run itself can clear the start.
        var activity = StandingStartThenSpikes();

        var withDefaults = GpsCleaner.Clean(activity, SyntheticRun.Options);
        var nearSighted = GpsCleaner.Clean(
            activity,
            SyntheticRun.Options with { LeadingAgreementPositions = 1 });

        Assert.Equal(2, withDefaults.DiscardedPositionCount);
        Assert.True(
            nearSighted.DiscardedPositionCount > 2,
            "a single voter cannot see past the spikes, so the standing start goes with them");
    }

    [Fact]
    public void How_long_a_stale_reference_vouches_is_read_from_the_options()
    {
        // A long run of garbage, then a point 5 km from the last position anyone trusted. The
        // bound is the only thing refusing it: let the reference vouch for the whole garbage run
        // and the point is laundered in.
        var activity = GarbageThenADistantPoint(garbageSeconds: 1_000, metersAway: 5_000);

        var withDefaults = GpsCleaner.Clean(activity, SyntheticRun.Options);
        var vouchingForever = GpsCleaner.Clean(
            activity,
            SyntheticRun.Options with { StaleReferenceVouchesForSeconds = 100_000 });

        Assert.Null(withDefaults.Activity.Records[^1].Position);
        Assert.NotNull(vouchingForever.Activity.Records[^1].Position);
    }

    [Fact]
    public void The_speed_cap_cleaning_judges_by_is_the_sport_profile_and_not_a_constant()
    {
        // 100 m steps at 1 Hz: impossible for a runner over any window, ordinary for nothing on
        // a bicycle either — but the cycling cap over five seconds allows 128 m, and so it stands.
        var activity = StraightRun(metersPerSecond: 100, seconds: 300);

        var asRunning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));
        var asCycling = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Cycling));

        Assert.Equal(0, asCycling.DiscardedPositionCount);
        Assert.True(
            asRunning.DiscardedPositionCount > 100,
            $"the running profile discarded only {asRunning.DiscardedPositionCount} of them");
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

        // Cleaning must not answer this test for the anchor stage: if it discarded the ride's
        // positions under the running profile, the comparison below would hold however the
        // anchor loop behaved, and the cap it exists to pin could be deleted (AD-25).
        Assert.Equal(0, running.Report.DiscardedPositionCount);
        Assert.Equal(0, cycling.Report.DiscardedPositionCount);
        Assert.Equal(0, derived.Report.DiscardedPositionCount);

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

        var asCycling = Hand(originalBytes, trackBytes, AlignmentOptions.ForSport(SportKind.Cycling));
        var asRunning = Hand(originalBytes, trackBytes, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(SportKind.Cycling, throughRoot.Sport);
        Assert.Equal(asCycling.Report.AnchorCount, throughRoot.Report.AnchorCount);

        // Same reason as above: the difference below has to come from the anchor stage, so
        // cleaning may not have thrown the ride away under the running profile (AD-25).
        Assert.Equal(0, asRunning.Report.DiscardedPositionCount);
        Assert.Equal(0, throughRoot.Report.DiscardedPositionCount);

        Assert.True(
            asRunning.Report.AnchorCount < throughRoot.Report.AnchorCount,
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

    /// <summary>A straight run east at 1 Hz, every step the same length.</summary>
    private static OriginalActivity StraightRun(double metersPerSecond, int seconds)
    {
        var start = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
        var records = new List<ActivityRecord>(seconds + 1);

        for (var second = 0; second <= seconds; second++)
            records.Add(new ActivityRecord
            {
                Timestamp = start.AddSeconds(second),
                Latitude = SyntheticRun.Offset(second * metersPerSecond, 0).Latitude,
                Longitude = SyntheticRun.Offset(second * metersPerSecond, 0).Longitude,
                Cadence = 85
            });

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>A short run, then nothing but teleports, then one point far from the last valid.</summary>
    private static OriginalActivity GarbageThenADistantPoint(int garbageSeconds, double metersAway)
    {
        var start = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
        var teleport = SyntheticRun.FarAway(kilometres: 2_000, bearingDegrees: 90);
        var records = new List<ActivityRecord>();

        for (var second = 0; second <= 10; second++)
            records.Add(Positioned(start.AddSeconds(second), SyntheticRun.Offset(second * 3.0, 0)));

        for (var second = 11; second <= 10 + garbageSeconds; second++)
            records.Add(Positioned(start.AddSeconds(second), teleport));

        records.Add(Positioned(start.AddSeconds(11 + garbageSeconds), SyntheticRun.Offset(metersAway, 0)));
        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    private static ActivityRecord Positioned(DateTime timestamp, GeoPoint position) => new()
    {
        Timestamp = timestamp,
        Latitude = position.Latitude,
        Longitude = position.Longitude,
        Cadence = 85
    };

    /// <summary>A minute standing at the start line, two bad fixes, then a 3 m/s run.</summary>
    private static OriginalActivity StandingStartThenSpikes()
    {
        var start = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
        var spike = SyntheticRun.FarAway(kilometres: 2_000, bearingDegrees: 90);
        var records = new List<ActivityRecord>();

        for (var second = 0; second <= 360; second++)
        {
            var position = second switch
            {
                61 or 62 => spike,
                <= 60 => SyntheticRun.Offset(0, 0),
                _ => SyntheticRun.Offset((second - 62) * 3.0, 0)
            };

            records.Add(new ActivityRecord
            {
                Timestamp = start.AddSeconds(second),
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                Cadence = 85
            });
        }

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
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
