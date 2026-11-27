using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

public class RepairServiceTests
{
    private static RepairResult RepairSyntheticRun(bool withGpsFailure = true, bool withAnyGps = true)
    {
        var run = SyntheticRun.Create(withGpsFailure, withAnyGps);
        return RepairService.Repair(run.Original, run.ReferenceTrack);
    }

    [Fact]
    public void Total_distance_is_the_sum_of_the_two_sources_and_not_the_track_length()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        // The whole track is used: this fixture's own time makes all of it plausible, so nothing
        // is trimmed (AD-17).
        Assert.Equal(track.Length, result.Report.UsedTrackLengthMeters, 0.001);

        // And the repaired distance is past it, legitimately (AD-26). The estimated zone drew the
        // metres the track holds beyond the last anchor; the measured zone measured its own, and
        // the real jitter in those positions is the surplus.
        Assert.Equal(
            track.Length + AlignmentOptionsTests.ExpectedMeasuredZoneSurplus,
            result.Report.RepairedDistanceMeters,
            0.01);
        Assert.True(
            result.Report.RepairedDistanceMeters > track.Length,
            $"the sum came to {result.Report.RepairedDistanceMeters:F1} m, at or below the track's "
                + $"{track.Length:F1} m: nothing in it came from the watch's own positions");

