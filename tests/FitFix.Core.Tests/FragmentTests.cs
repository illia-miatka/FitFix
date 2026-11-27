using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// Story 5.1 and AD-27: the Еталонний трек may cover only a <em>stretch</em> of the route. It
/// attaches by its own two endpoints — its first point to the last true position before the failure,
/// its last point to the first true position after it, and the activity's own start or finish where
/// no true position survives on that side — and the span between those two attach times is the
/// Розрахована зона, which takes the fragment's length as its increment. Everything outside it is
/// measured and keeps the watch's own coordinates and geodesic distance (AD-26). A fragment whose
/// ends find no place in the activity is a refusal, not a guess.
/// <para>
/// How far a chain of anchors reaches says nothing about any of this, which is why a fragment is not
/// aligned by one: a track covering the five broken kilometres of fifty is matched by the anchor
/// chain over the three minutes the lap's own closing metres happen to pass its far end, and the
/// other forty-seven minutes of honest GPS then fall into one estimated zone with no track under
/// them. Both failures this suite pins came from that mechanism.
/// </para>
/// <para>
/// Every fixture is the same synthetic course as the whole-track suite, sliced, so a fragment is the
/// geometry the whole track holds over that stretch point for point and the two repairs can be
/// compared metre by metre.
/// </para>
/// </summary>
public class FragmentTests
{
    /// <summary>
    /// Where the tail fragment begins along the course, m: 200 m before the GPS freezes, so the last
    /// true positions attach to it. It runs to the course's own end, which on this lap <em>is</em>
    /// the start line — the activity's first fix stands on the fragment's far end, which is the
    /// ambiguity a real lap route offers and the one a nearest-match search gets wrong.
    /// </summary>
    private const double TailFragmentFrom = 3_700;

    /// <summary>When the watch of the head-fragment run finds its first fix, s.</summary>
    private const int HeadFragmentFixAt = 700;

    /// <summary>The dropout of the middle-fragment run, s from the start — long enough that no
    /// anchor could survive inside it, and 1.2 km of route wide.</summary>
    private const int MiddleDropoutFrom = 700;

    /// <inheritdoc cref="MiddleDropoutFrom"/>
    private const int MiddleDropoutTo = 1_100;

    /// <summary>What the middle fragment covers along the course, m — the dropout and a little
    /// either side of it, so both of its ends attach to a true position.</summary>
    private const double MiddleFragmentFrom = 2_000;

    /// <inheritdoc cref="MiddleFragmentFrom"/>
    private const double MiddleFragmentTo = 3_400;

    // --- A tail fragment: the GPS died for the rest of the run and the track covers that rest.

    [Fact]
    public void A_tail_fragment_fills_the_failure_and_leaves_the_rest_to_the_watch()
    {
        var run = SyntheticRun.Create();
        var whole = RepairService.Repair(run.Original, run.ReferenceTrack);

        var fragment = run.WithTrackCovering(TailFragmentFrom, run.TrueRouteLength);
        var track = fragment.ReferenceTrack;
        var result = RepairService.Repair(fragment.Original, track);

        // The fragment's own far end is the lap's start line, where this activity began: a search
        // for the nearest match would attach it to the first seconds of the run and read the whole
        // activity backwards. The order the two ends have to be run in is what rules that out.
        Assert.Equal(0, Geo.Distance(track.Points[^1], run.Original.Records[0].Position!.Value), 25.0);

        var reach = result.Alignment.Reach;
        Assert.False(reach.OpensAtTrackStart);
        Assert.True(reach.ClosesAtTrackEnd);
        Assert.InRange(reach.FromSeconds, SyntheticRun.GpsFailureSeconds - 200, SyntheticRun.GpsFailureSeconds);
        Assert.Equal(SyntheticRun.DurationSeconds, reach.ToSeconds, 0.001);

        // Measured up to the failure, estimated from there — and the measured stretch is the
        // watch's own, not an interpolation, which is the whole point (AD-26).
        var zones = result.Report.Zones;
        Assert.Equal(2, zones.Count);
        Assert.Equal(ZoneKind.Measured, zones[0].Kind);
        Assert.Equal(reach.FromSeconds, zones[0].ToSeconds, 0.001);
        Assert.Equal(ZoneKind.Estimated, zones[1].Kind);
        Assert.Equal(SyntheticRun.DurationSeconds, zones[1].ToSeconds, 0.001);

        // Record for record what the same original repairs to without a fragment at all.
        AssertOutsideTheSpanIsUntouched(result, whole);

        // And the total is what the watch measured before the failure plus the fragment itself.
        Assert.Equal(
            DistanceAt(result, reach.FromSeconds) + track.Length,
            result.Report.RepairedDistanceMeters,
            1.0);

        AssertMonotone(result);
    }

