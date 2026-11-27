using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// Story 1.12: the track supplies geometry only where the watch could not (AD-26). Inside a
/// Виміряна зона the records keep their own coordinates and altitude; inside a Розрахована зона
/// they are drawn from the reference track, which contributes the <em>increment</em> between the
/// anchors bounding the zone rather than an absolute position along itself. The invariant under
/// test is which source each record came from — a repair that redrew everything produces a file
/// just as well formed, and only a test can tell the two apart.
/// </summary>
public class GeometryTests
{
    /// <summary>
    /// How close a position has to be to the track to count as drawn from it, m. The repair
    /// interpolates along a segment, so a drawn point is on the line to within the arithmetic.
    /// </summary>
    private const double OnTheTrackMeters = 0.01;

    // --- Which source each record came from.

    [Fact]
    public void Every_record_of_a_measured_zone_keeps_the_watch_s_own_position()
    {
        // The GPS drops out twice and comes back both times, so the activity has both kinds of
        // zone and the claim is about the measured ones only (FR-8).
        var run = SyntheticRun.CreateWithGpsReturning();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var cleaned = result.CleanedOriginal;
        var measured = MeasuredRecords(result);

        Assert.Contains(ZoneKind.Measured, result.Report.Zones.Select(zone => zone.Kind));
        Assert.Contains(ZoneKind.Estimated, result.Report.Zones.Select(zone => zone.Kind));

        var kept = 0;
        for (var index = 0; index < result.Records.Count; index++)
        {
            if (!measured[index] || cleaned.Records[index].Position is not { } own)
                continue;

            // To the millimetre, in degrees: these are the watch's own coordinates carried across,
            // not a projection of them onto anything.
            Assert.Equal(own.Latitude, result.Records[index].Latitude, 1e-12);
            Assert.Equal(own.Longitude, result.Records[index].Longitude, 1e-12);
            Assert.Equal(cleaned.Records[index].Altitude, result.Records[index].Altitude);
            kept++;
        }

        // The fixture is only worth what it covers: nearly the whole run was measured here.
        Assert.True(kept > 2_000, $"only {kept} records of {result.Records.Count} were measured");
    }

    [Fact]
    public void Every_record_of_an_estimated_zone_lies_on_the_track()
    {
        var run = SyntheticRun.CreateWithGpsReturning();
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);
        var measured = MeasuredRecords(result);

        // Each dropout is 45 s of estimated zone and holds 45 records: the 44 the watch wrote no
        // position for, plus the fix that closes the gap — a record on a zone's end belongs to the
        // zone that ends there, so the anchor at the far side of a dropout is drawn along with it
        // (AD-20). One record a seam, on the track within the anchor tolerance either way.
        var estimated = Enumerable.Range(0, result.Records.Count).Where(index => !measured[index]).ToArray();
        Assert.Equal(2 * SyntheticRun.GpsDropoutSeconds, estimated.Length);

        foreach (var index in estimated)
            Assert.InRange(DeviationOf(track, result.Records[index]), 0, OnTheTrackMeters);

        // And the measured records are not on it, which is the whole point: the route was drawn in
        // an editor and the athlete ran a metre or two to one side of it all the way round.
        var offTheTrack = Enumerable.Range(0, result.Records.Count)
            .Where(index => measured[index])
            .Average(index => DeviationOf(track, result.Records[index]));

