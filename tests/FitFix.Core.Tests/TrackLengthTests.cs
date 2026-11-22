using Dynastream.Fit;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// AD-17: the core computes both the whole length of the reference track and the part of it the
/// Original's own time makes plausible, reports both, and ends on the conservative one until the
/// user says the track is right. The invariant is a choice between two numbers, and only a test
/// says which one was taken — the written file looks equally well-formed either way.
/// </summary>
public class TrackLengthTests
{
    /// <summary>
    /// What a repaired run's own time makes plausible with no sensor declared: the distance its
    /// anchors measured, plus the running cap over the moving seconds left after them (AD-17).
    /// Read from the result rather than restated as a constant — the measured distance is the
    /// half of it no test can know in advance, and the cap comes from the options, so the day
    /// either moves this moves with it.
    /// </summary>
    private static double PlausibleDistanceOf(RepairResult result) =>
        PlausibleDistanceOf(result, SyntheticRun.Options.MaxSpeedMetersPerSecond);

    /// <inheritdoc cref="PlausibleDistanceOf(RepairResult)"/>
    private static double PlausibleDistanceOf(RepairResult result, double capMetersPerSecond)
    {
        var alignment = result.Alignment;
        var original = result.CleanedOriginal;
        var remaining = original.MovingSecondsFromStart(original.ElapsedSeconds)
            - original.MovingSecondsFromStart(alignment.MeasuredUntilSeconds);

        return alignment.MeasuredDistanceMeters + capMetersPerSecond * remaining;
    }

    [Fact]
    public void An_honest_track_is_used_whole_and_moves_no_number_the_suite_pins()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        var result = RepairService.Repair(run.Original, track);

        // The bound is generous on purpose: the anchors measured 3.9 km and the cap allows six
        // more over the moving time left, so a 6.5 km track is plausible and nothing about this
        // file may change.
        Assert.True(
            track.Length < PlausibleDistanceOf(result),
            $"the fixture's track is {track.Length:F0} m against {PlausibleDistanceOf(result):F0} m");
        Assert.DoesNotContain(DiagnosticCodes.TrackLongerThanActivity, result.Report.Warnings);
        Assert.Equal(track.Length, result.Report.TrackLengthMeters, 0.001);
        Assert.Equal(track.Length, result.Report.UsedTrackLengthMeters, 0.001);