    [Fact]
    public void A_blind_stretch_outside_the_span_keeps_the_athlete_where_the_watch_had_them()
    {
        // A minute without a fix, early in the run and nowhere near the fragment: it falls inside a
        // measured zone, where the track has nothing to say — the fragment covers the far end of
        // the route. Drawing it from the track would stand the athlete at the span's boundary, 1.7 km
        // from where they were, and freeze the distance for the whole minute (AD-26, AD-27).
        const int blindFrom = 300;
        const int blindTo = 360;

        var run = SyntheticRun.Create().WithoutFixesBetween(blindFrom, blindTo);
        var whole = RepairService.Repair(run.Original, run.ReferenceTrack);
        var fragment = run.WithTrackCovering(TailFragmentFrom, run.TrueRouteLength);

        var result = RepairService.Repair(fragment.Original, fragment.ReferenceTrack);

        for (var second = blindFrom; second <= blindTo; second++)
        {
            var repaired = new GeoPoint(result.Records[second].Latitude, result.Records[second].Longitude);
            var withoutAFragment = new GeoPoint(whole.Records[second].Latitude, whole.Records[second].Longitude);

            Assert.Equal(0, Geo.Distance(repaired, withoutAFragment), 5.0);
            Assert.True(
                result.Records[second].Distance > result.Records[second - 1].Distance,
                $"the distance stood still at t={second} s, inside a measured zone the athlete ran through");
        }

        AssertMonotone(result);
    }

    // --- A head fragment: the watch found nothing until the run was under way.

    [Fact]
    public void A_head_fragment_starts_the_activity_at_the_fragments_own_start()
    {
        var run = SyntheticRun
            .Create(withGpsFailure: false)
            .WithoutFixesBefore(HeadFragmentFixAt)
            .WithTrackCovering(0, 2_200);
        var track = run.ReferenceTrack;

        var result = RepairService.Repair(run.Original, track);

        // No true position survives before the fragment, so the activity's own start becomes its
        // start; its end attaches to the first true position after it, and the finish is the
        // athlete's own (AD-27).
        var reach = result.Alignment.Reach;
        Assert.True(reach.OpensAtTrackStart);
        Assert.False(reach.ClosesAtTrackEnd);
        Assert.Equal(0, reach.FromSeconds);
        Assert.InRange(reach.ToSeconds, HeadFragmentFixAt, HeadFragmentFixAt + 60);

        // The athlete's own last position is where the lap closes, metres from the fragment's first
        // point — so the activity's finish is a candidate for that end too, and only the arithmetic
        // of what a fragment can cover in five seconds rules it out.
        var zones = result.Report.Zones;
        Assert.Equal(2, zones.Count);
        Assert.Equal(ZoneKind.Estimated, zones[0].Kind);
        Assert.Equal(reach.ToSeconds, zones[0].ToSeconds, 0.001);
        Assert.Equal(ZoneKind.Measured, zones[1].Kind);

        // The first record stands on the fragment's first metre, and the rest of the activity is the
        // watch's own, coordinate for coordinate.
        var start = new GeoPoint(result.Records[0].Latitude, result.Records[0].Longitude);
        Assert.Equal(0, Geo.Distance(start, track.Points[0]), 1.0);

        for (var index = (int)reach.ToSeconds + 1; index < result.Records.Count; index++)
        {
            var own = run.Original.Records[index].Position!.Value;
            Assert.Equal(own.Latitude, result.Records[index].Latitude, 1e-12);
            Assert.Equal(own.Longitude, result.Records[index].Longitude, 1e-12);
        }

        // So the total is the fragment plus what the watch measured after it — the course the
        // athlete ran, within the per cent or two real jitter adds (AD-26).
        var ran = SyntheticRun.DurationSeconds * SyntheticRun.SpeedMetersPerSecond;
        Assert.Equal(ran, result.Report.RepairedDistanceMeters, ran * 0.03);

        AssertMonotone(result);
    }

    // --- A middle fragment: a dropout with no anchor anywhere inside it.

