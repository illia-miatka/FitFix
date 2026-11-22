using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// Story 1.9: trust is a list of Зони довіри rather than a single boundary (FR-4, FR-8). Real GPS
/// drops out, returns and drops again, so the report carries the stretches themselves — each one
/// measured or estimated, together covering the activity exactly once — and the measured share is
/// their sum rather than a prefix of the clock.
/// </summary>
public class ZoneTests
{
    // --- The tiling.

    [Fact]
    public void A_gps_that_returns_leaves_more_than_two_zones_tiling_the_activity()
    {
        var run = SyntheticRun.CreateWithGpsReturning();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        // Nothing froze, so no stall discarded the anchors after the first dropout: the fixture
        // has to be a recovery and not a failure wearing one (AD-25).
        Assert.Null(result.Report.GpsStallAtSeconds);

        var zones = result.Report.Zones;
        AssertTiles(zones, result.Report.ElapsedSeconds);

        // Three measured stretches with the two dropouts between them — the shape a boundary
        // cannot express at all.
        Assert.True(zones.Count > 2, $"the recovery produced only {zones.Count} zone(s)");
        Assert.Equal(3, zones.Count(zone => zone.Kind == ZoneKind.Measured));
        Assert.Equal(2, zones.Count(zone => zone.Kind == ZoneKind.Estimated));

        // And they are the dropouts the fixture wrote, not stretches the thresholds invented.
        var estimated = zones.Where(zone => zone.Kind == ZoneKind.Estimated).ToArray();
        Assert.Equal(SyntheticRun.FirstGpsDropoutSeconds, estimated[0].FromSeconds, 1.0);
        Assert.Equal(SyntheticRun.GpsDropoutSeconds, estimated[0].ToSeconds - estimated[0].FromSeconds, 1.0);
        Assert.Equal(SyntheticRun.SecondGpsDropoutSeconds, estimated[1].FromSeconds, 1.0);
        Assert.Equal(SyntheticRun.GpsDropoutSeconds, estimated[1].ToSeconds - estimated[1].FromSeconds, 1.0);
    }

    [Fact]
    public void An_activity_with_no_position_at_all_is_one_estimated_zone()
    {
        var run = SyntheticRun.Create(withGpsFailure: false, withAnyGps: false);

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Contains(DiagnosticCodes.NoOriginalGps, result.Report.Warnings);

        var zone = Assert.Single(result.Report.Zones);
        Assert.Equal(ZoneKind.Estimated, zone.Kind);
        Assert.Equal(0, zone.FromSeconds);
        Assert.Equal(result.Report.ElapsedSeconds, zone.ToSeconds, 1e-9);

        // Nothing was measured, so the share is nothing — and a watch that never found a fix must
        // not be shown a percentage the first synthetic anchor at the start line would have earned.
        Assert.Equal(0, result.Report.MeasuredFraction);
    }

    [Fact]
    public void An_honest_run_is_measured_except_where_its_gps_froze()
    {
        var run = SyntheticRun.Create();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var zones = result.Report.Zones;
        AssertTiles(zones, result.Report.ElapsedSeconds);

        // One measured stretch and one estimate after it: this run's GPS fails once and never
        // comes back, which is the only case the boundary ever described correctly.
        Assert.Equal(2, zones.Count);
        Assert.Equal(ZoneKind.Measured, zones[0].Kind);
        Assert.Equal(ZoneKind.Estimated, zones[1].Kind);
        Assert.Equal(AlignmentOptionsTests.ExpectedMeasuredUntil, zones[0].ToSeconds, 0.001);

        // The stall the detector found falls inside the estimated zone rather than beside it.
        var stall = Assert.NotNull(result.Report.GpsStallAtSeconds);
        Assert.InRange(stall, zones[1].FromSeconds, zones[1].ToSeconds);
    }

    // --- The threshold.

    [Fact]
    public void The_gap_threshold_decides_whether_a_stretch_was_measured()
    {
        // Smart recording: one point every twenty seconds, the GPS healthy throughout. Whether
        // that is a measured run or an interpolated one is the threshold's decision and nothing
        // else's (AD-10).
        var run = SyntheticRun.Create(withGpsFailure: false).WithRecordsEvery(20);
        var track = run.ReferenceTrack;

        var aboveTheSpacing = RepairService.Repair(
            run.Original, track, SyntheticRun.Options with { EstimatedZoneGapSeconds = 30 });
        var belowTheSpacing = RepairService.Repair(
            run.Original, track, SyntheticRun.Options with { EstimatedZoneGapSeconds = 10 });

        // Same activity, same anchors: only the classification moved.
        Assert.Equal(aboveTheSpacing.Report.AnchorCount, belowTheSpacing.Report.AnchorCount);

        AssertTiles(aboveTheSpacing.Report.Zones, aboveTheSpacing.Report.ElapsedSeconds);
        AssertTiles(belowTheSpacing.Report.Zones, belowTheSpacing.Report.ElapsedSeconds);

        Assert.True(
            aboveTheSpacing.Report.MeasuredFraction > 0.9,
            $"a threshold above the spacing measured only {aboveTheSpacing.Report.MeasuredFraction:P0}");

        var estimatedThroughout = Assert.Single(belowTheSpacing.Report.Zones);
        Assert.Equal(ZoneKind.Estimated, estimatedThroughout.Kind);
        Assert.Equal(0, belowTheSpacing.Report.MeasuredFraction);
    }

