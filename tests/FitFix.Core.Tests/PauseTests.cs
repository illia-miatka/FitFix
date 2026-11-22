using Dynastream.Fit;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;
using FitDateTime = Dynastream.Fit.DateTime;

namespace FitFix.Core.Tests;

/// <summary>
/// Story 1.5: a pause earns no metres. Pauses are intervals read from the Original's own
/// <c>timer</c> events (AD-7), distance is spread over moving time rather than over the clock
/// (FR-5), smoothing stops at a pause boundary (AD-21), and the written file's
/// <c>total_timer_time</c> is elapsed time minus the pauses (FR-6).
/// </summary>
public class PauseTests
{
    /// <summary>The fixtures' own clock, so a second here means what it means there.</summary>
    private static readonly System.DateTime Start = SyntheticRun.StartTime;

    // --- Reading the intervals.

    [Fact]
    public void Pauses_are_read_as_intervals_ordered_by_time()
    {
        var activity = new OriginalActivity
        {
            Records = StraightRun(seconds: 600),
            Sport = SportKind.Running,
            Pauses =
            [
                new PauseInterval(Start.AddSeconds(100), Start.AddSeconds(160)),
                new PauseInterval(Start.AddSeconds(400), Start.AddSeconds(430))
            ]
        };

        var read = Read(SyntheticFitBuilder.WriteActivity(activity));

        Assert.Equal(2, read.Pauses.Count);
        Assert.Equal(Start.AddSeconds(100), read.Pauses[0].Start);
        Assert.Equal(Start.AddSeconds(160), read.Pauses[0].End);
        Assert.Equal(Start.AddSeconds(400), read.Pauses[1].Start);
        Assert.Equal(Start.AddSeconds(430), read.Pauses[1].End);
        Assert.Equal(90, read.PausedSeconds);
    }

    [Fact]
    public void A_final_stop_that_nothing_reopens_is_not_a_pause()
    {
        // Where every activity ends: a timer start, the records, a stop_all and nothing after it.
        var bytes = SyntheticFitBuilder.WriteActivity(
            PauseFree(seconds: 300),
            [
                new SyntheticFitBuilder.TimerEvent(Start, EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(300), EventType.StopAll)
            ]);

        var read = Read(bytes);

        Assert.Empty(read.Pauses);
        Assert.Equal(0, read.PausedSeconds);
        Assert.Equal(read.ElapsedSeconds, read.MovingSecondsFromStart(read.ElapsedSeconds));
    }