    [Fact]
    public void A_middle_fragment_redraws_the_dropout_and_nothing_else()
    {
        var run = SyntheticRun
            .Create(withGpsFailure: false)
            .WithoutFixesBetween(MiddleDropoutFrom, MiddleDropoutTo);
        var whole = RepairService.Repair(run.Original, run.ReferenceTrack);
        var fragment = run.WithTrackCovering(MiddleFragmentFrom, MiddleFragmentTo);

        var result = RepairService.Repair(fragment.Original, fragment.ReferenceTrack);

        // 1.2 km of route with no position at all: the anchor chain cannot step across it — no
        // projection inside the dropout exists to be an anchor — and the fragment does not need it
        // to, because it attaches by its ends (AD-27).
        var reach = result.Alignment.Reach;
        Assert.False(reach.OpensAtTrackStart);
        Assert.False(reach.ClosesAtTrackEnd);
        Assert.InRange(reach.FromSeconds, 1, MiddleDropoutFrom);
        Assert.InRange(reach.ToSeconds, MiddleDropoutTo, SyntheticRun.DurationSeconds - 1);

        var zones = result.Report.Zones;
        Assert.Equal(3, zones.Count);
        Assert.Equal(ZoneKind.Measured, zones[0].Kind);
        Assert.Equal(ZoneKind.Estimated, zones[1].Kind);
        Assert.Equal(ZoneKind.Measured, zones[2].Kind);
        Assert.Equal(reach.FromSeconds, zones[1].FromSeconds, 0.001);
        Assert.Equal(reach.ToSeconds, zones[1].ToSeconds, 0.001);
        Assert.Equal(SyntheticRun.DurationSeconds, zones[2].ToSeconds, 0.001);

        // The whole dropout is inside the redrawn stretch, and nothing else is.
        Assert.True(zones[1].FromSeconds <= MiddleDropoutFrom, "the dropout starts before the redrawn stretch");
        Assert.True(zones[1].ToSeconds >= MiddleDropoutTo, "the dropout ends after the redrawn stretch");

        AssertOutsideTheSpanIsUntouched(result, whole);

        // The seams jump at neither end: the metres change source there and the athlete's step stays
        // their own 3 m, including the one at the far end of the estimated zone — the smoothing
        // window has to stop at the fragment's span on both sides.
        foreach (var boundary in zones.Select(zone => (int)zone.ToSeconds).Where(second => second > 0))
        {
            if (boundary >= result.Records.Count - 1)
                continue;

            var step = result.Records[boundary + 1].Distance - result.Records[boundary].Distance;
            Assert.Equal(SyntheticRun.SpeedMetersPerSecond, step, SyntheticRun.SpeedMetersPerSecond);
        }

        // The finish is not invented: the last seconds are the watch's own, so the series climbs
        // into the finish at the athlete's own pace rather than stepping onto a pinned total.
        var lastStep = result.Records[^1].Distance - result.Records[^2].Distance;
        Assert.Equal(SyntheticRun.SpeedMetersPerSecond, lastStep, SyntheticRun.SpeedMetersPerSecond);

        var ran = SyntheticRun.DurationSeconds * SyntheticRun.SpeedMetersPerSecond;
        Assert.Equal(ran, result.Report.RepairedDistanceMeters, ran * 0.03);

        AssertMonotone(result);
    }

    // --- Which pass over the fragment, when the athlete ran the route twice.

    [Fact]
    public void A_lap_run_twice_redraws_the_pass_that_broke_and_not_the_other()
    {
        // Both ends of the fragment match the activity twice over, and three of those pairs are in
        // the order they would have to be run in. Only one of them holds the dropout, and only one
        // of the other two is even reachable: the pair that starts on the first lap and ends on the
        // second claims the stretch took three quarters of an hour.
        var lap = SyntheticRun.LapSeconds;
        var blindFrom = lap + 700;
        var blindTo = lap + 1_100;

        var run = SyntheticRun
            .CreateTwoLaps(blindFrom, blindTo)
            .WithTrackCovering(MiddleFragmentFrom, MiddleFragmentTo);

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var redrawn = Assert.Single(result.Report.Zones, zone => zone.Kind == ZoneKind.Estimated);
        Assert.True(
            redrawn.FromSeconds > lap,
            $"the first lap's pass was redrawn instead: {redrawn.FromSeconds:F0}..{redrawn.ToSeconds:F0} s");
        Assert.True(redrawn.FromSeconds <= blindFrom, "the dropout starts before the redrawn stretch");
        Assert.True(redrawn.ToSeconds >= blindTo, "the dropout ends after the redrawn stretch");

        // And the first lap keeps the watch's own metres: its own pass over the same stretch is
        // measured, not redrawn from the fragment a second time.
        Assert.Equal(3, result.Report.Zones.Count);
        AssertMonotone(result);
    }