        Assert.True(
            offTheTrack > 0.5,
            $"the measured records sit {offTheTrack:F3} m from the track on average, so they are "
                + "indistinguishable from records drawn along it");
    }

    [Fact]
    public void An_activity_whose_gps_worked_throughout_is_its_own_geometry_and_its_own_distance()
    {
        // Nothing froze and nothing dropped out, so there is no estimated zone to draw and the
        // track contributes no metre at all.
        var run = SyntheticRun.Create(withGpsFailure: false);
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        Assert.All(result.Report.Zones, zone => Assert.Equal(ZoneKind.Measured, zone.Kind));

        for (var index = 0; index < result.Records.Count; index++)
        {
            var own = run.Original.Records[index].Position!.Value;
            Assert.Equal(own.Latitude, result.Records[index].Latitude, 1e-12);
            Assert.Equal(own.Longitude, result.Records[index].Longitude, 1e-12);
        }

        // The athlete ran 3 m/s for 2160 s, so the distance is their own 6480 m to within the
        // jitter — and not the 6499.5 m of track, which is the number the repair used to end on
        // whatever the GPS had measured (AD-26).
        var ownDistance = SyntheticRun.DurationSeconds * SyntheticRun.SpeedMetersPerSecond;
        Assert.Equal(ownDistance, result.Report.RepairedDistanceMeters, ownDistance * 0.03);
        Assert.True(
            Math.Abs(result.Report.RepairedDistanceMeters - track.Length) > 10,
            $"the distance came to {result.Report.RepairedDistanceMeters:F1} m, within ten metres "
                + $"of the track's {track.Length:F1} m");
    }

    [Fact]
    public void An_activity_with_no_position_at_all_comes_entirely_from_the_track()
    {
        var run = SyntheticRun.Create(withGpsFailure: false, withAnyGps: false);
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        Assert.Contains(DiagnosticCodes.NoOriginalGps, result.Report.Warnings);
        Assert.All(result.Report.Zones, zone => Assert.Equal(ZoneKind.Estimated, zone.Kind));
        Assert.All(result.Records, record => Assert.InRange(DeviationOf(track, record), 0, OnTheTrackMeters));

        // Every metre was drawn from the track, so the sum is exactly the length the repair chose:
        // with no measured zone there is no surplus to add to it.
        Assert.Equal(result.Report.UsedTrackLengthMeters, result.Report.RepairedDistanceMeters, 0.001);
    }

    // --- The seam.

    [Fact]
    public void The_distance_is_continuous_across_every_seam()
    {
        var run = SyntheticRun.CreateWithGpsReturning();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var cap = SyntheticRun.Options.MaxSpeedMetersPerSecond;

        // One record a second, so the sport's cap is the budget for a single step (AD-10). An
        // estimated zone that handed back an absolute track distance would jump by the tens of
        // metres the measured side had accrued of its own.
        var biggest = 0.0;
        var at = 0;
        for (var index = 1; index < result.Records.Count; index++)
        {
            var step = result.Records[index].Distance - result.Records[index - 1].Distance;
            if (step <= biggest)
                continue;

            biggest = step;
            at = index;
        }

        Assert.True(biggest <= cap, $"record {at} gained {biggest:F1} m in one second");

        // And at the seams themselves, where the source of the metres changes, the step is the
        // athlete's own 3 m rather than anything a reader would notice.
        foreach (var boundary in result.Report.Zones.Select(zone => (int)zone.ToSeconds).Where(second => second > 0))
        {
            if (boundary >= result.Records.Count - 1)
                continue;

            var step = result.Records[boundary + 1].Distance - result.Records[boundary].Distance;
            Assert.Equal(SyntheticRun.SpeedMetersPerSecond, step, SyntheticRun.SpeedMetersPerSecond);
        }
    }

    [Fact]
    public void An_estimated_zone_contributes_the_increment_between_the_anchors_that_bound_it()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var seam = result.Report.Zones.Single(zone => zone.Kind == ZoneKind.Measured).ToSeconds;
        var anchored = result.Alignment.MeasuredDistanceMeters;

        // The estimated tail of this run is worth what the track holds between the anchor that
        // ends the measured zone and the finish — and the measured zone keeps its own total, which
        // is what makes the two meet without a jump.
        var estimatedZone = result.Records[^1].Distance - result.Records[(int)seam].Distance;
        Assert.Equal(result.Report.UsedTrackLengthMeters - anchored, estimatedZone, 0.5);
        Assert.Equal(
            AlignmentOptionsTests.ExpectedMeasuredZoneSurplus,
            result.Records[(int)seam].Distance - anchored,
            0.01);
    }

    // --- Monotonicity, the smoothing and the report.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Distance_never_decreases(bool recovering)
    {
        var run = recovering ? SyntheticRun.CreateWithGpsReturning() : SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        // AD-8 survives AD-26: the metres come from two sources now, and a step of either is
        // non-negative by construction.
        for (var index = 1; index < result.Records.Count; index++)
            Assert.True(
                result.Records[index].Distance >= result.Records[index - 1].Distance,
                $"distance fell at record {index}: {result.Records[index - 1].Distance} "
                    + $"→ {result.Records[index].Distance}");
    }

    [Fact]
    public void An_over_long_track_is_still_refused_rather_than_drawn_at_the_finish()
    {
        // The guard of AD-8 that AD-26 keeps: the finish is no longer pinned, so the refusal lives
        // where the anchors are closed on a length. A track five times the route with the whole of
        // it confirmed cannot be reached in this activity's last second.
        var run = SyntheticRun
            .Create(withGpsFailure: false)
            .WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);

        var refusal = Assert.Throws<MalformedArgumentException>(
            () => RepairService.Repair(
                run.Original,
                run.ReferenceTrack,
                TrackLengthMode.Full,
                TrackCoverageMode.Detect));

        Assert.Equal(DiagnosticCodes.DistanceSeriesImplausible, refusal.Code);
    }

    [Fact]
    public void Smoothing_leaves_the_measured_positions_alone_and_still_works_on_the_estimated_ones()
    {
        var run = SyntheticRun.CreateWithGpsReturning();
        var track = run.ReferenceTrack;

        var narrow = RepairService.Repair(
            run.Original, track, SyntheticRun.Options with { EstimatedZoneSmoothingSeconds = 15 });
        var wide = RepairService.Repair(
            run.Original, track, SyntheticRun.Options with { EstimatedZoneSmoothingSeconds = 60 });
        var measured = MeasuredRecords(narrow);

        // A measured record is data: no window of any width touches its position or its distance,
        // because a smoothed real position is an estimate wearing data's clothes (AD-26).
        for (var index = 0; index < measured.Length; index++)
        {
            if (!measured[index])
                continue;

            Assert.Equal(narrow.Records[index].Latitude, wide.Records[index].Latitude, 1e-12);
            Assert.Equal(narrow.Records[index].Longitude, wide.Records[index].Longitude, 1e-12);
        }

        // Inside an estimated zone the series is an interpolation already, and the width still
        // decides what it looks like.
        var estimated = narrow.Report.Zones.First(zone => zone.Kind == ZoneKind.Estimated);
        var inside = (int)Math.Round((estimated.FromSeconds + estimated.ToSeconds) / 2);
        Assert.NotEqual(narrow.Records[inside].Distance, wide.Records[inside].Distance, 0.01);
    }

    [Fact]
    public void The_report_states_the_track_length_and_the_distance_and_they_differ()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        // Both numbers, and legitimately different: a reader of the report must be able to see
        // that the sum is not the track's length and why (AD-26, AD-17).
        Assert.Equal(track.Length, result.Report.TrackLengthMeters, 0.001);
        Assert.Equal(track.Length, result.Report.UsedTrackLengthMeters, 0.001);
        Assert.NotEqual(result.Report.TrackLengthMeters, result.Report.RepairedDistanceMeters, 0.01);
        Assert.Equal(
            AlignmentOptionsTests.ExpectedMeasuredZoneSurplus,
            result.Report.RepairedDistanceMeters - result.Report.TrackLengthMeters,
            0.01);
    }

    // --- Pauses.

    [Fact]
    public void A_pause_earns_no_distance_in_either_kind_of_zone()
    {
        var pauseEnd = SyntheticRun.PauseStartSeconds + SyntheticRun.PauseSeconds;

        // Measured: the watch kept writing fixes while the athlete stood at the light, and their
        // jitter must buy nothing (FR-5, AD-7).
        var measured = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var withFixes = RepairService.Repair(measured.Original, measured.ReferenceTrack);

        Assert.Equal(ZoneKind.Measured, KindAt(withFixes, SyntheticRun.PauseStartSeconds + 1));
        Assert.Equal(
            withFixes.Records[SyntheticRun.PauseStartSeconds].Distance,
            withFixes.Records[pauseEnd].Distance,
            0.001);

        // Estimated: the same pause with no fix written inside it, which leaves the stretch to be
        // interpolated. Spread over moving time, so it stays flat there too.
        var blind = measured.Original with
        {
            Records = [.. measured.Original.Records.Select(record =>
                measured.Original.IsPaused(record.Timestamp)
                    && record.Timestamp > measured.Original.StartTime.AddSeconds(SyntheticRun.PauseStartSeconds)
                    && record.Timestamp < measured.Original.StartTime.AddSeconds(pauseEnd)
                        ? record with { Latitude = null, Longitude = null }
                        : record)]
        };
        var withoutFixes = RepairService.Repair(blind, measured.ReferenceTrack);

        Assert.Equal(ZoneKind.Estimated, KindAt(withoutFixes, SyntheticRun.PauseStartSeconds + 1));
        Assert.Equal(
            withoutFixes.Records[SyntheticRun.PauseStartSeconds].Distance,
            withoutFixes.Records[pauseEnd].Distance,
            0.001);
    }

    // --- Helpers.

    /// <summary>
    /// Which records fall in a measured zone, by the repair's own rule: a record on a zone's end
    /// belongs to the zone that ends there. Derived from the reported zones, so a test cannot
    /// disagree with the service about where a seam is (AD-20).
    /// </summary>
    private static bool[] MeasuredRecords(RepairResult result)
    {
        var start = result.Records[0].Timestamp;
        return [.. result.Records.Select(record =>
            KindAt(result, (record.Timestamp - start).TotalSeconds) == ZoneKind.Measured)];
    }

    /// <inheritdoc cref="MeasuredRecords"/>
    private static ZoneKind KindAt(RepairResult result, double secondsFromStart)
    {
        var zones = result.Report.Zones;
        foreach (var zone in zones)
        {
            if (secondsFromStart <= zone.ToSeconds)
                return zone.Kind;
        }

        return zones[^1].Kind;
    }

    /// <summary>How far a repaired record sits from the reference track, m.</summary>
    private static double DeviationOf(ReferenceTrack track, RepairedRecord record) =>
        track.MinDeviationToWholeTrack(new GeoPoint(record.Latitude, record.Longitude));
}
