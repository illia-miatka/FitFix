using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// AD-25: a physically impossible GPS point is dropped in the core before the compatibility
/// check (FR-3), before the anchor search and before any series a shell would draw, so a
/// discarded point exists neither for the computation nor for the map. Every assertion reads the
/// cleaned activity — the input fixture still holds the spike, and saying so proves nothing.
/// </summary>
public class GpsCleanerTests
{
    /// <summary>The second a mid-activity spike is injected at, inside the measured zone.</summary>
    private const int SpikeIndex = 600;

    /// <summary>A destination no runner reached: 2000 km east of the start.</summary>
    private static GeoPoint Teleport => SyntheticRun.FarAway(kilometres: 2_000, bearingDegrees: 90);

    /// <summary>Another one, north-west and nearer, so direction is not an accident of one test.</summary>
    private static GeoPoint FarNorthWest => SyntheticRun.FarAway(kilometres: 1_000, bearingDegrees: 315);

    private static DateTime Start => new(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void A_teleport_is_discarded_and_counted()
    {
        var run = SyntheticRun.Create().WithTeleportAt(SpikeIndex, Teleport);

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Equal(1, result.Report.DiscardedPositionCount);
        Assert.Null(result.CleanedOriginal.Records[SpikeIndex].Position);
        Assert.Contains(DiagnosticCodes.PositionsDiscarded, result.Report.Warnings);
    }

    [Fact]
    public void A_teleport_does_not_move_the_repaired_distance()
    {
        var clean = SyntheticRun.Create();
        var spiked = clean.WithTeleportAt(SpikeIndex, Teleport);

        var withoutSpike = RepairService.Repair(clean.Original, clean.ReferenceTrack);
        var withSpike = RepairService.Repair(spiked.Original, spiked.ReferenceTrack);

        // One position out of 2161 is gone from the alignment and nothing else changed, so the
        // repair has to land on the same track length over the same measured zone.
        Assert.Equal(
            withoutSpike.Report.RepairedDistanceMeters,
            withSpike.Report.RepairedDistanceMeters,
            0.01);
        Assert.Equal(
            withoutSpike.Alignment.MeasuredUntilSeconds,
            withSpike.Alignment.MeasuredUntilSeconds,
            0.001);
        Assert.Equal(withoutSpike.Report.AnchorCount - 1, withSpike.Report.AnchorCount);
    }

    [Fact]
    public void A_run_without_an_impossible_point_is_not_touched_at_all()
    {
        var run = SyntheticRun.Create();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Equal(0, result.Report.DiscardedPositionCount);
        Assert.DoesNotContain(DiagnosticCodes.PositionsDiscarded, result.Report.Warnings);
        // The same instance, so no number the suite pins on this fixture can move because
        // cleaning ran at all — the pinned anchors, distance and boundary live in
        // AlignmentOptionsTests and keep passing unchanged.
        Assert.Same(run.Original, result.CleanedOriginal);
    }

    [Fact]
    public void The_record_of_a_discarded_position_keeps_its_timestamp_and_sensors()
    {
        var clean = SyntheticRun.Create();
        var spiked = clean.WithTeleportAt(SpikeIndex, Teleport);

        var result = RepairService.Repair(spiked.Original, spiked.ReferenceTrack);

        // FR-6: a position goes, a record never does.
        Assert.Equal(clean.Original.Records.Count, result.CleanedOriginal.Records.Count);
        var source = clean.Original.Records[SpikeIndex];
        var cleaned = result.CleanedOriginal.Records[SpikeIndex];

        Assert.Equal(source.Timestamp, cleaned.Timestamp);
        Assert.Equal(source.HeartRate, cleaned.HeartRate);
        Assert.Equal(source.Cadence, cleaned.Cadence);
        Assert.Equal(source.Power, cleaned.Power);
        Assert.Equal(source.Temperature, cleaned.Temperature);
        Assert.Equal(source.Altitude, cleaned.Altitude);
        Assert.Equal(source.Distance, cleaned.Distance);
        Assert.Null(cleaned.Latitude);
        Assert.Null(cleaned.Longitude);
    }

    [Fact]
    public void The_cleaned_series_a_shell_reads_holds_no_discarded_position()
    {
        var clean = SyntheticRun.Create();
        var spiked = clean.WithTeleportAt(SpikeIndex, Teleport);
        var start = clean.Original.Records[0].Position!.Value;

        var result = RepairService.Repair(spiked.Original, spiked.ReferenceTrack);

        // AD-25: the teleport is drawn nowhere, because the only series a shell is handed is
        // this one. 50 km is far beyond any route and far short of the 2000 km spike.
        Assert.All(
            Positions(result.CleanedOriginal),
            position => Assert.True(
                Geo.Distance(start, position) < 50_000,
                "a position 50 km from the start survived cleaning"));
    }

    [Fact]
    public void A_teleport_as_the_very_first_position_does_not_fail_the_track_check()
    {
        var run = SyntheticRun.Create().WithTeleportAt(0, Teleport);

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        // The track matches the rest of the run, and the compatibility check reads the cleaned
        // series, so the one impossible point cannot make it look like another city's route.
        Assert.DoesNotContain(DiagnosticCodes.TrackFarFromOriginal, result.Report.Warnings);
        Assert.Equal(1, result.Report.DiscardedPositionCount);
        Assert.Null(result.CleanedOriginal.Records[0].Position);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    // Beyond the matrix on purpose: a burst one record longer than the agreement run is the same
    // defect, and a rule that survives three repetitions but not four has only moved it.
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(60)]
    public void A_leading_burst_is_discarded_and_nothing_else_is(int burst)
    {
        var clean = SyntheticRun.Create();
        var spiked = clean.WithTeleportBurst(0, burst, SyntheticRun.NullIsland);

        var withoutBurst = RepairService.Repair(clean.Original, clean.ReferenceTrack);
        var result = RepairService.Repair(spiked.Original, spiked.ReferenceTrack);

        // The defect this guards against: the burst vouching for itself, becoming the reference
        // and condemning the whole route — 2159 of 2161 positions, and a false
        // TRACK_FAR_FROM_ORIGINAL on a track that matches.
        Assert.Equal(burst, result.Report.DiscardedPositionCount);
        Assert.DoesNotContain(DiagnosticCodes.TrackFarFromOriginal, result.Report.Warnings);
        Assert.Equal(
            withoutBurst.Alignment.MeasuredUntilSeconds,
            result.Alignment.MeasuredUntilSeconds,
            0.001);
        // The discarded positions were anchors of their own in the clean run, so the count may
        // fall by at most the burst — and by nothing else.
        Assert.InRange(
            result.Report.AnchorCount,
            withoutBurst.Report.AnchorCount - burst,
            withoutBurst.Report.AnchorCount);

        Assert.All(
            Enumerable.Range(0, burst),
            index => Assert.Null(result.CleanedOriginal.Records[index].Position));
        Assert.NotNull(result.CleanedOriginal.Records[burst].Position);
    }

    [Fact]
    public void A_burst_in_the_middle_and_a_burst_at_the_very_end_take_only_themselves()
    {
        var clean = SyntheticRun.Create();
        var last = clean.Original.Records.Count - 3;
        var spiked = clean
            .WithTeleportBurst(SpikeIndex, 5, SyntheticRun.NullIsland)
            .WithTeleportBurst(last, 3, Teleport);

        var result = RepairService.Repair(spiked.Original, spiked.ReferenceTrack);

        Assert.Equal(8, result.Report.DiscardedPositionCount);
        Assert.All(
            Enumerable.Range(SpikeIndex, 5).Concat(Enumerable.Range(last, 3)),
            index => Assert.Null(result.CleanedOriginal.Records[index].Position));
        // A stale reference must not keep widening its allowance while garbage runs on, and the
        // first real position after the burst must still be accepted.
        Assert.NotNull(result.CleanedOriginal.Records[SpikeIndex + 5].Position);
        Assert.NotNull(result.CleanedOriginal.Records[SpikeIndex - 1].Position);
    }

    [Fact]
    public void A_spike_at_the_second_position_takes_only_itself()
    {
        var run = SyntheticRun.Create().WithTeleportAt(1, Teleport);

        var cleaning = GpsCleaner.Clean(run.Original, SyntheticRun.Options);

        // The vote that saves the first position from a teleport must not do the reverse: a
        // genuine leading position is not guilty of the garbage that follows it.
        Assert.Equal(1, cleaning.DiscardedPositionCount);
        Assert.NotNull(cleaning.Activity.Records[0].Position);
        Assert.Null(cleaning.Activity.Records[1].Position);
    }

    [Fact]
    public void A_spike_inside_a_standing_start_takes_only_itself()
    {
        // A minute of standing at the start line, one bad fix inside it, then the run. A rule
        // that demanded every following position agree would discard the whole standing minute
        // along with the spike.
        var activity = StandingStart(standingSeconds: 60, spikesAt: [30]);

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(1, cleaning.DiscardedPositionCount);
        Assert.Null(cleaning.Activity.Records[30].Position);
        Assert.NotNull(cleaning.Activity.Records[0].Position);
    }

    [Fact]
    public void A_standing_start_followed_by_two_spikes_survives_them()
    {
        // The standing positions are all one place, so they cast no vote for each other; the two
        // spikes do vote, and against. Only the run itself can vouch for the start line.
        var activity = StandingStart(standingSeconds: 60, spikesAt: [61, 62]);

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(2, cleaning.DiscardedPositionCount);
        Assert.Null(cleaning.Activity.Records[61].Position);
        Assert.Null(cleaning.Activity.Records[62].Position);
        Assert.All(
            cleaning.Activity.Records.Take(61),
            record => Assert.NotNull(record.Position));
    }

    [Theory]
    [InlineData(30, 0.0)]
    [InlineData(5, 5.0)]
    public void A_distant_fix_is_not_laundered_by_repetition(int fixes, double jitterMeters)
    {
        // A stale fix a kilometre away, repeated — identically, or wandering by a few metres.
        // Summing a per-step allowance over the repetitions was measured to confirm it as the
        // reference and discard 263 genuine positions; one span from the candidate does not.
        var activity = LeadingFixes(fixes, FarNorthWest, jitterMeters, thenRunningSeconds: 300);

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(fixes, cleaning.DiscardedPositionCount);
        Assert.All(
            cleaning.Activity.Records.Take(fixes),
            record => Assert.Null(record.Position));
        Assert.All(
            cleaning.Activity.Records.Skip(fixes),
            record => Assert.NotNull(record.Position));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(300)]
    public void Garbage_in_the_middle_does_not_condemn_what_follows_it(int garbageSeconds)
    {
        // Per-second garbage long enough to outlast both the vote and the stale bound, then the
        // athlete reappears and keeps running. Counting staleness from the last discard left the
        // reference permanently stale and took all 600 genuine positions with it.
        var activity = MidActivityGarbage(garbageSeconds, runningSecondsEitherSide: 300);

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(garbageSeconds, cleaning.DiscardedPositionCount);
        Assert.All(
            cleaning.Activity.Records.Skip(301 + garbageSeconds),
            record => Assert.NotNull(record.Position));
    }

    [Fact]
    public void A_duplicate_timestamp_does_not_launder_a_leading_spike()
    {
        // The two genuine records share one second, an interval speed cannot judge. Treating that
        // as an unbounded allowance made every follower vote for the spike, which then became the
        // reference and condemned the route: 200 of 201 positions.
        var records = new List<ActivityRecord>
        {
            Positioned(Start, Teleport),
            Positioned(Start.AddSeconds(1), Home(0)),
            Positioned(Start.AddSeconds(1), Home(2))
        };
        for (var second = 2; second <= 300; second++)
            records.Add(Positioned(Start.AddSeconds(second), Home(second * 3.0)));

        var cleaning = GpsCleaner.Clean(
            new OriginalActivity { Records = records, Sport = SportKind.Running },
            AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(1, cleaning.DiscardedPositionCount);
        Assert.Null(cleaning.Activity.Records[0].Position);
        Assert.All(
            cleaning.Activity.Records.Skip(1),
            record => Assert.NotNull(record.Position));
    }

    [Fact]
    public void An_activity_of_nothing_but_garbage_is_cleaned_to_nothing()
    {
        var activity = AllGarbage(seconds: 200);

        var result = RepairService.Repair(activity, SyntheticRun.Create().ReferenceTrack);

        // Keeping one arbitrary survivor would make it the Original's first position and raise
        // the false TRACK_FAR_FROM_ORIGINAL this story exists to remove. Both codes are reported,
        // so a shell can tell a series cleaned to nothing from one that never had GPS at all.
        Assert.Equal(activity.Records.Count, result.Report.DiscardedPositionCount);
        Assert.All(result.CleanedOriginal.Records, record => Assert.Null(record.Position));
        Assert.Contains(DiagnosticCodes.PositionsDiscarded, result.Report.Warnings);
        Assert.Contains(DiagnosticCodes.NoOriginalGps, result.Report.Warnings);
        Assert.DoesNotContain(DiagnosticCodes.TrackFarFromOriginal, result.Report.Warnings);
    }

    [Fact]
    public void The_cleaner_refuses_options_that_make_its_own_rules_vacuous()
    {
        var run = SyntheticRun.Create();

        // A vote with no voters decides nothing, and a judging window of zero is the per-second
        // allowance that was measured to delete real data.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GpsCleaner.Clean(run.Original, SyntheticRun.Options with { LeadingAgreementPositions = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GpsCleaner.Clean(run.Original, SyntheticRun.Options with { LeadingAgreementPositions = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GpsCleaner.Clean(run.Original, SyntheticRun.Options with { ImpossibleSpeedJudgedOverSeconds = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GpsCleaner.Clean(run.Original, SyntheticRun.Options with { ImpossibleSpeedJudgedOverSeconds = -5 }));
    }

    [Fact]
    public void A_spike_followed_by_an_outage_does_not_condemn_the_resumption()
    {
        // The watch spikes, then reports no position for five minutes, then re-acquires 900 m
        // further on — 3 m/s, an ordinary pace. Only the spike is impossible.
        var activity = SpikeThenOutage();

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(1, cleaning.DiscardedPositionCount);
        Assert.NotNull(cleaning.Activity.Records[^1].Position);
    }

    [Fact]
    public void A_run_of_garbage_does_not_widen_the_allowance()
    {
        // Sixteen minutes of teleports every second, then a point 5 km from the last valid one.
        // Without the stale clamp the discarded run would have bought it an allowance of 7 km
        // and laundered it into the reference.
        var activity = GarbageRun(garbageSeconds: 1_000, thenMetersAway: 5_000);

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(1_001, cleaning.DiscardedPositionCount);
        Assert.Null(cleaning.Activity.Records[^1].Position);
    }

    [Fact]
    public void With_only_two_positions_the_teleport_goes_and_the_real_point_stays()
    {
        var activity = Activity(
            Positioned(Start, Teleport),
            Positioned(Start.AddSeconds(1), Home(0)));

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        // Nothing follows the second position to vouch for it, and the one thing known about the
        // first is that it disagrees with everything there is.
        Assert.Equal(1, cleaning.DiscardedPositionCount);
        Assert.Null(cleaning.Activity.Records[0].Position);
        Assert.NotNull(cleaning.Activity.Records[1].Position);
    }

    [Fact]
    public void Ordinary_noise_at_a_realistic_pace_is_not_impossible()
    {
        // A plausible consumer-watch file: 1 Hz sampling at 5 m/s with up to 5 m of jitter.
        var activity = Noisy(speedMetersPerSecond: 5, jitterMeters: 5, seconds: 1_200);

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(0, cleaning.DiscardedPositionCount);
        // And the file really does need the judging window: steps above the bare
        // max * dt + tolerance allowance of 10 m are what the measurement found on real data.
        var biggestStep = BiggestStep(activity);
        Assert.True(biggestStep > 10, $"the widest step was only {biggestStep:F1} m");
    }

    [Fact]
    public void A_ride_judged_with_running_thresholds_survives()
    {
        // 15 m/s is twice the running cap, and one-second steps of 15 m are still not impossible
        // over the judging window. Cleaning must leave the ride to the anchor stage, whose own
        // speed cap is what AlignmentOptionsTests measures.
        var ride = Straight(speedMetersPerSecond: 15, seconds: 600);

        var cleaning = GpsCleaner.Clean(ride, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(0, cleaning.DiscardedPositionCount);
        Assert.Same(ride, cleaning.Activity);
    }

    [Fact]
    public void A_hundred_kilometre_ride_across_several_cities_survives_intact()
    {
        // 100 km in two hours: nothing about the distance is impossible, and the criterion is
        // time, not distance (AD-25).
        var ride = Straight(speedMetersPerSecond: 100_000 / 7_200.0, seconds: 7_200, secondsBetweenRecords: 60);

        var cleaning = GpsCleaner.Clean(ride, AlignmentOptions.ForSport(SportKind.Cycling));

        Assert.Equal(0, cleaning.DiscardedPositionCount);
        Assert.Same(ride, cleaning.Activity);
    }

    [Fact]
    public void A_long_gap_in_the_records_is_not_a_teleport()
    {
        // Twenty minutes without a single record, then a position 2 km away: 1.7 m/s is a walk.
        var activity = Activity(
            Positioned(Start, Home(0)),
            Positioned(Start.AddMinutes(20), Home(2_000)));

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(0, cleaning.DiscardedPositionCount);
        Assert.Same(activity, cleaning.Activity);
    }

    [Fact]
    public void Timestamps_that_do_not_move_forward_are_never_impossible()
    {
        var activity = Activity(
            Positioned(Start, Home(0)),
            // The same second, 50 m away, and then a timestamp that goes backwards: speed has
            // no interval to judge either pair over.
            Positioned(Start, Home(50)),
            Positioned(Start.AddSeconds(-10), Home(300)));

        var cleaning = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(0, cleaning.DiscardedPositionCount);
        Assert.Same(activity, cleaning.Activity);
    }

    [Fact]
    public void A_record_out_of_order_after_a_discard_is_not_judged_by_the_stale_allowance()
    {
        var records = new List<ActivityRecord>();
        for (var second = 0; second <= 10; second++)
            records.Add(Positioned(Start.AddSeconds(second), Home(second * 3.0)));

        records.Add(Positioned(Start.AddSeconds(11), Teleport));
        // Its timestamp goes back behind the reference and behind the discard. An age counted
        // from either would come out negative, and a negative age is not staleness: speed has no
        // interval to judge this pair over at all.
        records.Add(Positioned(Start.AddSeconds(5), Home(300)));

        var cleaning = GpsCleaner.Clean(
            new OriginalActivity { Records = records, Sport = SportKind.Running },
            AlignmentOptions.ForSport(SportKind.Running));

        Assert.Equal(1, cleaning.DiscardedPositionCount);
        Assert.Null(cleaning.Activity.Records[11].Position);
        Assert.NotNull(cleaning.Activity.Records[12].Position);
    }

    [Fact]
    public void A_frozen_gps_is_not_garbage()
    {
        var run = SyntheticRun.Create();

        var cleaning = GpsCleaner.Clean(run.Original, SyntheticRun.Options);

        // A position that stops moving is the stall detector's business, not the cleaner's:
        // standing still is the one thing that is never impossible.
        Assert.Equal(0, cleaning.DiscardedPositionCount);
        Assert.All(
            cleaning.Activity.Records.Skip(SyntheticRun.GpsFailureSeconds),
            record => Assert.NotNull(record.Position));
    }

    [Fact]
    public void An_activity_without_any_position_is_left_alone()
    {
        var run = SyntheticRun.Create(withGpsFailure: false, withAnyGps: false);

        var cleaning = GpsCleaner.Clean(run.Original, SyntheticRun.Options);

        Assert.Equal(0, cleaning.DiscardedPositionCount);
        Assert.Same(run.Original, cleaning.Activity);
    }

    [Fact]
    public void The_cleaner_demands_both_arguments()
    {
        var run = SyntheticRun.Create();

        Assert.Throws<ArgumentNullException>(() => GpsCleaner.Clean(null!, SyntheticRun.Options));
        Assert.Throws<ArgumentNullException>(() => GpsCleaner.Clean(run.Original, null!));
    }

    [Fact]
    public void The_fixture_refuses_a_spike_it_cannot_place()
    {
        var run = SyntheticRun.Create();
        var withoutGps = SyntheticRun.Create(withGpsFailure: false, withAnyGps: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.WithTeleportAt(-1, Teleport));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => run.WithTeleportAt(run.Original.Records.Count, Teleport));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => run.WithTeleportBurst(run.Original.Records.Count - 2, 3, Teleport));
        Assert.Throws<ArgumentOutOfRangeException>(() => run.WithTeleportBurst(0, 0, Teleport));
        Assert.Throws<ArgumentException>(() => withoutGps.WithTeleportAt(0, Teleport));
    }

    private static IEnumerable<GeoPoint> Positions(OriginalActivity activity) =>
        activity.Records.Where(r => r.Position is not null).Select(r => r.Position!.Value);

    /// <summary>A point <paramref name="eastMeters"/> east of the fixture's start.</summary>
    private static GeoPoint Home(double eastMeters) =>
        SyntheticRun.Offset(eastMeters, northMeters: 0);

    private static ActivityRecord Positioned(DateTime timestamp, GeoPoint position) => new()
    {
        Timestamp = timestamp,
        Latitude = position.Latitude,
        Longitude = position.Longitude,
        HeartRate = 150,
        Cadence = 85
    };

    private static OriginalActivity Activity(params ActivityRecord[] records) =>
        new() { Records = records, Sport = SportKind.Running };

    /// <summary>A straight run east at a constant speed, sampled every so many seconds.</summary>
    private static OriginalActivity Straight(
        double speedMetersPerSecond,
        double seconds,
        int secondsBetweenRecords)
    {
        var records = new List<ActivityRecord>();
        for (var second = 0.0; second <= seconds; second += secondsBetweenRecords)
            records.Add(Positioned(Start.AddSeconds(second), Home(second * speedMetersPerSecond)));

        return new OriginalActivity { Records = records, Sport = SportKind.Cycling };
    }

    private static OriginalActivity Straight(double speedMetersPerSecond, double seconds) =>
        Straight(speedMetersPerSecond, seconds, secondsBetweenRecords: 1);

    /// <summary>
    /// A run that stands still for <paramref name="standingSeconds"/>, with a bad fix at each of
    /// <paramref name="spikesAt"/>, and then runs east at 3 m/s.
    /// </summary>
    private static OriginalActivity StandingStart(int standingSeconds, int[] spikesAt)
    {
        var records = new List<ActivityRecord>();
        for (var second = 0; second <= standingSeconds + 300; second++)
        {
            var east = second <= standingSeconds ? 0 : (second - standingSeconds) * 3.0;
            records.Add(Positioned(
                Start.AddSeconds(second),
                spikesAt.Contains(second) ? SyntheticRun.NullIsland : Home(east)));
        }

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>
    /// <paramref name="fixes"/> leading fixes at <paramref name="where"/>, each wandering by up to
    /// <paramref name="jitterMeters"/>, and then a genuine run from the fixture's own start.
    /// </summary>
    private static OriginalActivity LeadingFixes(
        int fixes,
        GeoPoint where,
        double jitterMeters,
        int thenRunningSeconds)
    {
        var noise = new Random(29);
        var records = new List<ActivityRecord>();

        for (var second = 0; second < fixes; second++)
        {
            var north = (noise.NextDouble() * 2 - 1) * jitterMeters;
            var east = (noise.NextDouble() * 2 - 1) * jitterMeters;
            records.Add(Positioned(Start.AddSeconds(second), Wander(where, east, north)));
        }

        for (var second = 0; second <= thenRunningSeconds; second++)
            records.Add(Positioned(Start.AddSeconds(fixes + second), Home(second * 3.0)));

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>
    /// A run, then <paramref name="garbageSeconds"/> of per-second garbage that is somewhere
    /// different every second, then the run resuming at the pace it left off.
    /// </summary>
    private static OriginalActivity MidActivityGarbage(int garbageSeconds, int runningSecondsEitherSide)
    {
        var records = new List<ActivityRecord>();
        for (var second = 0; second <= runningSecondsEitherSide; second++)
            records.Add(Positioned(Start.AddSeconds(second), Home(second * 3.0)));

        for (var second = 0; second < garbageSeconds; second++)
            records.Add(Positioned(
                Start.AddSeconds(runningSecondsEitherSide + 1 + second),
                SyntheticRun.FarAway(500 + second * 7, bearingDegrees: 225)));

        for (var second = 0; second <= runningSecondsEitherSide; second++)
            records.Add(Positioned(
                Start.AddSeconds(runningSecondsEitherSide + 1 + garbageSeconds + second),
                Home((runningSecondsEitherSide + garbageSeconds + second) * 3.0)));

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>An activity with no genuine position at all: every second somewhere impossible.</summary>
    private static OriginalActivity AllGarbage(int seconds)
    {
        var noise = new Random(3);
        var records = new List<ActivityRecord>(seconds);

        for (var second = 0; second < seconds; second++)
            records.Add(Positioned(
                Start.AddSeconds(second),
                SyntheticRun.FarAway(500 + noise.NextDouble() * 2_000, noise.NextDouble() * 360)));

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    private static GeoPoint Wander(GeoPoint around, double eastMeters, double northMeters) => new(
        around.Latitude + northMeters / Geo.MetersPerDegreeLatitude,
        around.Longitude
            + eastMeters / (Geo.MetersPerDegreeLatitude * Math.Cos(around.Latitude * Math.PI / 180.0)));

    /// <summary>A run, one spike, five minutes with no position at all, then 900 m further on.</summary>
    private static OriginalActivity SpikeThenOutage()
    {
        var records = new List<ActivityRecord>();
        for (var second = 0; second <= 600; second++)
            records.Add(Positioned(Start.AddSeconds(second), Home(second * 3.0)));

        records.Add(Positioned(Start.AddSeconds(601), Teleport));

        // Records keep coming, with sensors and no position — exactly what a watch writes while
        // it is looking for a fix.
        for (var tick = 0; tick < 60; tick++)
            records.Add(new ActivityRecord { Timestamp = Start.AddSeconds(602 + tick * 5), HeartRate = 150 });

        for (var second = 0; second <= 300; second++)
            records.Add(Positioned(Start.AddSeconds(905 + second), Home(1_800 + 900 + second * 3.0)));

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>A short real run, then nothing but teleports, then one point far from the last valid.</summary>
    private static OriginalActivity GarbageRun(int garbageSeconds, double thenMetersAway)
    {
        var records = new List<ActivityRecord>();
        for (var second = 0; second <= 10; second++)
            records.Add(Positioned(Start.AddSeconds(second), Home(second * 3.0)));

        for (var second = 11; second <= 10 + garbageSeconds; second++)
            records.Add(Positioned(Start.AddSeconds(second), Teleport));

        records.Add(Positioned(Start.AddSeconds(11 + garbageSeconds), Home(thenMetersAway)));
        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>A straight run east at 1 Hz with square jitter of the given radius.</summary>
    private static OriginalActivity Noisy(double speedMetersPerSecond, double jitterMeters, int seconds)
    {
        var noise = new Random(17);
        var records = new List<ActivityRecord>(seconds + 1);

        for (var second = 0; second <= seconds; second++)
        {
            var east = second * speedMetersPerSecond + (noise.NextDouble() * 2 - 1) * jitterMeters;
            var north = (noise.NextDouble() * 2 - 1) * jitterMeters;
            records.Add(Positioned(Start.AddSeconds(second), SyntheticRun.Offset(east, north)));
        }

        return new OriginalActivity { Records = records, Sport = SportKind.Running };
    }

    /// <summary>The widest step between two consecutive positions, m; zero if there is no pair.</summary>
    private static double BiggestStep(OriginalActivity activity)
    {
        var positions = Positions(activity).ToArray();
        return positions.Length < 2
            ? 0
            : positions.Zip(positions.Skip(1), Geo.Distance).Max();
    }
}