    // --- Refusals and the threshold.

    [Fact]
    public void A_fragment_whose_ends_find_no_place_is_refused_with_its_own_code()
    {
        // The same stretch of route, four kilometres north: the athlete's own loop is 1.75 km
        // across, so neither end of it comes near a single position of theirs.
        var run = SyntheticRun.Create();
        var elsewhere = Shifted(run.WithTrackCovering(TailFragmentFrom, run.TrueRouteLength), northMeters: 4_000);

        var refusal = Assert.Throws<MalformedArgumentException>(
            () => RepairService.Repair(run.Original, elsewhere));

        Assert.Equal(DiagnosticCodes.TrackAttachesNowhere, refusal.Code);
        Assert.True(DiagnosticCodes.IsBlocking(refusal.Code));
    }

    [Fact]
    public void A_fragment_too_long_for_the_stretch_it_attaches_across_is_refused()
    {
        // The route editor's mistake on a fragment: the same stretch drawn twice over, so its two
        // ends are still the two places the athlete passed but three times as much route is claimed
        // between them. Seven and a half minutes of running cannot hold 4.2 km, and a fragment is
        // used whole or not at all — there is no part of it to fall back to (AD-17, AD-27).
        var run = SyntheticRun
            .Create(withGpsFailure: false)
            .WithoutFixesBetween(MiddleDropoutFrom, MiddleDropoutTo)
            .WithTrackCovering(MiddleFragmentFrom, MiddleFragmentTo)
            .WithTrackDrawn(2);

        var refusal = Assert.Throws<MalformedArgumentException>(
            () => RepairService.Repair(run.Original, run.ReferenceTrack));

        Assert.Equal(DiagnosticCodes.TrackAttachesNowhere, refusal.Code);
    }

    [Fact]
    public void A_frozen_watch_and_a_track_from_another_region_is_refused_rather_than_repaired()
    {
        // Every position the same place from the first second, so the stall detector disbelieves the
        // lot and the anchor search is left with nothing to judge. Its own first position against
        // the whole track is what answers the compatibility question then — without it a route
        // 129 km away was laid over the athlete's time with no warning at all, and AD-22's gate had
        // nothing to withhold the download for.
        var frozen = FrozenFromTheStart(seconds: 400);
        var run = SyntheticRun.Create();
        var anotherRegion = run.TrackPoints.Select(point => new TrackPoint(
            new GeoPoint(point.Position.Latitude + 1.0, point.Position.Longitude + 1.0),
            point.Altitude));

        var refusal = Assert.Throws<MalformedArgumentException>(() => RepairService.Repair(
            frozen, ReferenceTrack.FromPoints(anotherRegion, SyntheticRun.Options)));

        Assert.Equal(DiagnosticCodes.TrackAttachesNowhere, refusal.Code);
    }

    [Fact]
    public void A_fragment_drawn_fifty_metres_off_the_path_still_attaches()
    {
        // What a route drawn by hand after the run looks like: the right stretch, a street's width
        // out. On Ілля's own files the two ends of a real fragment sit 7 m and 44 m from his
        // positions, so a tolerance that only admits GPS error would refuse the very case this
        // story exists for — and the refusal would be the service's, not the file's.
        var run = SyntheticRun.Create();
        var fragment = run.WithTrackCovering(TailFragmentFrom, run.TrueRouteLength);
        var drawnOff = Shifted(fragment, northMeters: 50);

        var result = RepairService.Repair(fragment.Original, drawnOff);

        Assert.InRange(
            result.Alignment.Reach.FromSeconds,
            SyntheticRun.GpsFailureSeconds - 200,
            SyntheticRun.GpsFailureSeconds);
        Assert.Equal(SyntheticRun.DurationSeconds, result.Alignment.Reach.ToSeconds, 0.001);
        Assert.Equal(
            DistanceAt(result, result.Alignment.Reach.FromSeconds) + drawnOff.Length,
            result.Report.RepairedDistanceMeters,
            1.0);

        AssertMonotone(result);
    }