        // The numbers pinned before this story, read from their one owner.
        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.01);
        Assert.Equal(AlignmentOptionsTests.ExpectedAnchorCount, result.Report.AnchorCount);
        Assert.Equal(AlignmentOptionsTests.ExpectedMeasuredUntil, result.Report.MeasuredUntilSeconds, 0.001);
    }

    [Fact]
    public void One_lap_too_many_is_reported_with_both_numbers_and_the_shorter_one_is_used()
    {
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var track = run.ReferenceTrack;

        var result = RepairService.Repair(run.Original, track);

        var plausible = PlausibleDistanceOf(result);
        Assert.True(
            track.Length > 2 * plausible,
            $"the fixture's track is {track.Length:F0} m against {plausible:F0} m of plausible movement");

        // The warning, and at the severity that withholds the download until the user answers.
        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, result.Report.Warnings);
        Assert.True(DiagnosticCodes.IsBlocking(DiagnosticCodes.TrackLongerThanActivity));

        // Both numbers, so the user can decide between them.
        Assert.Equal(track.Length, result.Report.TrackLengthMeters, 0.001);
        Assert.Equal(plausible, result.Report.UsedTrackLengthMeters, 0.001);

        // And the repair ends on the shorter one: a preview must not open with an inflated pace.
        Assert.Equal(plausible, result.Report.RepairedDistanceMeters, 0.001);
    }

    [Fact]
    public void The_mode_that_trusts_the_track_ends_on_the_whole_length_and_keeps_the_warning()
    {
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var track = run.ReferenceTrack;

        var trusted = RepairService.Repair(run.Original, track, TrackLengthMode.Full);

        Assert.Equal(track.Length, trusted.Report.UsedTrackLengthMeters, 0.001);
        Assert.Equal(track.Length, trusted.Report.RepairedDistanceMeters, 0.001);

        // Confirming changes the result; it does not make the track plausible, and a shell showing
        // the confirmed file still has to be able to say so.
        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, trusted.Report.Warnings);
        Assert.Equal(track.Length, trusted.Report.TrackLengthMeters, 0.001);
    }

    [Fact]
    public void A_track_longer_by_less_than_the_tolerance_is_used_whole()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        // A footpod that measured a little less than the route it was drawn on: the honest
        // disagreement between a sensor and a map, which must not become a blocking warning.
        var activity = WithFootpodMeasuring(
            run,
            track.Length - SyntheticRun.Options.TrackLengthToleranceMeters / 2);

        var result = RepairService.Repair(activity, track);

        Assert.DoesNotContain(DiagnosticCodes.TrackLongerThanActivity, result.Report.Warnings);
        Assert.Equal(track.Length, result.Report.UsedTrackLengthMeters, 0.001);
        Assert.Equal(track.Length, result.Report.RepairedDistanceMeters, 0.001);
    }

    [Fact]
    public void The_tolerance_between_the_two_lengths_is_read_from_the_options()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        const double shortfall = 300;

        var activity = WithFootpodMeasuring(run, track.Length - shortfall);

        var withDefaults = RepairService.Repair(activity, track);
        var forgiving = RepairService.Repair(
            activity,
            track,
            SyntheticRun.Options with { TrackLengthToleranceMeters = shortfall * 2 });

        // 300 m past a 200 m tolerance is a divergence; 300 m past a 600 m one is noise. The
        // threshold has to be the thing deciding that, not a constant inside the service (AD-10).
        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, withDefaults.Report.Warnings);
        Assert.Equal(track.Length - shortfall, withDefaults.Report.UsedTrackLengthMeters, 0.001);

        Assert.DoesNotContain(DiagnosticCodes.TrackLongerThanActivity, forgiving.Report.Warnings);
        Assert.Equal(track.Length, forgiving.Report.UsedTrackLengthMeters, 0.001);
    }

    [Fact]
    public void A_declared_footpod_bounds_the_length_by_the_device_distance_and_not_by_the_cap()
    {
        // The Chabany shape with a footpod on top: the GPS froze at 3.9 km, but something that
        // measures movement without a fix was there, so its own distance is the better witness.
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);

        // The footpod kept measuring after the fix died, so it reached further than the anchors.
        var activity = WithFootpodMeasuring(run, metres: 5_000);
        var result = RepairService.Repair(activity, run.ReferenceTrack);

        Assert.Equal(5_000, activity.LargestRecordedDistance!.Value, 0.001);
        Assert.Equal(5_000, result.Report.UsedTrackLengthMeters, 0.001);
        Assert.Equal(5_000, result.Report.RepairedDistanceMeters, 0.001);
        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, result.Report.Warnings);

        // The cap rule would have allowed almost twice as far, which is what makes this the
        // sensor's number and not the cap's.
        Assert.True(
            PlausibleDistanceOf(result) > 1.5 * result.Report.UsedTrackLengthMeters,
            $"the cap allowed {PlausibleDistanceOf(result):F0} m");
    }

    [Fact]
    public void A_speed_the_device_derived_from_gps_is_never_a_source()
    {
        // The same files with nothing declared — the real Chabany case. The device's own distance
        // froze at 3.9 km when the GPS died, and believing it would have thrown away 2.6 km the
        // athlete actually ran.
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var frozen = run.Original.LargestRecordedDistance!.Value;

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.False(run.Original.DeclaresSpeedSensor);
        Assert.Equal(PlausibleDistanceOf(result), result.Report.UsedTrackLengthMeters, 0.001);
        Assert.True(
            result.Report.UsedTrackLengthMeters > frozen * 2,
            $"the chosen length was {result.Report.UsedTrackLengthMeters:F0} m, "
                + $"near the device's frozen {frozen:F0} m");
    }

    [Fact]
    public void The_cap_bounds_only_the_stretch_the_anchors_could_not_measure()
    {
        // The Chabany proportions: 36 minutes, the GPS measuring 3.9 km before it froze. A cap
        // spread over every second treats the measured stretch as if it too could have been run at
        // 7 m/s, which allows 15.1 km and leaves a 2:23/km preview on an over-long track — the
        // number this rule exists not to produce. Only what is unknown may be bounded by a cap.
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var cap = SyntheticRun.Options.MaxSpeedMetersPerSecond;
        var overTheWholeActivity = cap * SyntheticRun.DurationSeconds;

        Assert.Equal(
            result.Alignment.MeasuredDistanceMeters
                + cap * (SyntheticRun.DurationSeconds - result.Alignment.MeasuredUntilSeconds),
            result.Report.UsedTrackLengthMeters,
            0.001);
        Assert.True(
            overTheWholeActivity - result.Report.UsedTrackLengthMeters > 4_000,
            $"the chosen {result.Report.UsedTrackLengthMeters:F0} m is barely below the "
                + $"{overTheWholeActivity:F0} m a cap over the whole activity would allow");
    }

    [Fact]
    public void The_speed_cap_the_remainder_is_bounded_by_is_read_from_the_options()
    {
        // The cap governs the stretch the anchors could not measure — the estimated zone of the
        // run whose GPS froze — so that is where a constant inside the service would show.
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        const double belowRunningPace = 1.0;

        var withDefaults = RepairService.Repair(run.Original, track);
        var crawling = RepairService.Repair(
            run.Original,
            track,
            SyntheticRun.Options with { MaxSpeedMetersPerSecond = belowRunningPace });

        // At the running cap the rest of the track is well within reach, so nothing is trimmed.
        Assert.DoesNotContain(DiagnosticCodes.TrackLongerThanActivity, withDefaults.Report.Warnings);
        Assert.Equal(track.Length, withDefaults.Report.UsedTrackLengthMeters, 0.001);

        // At a cap below the athlete's own pace the same track cannot be reached, and the chosen
        // length is the cap's own arithmetic rather than a number the service kept to itself.
        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, crawling.Report.Warnings);
        Assert.Equal(
            PlausibleDistanceOf(crawling, belowRunningPace),
            crawling.Report.UsedTrackLengthMeters,
            0.001);
        Assert.True(crawling.Report.UsedTrackLengthMeters < track.Length);

        // However small the cap, the floor holds: the chosen length never falls below what the
        // anchors established, so the athlete cannot be stopped inside the measured zone.
        Assert.True(
            crawling.Report.UsedTrackLengthMeters >= crawling.Alignment.MeasuredDistanceMeters,
            $"the chosen {crawling.Report.UsedTrackLengthMeters:F0} m is behind the anchors' "
                + $"{crawling.Alignment.MeasuredDistanceMeters:F0} m");
    }

    [Fact]
    public void The_remainder_is_measured_in_moving_seconds_and_not_on_the_clock()
    {
        // Five minutes at a traffic light. The cap is dropped below the athlete's own pace for one
        // reason: at the running cap this fixture's anchors reach its last record, and a remainder
        // of nothing cannot show which measure it was taken over (FR-5, AD-7).
        const double belowRunningPace = 1.0;
        var paused = SyntheticRun
            .CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds)
            .WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);

        var result = RepairService.Repair(
            paused.Original,
            paused.ReferenceTrack,
            SyntheticRun.Options with { MaxSpeedMetersPerSecond = belowRunningPace });

        var original = result.CleanedOriginal;
        var measuredUntil = result.Alignment.MeasuredUntilSeconds;
        var overTheClock = result.Alignment.MeasuredDistanceMeters
            + belowRunningPace * (original.ElapsedSeconds - measuredUntil);

        Assert.Equal(SyntheticRun.PauseSeconds, result.Report.PausedSeconds, 0.001);
        Assert.Equal(
            PlausibleDistanceOf(result, belowRunningPace),
            result.Report.UsedTrackLengthMeters,
            0.001);

        // And the two measures really do differ here, by the pause the clock would have paid for.
        Assert.Equal(
            SyntheticRun.PauseSeconds * belowRunningPace,
            overTheClock - result.Report.UsedTrackLengthMeters,
            0.001);
    }

    [Fact]
    public void A_sensor_that_contradicts_the_anchors_does_not_freeze_the_athlete()
    {
        // A footpod claiming 2 km where the GPS measured almost 4: positions can be pointed at, so
        // the sensor is set aside. Believing it would have the series reach its finish inside the
        // measured zone and hold the athlete there for the rest of the activity.
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var activity = WithFootpodMeasuring(run, metres: 2_000);

        var result = RepairService.Repair(activity, run.ReferenceTrack);
        var anchored = result.Alignment.MeasuredDistanceMeters;

        Assert.True(activity.LargestRecordedDistance < anchored, "the fixture must contradict the GPS");
        Assert.True(
            result.Report.UsedTrackLengthMeters > anchored,
            $"the chosen {result.Report.UsedTrackLengthMeters:F0} m against the anchors' {anchored:F0} m");

        // The cap over the remaining moving time decides instead, and the athlete keeps going.
        Assert.Equal(PlausibleDistanceOf(result), result.Report.UsedTrackLengthMeters, 0.001);
        Assert.True(
            result.Records[^1].Distance - result.Records[^121].Distance > 0,
            "the last two minutes of the activity covered no distance");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_declared_sensor_that_measured_nothing_leaves_the_cap_to_decide(bool zeroRatherThanAbsent)
    {
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var records = run.Original.Records
            .Select(record => record with { Distance = zeroRatherThanAbsent ? 0 : null });
        var activity = run.Original with { Records = [.. records], DeclaresSpeedSensor = true };

        var result = RepairService.Repair(activity, run.ReferenceTrack);

        Assert.Equal(PlausibleDistanceOf(result), result.Report.UsedTrackLengthMeters, 0.001);
        // A sensor that measured nothing must not pin the chosen length at nothing: that is the
        // one value the alignment refuses outright.
        Assert.True(result.Report.UsedTrackLengthMeters > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_alignment_refuses_a_chosen_length_of_nothing(double chosenTrackLength)
    {
        // A caller's defect rather than a claim about the athlete's files, so it carries no
        // declared code — and it must not be reportable as a problem with their input.
        var run = SyntheticRun.Create();
        var measured = Aligner.Measure(
            run.Original, run.Original, run.ReferenceTrack, SyntheticRun.Options);

        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Aligner.EndingAt(
            measured, run.Original.ElapsedSeconds, chosenTrackLength, SyntheticRun.Options));

        Assert.IsNotType<FitFixException>(refusal, exactMatch: false);
    }

    [Theory]
    [InlineData(TrackLengthMode.Plausible)]
    [InlineData(TrackLengthMode.Full)]
    public void Distance_never_decreases_and_ends_exactly_on_the_chosen_length(TrackLengthMode mode)
    {
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);

        // The shortfall guard of Story 1.5 is the one thing that could refuse this outright: it
        // would throw DISTANCE_SERIES_IMPLAUSIBLE rather than return.
        var result = RepairService.Repair(run.Original, run.ReferenceTrack, mode);

        for (var i = 1; i < result.Records.Count; i++)
            Assert.True(
                result.Records[i].Distance >= result.Records[i - 1].Distance,
                $"distance fell at record {i}: {result.Records[i - 1].Distance} → {result.Records[i].Distance}");

        Assert.Equal(result.Report.UsedTrackLengthMeters, result.Records[^1].Distance, 0.0001);

        // Exactly on it, and not before it. An interpolation still aimed at the whole track would
        // run into the monotonicity clamp partway through the estimated zone and hold the athlete
        // at the finish distance for the last several minutes: the same total, the same monotone
        // series, and a file saying they stood still to the end (AD-17).
        Assert.True(
            result.Records[^2].Distance < result.Report.UsedTrackLengthMeters,
            $"the finish distance was already reached one record early, at "
                + $"{result.Records[^2].Distance:F1} m");

        var lastTwoMinutes = result.Records[^1].Distance - result.Records[^121].Distance;
        Assert.True(lastTwoMinutes > 0, "the last two minutes of the activity covered no distance");
    }

    [Theory]
    [InlineData(TrackLengthMode.Plausible)]
    [InlineData(TrackLengthMode.Full)]
    public void The_summary_the_report_and_the_written_file_state_the_same_chosen_length(TrackLengthMode mode)
    {
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);

        var result = RepairService.Repair(run.Original, run.ReferenceTrack, mode);
        var session = FitMessageInspector
            .InspectTiming(FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0))
            .Session;

        // One owner for the number (AD-16): the summary settles it, the report carries it and the
        // file transcribes it. Three values that agree only by accident would be three owners.
        Assert.Equal(result.Report.UsedTrackLengthMeters, result.Summary.Total.DistanceMeters, 0.001);
        Assert.Equal(result.Report.UsedTrackLengthMeters, result.Report.RepairedDistanceMeters, 0.001);
        Assert.NotNull(session.DistanceMeters);
        Assert.Equal(result.Report.UsedTrackLengthMeters, session.DistanceMeters!.Value, 1.0);
    }

    [Theory]
    // A footpod, a wheel sensor and a power meter measure movement without a fix (AD-17).
    [InlineData(AntplusDeviceType.StrideSpeedDistance, true)]
    [InlineData(AntplusDeviceType.BikeSpeed, true)]
    [InlineData(AntplusDeviceType.BikeSpeedCadence, true)]
    [InlineData(AntplusDeviceType.BikePower, true)]
    // Everything else does not, however much else it measures.
    [InlineData(AntplusDeviceType.HeartRate, false)]
    [InlineData(AntplusDeviceType.BikeCadence, false)]
    [InlineData(AntplusDeviceType.EnvironmentSensorLegacy, false)]
    public void An_antplus_sensor_is_a_speed_source_only_when_it_measures_movement(byte profile, bool expected)
    {
        var activity = ReadWithDevices(SyntheticFitBuilder.Antplus(profile));

        Assert.Equal(expected, activity.DeclaresSpeedSensor);
    }

    [Theory]
    [InlineData(BleDeviceType.Footpod, true)]
    [InlineData(BleDeviceType.BikeSpeed, true)]
    [InlineData(BleDeviceType.BikeSpeedCadence, true)]
    [InlineData(BleDeviceType.BikePower, true)]
    [InlineData(BleDeviceType.HeartRate, false)]
    [InlineData(BleDeviceType.BikeCadence, false)]
    // A watch whose speed came from its own GPS pairs nothing: the profile numbers differ between
    // ANT+ and Bluetooth, and a reader that confused the two tables would read 6 as a footpod.
    [InlineData(BleDeviceType.ConnectedGps, false)]
    public void A_bluetooth_sensor_is_a_speed_source_only_when_it_measures_movement(byte profile, bool expected)
    {
        var activity = ReadWithDevices(SyntheticFitBuilder.Ble(profile));

        Assert.Equal(expected, activity.DeclaresSpeedSensor);
    }

    [Fact]
    public void A_watch_that_declares_only_its_own_gps_declares_no_speed_source()
    {
        // The Chabany watch: a local GPS and nothing else, whose speed is exactly the one that
        // reads zero after the failure.
        var activity = ReadWithDevices(
            new SyntheticFitBuilder.DeclaredDevice(SourceType.Local, LocalDeviceType.Gps));

        Assert.False(activity.DeclaresSpeedSensor);
    }

    [Fact]
    public void A_file_with_no_device_info_at_all_declares_no_speed_source()
    {
        var run = SyntheticRun.Create();
        using var stream = new MemoryStream(SyntheticFitBuilder.WriteActivity(run.Original));

        Assert.False(FitActivityReader.Read(stream).DeclaresSpeedSensor);
    }

    [Fact]
    public void One_declared_sensor_among_several_devices_is_enough()
    {
        var activity = ReadWithDevices(
            new SyntheticFitBuilder.DeclaredDevice(SourceType.Local, LocalDeviceType.Gps),
            SyntheticFitBuilder.Antplus(AntplusDeviceType.HeartRate),
            SyntheticFitBuilder.Antplus(AntplusDeviceType.StrideSpeedDistance));

        Assert.True(activity.DeclaresSpeedSensor);
    }

    [Theory]
    [InlineData(TrackLengthMode.Plausible)]
    [InlineData(TrackLengthMode.Full)]
    public void The_composition_root_carries_the_mode_and_the_declared_sensor_through(TrackLengthMode mode)
    {
        // End to end on bytes: the device_info the file declares reaches the choice of length, and
        // the mode reaches the repair, through the one composition the CLI and the API share.
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var originalBytes = SyntheticFitBuilder.WriteActivityWithDevices(
            run.Original,
            [SyntheticFitBuilder.Antplus(AntplusDeviceType.StrideSpeedDistance)]);
        var trackBytes = SyntheticFitBuilder.WriteTrack(run.TrackPoints, run.Original.StartTime);
        var measured = run.Original.RecordedDistance!.Value;

        using var original = new MemoryStream(originalBytes);
        using var track = new MemoryStream(trackBytes);
        var result = RepairPipeline.Repair(original, track, mode);

        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, result.Report.Warnings);
        Assert.Equal(
            mode == TrackLengthMode.Full ? result.Report.TrackLengthMeters : measured,
            result.Report.UsedTrackLengthMeters,
            1.0);
    }

    [Theory]
    [InlineData(TrackLengthMode.Plausible)]
    [InlineData(TrackLengthMode.Full)]
    public void The_file_overload_the_cli_calls_carries_the_mode_too(TrackLengthMode mode)
    {
        // The entry point the CLI's --trust-track actually reaches. Its answer has to be the
        // stream overload's, or the only shell today would be the one place the mode is lost.
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var directory = Path.Combine(AppContext.BaseDirectory, "track-length");
        Directory.CreateDirectory(directory);

        var originalPath = Path.Combine(directory, "original.fit");
        var trackPath = Path.Combine(directory, "track.fit");
        System.IO.File.WriteAllBytes(originalPath, SyntheticFitBuilder.WriteActivity(run.Original));
        System.IO.File.WriteAllBytes(
            trackPath,
            SyntheticFitBuilder.WriteTrack(run.TrackPoints, run.Original.StartTime));

        var fromFiles = RepairPipeline.RepairFiles(originalPath, trackPath, mode);

        using var original = System.IO.File.OpenRead(originalPath);
        using var track = System.IO.File.OpenRead(trackPath);
        var fromStreams = RepairPipeline.Repair(original, track, mode);

        Assert.Equal(
            fromStreams.Report.UsedTrackLengthMeters,
            fromFiles.Report.UsedTrackLengthMeters,
            0.001);
        Assert.Equal(fromStreams.Report.TrackLengthMeters, fromFiles.Report.TrackLengthMeters, 0.001);
        Assert.Equal(
            mode == TrackLengthMode.Full,
            fromFiles.Report.UsedTrackLengthMeters > fromFiles.Report.TrackLengthMeters - 0.5);
    }

    /// <summary>
    /// The run with a footpod declared whose odometer reached exactly <paramref name="metres"/> —
    /// the only way to state a plausible distance to the metre, since the alternative is a speed
    /// cap multiplied by whatever the anchors left over. Every reading is clipped to that figure
    /// and the last one is set to it, so the furthest the device ever reached is the number asked
    /// for whether it is above or below what the watch's own GPS recorded.
    /// </summary>
    private static OriginalActivity WithFootpodMeasuring(SyntheticRun run, double metres)
    {
        var records = run.Original.Records
            .Select(record => record with
            {
                Distance = record.Distance is { } measured ? Math.Min(measured, metres) : null
            })
            .ToArray();
        records[^1] = records[^1] with { Distance = metres };

        return run.Original with { Records = records, DeclaresSpeedSensor = true };
    }

    /// <summary>The synthetic run written out with the given <c>device_info</c> messages and read back.</summary>
    private static OriginalActivity ReadWithDevices(params SyntheticFitBuilder.DeclaredDevice[] devices)
    {
        var run = SyntheticRun.Create();
        using var stream = new MemoryStream(
            SyntheticFitBuilder.WriteActivityWithDevices(run.Original, devices));

        return FitActivityReader.Read(stream);
    }
}
