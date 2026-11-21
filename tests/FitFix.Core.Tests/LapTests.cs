using Dynastream.Fit;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// Story 1.6: the athlete's laps survive the repair. The Original's <c>lap</c> messages become
/// intervals in the model (AD-24), the model derives a partition of the activity from their
/// starts and ends, and the writer emits one lap message per interval with a distance the repair
/// earned (FR-6). Each lap's boundary stays where the athlete pressed the button; each lap's
/// distance, timer time, averages and positions are its own.
/// </summary>
public class LapTests
{
    /// <summary>The fixtures' own clock, so a second here means what it means there.</summary>
    private static readonly System.DateTime Start = SyntheticRun.StartTime;

    /// <summary>The three unequal laps of the matrix: 300 s, 1200 s and the rest.</summary>
    private const int FirstLapEnd = 300;
    private const int SecondLapEnd = 1500;

    // --- Reading the intervals.

    [Fact]
    public void Laps_are_read_as_intervals_in_time_order_with_the_files_own_boundaries()
    {
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);

        var read = Read(SyntheticFitBuilder.WriteActivity(run.Original));

        Assert.Equal(3, read.Laps.Count);
        Assert.Equal(Start, read.Laps[0].Start);
        Assert.Equal(Start.AddSeconds(FirstLapEnd), read.Laps[0].End);
        Assert.Equal(Start.AddSeconds(FirstLapEnd), read.Laps[1].Start);
        Assert.Equal(Start.AddSeconds(SecondLapEnd), read.Laps[1].End);
        Assert.Equal(Start.AddSeconds(SecondLapEnd), read.Laps[2].Start);
        Assert.Equal(Start.AddSeconds(SyntheticRun.DurationSeconds), read.Laps[2].End);
    }

    [Fact]
    public void A_file_with_no_lap_message_has_no_laps()
    {
        var read = Read(SyntheticFitBuilder.WriteActivity(SyntheticRun.Create().Original));

        // Empty, not one invented interval: the partition is what turns "no laps" into one lap,
        // and a model that guessed here would have nothing left to say about a file that has laps.
        Assert.Empty(read.Laps);
    }

    [Fact]
    public void A_lap_with_no_start_time_keeps_the_end_the_partition_needs()
    {
        // A boundary the athlete pressed, in a file that did not state where the lap began.
        // Dropping the whole message to avoid inventing a beginning would throw the boundary away.
        var bytes = SyntheticFitBuilder.WriteActivityWithRawLaps(
            SyntheticRun.Create().Original,
            [new SyntheticFitBuilder.RawLap(null, Start.AddSeconds(600))]);

        var read = Read(bytes);

        var lap = Assert.Single(read.Laps);
        Assert.Equal(Start.AddSeconds(600), lap.End);
        Assert.Equal(lap.End, lap.Start);
        Assert.Equal(2, read.LapPartition().Count);
        Assert.Equal(Start.AddSeconds(600), read.LapPartition()[0].End);
    }

    [Fact]
    public void A_lap_with_no_end_is_ignored_rather_than_guessed_at()
    {
        // Nothing in the message says where the lap finished, and an end derived from an elapsed
        // time would move a boundary the athlete made.
        var bytes = SyntheticFitBuilder.WriteActivityWithRawLaps(
            SyntheticRun.Create().Original,
            [new SyntheticFitBuilder.RawLap(Start.AddSeconds(300), null)]);

        var read = Read(bytes);

        Assert.Empty(read.Laps);
        Assert.Single(read.LapPartition());
    }

    [Fact]
    public void A_lap_belonging_to_something_else_in_the_file_is_not_this_activitys()
    {
        // A FIT file may hold more than one session, and a course or a previous workout leaves
        // lap messages of its own. Taking them as cut points would split this activity where
        // nobody ran.
        var run = SyntheticRun.Create();
        var bytes = SyntheticFitBuilder.WriteActivityWithRawLaps(
            run.Original,
            [
                new SyntheticFitBuilder.RawLap(Start.AddSeconds(-500), Start.AddSeconds(-200)),
                new SyntheticFitBuilder.RawLap(
                    run.Original.EndTime.AddSeconds(200), run.Original.EndTime.AddSeconds(500)),
                new SyntheticFitBuilder.RawLap(Start, Start.AddSeconds(600))
            ]);

        var read = Read(bytes);

        var lap = Assert.Single(read.Laps);
        Assert.Equal(Start.AddSeconds(600), lap.End);
    }

    [Fact]
    public void A_lap_read_from_a_file_reaches_the_repair()
    {
        // End to end through the composition root (AD-10): the lap messages in the file are the
        // only thing that says how the athlete split the run, so if they stop at the reader the
        // written file comes back as one undifferentiated effort.
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        using var original = new MemoryStream(SyntheticFitBuilder.WriteActivity(run.Original));
        using var track = new MemoryStream(
            SyntheticFitBuilder.WriteTrack(run.TrackPoints, run.Original.StartTime));

        var result = RepairPipeline.Repair(original, track);

        Assert.Equal(3, result.CleanedOriginal.Laps.Count);
        Assert.Equal(3, result.Report.LapCount);
        Assert.Equal(3, FitMessageInspector
            .Inspect(FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0))
            .LapCount);
    }

    // --- The partition.

    [Fact]
    public void An_activity_with_no_laps_is_one_lap_covering_everything()
    {
        var activity = SyntheticRun.Create().Original;

        var lap = Assert.Single(activity.LapPartition());

        Assert.Equal(activity.StartTime, lap.Start);
        Assert.Equal(activity.EndTime, lap.End);
    }

    [Fact]
    public void An_activity_with_no_records_has_no_laps_rather_than_an_exception()
    {
        // The project's rule for bad input is a declared code or nothing (AD-18), never an index
        // out of range out of the core.
        var empty = new OriginalActivity { Records = [], Sport = SportKind.Running };

        Assert.Empty(empty.LapPartition());
    }

    [Fact]
    public void The_partition_keeps_the_boundaries_the_athlete_pressed()
    {
        var activity = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd).Original;

        var partition = activity.LapPartition();

        Assert.Equal(activity.Laps, partition);
    }

    [Fact]
    public void A_lap_that_does_not_begin_at_the_start_keeps_its_own_beginning()
    {
        // One lap in the middle of an otherwise unlapped run. Cutting only at lap ends would give
        // back [start, 1000] and [1000, end] — neither of them the lap in the file — with the
        // arithmetic still adding up, so nothing would look wrong.
        var activity = SyntheticRun.Create().Original with
        {
            Laps = [new LapInterval(Start.AddSeconds(500), Start.AddSeconds(1000))]
        };

        var partition = activity.LapPartition();

        Assert.Equal(3, partition.Count);
        AssertTiles(activity, partition);
        Assert.Contains(activity.Laps[0], partition);
        Assert.Equal(Start.AddSeconds(500), partition[0].End);
        Assert.Equal(Start.AddSeconds(1000), partition[1].End);
    }

    [Fact]
    public void Four_laps_come_back_as_four_laps()
    {
        var activity = SyntheticRun.Create().WithLapsEndingAt(400, 800, 1200).Original;

        Assert.Equal(4, activity.Laps.Count);
        Assert.Equal(activity.Laps, activity.LapPartition());
    }

    [Fact]
    public void Laps_that_overlap_or_repeat_an_end_still_tile_the_activity_once()
    {
        // A device that writes a lap inside another, a second lap ending where the first did, and
        // a lap spanning the whole activity. Copying those spans across would hand Strava a file
        // whose lap distances do not add up to its own total.
        var activity = SyntheticRun.Create().Original with
        {
            Laps =
            [
                new LapInterval(Start, Start.AddSeconds(600)),
                new LapInterval(Start.AddSeconds(300), Start.AddSeconds(600)),
                new LapInterval(Start.AddSeconds(400), Start.AddSeconds(1200)),
                new LapInterval(Start, Start.AddSeconds(SyntheticRun.DurationSeconds))
            ]
        };

        var partition = activity.LapPartition();

        // Every distinct boundary any of them states, used once: 300, 400, 600 and 1200.
        Assert.Equal(5, partition.Count);
        AssertTiles(activity, partition);
        Assert.Equal(
            [300, 400, 600, 1200, SyntheticRun.DurationSeconds],
            partition.Select(lap => (int)activity.SecondsFromStart(lap.End)));
    }

    [Fact]
    public void A_lap_beyond_the_activity_is_clipped_to_its_end()
    {
        // The lap message outlives the records — a watch that wrote the lap it was in when the
        // battery went. Its end is no cut point; the last lap simply runs to the last record.
        var activity = SyntheticRun.Create().Original with
        {
            Laps =
            [
                new LapInterval(Start, Start.AddSeconds(1000)),
                new LapInterval(Start.AddSeconds(1000), Start.AddSeconds(5000))
            ]
        };

        var partition = activity.LapPartition();

        Assert.Equal(2, partition.Count);
        AssertTiles(activity, partition);
        Assert.Equal(activity.EndTime, partition[^1].End);
    }

    [Fact]
    public void A_lap_ending_at_or_before_the_first_record_cuts_nothing()
    {
        // A lap the watch closed as the activity opened, and one that ended before it began.
        // A cut at or before the first record would open the partition with an interval of no
        // duration — or a backwards one.
        var activity = SyntheticRun.Create().Original with
        {
            Laps =
            [
                new LapInterval(Start.AddSeconds(-200), Start.AddSeconds(-100)),
                new LapInterval(Start.AddSeconds(-100), Start)
            ]
        };

        var lap = Assert.Single(activity.LapPartition());

        Assert.Equal(Start, lap.Start);
        Assert.Equal(activity.EndTime, lap.End);
        Assert.True(lap.End > lap.Start, "the only lap of the activity spans no time");
    }

    [Fact]
    public void A_cut_resolving_onto_the_activitys_own_end_records_is_dropped()
    {
        // Under smart recording a boundary a few seconds inside the activity still resolves to
        // the first record, and one a few seconds short of the finish to the last. Keeping either
        // would open or close the partition with an interval holding a single record — no
        // duration to speak of, no distance, no pace — which the time bounds alone do not catch
        // because both cuts are honestly inside the span.
        const int gapSeconds = 10;
        var sparse = SyntheticRun.Create(withGpsFailure: false).WithRecordsEvery(gapSeconds).Original;
        var activity = sparse with
        {
            Laps =
            [
                new LapInterval(Start, Start.AddSeconds(3)),
                new LapInterval(Start.AddSeconds(3), sparse.EndTime.AddSeconds(-2)),
                new LapInterval(sparse.EndTime.AddSeconds(-2), sparse.EndTime)
            ]
        };

        var partition = activity.LapPartition();

        Assert.Single(partition);
        AssertTiles(activity, partition);
    }

    [Fact]
    public void A_cut_no_record_separates_from_the_previous_one_is_dropped()
    {
        // A manual press beside an auto-lap, two tenths of a second apart on a 1 Hz series. Both
        // resolve to the same record, so writing both would put a lap of no distance and no pace
        // between them.
        var activity = SyntheticRun.Create().Original with
        {
            Laps =
            [
                new LapInterval(Start, Start.AddSeconds(600)),
                new LapInterval(Start, Start.AddSeconds(600.2)),
                new LapInterval(Start.AddSeconds(600.2), Start.AddSeconds(1200))
            ]
        };

        var partition = activity.LapPartition();

        Assert.Equal(3, partition.Count);
        AssertTiles(activity, partition);
    }

    [Fact]
    public void A_backwards_lap_is_dropped_and_a_lap_the_file_gave_no_start_is_a_point()
    {
        var activity = SyntheticRun.Create().Original with
        {
            Laps =
            [
                new LapInterval(Start.AddSeconds(900), Start.AddSeconds(600)),
                new LapInterval(Start.AddSeconds(600), Start.AddSeconds(600))
            ]
        };

        // The backwards one describes no stretch of the activity and is not in the model at all.
        // The point is how an end-only lap is carried, and its end is still a boundary.
        var lap = Assert.Single(activity.Laps);
        Assert.Equal(Start.AddSeconds(600), lap.Start);
        Assert.Equal(Start.AddSeconds(600), lap.End);
        Assert.Equal(2, activity.LapPartition().Count);
    }

    [Fact]
    public void Laps_are_ordered_by_start_and_then_by_end()
    {
        var activity = SyntheticRun.Create().Original with
        {
            Laps =
            [
                new LapInterval(Start.AddSeconds(SecondLapEnd), Start.AddSeconds(2160)),
                new LapInterval(Start.AddSeconds(FirstLapEnd), Start.AddSeconds(900)),
                new LapInterval(Start.AddSeconds(FirstLapEnd), Start.AddSeconds(SecondLapEnd)),
                new LapInterval(Start, Start.AddSeconds(FirstLapEnd))
            ]
        };

        // The one place a lap list is put in order, so no reader has to sort it again and the
        // partition's cuts arrive in a predictable sequence.
        Assert.Equal(
            [
                (0, FirstLapEnd),
                (FirstLapEnd, 900),
                (FirstLapEnd, SecondLapEnd),
                (SecondLapEnd, 2160)
            ],
            activity.Laps.Select(lap =>
                ((int)activity.SecondsFromStart(lap.Start), (int)activity.SecondsFromStart(lap.End))));
    }

    // --- The written file.

    [Fact]
    public void Three_laps_come_back_as_three_laps_at_their_own_boundaries()
    {
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var bytes = FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0);

        var counts = FitMessageInspector.Inspect(bytes);
        var timing = FitMessageInspector.InspectTiming(bytes);

        Assert.Equal(3, counts.LapCount);
        Assert.Equal(3, timing.Laps.Count);

        // AD-19: the session says how many laps precede it, so a reader can find them.
        Assert.Equal((ushort)3, timing.SessionNumLaps);

        // message_index counts from zero, in time order.
        Assert.Equal([(ushort)0, (ushort)1, (ushort)2], timing.Laps.Select(l => l.MessageIndex!.Value));

        Assert.Equal(Start, timing.Laps[0].Start);
        Assert.Equal(Start.AddSeconds(FirstLapEnd), timing.Laps[0].End);
        Assert.Equal(Start.AddSeconds(FirstLapEnd), timing.Laps[1].Start);
        Assert.Equal(Start.AddSeconds(SecondLapEnd), timing.Laps[1].End);
        Assert.Equal(Start.AddSeconds(SecondLapEnd), timing.Laps[2].Start);
        Assert.Equal(Start.AddSeconds(SyntheticRun.DurationSeconds), timing.Laps[2].End);

        Assert.Equal(FirstLapEnd, timing.Laps[0].ElapsedSeconds!.Value, 0.001);
        Assert.Equal(SecondLapEnd - FirstLapEnd, timing.Laps[1].ElapsedSeconds!.Value, 0.001);
        Assert.Equal(
            SyntheticRun.DurationSeconds - SecondLapEnd, timing.Laps[2].ElapsedSeconds!.Value, 0.001);

        // Why the lap ended, which Strava reads to tell a split from an automatic one.
        Assert.Equal([LapTrigger.Manual, LapTrigger.Manual, LapTrigger.SessionEnd],
            timing.Laps.Select(lap => lap.Trigger!.Value));
    }

    [Fact]
    public void Lap_distances_are_the_metres_the_repair_earned_in_each_lap()
    {
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        var distances = result.Records.Select(r => r.Distance).ToArray();
        var expected = new[]
        {
            distances[FirstLapEnd] - distances[0],
            distances[SecondLapEnd] - distances[FirstLapEnd],
            distances[^1] - distances[SecondLapEnd]
        };

        for (var lap = 0; lap < 3; lap++)
            Assert.Equal(expected[lap], timing.Laps[lap].DistanceMeters!.Value, 0.01);

        // Never the distance the device measured with the GPS that failed, which is the whole
        // point of recomputing them: the fixture writes an absurd one into every lap message.
        Assert.All(timing.Laps, lap =>
            Assert.NotEqual(
                SyntheticFitBuilder.DeviceLapDistanceMeters, lap.DistanceMeters!.Value, 0.01));

        // And not the even split a writer averaging over the whole activity would produce: the
        // laps are unequal, so three equal distances would mean the boundaries were ignored.
        var total = distances[^1];
        Assert.True(
            Math.Abs(timing.Laps[0].DistanceMeters!.Value - total / 3) > 100,
            $"lap 1 covered {timing.Laps[0].DistanceMeters!.Value:F0} m, which is a third of the run");
    }

    [Fact]
    public void The_laps_add_up_to_the_session_and_tile_it_without_gap_or_overlap()
    {
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        // Within a metre: a file whose lap distances do not add up to its total is worse than one
        // with a single lap, because Strava shows the arithmetic.
        Assert.Equal(
            timing.Session.DistanceMeters!.Value,
            timing.Laps.Sum(lap => lap.DistanceMeters!.Value),
            1.0);

        Assert.Equal(
            timing.Session.ElapsedSeconds!.Value,
            timing.Laps.Sum(lap => lap.ElapsedSeconds!.Value),
            0.01);
        Assert.Equal(
            timing.Session.TimerSeconds!.Value,
            timing.Laps.Sum(lap => lap.TimerSeconds!.Value),
            0.01);

        Assert.Equal(timing.Session.Start, timing.Laps[0].Start);
        Assert.Equal(timing.Session.End, timing.Laps[^1].End);
        for (var lap = 1; lap < timing.Laps.Count; lap++)
            Assert.Equal(timing.Laps[lap - 1].End, timing.Laps[lap].Start);
    }

    [Fact]
    public void Each_lap_is_pinned_to_the_place_it_was_run()
    {
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        // Lap i ends where lap i+1 begins: one record, read at one index, so the map has no jump
        // between laps and no stretch belongs to two of them.
        for (var lap = 1; lap < timing.Laps.Count; lap++)
            Assert.Equal(timing.Laps[lap - 1].EndPosition, timing.Laps[lap].StartPosition);

        // And the pins are not all the activity's own two ends: on a loop every lap would land in
        // the same spot and nothing above would fail.
        Assert.Equal(3, timing.Laps.Select(lap => lap.StartPosition).Distinct().Count());
        Assert.Equal(3, timing.Laps.Select(lap => lap.EndPosition).Distinct().Count());

        Assert.Equal(timing.Session.StartPosition, timing.Laps[0].StartPosition);
    }

    [Fact]
    public void A_lap_boundary_between_two_records_is_cut_at_the_nearest_one()
    {
        // A boundary a fraction of a second off the records of a 1 Hz series. The boundary itself
        // is written as the athlete made it; the distance is read at the closest record, and both
        // laps read it at the same one so no metre is counted twice or lost.
        var run = SyntheticRun.Create();
        var activity = run.Original with
        {
            Laps =
            [
                new LapInterval(Start, Start.AddSeconds(300.4)),
                new LapInterval(Start.AddSeconds(300.4), run.Original.EndTime)
            ]
        };

        var result = RepairService.Repair(activity, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        Assert.Equal(2, timing.Laps.Count);

        // The span the athlete ran, to the tenth of a second the interval states.
        Assert.Equal(300.4, timing.Laps[0].ElapsedSeconds!.Value, 0.001);

        var distances = result.Records.Select(r => r.Distance).ToArray();
        Assert.Equal(distances[300] - distances[0], timing.Laps[0].DistanceMeters!.Value, 0.01);
        Assert.Equal(distances[^1] - distances[300], timing.Laps[1].DistanceMeters!.Value, 0.01);
        Assert.Equal(
            timing.Session.DistanceMeters!.Value,
            timing.Laps.Sum(lap => lap.DistanceMeters!.Value),
            1.0);
    }

    [Fact]
    public void Under_smart_recording_a_boundary_moves_by_at_most_one_record_gap()
    {
        // A watch writing a point every ten seconds, which is where the nearest-record rule costs
        // something real: a boundary between two records is attributed to one of them, so a lap's
        // distance can be out by what the athlete covered in the gap. The laps still tile the
        // activity and still sum to the total — nothing is lost — but the misattribution is
        // bounded here rather than left to be discovered on a file.
        const int gapSeconds = 10;
        var run = SyntheticRun.Create(withGpsFailure: false)
            .WithRecordsEvery(gapSeconds)
            .WithLapsEndingAt(305, 1495);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        Assert.Equal(3, timing.Laps.Count);
        Assert.Equal(
            timing.Session.DistanceMeters!.Value,
            timing.Laps.Sum(lap => lap.DistanceMeters!.Value),
            1.0);

        // Each lap's distance is within one record gap of what its own span implies at the pace
        // the repair settled on — the track's length over the activity's own time, not the
        // fixture's nominal speed, because the finish is pinned to the track (AD-8).
        var pace = result.Report.RepairedDistanceMeters / result.Report.ElapsedSeconds;
        var allowance = gapSeconds * pace;
        foreach (var lap in timing.Laps)
        {
            var implied = lap.ElapsedSeconds!.Value * pace;
            Assert.True(
                Math.Abs(lap.DistanceMeters!.Value - implied) <= allowance,
                $"a lap of {lap.ElapsedSeconds!.Value:F0} s was written as "
                    + $"{lap.DistanceMeters!.Value:F0} m against {implied:F0} m implied");
        }
    }

    [Fact]
    public void A_lap_over_a_pause_keeps_its_span_and_loses_the_paused_seconds()
    {
        // The five minutes of FR-5, inside the middle lap. The lap's elapsed span is the clock;
        // its timer time is that span minus the wait, and the laps still sum to the session.
        var run = SyntheticRun
            .CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds)
            .WithLapsEndingAt(600, 1800);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        Assert.Equal(3, timing.Laps.Count);

        Assert.Equal(600, timing.Laps[0].ElapsedSeconds!.Value, 0.001);
        Assert.Equal(600, timing.Laps[0].TimerSeconds!.Value, 0.001);

        Assert.Equal(1200, timing.Laps[1].ElapsedSeconds!.Value, 0.001);
        Assert.Equal(1200 - SyntheticRun.PauseSeconds, timing.Laps[1].TimerSeconds!.Value, 0.001);

        var lastSpan = run.Original.ElapsedSeconds - 1800;
        Assert.Equal(lastSpan, timing.Laps[2].ElapsedSeconds!.Value, 0.001);
        Assert.Equal(lastSpan, timing.Laps[2].TimerSeconds!.Value, 0.001);

        Assert.Equal(
            SyntheticRun.DurationSeconds, timing.Laps.Sum(lap => lap.TimerSeconds!.Value), 0.01);
        Assert.Equal(
            timing.Session.TimerSeconds!.Value,
            timing.Laps.Sum(lap => lap.TimerSeconds!.Value),
            0.01);
    }

    [Fact]
    public void A_lap_that_is_nothing_but_a_pause_claims_no_timer_time_and_no_distance()
    {
        // The athlete pressed lap at the light and again when it changed. That lap was standing
        // still: it earned no metres (FR-5) and no moving seconds, and saying otherwise would make
        // the laps' timer times overshoot the session's.
        var run = SyntheticRun
            .CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds)
            .WithLapsEndingAt(
                SyntheticRun.PauseStartSeconds,
                SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        var standing = timing.Laps[1];
        Assert.Equal(SyntheticRun.PauseSeconds, standing.ElapsedSeconds!.Value, 0.001);
        Assert.Equal(0, standing.TimerSeconds!.Value, 0.001);
        Assert.Equal(0, standing.DistanceMeters!.Value, 0.01);

        // Story 1.7 states no average speed at all here, where the writer used to state zero: a
        // lap with no moving second has no average speed, and dividing its nothing by its nothing
        // is a claim about an athlete who was standing at a light (AD-16).
        Assert.Null(standing.AvgSpeedMetersPerSecond);

        // The one number that proves the average is the lap's own and not the activity's.
        Assert.True(
            timing.Session.AvgSpeedMetersPerSecond!.Value > 2,
            "the session's own average speed collapsed, so this proves nothing about the lap");
    }

    [Fact]
    public void Each_laps_average_speed_is_its_own_distance_over_its_own_timer_time()
    {
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        foreach (var lap in timing.Laps)
            Assert.Equal(
                lap.DistanceMeters!.Value / lap.TimerSeconds!.Value,
                lap.AvgSpeedMetersPerSecond!.Value,
                0.01);
    }

    [Fact]
    public void Each_laps_sensor_averages_are_its_own_and_not_the_activitys()
    {
        // The athlete working harder in every lap. A writer averaging over the whole activity
        // would give all three the same figure, and that figure would be none of theirs.
        var run = SyntheticRun.Create()
            .WithLapsEndingAt(FirstLapEnd, SecondLapEnd)
            .WithSensorsPerLap();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        for (var lap = 0; lap < 3; lap++)
        {
            var heartRate = (byte)(SyntheticRun.HeartRatePerLap + SyntheticRun.LapHeartRateStep * lap);
            var cadence = (byte)(SyntheticRun.CadencePerLap + SyntheticRun.LapCadenceStep * lap);

            Assert.Equal(heartRate, timing.Laps[lap].AvgHeartRate);
            Assert.Equal(heartRate, timing.Laps[lap].MaxHeartRate);
            Assert.Equal(cadence, timing.Laps[lap].AvgCadence);
        }

        // And none of them is the whole activity's, which is the number a regression would write.
        Assert.DoesNotContain(timing.Session.AvgHeartRate, timing.Laps.Select(lap => lap.AvgHeartRate));
        Assert.DoesNotContain(timing.Session.AvgCadence, timing.Laps.Select(lap => lap.AvgCadence));
        Assert.Equal(
            (byte)(SyntheticRun.HeartRatePerLap + SyntheticRun.LapHeartRateStep * 2),
            timing.Session.MaxHeartRate);
    }

    [Fact]
    public void The_record_two_laps_share_belongs_to_the_earlier_laps_averages()
    {
        // Two adjacent laps meet on one record: the distances telescope across it, so counting
        // its heart rate in both laps would be the one value paid for twice. The laps are short
        // here on purpose — over twelve hundred records a single misplaced beat rounds away, and
        // the test would pass whichever lap owned it.
        var run = SyntheticRun.Create().WithLapsEndingAt(10, 20).WithSensorsPerLap();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        Assert.Equal(3, timing.Laps.Count);
        for (var lap = 0; lap < 3; lap++)
            Assert.Equal(
                (byte)(SyntheticRun.HeartRatePerLap + (SyntheticRun.LapHeartRateStep * lap)),
                timing.Laps[lap].AvgHeartRate);
    }

    [Fact]
    public void More_laps_than_a_fit_file_can_index_are_refused_with_a_code()
    {
        // message_index reserves its high bits, leaving twelve for the number itself. Writing
        // past that would reuse indices silently; a declared code is this project's answer to
        // input it cannot honour (AD-18).
        var refused = Assert.Throws<MalformedArgumentException>(
            () => FitActivityWriter.WriteToArray(WithLapCount(4100), utcOffsetSeconds: 0));

        Assert.Equal(DiagnosticCodes.ActivityTooManyLaps, refused.Code);
        Assert.True(DiagnosticCodes.IsDeclared(refused.Code));

        // A plausible interval session is written without complaint.
        var written = FitMessageInspector.Inspect(
            FitActivityWriter.WriteToArray(WithLapCount(40), utcOffsetSeconds: 0));
        Assert.Equal(40, written.LapCount);
    }

    // --- Nothing moves for an activity nobody lapped.

    [Fact]
    public void An_activity_without_laps_is_written_with_one_lap_and_the_very_same_numbers()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var bytes = FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0);

        var counts = FitMessageInspector.Inspect(bytes);
        var timing = FitMessageInspector.InspectTiming(bytes);

        Assert.Equal(1, counts.LapCount);
        Assert.Equal((ushort)1, timing.SessionNumLaps);
        Assert.Equal(1, result.Report.LapCount);

        var lap = Assert.Single(timing.Laps);
        Assert.Equal((ushort)0, lap.MessageIndex);
        Assert.Equal(Start, lap.Start);
        Assert.Equal(Start.AddSeconds(SyntheticRun.DurationSeconds), lap.End);
        Assert.Equal(SyntheticRun.DurationSeconds, lap.ElapsedSeconds!.Value, 0.001);
        Assert.Equal(SyntheticRun.DurationSeconds, lap.TimerSeconds!.Value, 0.001);
        Assert.Equal(timing.Session.DistanceMeters!.Value, lap.DistanceMeters!.Value, 0.01);
        Assert.Equal(
            timing.Session.AvgSpeedMetersPerSecond!.Value, lap.AvgSpeedMetersPerSecond!.Value, 0.001);
        Assert.Equal(timing.Session.AvgHeartRate, lap.AvgHeartRate);

        // And every number the suite pins is where it was before laps existed.
        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.01);
        Assert.Equal(AlignmentOptionsTests.ExpectedAnchorCount, result.Report.AnchorCount);
        Assert.Equal(AlignmentOptionsTests.ExpectedMeasuredUntil, result.Report.MeasuredUntilSeconds, 0.001);
    }

    [Fact]
    public void A_repaired_file_read_back_carries_the_laps_it_was_written_with()
    {
        // SM-2: the written file parses back, and the laps are in it rather than only in the
        // encoder's buffer.
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var reread = Read(FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        Assert.Equal(3, reread.Laps.Count);
        Assert.Equal(run.Original.LapPartition(), reread.LapPartition());
    }

    private static OriginalActivity Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return FitActivityReader.Read(stream);
    }

    /// <summary>
    /// A repaired activity with <paramref name="laps"/> laps of two seconds each, assembled
    /// rather than repaired: the point is the number of laps the writer is handed, and running an
    /// alignment over a track long enough for four thousand of them would measure something else.
    /// </summary>
    private static RepairResult WithLapCount(int laps)
    {
        // Two seconds a lap, and the activity ends where the last lap does, so the laps are the
        // partition exactly rather than leaving an unlapped tail behind them.
        var seconds = laps * 2;
        var reference = RepairService.Repair(SyntheticRun.Create().Original, SyntheticRun.Create().ReferenceTrack);

        var original = new OriginalActivity
        {
            Records = [.. Enumerable.Range(0, seconds + 1).Select(second => new ActivityRecord
            {
                Timestamp = Start.AddSeconds(second),
                Latitude = SyntheticRun.StartLatitude,
                Longitude = SyntheticRun.StartLongitude,
                Distance = second
            })],
            Sport = SportKind.Running,
            Laps = [.. Enumerable.Range(0, laps).Select(lap => new LapInterval(
                Start.AddSeconds(lap * 2), Start.AddSeconds((lap * 2) + 2)))]
        };

        IReadOnlyList<RepairedRecord> records = [.. Enumerable.Range(0, seconds + 1).Select(second => new RepairedRecord
        {
            Timestamp = Start.AddSeconds(second),
            Latitude = SyntheticRun.StartLatitude,
            Longitude = SyntheticRun.StartLongitude,
            Distance = second,
            Speed = 1
        })];

        return new RepairResult
        {
            Records = records,
            Report = reference.Report,
            Summary = ActivitySummary.Of(
                records, original, reference.Alignment, SyntheticRun.Options),
            Alignment = reference.Alignment,
            CleanedOriginal = original,
            Sport = SportKind.Running
        };
    }

    /// <summary>
    /// The one property a partition has to have: it runs from the activity's start to its end,
    /// each interval begins where the previous one ended, and every one of them has duration.
    /// </summary>
    private static void AssertTiles(OriginalActivity activity, IReadOnlyList<LapInterval> partition)
    {
        Assert.NotEmpty(partition);
        Assert.Equal(activity.StartTime, partition[0].Start);
        Assert.Equal(activity.EndTime, partition[^1].End);

        for (var lap = 0; lap < partition.Count; lap++)
        {
            Assert.True(
                partition[lap].End > partition[lap].Start,
                $"lap {lap} spans no time at all");
            if (lap > 0)
                Assert.Equal(partition[lap - 1].End, partition[lap].Start);
        }
    }
}