        // Within the 1–3 % AD-26 budgets for a measured stretch, over the part of the activity
        // the surplus was earned in: the price of real data, and no licence to inflate a run.
        var inflation = AlignmentOptionsTests.ExpectedMeasuredZoneSurplus
            / result.Alignment.MeasuredDistanceMeters;
        Assert.InRange(inflation, 0.001, 0.03);
    }

    [Fact]
    public void Timestamps_and_elapsed_time_are_preserved()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Equal(run.Original.Records.Count, result.Records.Count);
        Assert.Equal(
            run.Original.Records.Select(r => r.Timestamp),
            result.Records.Select(r => r.Timestamp));
        Assert.Equal(run.Original.ElapsedSeconds, result.Report.ElapsedSeconds, 1.0);
    }

    [Fact]
    public void Distance_is_monotonically_non_decreasing()
    {
        var result = RepairSyntheticRun();

        for (var i = 1; i < result.Records.Count; i++)
            Assert.True(
                result.Records[i].Distance >= result.Records[i - 1].Distance,
                $"Дистанція зменшилась на записі {i}: {result.Records[i - 1].Distance} → {result.Records[i].Distance}");
    }

    [Fact]
    public void Sensor_values_are_carried_over_unchanged()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        for (var i = 0; i < result.Records.Count; i++)
        {
            Assert.Equal(run.Original.Records[i].HeartRate, result.Records[i].HeartRate);
            Assert.Equal(run.Original.Records[i].Cadence, result.Records[i].Cadence);
            Assert.Equal(run.Original.Records[i].Temperature, result.Records[i].Temperature);
        }
    }

    [Fact]
    public void Gps_stall_is_detected_without_hardcoded_cutoff()
    {
        var result = RepairSyntheticRun();

        Assert.Contains(DiagnosticCodes.GpsStallDetected, result.Report.Warnings);
        Assert.NotNull(result.Report.GpsStallAtSeconds);
        // Детектор має знайти збій біля реального моменту, а не за жорстким порогом.
        Assert.InRange(result.Report.GpsStallAtSeconds!.Value, SyntheticRun.GpsFailureSeconds - 30, SyntheticRun.GpsFailureSeconds + 120);
    }

    [Fact]
    public void Cleaning_a_mid_activity_burst_does_not_invent_a_stall()
    {
        // The athlete runs out and back, and loses the fix for a hundred seconds in between. The
        // stall detector walks positioned records only, so reading the cleaned series would see
        // one place a hundred seconds apart and report a stall that never happened — after which
        // every later anchor is skipped and the rest of the activity falls into the estimated zone.
        var run = SyntheticRun.Create();
        var activity = OutAndBackThroughGarbage();

        var result = RepairService.Repair(activity, run.ReferenceTrack);

        Assert.Equal(100, result.Report.DiscardedPositionCount);
        Assert.Null(result.Report.GpsStallAtSeconds);
        Assert.DoesNotContain(DiagnosticCodes.GpsStallDetected, result.Report.Warnings);
        // The detector finds nothing in the series as it arrived, and the repair must agree.
        Assert.Null(GpsStallDetector.Detect(activity, SyntheticRun.Options));
    }

    [Fact]
    public void Repair_of_a_two_hour_activity_of_leading_garbage_takes_less_than_three_seconds()
    {
        // SM-3 against the cleaner's worst input: every position of a two-hour activity a
        // teleport until the last five minutes, so the vote runs for each of them. The budget is
        // the product requirement and stays, but the margin is what keeps it honest on a shared
        // runner — the work is linear now, and the growth rate itself is pinned below.
        var run = SyntheticRun.Create();
        var activity = LeadingGarbage(garbageRecords: 7_200, thenRunningSeconds: 300);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = RepairService.Repair(activity, run.ReferenceTrack);
        stopwatch.Stop();

        Assert.Equal(7_200, result.Report.DiscardedPositionCount);
        Assert.True(stopwatch.Elapsed.TotalSeconds < 3, $"Обробка зайняла {stopwatch.Elapsed.TotalSeconds:F2} с");
    }

    [Fact]
    public void Cleaning_leading_garbage_costs_no_worse_than_linearly_in_its_length()
    {
        // A wall-clock budget on shared hardware measures the runner, so the guarantee is pinned
        // as a growth rate, which the runner's speed cancels out of: ten times the garbage must
        // not cost anything like a hundred times the work. The vote used to rescan the whole run
        // from every candidate, and that is exactly what a quadratic ratio would mean again.
        GpsCleaner.Clean(LeadingGarbage(200, 10), SyntheticRun.Options);

        var small = FastestCleaning(LeadingGarbage(5_000, 10));
        var tenTimesLarger = FastestCleaning(LeadingGarbage(50_000, 10));

        Assert.True(small > TimeSpan.Zero, "the smaller run was too quick to measure at all");
        // The bound is 50 and not 25: the smaller run is milliseconds, so a loaded machine can
        // stretch the ratio well past its unloaded 7.8 — it reached 30 under four CPU hogs — while
        // quadratic growth lands near 100 either way. CI runners are shared hardware.
        Assert.True(
            tenTimesLarger < small * 50,
            $"ten times the garbage cost {tenTimesLarger / small:F0} times the work "
                + $"({small.TotalMilliseconds:F1} ms → {tenTimesLarger.TotalMilliseconds:F1} ms)");
    }

    /// <summary>
    /// The quickest of three cleanings. The fastest run is the one least disturbed by whatever
    /// else the machine was doing, which is what makes a ratio of two sizes worth asserting on.
    /// </summary>
    private static TimeSpan FastestCleaning(OriginalActivity activity)
    {
        var best = TimeSpan.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            GpsCleaner.Clean(activity, SyntheticRun.Options);
            stopwatch.Stop();
            if (stopwatch.Elapsed < best)
                best = stopwatch.Elapsed;
        }

        return best;
    }

    [Fact]
    public void Measured_zone_pace_matches_the_athlete_within_five_percent()
    {
        var result = RepairSyntheticRun();
        var measuredUntil = result.Alignment.MeasuredUntilSeconds;

        Assert.True(measuredUntil > 1000, $"Надійна зона надто коротка: {measuredUntil} с");

        var start = result.Records[0];
        var endIndex = (int)measuredUntil;
        var end = result.Records[endIndex];
        var averageSpeed = (end.Distance - start.Distance) / (end.Timestamp - start.Timestamp).TotalSeconds;

        // SM-1: темп у Надійній зоні відрізняється від реального ≤ 5 %.
        Assert.Equal(SyntheticRun.SpeedMetersPerSecond, averageSpeed, SyntheticRun.SpeedMetersPerSecond * 0.05);
    }

    [Fact]
    public void Zero_power_from_broken_gps_is_not_carried_over()
    {
        var result = RepairSyntheticRun();

        var afterFailure = result.Records
            .Where(r => (r.Timestamp - result.Records[0].Timestamp).TotalSeconds > SyntheticRun.GpsFailureSeconds + 30)
            .ToArray();

        Assert.All(afterFailure, record => Assert.Null(record.Power));

        var beforeFailure = result.Records
            .Where(r => (r.Timestamp - result.Records[0].Timestamp).TotalSeconds < SyntheticRun.GpsFailureSeconds - 30)
            .ToArray();

        Assert.All(beforeFailure, record => Assert.Equal((ushort)200, record.Power));
    }

    [Fact]
    public void Altitude_follows_the_source_of_its_own_zone()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        // A measured record keeps the watch's own altitude, to the millimetre (AD-26) — the start
        // line, where the barometer read 182.8 m.
        Assert.NotNull(result.Records[0].Altitude);
        Assert.Equal(run.Original.Records[0].Altitude!.Value, result.Records[0].Altitude!.Value, 1e-9);
        Assert.Equal(SyntheticRun.OriginalStartAltitude, result.Records[0].Altitude!.Value, 1e-9);

        // The last record is inside the estimated zone and sits at the end of the track, so its
        // altitude is the track's own there plus the shift that lines the track up with the
        // Original's start — this fixture's track is 400.8 m low, as FFF.fit was (FR-6).
        var shift = SyntheticRun.OriginalStartAltitude - track.StartAltitude!.Value;
        Assert.Equal(-SyntheticRun.TrackAltitudeOffset, shift, 1e-9);
        Assert.Equal(
            track.At(track.Length).Altitude!.Value + shift,
            result.Records[^1].Altitude!.Value,
            1e-9);
    }

    [Fact]
    public void Activity_without_any_gps_is_fully_estimated()
    {
        var result = RepairSyntheticRun(withGpsFailure: false, withAnyGps: false);

        Assert.Contains(DiagnosticCodes.NoOriginalGps, result.Report.Warnings);
        Assert.Equal(0, result.Alignment.MeasuredUntilSeconds);
        // The length the repair chose, which is what the series ends on (AD-17) — equal to the
        // whole track here, since this fixture's time makes the whole of it plausible.
        Assert.Equal(result.Report.UsedTrackLengthMeters, result.Report.RepairedDistanceMeters, 1.0);

        // Дистанція розподілена рівномірно в часі.
        var middle = result.Records[result.Records.Count / 2];
        Assert.Equal(
            result.Report.UsedTrackLengthMeters / 2,
            middle.Distance,
            result.Report.UsedTrackLengthMeters * 0.02);
    }

    [Fact]
    public void Track_from_another_city_is_refused_rather_than_repaired()
    {
        // A track about another route attaches to the activity at neither of its own ends, nor
        // anywhere between them, and AD-27 answers that with a refusal: before story 5.1 the repair
        // went ahead on the assumption that the activity begins where the track does, which for
        // these two files draws the athlete 129 km from where they ran.
        var run = SyntheticRun.Create();
        var shifted = run.TrackPoints
            .Select(p => new TrackPoint(new GeoPoint(p.Position.Latitude + 1.0, p.Position.Longitude + 1.0), p.Altitude))
            .ToArray();

        var refusal = Assert.Throws<MalformedArgumentException>(() => RepairService.Repair(
            run.Original,
            ReferenceTrack.FromPoints(shifted, SyntheticRun.Options)));

        Assert.Equal(DiagnosticCodes.TrackAttachesNowhere, refusal.Code);
        Assert.True(DiagnosticCodes.IsDeclared(refusal.Code));
    }

    [Fact]
    public void Repair_of_a_two_hour_activity_takes_less_than_three_seconds()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        RepairService.Repair(run.Original, track);
        stopwatch.Stop();

        // SM-3: обробка активності на 2 години — менше 3 с.
        Assert.True(stopwatch.Elapsed.TotalSeconds < 3, $"Обробка зайняла {stopwatch.Elapsed.TotalSeconds:F2} с");
    }

    /// <summary>
    /// Out 600 m, a hundred seconds of garbage that is somewhere different every second, then
    /// back to where the garbage began — so the cleaned series looks frozen and the recorded one
    /// plainly is not.
    /// </summary>
    private static OriginalActivity OutAndBackThroughGarbage()
    {
        var start = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
        var records = new List<ActivityRecord>();

        for (var second = 0; second <= 200; second++)
            records.Add(At(start.AddSeconds(second), SyntheticRun.Offset(second * 3.0, 0)));

        for (var second = 0; second < 100; second++)
            records.Add(At(
                start.AddSeconds(201 + second),
                SyntheticRun.FarAway(500 + second * 7, bearingDegrees: 45)));

        for (var second = 0; second <= 200; second++)
            records.Add(At(start.AddSeconds(301 + second), SyntheticRun.Offset(600 - second * 3.0, 0)));

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>Nothing but teleports, then a short genuine run.</summary>
    private static OriginalActivity LeadingGarbage(int garbageRecords, int thenRunningSeconds)
    {
        var start = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
        var teleport = SyntheticRun.FarAway(kilometres: 2_000, bearingDegrees: 90);
        var records = new List<ActivityRecord>(garbageRecords + thenRunningSeconds + 1);

        for (var second = 0; second < garbageRecords; second++)
            records.Add(At(start.AddSeconds(second), teleport));

        for (var second = 0; second <= thenRunningSeconds; second++)
            records.Add(At(start.AddSeconds(garbageRecords + second), SyntheticRun.Offset(second * 3.0, 0)));

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    private static ActivityRecord At(DateTime timestamp, GeoPoint position) => new()
    {
        Timestamp = timestamp,
        Latitude = position.Latitude,
        Longitude = position.Longitude,
        HeartRate = 150,
        Cadence = 85
    };
}
