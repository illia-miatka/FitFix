using System.Text.RegularExpressions;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// Story 1.7: every summary number has one owner, and the owner is the core (AD-16). The writer
/// transcribes <see cref="ActivitySummary"/> and performs no arithmetic over a series; the averages
/// are taken over moving time and over the records the athlete was moving for (FR-5, AD-7); and the
/// report carries the finished numbers rather than deriving them on the way out (AD-14).
/// </summary>
public class SummaryTests
{
    /// <summary>The lap boundaries of the three-lap fixture, s from the start.</summary>
    private const int FirstLapEnd = 300;

    /// <inheritdoc cref="FirstLapEnd"/>
    private const int SecondLapEnd = 1500;

    /// <summary>The fixtures' own clock, so a second here means what it means there.</summary>
    private static readonly DateTime Start = SyntheticRun.StartTime;

    // --- The file states the summary.

    [Fact]
    public void Every_value_the_written_file_states_is_the_summarys_own()
    {
        // The invariant of this story is an equality between two artifacts: the summary the core
        // handed over and the file the writer produced from it. Read the file back and every
        // number in its session and its laps has to be the summary's, field by field — an average
        // the writer worked out for itself would show up here as a difference.
        var run = SyntheticRun.Create()
            .WithLapsEndingAt(FirstLapEnd, SecondLapEnd)
            .WithSensorsPerLap();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        AssertStates(result.Summary.Total, timing.Session, "session");

        Assert.Equal(result.Summary.Laps.Count, timing.Laps.Count);
        for (var lap = 0; lap < timing.Laps.Count; lap++)
            AssertStates(result.Summary.Laps[lap], timing.Laps[lap], $"lap {lap}");
    }

    [Fact]
    public void The_writer_holds_no_arithmetic_over_a_series()
    {
        // The equality above says the file agrees with the summary today; this says the writer has
        // no way of disagreeing tomorrow. The pattern covers the story's own verification grep and
        // the shapes that would slip past it: a hand-rolled accumulator, and any division at all.
        var source = WriterSource();

        Assert.True(
            source is not null,
            $"the writer's source was not found above {AppContext.BaseDirectory}, so this test "
                + "checked nothing; point it at the source tree rather than letting it pass");

        var offenders = File.ReadAllLines(source!)
            .Select((line, number) => (Code: BeforeAComment(line), Number: number + 1))
            .Where(entry => Regex.IsMatch(entry.Code, @"\b(Sum|Average|Aggregate|Max|Min)\s*\(|\+=|-=|/"))
            .Select(entry => $"{entry.Number}: {entry.Code.Trim()}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"arithmetic over a series survives in the writer:\n{string.Join("\n", offenders)}");
    }

    [Fact]
    public void The_report_agrees_with_the_file_it_was_written_beside()
    {
        // The pace the shell prints has to be the pace the file states: distance over the moving
        // seconds in its own session message. The report used to divide by elapsed time while the
        // file divided by moving time, and the two contradicted each other in front of the user.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var session = FitMessageInspector
            .InspectTiming(FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0))
            .Session;

        var statedPace = session.TimerSeconds!.Value / (session.DistanceMeters!.Value / 1000.0);

        Assert.NotNull(result.Report.RepairedPaceSecondsPerKm);
        Assert.Equal(statedPace, result.Report.RepairedPaceSecondsPerKm!.Value, 0.01);
        Assert.Equal(session.ElapsedSeconds!.Value, result.Report.ElapsedSeconds, 0.001);
        Assert.Equal(session.TimerSeconds!.Value, result.Report.MovingSeconds, 0.001);
        Assert.Equal(session.DistanceMeters!.Value, result.Report.RepairedDistanceMeters, 0.01);
        Assert.Equal(session.ElevationGainMeters!.Value, Math.Round(result.Report.ElevationGainMeters!.Value));

        // Elapsed time and the pauses inside it add up to what passed, which is what stops the
        // shell printing a moving time of forty minutes beside forty minutes of pause.
        Assert.Equal(
            result.Report.ElapsedSeconds,
            result.Report.MovingSeconds + result.Report.PausedSeconds,
            0.001);

        // The measured fraction is a settled number rather than a division a second reader could
        // do differently (AD-14), and it is the measured zones added up over the elapsed time
        // rather than a prefix of it (FR-8).
        var measuredSeconds = result.Report.Zones
            .Where(zone => zone.Kind == ZoneKind.Measured)
            .Sum(zone => zone.ToSeconds - zone.FromSeconds);
        Assert.Equal(
            measuredSeconds / result.Report.ElapsedSeconds,
            result.Report.MeasuredFraction,
            1e-9);
    }