    // --- The share.

    [Fact]
    public void The_measured_share_is_the_sum_of_the_measured_zones_and_not_a_prefix()
    {
        var run = SyntheticRun.CreateWithGpsReturning();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        var measuredSeconds = result.Report.Zones
            .Where(zone => zone.Kind == ZoneKind.Measured)
            .Sum(zone => zone.ToSeconds - zone.FromSeconds);

        Assert.Equal(
            measuredSeconds / result.Report.ElapsedSeconds,
            result.Report.MeasuredFraction,
            1e-9);

        // Both dropouts are missing from the share: counted as two stretches of 45 s, they are
        // the 90 s the activity was not measured for.
        Assert.Equal(
            result.Report.ElapsedSeconds - 2 * SyntheticRun.GpsDropoutSeconds,
            measuredSeconds,
            2.0);

        // And the number a prefix would have given is a different number. The last anchor of this
        // run sits at its final second, so "measured until" would have called the whole activity
        // measured and hidden both dropouts from the athlete.
        var asAPrefix = result.Alignment.MeasuredUntilSeconds / result.Report.ElapsedSeconds;
        Assert.True(
            asAPrefix - result.Report.MeasuredFraction > 0.03,
            $"a prefix claimed {asAPrefix:P1} and the zones {result.Report.MeasuredFraction:P1}: "
                + "the fixture no longer distinguishes the two models");
        Assert.True(
            result.Report.MeasuredFraction < 1.0,
            "a run with two dropouts cannot have been measured throughout");
    }

    // --- Nothing moved behind the zones.

    [Fact]
    public void The_distances_and_the_written_file_are_where_they_were()
    {
        // The honest run is one measured prefix and one estimate, so the per-zone smoothing has to
        // reproduce exactly what the single boundary produced — the zones changed what is reported
        // about the repair, not what the repair does (AD-21).
        var run = SyntheticRun.Create();

        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var session = FitMessageInspector
            .InspectTiming(FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0))
            .Session;

        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.01);
        Assert.Equal(AlignmentOptionsTests.ExpectedAnchorCount, result.Report.AnchorCount);
        Assert.Equal(AlignmentOptionsTests.ExpectedRepairedDistance, session.DistanceMeters!.Value, 0.01);
        Assert.Equal(SyntheticRun.DurationSeconds, session.ElapsedSeconds!.Value, 0.001);
    }

    // --- Helpers.

    [Fact]
    public void The_window_a_record_is_smoothed_with_comes_from_its_own_zone()
    {
        // Deleting the per-zone width, or classifying every record by the first zone, left all
        // 217 tests green: nothing observed the widening, so the kinks inside a dropout that
        // later recovered stayed as sharp as the interpolation made them (AD-21).
        var run = SyntheticRun.CreateWithGpsReturning();

        var narrow = RepairService.Repair(
            run.Original, run.ReferenceTrack, SyntheticRun.Options with { EstimatedZoneSmoothingSeconds = 15 });
        var wide = RepairService.Repair(
            run.Original, run.ReferenceTrack, SyntheticRun.Options with { EstimatedZoneSmoothingSeconds = 60 });

        var estimated = narrow.Report.Zones.First(zone => zone.Kind == ZoneKind.Estimated);
        var inside = (int)Math.Round((estimated.FromSeconds + estimated.ToSeconds) / 2);
        var measured = narrow.Report.Zones.First(zone => zone.Kind == ZoneKind.Measured);
        var deepInMeasured = (int)Math.Round(measured.FromSeconds + (measured.ToSeconds - measured.FromSeconds) / 2);

        Assert.NotEqual(narrow.Records[inside].Distance, wide.Records[inside].Distance, 0.01);
        Assert.Equal(narrow.Records[deepInMeasured].Distance, wide.Records[deepInMeasured].Distance, 1e-9);
    }

    /// <summary>
    /// The invariant the whole model rests on: the zones are in time order, each covers some time,
    /// adjacent ones meet exactly and differ in kind, and together they cover
    /// <c>[0, <paramref name="elapsedSeconds"/>]</c> once — no gap, no overlap, nothing counted
    /// twice.
    /// </summary>
    private static void AssertTiles(IReadOnlyList<TrustZone> zones, double elapsedSeconds)
    {
        Assert.NotEmpty(zones);
        Assert.Equal(0, zones[0].FromSeconds);
        Assert.Equal(elapsedSeconds, zones[^1].ToSeconds, 1e-9);

        for (var index = 0; index < zones.Count; index++)
        {
            Assert.True(
                zones[index].ToSeconds > zones[index].FromSeconds,
                $"zone {index} covers no time at all: {zones[index]}");

            if (index == 0)
                continue;

            Assert.Equal(zones[index - 1].ToSeconds, zones[index].FromSeconds, 1e-9);
            Assert.NotEqual(zones[index - 1].Kind, zones[index].Kind);
        }
    }
}