    [Fact]
    public void The_distance_an_endpoint_attaches_within_is_read_from_the_options()
    {
        // The fragment's first point sits on the athlete's own path to within a few metres of GPS
        // error, and its last on the lap's closing metres. A tolerance below that error attaches
        // neither end, and the refusal says so rather than a repair guessing (AD-10, AD-27).
        var run = SyntheticRun.Create();
        var fragment = run.WithTrackCovering(TailFragmentFrom, run.TrueRouteLength);
        var track = fragment.ReferenceTrack;

        var attached = RepairService.Repair(fragment.Original, track, SyntheticRun.Options);
        var exacting = Assert.Throws<MalformedArgumentException>(() => RepairService.Repair(
            fragment.Original, track, SyntheticRun.Options with { TrackEndAttachmentMeters = 0.5 }));

        Assert.False(attached.Alignment.Reach.OpensAtTrackStart);
        Assert.Equal(DiagnosticCodes.TrackAttachesNowhere, exacting.Code);
    }

    // --- The whole-track path, which none of this may disturb.

    [Fact]
    public void A_whole_lap_route_is_not_a_fragment_even_when_the_run_starts_along_it()
    {
        // The whole route, cut 50 m past where the athlete pressed start — a route drawn after the
        // run begins where the editor's first click was, not at the start line. It is still the
        // route: the whole-track mechanism aligns it, its chain of anchors is intact, and the
        // repaired distance is the one the suite pins for this activity.
        var run = SyntheticRun.Create();
        var fiftyMetresAlong = run.WithTrackCovering(50, run.TrueRouteLength);

        var result = RepairService.Repair(fiftyMetresAlong.Original, fiftyMetresAlong.ReferenceTrack);

        Assert.True(result.Alignment.Reach.OpensAtTrackStart);
        Assert.True(result.Alignment.Reach.ClosesAtTrackEnd);
        Assert.True(
            result.Report.AnchorCount > SyntheticRun.DurationSeconds / 2,
            $"{result.Report.AnchorCount} anchors: this was aligned as a fragment, not as a route");
        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.5);