    [Fact]
    public void A_stop_disable_pressed_by_the_athlete_opens_a_pause_too()
    {
        // What a real watch writes when the user presses stop and then start again: not `stop`
        // but `stop_disable_all`. Reading only the two self-paused types would leave a genuinely
        // paused file behaving exactly as it did before this story.
        var bytes = SyntheticFitBuilder.WriteActivity(
            PauseFree(seconds: 300),
            [
                new SyntheticFitBuilder.TimerEvent(Start, EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(100), EventType.StopDisableAll),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(160), EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(200), EventType.StopDisable),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(230), EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(300), EventType.StopDisableAll)
            ]);

        var read = Read(bytes);

        Assert.Equal(2, read.Pauses.Count);
        Assert.Equal(90, read.PausedSeconds);
    }

    [Fact]
    public void A_stop_the_records_outlive_pauses_to_the_last_record()
    {
        // The watch was stopped and went on writing records — the device behaviour the paused
        // fixture models. Those seconds are standing still, not the end of the activity.
        var bytes = SyntheticFitBuilder.WriteActivity(
            PauseFree(seconds: 300),
            [
                new SyntheticFitBuilder.TimerEvent(Start, EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(200), EventType.Stop)
            ]);

        var read = Read(bytes);

        var pause = Assert.Single(read.Pauses);
        Assert.Equal(Start.AddSeconds(200), pause.Start);
        Assert.Equal(Start.AddSeconds(300), pause.End);
    }

    [Fact]
    public void A_start_with_no_stop_before_it_invents_no_pause()
    {
        var bytes = SyntheticFitBuilder.WriteActivity(
            PauseFree(seconds: 300),
            [
                new SyntheticFitBuilder.TimerEvent(Start, EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(150), EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(300), EventType.StopAll)
            ]);

        Assert.Empty(Read(bytes).Pauses);
    }

    [Fact]
    public void Two_stops_before_one_start_are_one_interval()
    {
        var bytes = SyntheticFitBuilder.WriteActivity(
            PauseFree(seconds: 300),
            [
                new SyntheticFitBuilder.TimerEvent(Start, EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(100), EventType.Stop),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(120), EventType.StopAll),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(200), EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(300), EventType.StopAll)
            ]);

        var read = Read(bytes);

        // One interval from the first stop to the start that ends it — not two that overlap, and
        // not 120 s of paused time where only 100 passed.
        var pause = Assert.Single(read.Pauses);
        Assert.Equal(Start.AddSeconds(100), pause.Start);
        Assert.Equal(Start.AddSeconds(200), pause.End);
        Assert.Equal(100, read.PausedSeconds);
    }

    [Fact]
    public void A_stop_and_a_start_at_the_same_instant_are_no_pause()
    {
        var bytes = SyntheticFitBuilder.WriteActivity(
            PauseFree(seconds: 300),
            [
                new SyntheticFitBuilder.TimerEvent(Start, EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(100), EventType.Stop),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(100), EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(300), EventType.StopAll)
            ]);

        // An interval of no duration is nothing a shell should draw and nothing anyone waited
        // through, so it is not in the list at all.
        Assert.Empty(Read(bytes).Pauses);
    }

    [Fact]
    public void A_start_before_a_stop_at_the_same_instant_opens_a_pause()
    {
        // The other order of the same tie, which is the only thing that tells the two apart: the
        // start closes nothing, and the stop that follows it opens a pause the records outlive.
        var bytes = SyntheticFitBuilder.WriteActivity(
            PauseFree(seconds: 300),
            [
                new SyntheticFitBuilder.TimerEvent(Start, EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(100), EventType.Start),
                new SyntheticFitBuilder.TimerEvent(Start.AddSeconds(100), EventType.Stop)
            ]);

        var pause = Assert.Single(Read(bytes).Pauses);

        Assert.Equal(Start.AddSeconds(100), pause.Start);
        Assert.Equal(Start.AddSeconds(300), pause.End);
    }

    [Fact]
    public void A_repaired_file_read_back_carries_no_pause_of_its_own()
    {
        // The writer emits a start and a stop_all and nothing else (AD-19 and Story 1.10 own that
        // structure), so a repaired file must not come back claiming a pause.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var reread = Read(FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        Assert.Empty(reread.Pauses);
    }

    // --- The arithmetic.

    [Fact]
    public void Overlapping_and_unordered_intervals_become_one_ordered_list()
    {
        // The reader cannot produce these, but the model is the one place a pause list is put in
        // order, and both the moving-time arithmetic and the smoothing boundaries read it: an
        // overlap left raw would be paid for twice in one and split into three stretches in the
        // other, and a list out of order would lose seconds outright.
        var activity = new OriginalActivity
        {
            Records = StraightRun(seconds: 600),
            Sport = SportKind.Running,
            Pauses =
            [
                new PauseInterval(Start.AddSeconds(400), Start.AddSeconds(450)),
                new PauseInterval(Start.AddSeconds(150), Start.AddSeconds(250)),
                new PauseInterval(Start.AddSeconds(100), Start.AddSeconds(200)),
                new PauseInterval(Start.AddSeconds(160), Start.AddSeconds(180)),
                new PauseInterval(Start.AddSeconds(300), Start.AddSeconds(300))
            ]
        };

        Assert.Equal(2, activity.Pauses.Count);
        Assert.Equal(Start.AddSeconds(100), activity.Pauses[0].Start);
        Assert.Equal(Start.AddSeconds(250), activity.Pauses[0].End);
        Assert.Equal(Start.AddSeconds(400), activity.Pauses[1].Start);
        Assert.Equal(Start.AddSeconds(450), activity.Pauses[1].End);

        Assert.Equal(200, activity.PausedSeconds);
        Assert.Equal(100, activity.PausedSecondsBefore(200));
        Assert.Equal(400, activity.MovingSecondsFromStart(600));
    }

    [Fact]
    public void A_record_inside_a_merged_pause_is_still_inside_a_pause()
    {
        // The smoothing boundaries come from the same list, so what the arithmetic calls paused
        // and what immobility detection calls paused cannot disagree.
        var activity = new OriginalActivity
        {
            Records = StraightRun(seconds: 600),
            Sport = SportKind.Running,
            Pauses =
            [
                new PauseInterval(Start.AddSeconds(150), Start.AddSeconds(250)),
                new PauseInterval(Start.AddSeconds(100), Start.AddSeconds(200))
            ]
        };

        Assert.True(activity.IsPaused(Start.AddSeconds(100)));
        Assert.True(activity.IsPaused(Start.AddSeconds(220)));
        Assert.True(activity.IsPaused(Start.AddSeconds(250)));
        Assert.False(activity.IsPaused(Start.AddSeconds(99)));
        Assert.False(activity.IsPaused(Start.AddSeconds(251)));
    }

    [Fact]
    public void A_pause_wider_than_the_activity_is_clipped_to_it()
    {
        var activity = new OriginalActivity
        {
            Records = StraightRun(seconds: 600),
            Sport = SportKind.Running,
            Pauses = [new PauseInterval(Start.AddSeconds(-60), Start.AddSeconds(700))]
        };

        Assert.Equal(600, activity.PausedSeconds);
        Assert.Equal(0, activity.MovingSecondsFromStart(600));
    }

    // --- Distance over moving time.

    [Fact]
    public void Distance_stands_still_across_a_pause()
    {
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var opened = result.Records[SyntheticRun.PauseStartSeconds];
        var closed = result.Records[SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds];

        // FR-5: five minutes of standing at a light buy no distance at all. To the millimetre,
        // not to the metre: both records interpolate at the same moving second, so anything else
        // means the two measures were mixed.
        Assert.Equal(opened.Distance, closed.Distance, 0.001);
    }

    [Fact]
    public void The_pace_after_a_pause_is_the_pace_before_it()
    {
        // Holding the distance flat inside the interval is not enough: what follows the pause has
        // to advance at the athlete's own rate. Spreading distance over moving time while
        // measuring the span in elapsed seconds leaves exactly this assertion short, and the
        // forced finish then absorbs the shortfall as one enormous step.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var distances = result.Records.Select(r => r.Distance).ToArray();

        var pauseEnd = SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds;
        var before = distances[SyntheticRun.PauseStartSeconds] - distances[SyntheticRun.PauseStartSeconds - 100];
        var after = distances[pauseEnd + 100] - distances[pauseEnd];

        Assert.Equal(before, after, 100 * SyntheticRun.SpeedMetersPerSecond * 0.05);

        // And no second may gain a whole missing stretch. The ceiling is the athlete's own cap
        // (AD-10) plus the metres the chosen length holds beyond what this fixture's moving time
        // covers: the finish is pinned to that length (AD-8, AD-17), so the last seconds carry the
        // surplus. Anything more is a stretch that was interpolated short and paid for at the line.
        var surplus = result.Report.UsedTrackLengthMeters
            - SyntheticRun.DurationSeconds * SyntheticRun.SpeedMetersPerSecond;
        var biggest = 0.0;
        var at = 0;
        for (var i = 1; i < distances.Length; i++)
        {
            if (distances[i] - distances[i - 1] <= biggest)
                continue;

            biggest = distances[i] - distances[i - 1];
            at = i;
        }

        Assert.True(
            biggest <= SyntheticRun.Options.MaxSpeedMetersPerSecond + surplus,
            $"record {at} gained {biggest:F1} m in one second");
    }

    [Fact]
    public void A_pause_read_from_a_file_reaches_the_repair()
    {
        // End to end through the composition root (AD-10): the timer events in the file are the
        // only thing that says the athlete stood still, so if they stop at the reader the
        // distance below grows.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        using var original = new MemoryStream(SyntheticFitBuilder.WriteActivity(run.Original));
        using var track = new MemoryStream(
            SyntheticFitBuilder.WriteTrack(run.TrackPoints, run.Original.StartTime));

        var result = RepairPipeline.Repair(original, track);

        Assert.Equal(SyntheticRun.PauseSeconds, result.CleanedOriginal.PausedSeconds);
        Assert.Equal(
            result.Records[SyntheticRun.PauseStartSeconds].Distance,
            result.Records[SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds].Distance,
            1.0);
    }

    [Fact]
    public void Smoothing_does_not_carry_movement_into_a_pause()
    {
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var opened = result.Records[SyntheticRun.PauseStartSeconds].Distance;
        var inside = result.Records
            .Skip(SyntheticRun.PauseStartSeconds + 1)
            .Take(SyntheticRun.PauseSeconds - 1)
            .ToArray();

        // AD-21: the window stops at the boundary, so a record inside the pause holds exactly the
        // distance of the record that opened it — the moving records on either side reach none of
        // them, however wide the window is.
        Assert.Equal(SyntheticRun.PauseSeconds - 1, inside.Length);
        Assert.All(inside, record => Assert.Equal(opened, record.Distance, 0.001));
    }

    [Fact]
    public void The_same_records_without_the_intervals_let_the_pause_earn_metres()
    {
        // The counter-case that says what is doing the work: the very same records, with the
        // pauses struck out of the model. Nothing about the series betrays the pause — only the
        // timer events do (AD-7) — so the distance is spread over the clock and the wait is paid.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var blind = run.Original with { Pauses = [] };

        var result = RepairService.Repair(blind, run.ReferenceTrack);
        var opened = result.Records[SyntheticRun.PauseStartSeconds].Distance;
        var closed = result.Records[SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds].Distance;

        Assert.True(
            closed - opened > 500,
            $"the pause earned only {closed - opened:F0} m, so this is no longer the broken case");
    }

    [Fact]
    public void Standing_still_through_a_pause_is_not_a_frozen_gps()
    {
        // A watch that keeps recording while the athlete waits five minutes at a light is
        // immobile for a reason the file states outright (AD-7). Reading it as a stalled GPS
        // would skip every anchor after the pause and leave most of the run estimated.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Null(GpsStallDetector.Detect(run.Original, SyntheticRun.Options));
        Assert.Null(result.Report.GpsStallAtSeconds);

        // Not because the cadence happens to be zero there: a watch that reports no cadence at
        // all must be read the same way, which is what skipping the paused records does.
        var silent = run.Original with
        {
            Records = [.. run.Original.Records.Select(r =>
                run.Original.IsPaused(r.Timestamp) ? r with { Cadence = null } : r)]
        };
        Assert.Null(GpsStallDetector.Detect(silent, SyntheticRun.Options));

        Assert.DoesNotContain(DiagnosticCodes.GpsStallDetected, result.Report.Warnings);
        Assert.True(
            result.Alignment.MeasuredUntilSeconds > SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds,
            $"the measured zone ended at {result.Alignment.MeasuredUntilSeconds} s, before the pause was over");
    }

    [Fact]
    public void Monotonicity_survives_a_pause()
    {
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        for (var i = 1; i < result.Records.Count; i++)
            Assert.True(
                result.Records[i].Distance >= result.Records[i - 1].Distance,
                $"distance fell at record {i}: {result.Records[i - 1].Distance} → {result.Records[i].Distance}");

        // AD-8: and it still ends exactly on the chosen track length.
        Assert.Equal(track.Length, result.Records[^1].Distance, 0.001);
    }

    [Fact]
    public void A_pause_at_the_very_start_earns_no_distance()
    {
        // The timer was stopped before the watch wrote its first record and started a minute in:
        // the athlete waited at the line. Nothing may move until the pause ends.
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        var activity = WaitingStart(track, waitSeconds: 60, thenRunningSeconds: 540);

        // The half-minute before the first record is not part of the activity and is not paid
        // for: the pause is worth the minute that falls inside it, and the clock starts moving
        // the moment it ends.
        Assert.Equal(60, activity.PausedSeconds);
        Assert.Equal(0, activity.MovingSecondsFromStart(60));
        Assert.Equal(30, activity.MovingSecondsFromStart(90));

        var result = RepairService.Repair(activity, track);
        var waited = result.Records.Take(61).ToArray();

        Assert.All(waited, record => Assert.Equal(0, record.Distance, 0.001));
        Assert.Equal(track.At(0).Position.Latitude, result.Records[0].Latitude, 1e-9);
        Assert.Equal(track.At(0).Position.Longitude, result.Records[0].Longitude, 1e-9);
    }

    // --- The written file.

    [Fact]
    public void Timer_time_is_elapsed_time_minus_the_pause()
    {
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        var elapsed = (float)run.Original.ElapsedSeconds;
        var timer = elapsed - SyntheticRun.PauseSeconds;

        // FR-6: elapsed time is the Original's own span, timer time excludes the five minutes.
        var lap = Assert.Single(timing.Laps);
        Assert.Equal(elapsed, lap.ElapsedSeconds!.Value, 0.001);
        Assert.Equal(elapsed, timing.Session.ElapsedSeconds!.Value, 0.001);
        Assert.Equal(timer, lap.TimerSeconds!.Value, 0.001);
        Assert.Equal(timer, timing.Session.TimerSeconds!.Value, 0.001);
        Assert.Equal(timer, timing.ActivityTimerSeconds!.Value, 0.001);

        // And the average speed is derived from that same timer time, not from the clock: a file
        // claiming 36 minutes beside a 41-minute average contradicts itself.
        var expectedSpeed = result.Records[^1].Distance / timer;
        Assert.Equal(expectedSpeed, lap.AvgSpeedMetersPerSecond!.Value, 0.01);
        Assert.Equal(expectedSpeed, timing.Session.AvgSpeedMetersPerSecond!.Value, 0.01);

        // The report says the same thing, so the CLI cannot print a pace over the whole clock
        // beside a file that states a shorter one.
        Assert.Equal(SyntheticRun.PauseSeconds, result.Report.PausedSeconds);
        Assert.Equal(SyntheticRun.DurationSeconds, result.Report.MovingSeconds);
    }

    [Fact]
    public void Pauses_that_swallow_the_whole_activity_are_ignored_for_the_timer_time()
    {
        // An event stream that says the athlete never moved, beside a full distance: a timer time
        // of zero there reads as an infinite pace, so the pauses are the thing not to be trusted.
        // Struck out before the repair computes a single moving second (AD-7), so the whole
        // pipeline survives such a file rather than only the writer: spread over a measure that
        // never advances, the distance would leave every record at the start line.
        var run = SyntheticRun.Create();
        var nonsense = run.Original with
        {
            Pauses = [new PauseInterval(run.Original.StartTime, run.Original.EndTime)]
        };

        var result = RepairService.Repair(nonsense, run.ReferenceTrack);
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        var lap = Assert.Single(timing.Laps);
        Assert.Equal(SyntheticRun.DurationSeconds, lap.TimerSeconds!.Value, 0.001);
        Assert.Equal(SyntheticRun.DurationSeconds, timing.Session.TimerSeconds!.Value, 0.001);
        Assert.Equal(SyntheticRun.DurationSeconds, timing.ActivityTimerSeconds!.Value, 0.001);

        // And the report says the same, from the same activity: a moving time taken from the
        // summary beside paused seconds taken from the list it disbelieved would have the two add
        // up past the elapsed time, and the CLI would print a forty-minute pause inside a
        // forty-minute run.
        Assert.Equal(0, result.Report.PausedSeconds);
        Assert.Equal(SyntheticRun.DurationSeconds, result.Report.MovingSeconds, 0.001);
        Assert.Empty(result.CleanedOriginal.Pauses);

        // The distance still reaches the end of the track, which is what a measure that never
        // advanced would have made impossible (AD-8).
        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.01);
    }

    [Fact]
    public void Timer_time_is_elapsed_time_when_nothing_was_paused()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        var lap = Assert.Single(timing.Laps);
        Assert.Equal(SyntheticRun.DurationSeconds, lap.TimerSeconds!.Value, 0.001);
        Assert.Equal(SyntheticRun.DurationSeconds, timing.Session.TimerSeconds!.Value, 0.001);
        Assert.Equal(SyntheticRun.DurationSeconds, timing.ActivityTimerSeconds!.Value, 0.001);
        Assert.Equal(0, result.Report.PausedSeconds);
    }

    // --- Nothing moves for an activity nobody paused.

    [Fact]
    public void An_activity_without_pauses_repairs_to_the_very_same_numbers()
    {
        var run = SyntheticRun.Create();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        // Moving time is elapsed time here, so the conversion is the identity and every number
        // the suite pins has to be where it was before pauses existed.
        Assert.Empty(run.Original.Pauses);
        Assert.Equal(0, run.Original.PausedSeconds);
        Assert.Equal(
            run.Original.ElapsedSeconds,
            run.Original.MovingSecondsFromStart(run.Original.ElapsedSeconds));
        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.01);
        Assert.Equal(AlignmentOptionsTests.ExpectedAnchorCount, result.Report.AnchorCount);
        Assert.Equal(AlignmentOptionsTests.ExpectedMeasuredUntil, result.Alignment.MeasuredUntilSeconds, 0.001);
    }

    private static OriginalActivity Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return FitActivityReader.Read(stream);
    }

    private static OriginalActivity PauseFree(int seconds) =>
        new() { Records = StraightRun(seconds), Sport = SportKind.Running };

    /// <summary>A straight run east at the fixture's own speed, one record a second.</summary>
    private static List<ActivityRecord> StraightRun(int seconds)
    {
        var records = new List<ActivityRecord>(seconds + 1);
        for (var second = 0; second <= seconds; second++)
        {
            var position = SyntheticRun.Offset(second * SyntheticRun.SpeedMetersPerSecond, 0);
            records.Add(new ActivityRecord
            {
                Timestamp = Start.AddSeconds(second),
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                Distance = second * SyntheticRun.SpeedMetersPerSecond,
                HeartRate = 150,
                Cadence = 85
            });
        }

        return records;
    }

    /// <summary>
    /// An athlete who starts the watch, waits <paramref name="waitSeconds"/> at the line and then
    /// runs along the track — with the timer stopped half a minute before the first record, which
    /// is where a watch puts it when the activity is created paused.
    /// </summary>
    private static OriginalActivity WaitingStart(ReferenceTrack track, int waitSeconds, int thenRunningSeconds)
    {
        var records = new List<ActivityRecord>(waitSeconds + thenRunningSeconds + 1);

        for (var second = 0; second <= waitSeconds + thenRunningSeconds; second++)
        {
            var moving = Math.Max(0, second - waitSeconds);
            var at = track.At(moving * SyntheticRun.SpeedMetersPerSecond);
            records.Add(new ActivityRecord
            {
                Timestamp = Start.AddSeconds(second),
                Latitude = at.Position.Latitude,
                Longitude = at.Position.Longitude,
                Altitude = at.Altitude,
                Distance = moving * SyntheticRun.SpeedMetersPerSecond,
                HeartRate = 150,
                Cadence = (byte)(second < waitSeconds ? 0 : 85)
            });
        }

        return new OriginalActivity
        {
            Records = records,
            Sport = SportKind.Running,
            Pauses = [new PauseInterval(Start.AddSeconds(-30), Start.AddSeconds(waitSeconds))]
        };
    }
}