    [Fact]
    public void The_before_pace_is_the_devices_own_distance_over_its_own_moving_time()
    {
        // The headline number of the whole product is "what the repair changed", and it only means
        // something if the "before" is what the watch itself claimed. Build it from the repaired
        // distance and the two paces collapse into one, with nothing in the suite to notice.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var deviceDistance = run.Original.RecordedDistance!.Value;
        var ownMovingSeconds = run.Original.MovingSecondsFromStart(run.Original.ElapsedSeconds);

        // The paused fixture tells the two times apart: its moving time is five minutes short of
        // its elapsed time, so a "before" pace over the clock would be a different number.
        Assert.NotEqual(run.Original.ElapsedSeconds, ownMovingSeconds);
        Assert.Equal(
            ownMovingSeconds / (deviceDistance / 1000.0),
            result.Report.OriginalPaceSecondsPerKm!.Value,
            0.001);

        // And on the run whose GPS froze, the two paces are far apart — which is the headline the
        // whole product exists to show. Building the "before" from the repaired distance would
        // collapse them onto one number with nothing in the suite to notice.
        var broken = SyntheticRun.Create();
        var repaired = RepairService.Repair(broken.Original, broken.ReferenceTrack);

        Assert.True(
            repaired.Report.OriginalPaceSecondsPerKm!.Value
                > repaired.Report.RepairedPaceSecondsPerKm!.Value + 60,
            $"before {repaired.Report.OriginalPaceSecondsPerKm:F0} s/km against after "
                + $"{repaired.Report.RepairedPaceSecondsPerKm:F0} s/km: the two paces hardly differ");

        // An original whose device recorded no distance at all states no "before" pace rather
        // than an infinite one.
        var withoutDistance = run.Original with
        {
            Records = [.. run.Original.Records.Select(record => record with { Distance = null })]
        };
        Assert.Null(RepairService.Repair(withoutDistance, run.ReferenceTrack).Report.OriginalPaceSecondsPerKm);
    }

    // --- A pause dilutes nothing, and nothing moves inside one.

    [Fact]
    public void A_pause_does_not_dilute_the_sensor_averages()
    {
        var run = SyntheticRun
            .CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds)
            .WithRestingHeartRateWhilePaused();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var moving = run.Original.Records
            .Where(record => !run.Original.IsPaused(record.Timestamp))
            .ToArray();

        var movingHeartRate = (byte)Math.Round(moving.Average(record => (double)record.HeartRate!.Value));
        var everyHeartRate = (byte)Math.Round(run.Original.Records.Average(record => (double)record.HeartRate!.Value));

        Assert.Equal(movingHeartRate, result.Summary.Total.AverageHeartRate);
        Assert.NotEqual(everyHeartRate, result.Summary.Total.AverageHeartRate);

        // Cadence the same way: the watch writes zero while the athlete stands, and those records
        // describe standing rather than a cadence of eighty-five dragged down.
        Assert.Equal(
            (byte)Math.Round(moving.Average(record => (double)record.Cadence!.Value)),
            result.Summary.Total.AverageCadence);
        Assert.NotEqual(
            (byte)Math.Round(run.Original.Records.Average(record => (double)record.Cadence!.Value)),
            result.Summary.Total.AverageCadence);