        var zones = result.Report.Zones;
        Assert.Equal(2, zones.Count);
        Assert.Equal(ZoneKind.Measured, zones[0].Kind);
        Assert.Equal(AlignmentOptionsTests.ExpectedMeasuredUntil, zones[0].ToSeconds, 0.001);
    }

    [Fact]
    public void A_whole_route_whose_watch_over_reported_its_distance_is_still_a_whole_route()
    {
        // A footpod reading ten per cent long, which is ordinary, and the route the athlete actually
        // ran. By length alone the route is now "materially shorter than the activity's own
        // distance" — the fragment question's first test — and attaching it by its ends would cover
        // the whole activity with one estimated zone and throw the watch's own geometry away. A
        // stretch that covers everything is not a stretch of anything (AD-26, AD-27).
        var run = SyntheticRun.Create(withGpsFailure: false);
        var track = run.ReferenceTrack;
        var overReporting = run.Original with
        {
            Records = [.. run.Original.Records.Select(record => record with
            {
                Distance = record.Distance * 1.1
            })]
        };

        var honest = RepairService.Repair(run.Original, track);
        var result = RepairService.Repair(overReporting, track);

        Assert.True(
            overReporting.LargestRecordedDistance > track.Length,
            "the fixture no longer over-reports past the track's own length");

        // The watch's own distance figure decides whether the track might be a fragment; it must
        // decide nothing else, so inflating it moves no number of the repair at all.
        Assert.Equal(honest.Report.RepairedDistanceMeters, result.Report.RepairedDistanceMeters, 0.001);
        Assert.Equal(honest.Report.AnchorCount, result.Report.AnchorCount);
        Assert.Equal(honest.Report.Zones.Count, result.Report.Zones.Count);
        Assert.Equal(ZoneKind.Measured, result.Report.Zones[0].Kind);
        Assert.True(
            result.Report.MeasuredFraction > 0.9,
            $"only {result.Report.MeasuredFraction:P0} of the activity stayed the watch's own: this "
                + "was aligned as a fragment rather than as the route");
    }

    [Fact]
    public void An_honest_whole_track_reaches_from_the_first_second_to_the_last()
    {
        var run = SyntheticRun.Create();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var reach = result.Alignment.Reach;
        Assert.True(reach.OpensAtTrackStart);
        Assert.True(reach.ClosesAtTrackEnd);
        Assert.Equal(0, reach.FromSeconds);
        Assert.Equal(SyntheticRun.DurationSeconds, reach.ToSeconds, 0.001);
        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.01);
    }

    // --- Helpers.

    /// <summary>
    /// Outside the fragment's span the repair keeps the watch's own coordinates, to the millimetre
    /// (AD-26) — and so agrees, record for record, with what the same original repairs to without a
    /// fragment at all wherever that repair kept them too.
    /// <para>
    /// The qualification is not a loophole. A whole-track repair interpolates the stretches its own
    /// anchor chain could not reach, and those it draws from the route; the fragment repair has the
    /// watch's own positions there and keeps them, which is a different number and the better one.
    /// So the comparison is made where the two are answering the same question, and the athlete's own
    /// positions are checked everywhere outside the span.
    /// </para>
    /// </summary>
    private static void AssertOutsideTheSpanIsUntouched(RepairResult result, RepairResult whole)
    {
        var reach = result.Alignment.Reach;
        var own = 0;
        var against = 0;

        for (var index = 0; index < result.Records.Count; index++)
        {
            var seconds = (result.Records[index].Timestamp - result.Records[0].Timestamp).TotalSeconds;
            if (seconds >= reach.FromSeconds && seconds <= reach.ToSeconds)
                continue;

            // A record whose own position the cleaning blanked is answered differently by the two
            // repairs on purpose — the whole track draws it from the route, and out here the
            // fragment repair has the watch's own neighbours to bridge it between — so the
            // comparison is over the positions the watch itself kept.
            if (result.CleanedOriginal.Records[index].Position is not { } position)
                continue;

            // The watch's own, and nothing drawn from any track.
            Assert.Equal(position.Latitude, result.Records[index].Latitude, 1e-12);
            Assert.Equal(position.Longitude, result.Records[index].Longitude, 1e-12);
            own++;

            if (KindAt(whole, seconds) != ZoneKind.Measured)
                continue;

            Assert.Equal(whole.Records[index].Latitude, result.Records[index].Latitude, 1e-12);
            Assert.Equal(whole.Records[index].Longitude, result.Records[index].Longitude, 1e-12);
            Assert.Equal(whole.Records[index].Distance, result.Records[index].Distance, 1e-9);
            against++;
        }

        Assert.True(own > 100, $"only {own} of the watch's own positions lie outside the span");
        Assert.True(against > 100, $"only {against} records were compared with the whole-track repair");
    }

    /// <summary>Which kind of zone a second of a repair fell in, by the repair's own report.</summary>
    private static ZoneKind KindAt(RepairResult result, double seconds)
    {
        foreach (var zone in result.Report.Zones)
        {
            if (seconds <= zone.ToSeconds)
                return zone.Kind;
        }

        return result.Report.Zones[^1].Kind;
    }

    /// <summary>The repaired distance at a second of the activity.</summary>
    private static double DistanceAt(RepairResult result, double seconds) =>
        result.Records
            .Last(record => (record.Timestamp - result.Records[0].Timestamp).TotalSeconds <= seconds)
            .Distance;

    /// <summary>The run's own course, moved <paramref name="northMeters"/> away.</summary>
    private static ReferenceTrack Shifted(SyntheticRun run, double northMeters)
    {
        var moved = run.TrackPoints.Select(point => new TrackPoint(
            new GeoPoint(
                point.Position.Latitude + northMeters / Geo.MetersPerDegreeLatitude,
                point.Position.Longitude),
            point.Altitude));

        return ReferenceTrack.FromPoints(moved, SyntheticRun.Options);
    }

    /// <summary>
    /// A watch that reported the same place from its very first second: the stall detector
    /// disbelieves every position of it, so the anchor search has nothing left to look at.
    /// </summary>
    private static OriginalActivity FrozenFromTheStart(int seconds)
    {
        var at = SyntheticRun.Offset(0, 0);
        var records = Enumerable.Range(0, seconds + 1).Select(second => new ActivityRecord
        {
            Timestamp = SyntheticRun.StartTime.AddSeconds(second),
            Latitude = at.Latitude,
            Longitude = at.Longitude,
            HeartRate = 150,
            Cadence = 85
        });

        return new OriginalActivity { Records = [.. records], Sport = SportKind.Running };
    }

    /// <summary>AD-8, which no fragment may break: the distance never decreases.</summary>
    private static void AssertMonotone(RepairResult result)
    {
        for (var index = 1; index < result.Records.Count; index++)
            Assert.True(
                result.Records[index].Distance >= result.Records[index - 1].Distance,
                $"distance fell at record {index}: {result.Records[index - 1].Distance} "
                    + $"→ {result.Records[index].Distance}");
    }
}
