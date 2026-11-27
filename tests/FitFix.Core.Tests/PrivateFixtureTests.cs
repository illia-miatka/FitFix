using FitFix.Core;
using FitFix.Core.Fit;
using FitFix.Core.Tracks;

namespace FitFix.Core.Tests;

/// <summary>
/// Tests over real pairs of files. The files carry GPS traces, so they live in
/// tests/fixtures/private, are outside the repository, and a pair that is absent
/// skips its row silently.
/// </summary>
public class PrivateFixtureTests
{
    /// <summary>
    /// The two pairs Ілля has repaired by hand: the 19.11.2026 case (SM-1, a FIT track from
    /// an online editor) and the 23.11.2026 case (a GPX route drawn after the run), whose
    /// expected length is the one the source tool reported.
    /// </summary>
    /// <param name="expectedRepairedDistance">
    /// The metres this pair repairs to, or 0 where nobody has measured them on this machine. A
    /// window alone is not enough: Story 5.1 moved this pair by 29 m with every zone, anchor and
    /// warning identical, and only a pinned figure makes a change like that announce itself.
    /// </param>
    [Theory]
    [InlineData("Run_4k_Chabany.fit", "FFF.fit", 6537.0, 0.0)]
    [InlineData("Run_5k_Chabany.fit", "Route_6_32km_Kyiv.gpx", 6320.0, 6324.23)]
    public void A_real_pair_is_repaired_to_a_plausible_distance_and_the_original_time(
        string originalName,
        string trackName,
        double expectedTrackLength,
        double expectedRepairedDistance)
    {
        var directory = FindPrivateFixtures();
        var originalPath = Path.Combine(directory, originalName);
        var trackPath = Path.Combine(directory, trackName);

        // xunit 2.9 cannot skip a row at run time -- leave quietly, as CLAUDE.md settles it.
        if (!File.Exists(originalPath) || !File.Exists(trackPath))
            return;

        var original = FitActivityReader.ReadFile(originalPath);
        var track = TrackReader.ReadReferenceTrackFile(trackPath, AlignmentOptions.ForSport(original.Sport));
        var result = RepairService.Repair(original, track);

        Assert.Equal(expectedTrackLength, track.Length, expectedTrackLength * 0.01);
        Assert.Equal(original.ElapsedSeconds, result.Report.ElapsedSeconds, 1.0);

        // Since story 1.12 the distance is a sum and not the track's length (AD-26): a measured
        // stretch is measured along the watch's own positions, so it both gains the metres real
        // GPS jitter adds and loses the metres a drawn polyline spends going the long way round.
        // Either direction is data; 3 % each way is AD-26's budget for it, and past that the
        // positions are the problem rather than the track. Measured on the 23.11.2026 pair: the
        // repaired distance comes in 0.33 % under the chosen length.
        Assert.InRange(
            result.Report.RepairedDistanceMeters,
            result.Report.UsedTrackLengthMeters * 0.97,
            result.Report.UsedTrackLengthMeters * 1.03);

        if (expectedRepairedDistance > 0)
            Assert.Equal(expectedRepairedDistance, result.Report.RepairedDistanceMeters, 0.5);

        // The measured zone has to cover at least half the activity, or the algorithm lost its
        // anchors: both pairs keep the watch's own geometry over most of the run.
        Assert.True(
            result.Report.MeasuredFraction > 0.5,
            $"The measured zone covers only {result.Report.MeasuredFraction:P0}");

        for (var i = 1; i < result.Records.Count; i++)
            Assert.True(result.Records[i].Distance >= result.Records[i - 1].Distance);

        // The file has to read back (SM-2).
        var bytes = FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0);
        using var stream = new MemoryStream(bytes);
        var reread = FitActivityReader.Read(stream);
        Assert.Equal(result.Records.Count, reread.Records.Count);
    }

    /// <summary>
    /// Story 5.1 on the real pair, with the reference track cut down to a stretch of itself (AD-27).
    /// The cut is made here rather than kept as a file of its own: the route carries Ілля's own GPS
    /// and nothing of it may enter the repository, and slicing the private route by its own
    /// cumulative distance is the same fragment either way.
    /// <para>
    /// The two rows are the two shapes the real activity offers. Route metres 4905–6139 are the
    /// stretch the watch lost — its own distance stops at 4793.9 m around 1697 s and its positions
    /// come and go from 1699 s to the finish — so the fragment attaches across the failure. Route
    /// metres 2505–3482 are a stretch nothing was wrong with, run between about 828 s and 1122 s:
    /// the fragment attaches there, because that is where that geometry is, and redraws a stretch
    /// the watch had already measured. Neither pair may be read as covering the other's stretch.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(4_905.0, 6_139.0, 1_612.0, 1_976.0)]
    [InlineData(2_505.0, 3_482.0, 828.0, 1_122.0)]
    public void A_fragment_of_a_real_route_repairs_its_own_stretch_and_leaves_the_rest_alone(
        double fromMetres,
        double toMetres,
        double expectedSpanFrom,
        double expectedSpanTo)
    {
        var directory = FindPrivateFixtures();
        var originalPath = Path.Combine(directory, "Run_5k_Chabany.fit");
        var routePath = Path.Combine(directory, "Route_6_32km_Kyiv.gpx");

        if (!File.Exists(originalPath) || !File.Exists(routePath))
            return;

        var original = FitActivityReader.ReadFile(originalPath);
        var options = AlignmentOptions.ForSport(original.Sport);
        var route = TrackReader.ReadReferenceTrackFile(routePath, options);
        var fragment = Slice(route, fromMetres, toMetres, options);

        var whole = RepairService.Repair(original, route);
        var result = RepairService.Repair(original, fragment);

        // The stretch the fragment is laid over is the stretch it is a drawing of.
        var reach = result.Alignment.Reach;
        Assert.Equal(expectedSpanFrom, reach.FromSeconds, 15.0);
        Assert.Equal(expectedSpanTo, reach.ToSeconds, 15.0);
        Assert.False(reach.OpensAtTrackStart);
        Assert.False(reach.ClosesAtTrackEnd);

        // Three zones: the watch's own, the redrawn stretch, the watch's own again (AD-26).
        var zones = result.Report.Zones;
        Assert.Equal(3, zones.Count);
        Assert.Equal(ZoneKind.Measured, zones[0].Kind);
        Assert.Equal(ZoneKind.Estimated, zones[1].Kind);
        Assert.Equal(ZoneKind.Measured, zones[2].Kind);
        Assert.Equal(reach.FromSeconds, zones[1].FromSeconds, 0.001);
        Assert.Equal(reach.ToSeconds, zones[1].ToSeconds, 0.001);

        // Outside the span the repair is the athlete's own file: its coordinates to the millimetre,
        // and the same numbers the whole route produces wherever that repair also kept them rather
        // than interpolating a dropout of its own.
        var start = result.Records[0].Timestamp;
        var own = 0;
        var against = 0;

        for (var index = 0; index < result.Records.Count; index++)
        {
            var seconds = (result.Records[index].Timestamp - start).TotalSeconds;
            if (seconds >= reach.FromSeconds && seconds <= reach.ToSeconds)
                continue;

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
            against++;
        }

        Assert.True(own > 500, $"only {own} of the watch's own positions lie outside the span");
        Assert.True(against > 500, $"only {against} records were compared with the whole route's");

        // And the metres: the fragment's own length across the span, the watch's own either side,
        // so the total is the sum of the two sources and nothing was invented (AD-26).
        Assert.Equal(
            fragment.Length,
            DistanceAt(result, reach.ToSeconds) - DistanceAt(result, reach.FromSeconds),
            2.0);
        Assert.Equal(
            DistanceAt(result, reach.FromSeconds)
                + fragment.Length
                + (result.Records[^1].Distance - DistanceAt(result, reach.ToSeconds)),
            result.Report.RepairedDistanceMeters,
            2.0);

        // A repaired run has to stay a run: monotone, and with its own time (AD-8).
        Assert.Equal(original.ElapsedSeconds, result.Report.ElapsedSeconds, 1.0);
        for (var index = 1; index < result.Records.Count; index++)
            Assert.True(
                result.Records[index].Distance >= result.Records[index - 1].Distance,
                $"distance fell at record {index}");
    }

    /// <summary>The stretch of <paramref name="route"/> between two of its own cumulative distances.</summary>
    private static ReferenceTrack Slice(
        ReferenceTrack route,
        double fromMetres,
        double toMetres,
        AlignmentOptions options)
    {
        var points = route.Points
            .Select((point, index) => (Point: point, Metre: route.Cumulative[index]))
            .Where(pair => pair.Metre >= fromMetres && pair.Metre <= toMetres)
            .Select(pair => new TrackPoint(pair.Point, null))
            .ToArray();

        return ReferenceTrack.FromPoints(points, options);
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

    private static string FindPrivateFixtures()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "private");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "fixtures", "private");
    }
}