        // The denominator moved too: the average speed is over the moving seconds, so it is the
        // athlete's own pace rather than the pace plus five minutes of waiting.
        Assert.Equal(
            SyntheticRun.SpeedMetersPerSecond,
            result.Summary.Total.AverageSpeedMetersPerSecond!.Value,
            SyntheticRun.SpeedMetersPerSecond * 0.05);
    }

    [Fact]
    public void Every_record_a_pause_covers_states_no_speed()
    {
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var paused = result.Records
            .Where(record => result.CleanedOriginal.IsPaused(record.Timestamp))
            .ToArray();

        // Every second of the wait, not only the middle of it. A window that straddles a boundary
        // holds a full window of metres and a sliver of moving time, so the records just inside a
        // pause used to read faster than the athlete ever ran — Strava would show them sprinting
        // while standing at the light.
        Assert.Equal(SyntheticRun.PauseSeconds + 1, paused.Length);
        Assert.All(paused, record => Assert.Equal(0, record.Speed, 1e-9));

        // And the moving records beside the wait keep the athlete's own pace: neither diluted by
        // the seconds that passed nor inflated above what they ran at.
        var pauseEnd = SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds;
        var window = SyntheticRun.Options.SpeedWindowHalfWidthRecords;
        var beside = Enumerable.Range(SyntheticRun.PauseStartSeconds - window, window)
            .Concat(Enumerable.Range(pauseEnd + 1, window));

        foreach (var index in beside)
        {
            Assert.Equal(
                SyntheticRun.SpeedMetersPerSecond,
                result.Records[index].Speed,
                SyntheticRun.SpeedMetersPerSecond * 0.1);
        }
    }

    [Fact]
    public void A_lap_that_is_entirely_a_pause_invents_nothing()
    {
        // The athlete pressed lap at the light and again when it changed, so the middle lap is the
        // wait itself (Story 1.6). It covered nothing, moved for no second, and has no average of
        // any kind — not a zero, which would be a claim about an athlete who was standing still.
        var run = SyntheticRun
            .CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds)
            .WithLapsEndingAt(
                SyntheticRun.PauseStartSeconds,
                SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var standing = result.Summary.Laps[1];

        Assert.Equal(SyntheticRun.PauseSeconds, standing.ElapsedSeconds, 0.001);
        Assert.Equal(0, standing.MovingSeconds, 0.001);
        Assert.Equal(0, standing.DistanceMeters, 0.01);
        Assert.Null(standing.AverageSpeedMetersPerSecond);
        Assert.Null(standing.MaximumSpeedMetersPerSecond);
        Assert.Null(standing.PaceSecondsPerKilometer);
        Assert.Null(standing.AverageHeartRate);
        Assert.Null(standing.MaximumHeartRate);
        Assert.Null(standing.AverageCadence);
        Assert.Null(standing.MaximumCadence);

        // The file says the same, field for field — including the fields it leaves out.
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));
        AssertStates(standing, timing.Laps[1], "the standing lap");
    }

    // --- The maximum speed.

    [Fact]
    public void The_maximum_speed_never_exceeds_the_sports_cap()
    {
        // A record faster than the sport can go is a defect upstream rather than the athlete's
        // fastest second — and a value over 65.535 m/s would wrap the file's own `uint16`. The cap
        // comes from the options (AD-10).
        var cap = SyntheticRun.Options.MaxSpeedMetersPerSecond;

        var sprint = Hand(
            seconds: 20,
            speed: second => second == 10 ? 100.0 : 3.0,
            altitude: _ => null);
        var summary = ActivitySummary.Of(sprint.Records, sprint.Original, NoAnchors, SyntheticRun.Options);

        // Not a tautology: the series itself holds a record the cap excludes.
        Assert.True(sprint.Records.Max(record => record.Speed) > cap);
        Assert.Equal(3.0, summary.Total.MaximumSpeedMetersPerSecond!.Value, 1e-9);

        // And on a real repair there is no such record left to keep out. Until story 1.12 the
        // finish was pinned to the chosen track length and paid the whole shortfall at the last
        // record, which ramped the final seconds far past anything the athlete ran; the distance
        // is a sum now, so nothing is owed at the line (AD-26). The filter above still stands for
        // a defective series, and the summary of an honest one states the series' own maximum.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var fastest = result.Records.Max(record => record.Speed);

        Assert.True(fastest <= cap, $"a repaired second reached {fastest:F2} m/s past the {cap} m/s cap");
        Assert.Equal(fastest, result.Summary.Total.MaximumSpeedMetersPerSecond!.Value, 1e-9);
    }

    // --- The climb.

    [Fact]
    public void Elevation_gain_is_the_sum_of_the_rises_and_not_the_net_change()
    {
        // The fixture's route climbs twenty metres to the far side of the loop and comes back
        // down. The net change is nothing; the climb is twenty, the descent is twenty, and neither
        // is subtracted from the other.
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var altitudes = result.Records.Select(record => record.Altitude!.Value).ToArray();
        var hill = altitudes.Max() - altitudes[0];

        Assert.Equal(hill, result.Summary.Total.ElevationGainMeters!.Value, 0.5);
        Assert.Equal(20, result.Summary.Total.ElevationGainMeters!.Value, 1.0);
        Assert.Equal(20, result.Summary.Total.ElevationLossMeters!.Value, 1.0);

        // The descent is real, so a net change would have been close to nothing — which is the
        // number this assertion exists to tell the gain from.
        Assert.Equal(0, altitudes[^1] - altitudes[0], 1.0);
    }

    [Fact]
    public void A_track_without_altitude_states_no_climb_rather_than_a_flat_zero()
    {
        var run = SyntheticRun.Create();
        var flat = ReferenceTrack.FromPoints(
            [.. run.TrackPoints.Select(point => new TrackPoint(point.Position, null))],
            SyntheticRun.Options);
        var original = run.Original with
        {
            Records = [.. run.Original.Records.Select(record => record with { Altitude = null })]
        };

        var result = RepairService.Repair(original, flat);

        Assert.All(result.Records, record => Assert.Null(record.Altitude));
        Assert.Null(result.Summary.Total.ElevationGainMeters);
        Assert.Null(result.Summary.Total.ElevationLossMeters);
        Assert.Null(result.Report.ElevationGainMeters);
        Assert.Null(result.Report.ElevationLossMeters);

        // And the file carries no `total_ascent` at all, rather than a flat zero the athlete would
        // read as a measurement of flat ground.
        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));
        Assert.Null(timing.Session.ElevationGainMeters);
        Assert.Null(timing.Session.ElevationLossMeters);
        Assert.All(timing.Laps, lap => Assert.Null(lap.ElevationGainMeters));
    }

    // --- The laps add up to the activity.

    [Fact]
    public void The_laps_sum_to_the_activity()
    {
        var run = SyntheticRun.Create().WithLapsEndingAt(FirstLapEnd, SecondLapEnd);
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var summary = result.Summary;

        Assert.Equal(3, summary.Laps.Count);
        Assert.Equal(summary.Total.DistanceMeters, summary.Laps.Sum(lap => lap.DistanceMeters), 1.0);
        Assert.Equal(summary.Total.MovingSeconds, summary.Laps.Sum(lap => lap.MovingSeconds), 1.0);
        Assert.Equal(summary.Total.ElapsedSeconds, summary.Laps.Sum(lap => lap.ElapsedSeconds), 1.0);
        Assert.Equal(
            summary.Total.ElevationGainMeters!.Value,
            summary.Laps.Sum(lap => lap.ElevationGainMeters!.Value),
            0.001);

        // The laps tile the activity: each begins on the record the previous one ended on.
        Assert.Equal(0, summary.Laps[0].FirstRecord);
        Assert.Equal(result.Records.Count - 1, summary.Laps[^1].LastRecord);
        for (var lap = 1; lap < summary.Laps.Count; lap++)
            Assert.Equal(summary.Laps[lap - 1].LastRecord, summary.Laps[lap].FirstRecord);
    }

    [Fact]
    public void A_lap_boundary_without_an_altitude_loses_no_climb()
    {
        // A boundary record carrying no altitude: the lap that ends there cannot count the rise
        // across it, and the lap that begins there must — otherwise those metres are climbed by
        // nobody and the laps stop summing to the activity.
        var pair = Hand(
            seconds: 20,
            speed: _ => 3.0,
            altitude: second => second == 10 ? null : second,
            laps: [new LapInterval(Start, Start.AddSeconds(10)), new LapInterval(Start.AddSeconds(10), Start.AddSeconds(20))]);

        var summary = ActivitySummary.Of(pair.Records, pair.Original, NoAnchors, SyntheticRun.Options);

        Assert.Equal(2, summary.Laps.Count);
        Assert.Equal(20, summary.Total.ElevationGainMeters!.Value, 1e-9);
        Assert.Equal(
            summary.Total.ElevationGainMeters!.Value,
            summary.Laps.Sum(lap => lap.ElevationGainMeters!.Value),
            1e-9);
    }

    // --- An activity with nothing to average.

    [Fact]
    public void An_activity_without_sensors_states_no_averages_at_all()
    {
        // A watch with no strap, no power meter and no footpod. Zero is a measurement and absence
        // is not, so the summary and the file both say nothing rather than nothing-per-hour.
        var run = SyntheticRun.Create(withGpsFailure: false);
        var bare = run.Original with
        {
            Records = [.. run.Original.Records.Select(record => record with
            {
                HeartRate = null,
                Power = null,
                Cadence = null
            })]
        };

        var result = RepairService.Repair(bare, run.ReferenceTrack);
        var summary = result.Summary.Total;

        Assert.Null(summary.AverageHeartRate);
        Assert.Null(summary.MaximumHeartRate);
        Assert.Null(summary.AveragePower);
        Assert.Null(summary.MaximumPower);
        Assert.Null(summary.AverageCadence);
        Assert.Null(summary.MaximumCadence);

        // The distance and the durations are still there: an activity without sensors is still an
        // activity.
        Assert.NotNull(summary.AverageSpeedMetersPerSecond);

        var timing = FitMessageInspector.InspectTiming(
            FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0));

        Assert.Null(timing.Session.AvgHeartRate);
        Assert.Null(timing.Session.MaxHeartRate);
        Assert.Null(timing.Session.AvgPower);
        Assert.Null(timing.Session.MaxPower);
        Assert.Null(timing.Session.AvgCadence);
        Assert.Null(timing.Session.MaxCadence);
        AssertStates(result.Summary.Total, timing.Session, "session");
    }

    // --- A series that is not the activity's.

    [Fact]
    public void A_series_that_is_not_the_activitys_is_refused_with_a_code()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        // Every summary number is read at a record index, so a series of another length would
        // quote one stretch's heart rate against another's records (AD-18).
        var mismatched = Assert.Throws<MalformedArgumentException>(() => ActivitySummary.Of(
            [.. result.Records.Take(5)], result.CleanedOriginal, result.Alignment, SyntheticRun.Options));

        Assert.Equal(DiagnosticCodes.SummarySeriesMismatch, mismatched.Code);
        Assert.True(DiagnosticCodes.IsDeclared(mismatched.Code));

        var empty = Assert.Throws<MalformedArgumentException>(() => ActivitySummary.Of(
            [], result.CleanedOriginal, result.Alignment, SyntheticRun.Options));

        Assert.Equal(DiagnosticCodes.SummaryWithoutRecords, empty.Code);
        Assert.True(DiagnosticCodes.IsDeclared(empty.Code));
    }

    [Fact]
    public void The_writer_refuses_a_summary_that_cannot_describe_the_series()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        // An activity nobody lapped is one lap covering everything (AD-24), so no lap at all is a
        // file with no `lap` message and `num_laps = 0`, which Strava may refuse (AD-19).
        var lapless = Assert.Throws<MalformedArgumentException>(() => FitActivityWriter.WriteToArray(
            Carrying(result, result.Summary with { Laps = [] }), utcOffsetSeconds: 0));

        Assert.Equal(DiagnosticCodes.SummaryWithoutLaps, lapless.Code);
        Assert.True(DiagnosticCodes.IsDeclared(lapless.Code));

        // A boundary index past the series: a declared refusal rather than an index out of range
        // nobody can report to the user (AD-18).
        var beyond = Assert.Throws<MalformedArgumentException>(() => FitActivityWriter.WriteToArray(
            Carrying(
                result,
                result.Summary with
                {
                    Laps = [result.Summary.Laps[0] with { LastRecord = result.Records.Count }]
                }),
            utcOffsetSeconds: 0));

        Assert.Equal(DiagnosticCodes.SummarySeriesMismatch, beyond.Code);
        Assert.True(DiagnosticCodes.IsDeclared(beyond.Code));
    }

    // --- Helpers.

    /// <summary>A hand-built repaired series and the Original it was measured from.</summary>
    private sealed record HandBuilt(IReadOnlyList<RepairedRecord> Records, OriginalActivity Original);

    /// <summary>An alignment that measured nothing, for a series assembled rather than repaired.</summary>
    private static Alignment NoAnchors =>
        new() { Anchors = [], Zones = [], MeasuredUntilSeconds = 0, MeasuredDistanceMeters = 0, Warnings = [] };

    /// <summary>
    /// A series of <paramref name="seconds"/> + 1 records at 1 Hz, with the speed and the altitude
    /// of each second stated outright. Assembled rather than repaired, because the cases below are
    /// about what the summary makes of a series and not about how the series came to be.
    /// </summary>
    private static HandBuilt Hand(
        int seconds,
        Func<int, double> speed,
        Func<int, double?> altitude,
        IReadOnlyList<LapInterval> laps)
    {
        var records = Enumerable.Range(0, seconds + 1).Select(second => new RepairedRecord
        {
            Timestamp = Start.AddSeconds(second),
            Latitude = SyntheticRun.StartLatitude,
            Longitude = SyntheticRun.StartLongitude,
            Altitude = altitude(second),
            Distance = second * SyntheticRun.SpeedMetersPerSecond,
            Speed = speed(second)
        }).ToArray();

        var original = new OriginalActivity
        {
            Records = [.. records.Select(record => new ActivityRecord
            {
                Timestamp = record.Timestamp,
                Latitude = record.Latitude,
                Longitude = record.Longitude,
                Distance = record.Distance
            })],
            Sport = SportKind.Running,
            Laps = laps
        };

        return new HandBuilt(records, original);
    }

    /// <inheritdoc cref="Hand(int, Func{int, double}, Func{int, double?}, IReadOnlyList{LapInterval})"/>
    private static HandBuilt Hand(int seconds, Func<int, double> speed, Func<int, double?> altitude) =>
        Hand(seconds, speed, altitude, []);

    /// <summary>The same repaired activity carrying another summary, to hand the writer.</summary>
    private static RepairResult Carrying(RepairResult result, ActivitySummary summary) => new()
    {
        Records = result.Records,
        Report = result.Report,
        Summary = summary,
        Alignment = result.Alignment,
        CleanedOriginal = result.CleanedOriginal,
        Sport = result.Sport
    };

    /// <summary>The code of a line, with anything from its first <c>//</c> onwards dropped.</summary>
    private static string BeforeAComment(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    /// <summary>
    /// Every field of one written summary message against the summary it was transcribed from.
    /// <paramref name="what"/> names the message, so a failure says which lap disagreed.
    /// </summary>
    private static void AssertStates(
        IntervalSummary summary,
        FitMessageInspector.Summary written,
        string what)
    {
        Assert.Equal(summary.Start, written.Start);
        Assert.Equal(summary.End, written.End);

        AssertStates(summary.ElapsedSeconds, written.ElapsedSeconds, 0.001, $"{what} elapsed time");
        AssertStates(summary.MovingSeconds, written.TimerSeconds, 0.001, $"{what} timer time");
        AssertStates(summary.DistanceMeters, written.DistanceMeters, 0.01, $"{what} distance");
        AssertStates(
            summary.AverageSpeedMetersPerSecond, written.AvgSpeedMetersPerSecond, 0.001, $"{what} average speed");
        AssertStates(
            summary.MaximumSpeedMetersPerSecond, written.MaxSpeedMetersPerSecond, 0.001, $"{what} maximum speed");

        Assert.Equal(summary.AverageHeartRate, written.AvgHeartRate);
        Assert.Equal(summary.MaximumHeartRate, written.MaxHeartRate);
        Assert.Equal(summary.AveragePower, written.AvgPower);
        Assert.Equal(summary.MaximumPower, written.MaxPower);
        Assert.Equal(summary.AverageCadence, written.AvgCadence);
        Assert.Equal(summary.MaximumCadence, written.MaxCadence);

        // total_ascent and total_descent are stated in whole metres, which is the only conversion
        // the writer makes — and absent where the summary states nothing.
        AssertWholeMetres(summary.ElevationGainMeters, written.ElevationGainMeters, $"{what} climb");
        AssertWholeMetres(summary.ElevationLossMeters, written.ElevationLossMeters, $"{what} descent");
    }

    /// <summary>
    /// One nullable number against the field it was written to: absent against absent, or equal
    /// within <paramref name="tolerance"/> once the <c>float</c> of the FIT field is accounted for.
    /// </summary>
    private static void AssertStates(double? expected, float? written, double tolerance, string what)
    {
        if (expected is null)
        {
            Assert.True(written is null, $"{what} was written as {written} where the summary states nothing");
            return;
        }

        Assert.True(written is not null, $"{what} was not written, and the summary states {expected}");
        Assert.Equal(expected.Value, written!.Value, tolerance);
    }

    /// <inheritdoc cref="AssertStates(double?, float?, double, string)"/>
    private static void AssertWholeMetres(double? expected, ushort? written, string what)
    {
        if (expected is null)
        {
            Assert.True(written is null, $"{what} was written as {written} where the summary states nothing");
            return;
        }

        Assert.True(written is not null, $"{what} was not written, and the summary states {expected}");
        Assert.Equal((ushort)Math.Round(expected.Value), written!.Value);
    }

    /// <summary>
    /// The writer's own source file, or null when the suite runs without a source tree beside it.
    /// </summary>
    private static string? WriterSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "FitFix.Core", "Fit", "FitActivityWriter.cs");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
