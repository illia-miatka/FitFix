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
    [Theory]
    [InlineData("Run_4k_Chabany.fit", "FFF.fit", 6537.0)]
    [InlineData("Run_5k_Chabany.fit", "Route_6_32km_Kyiv.gpx", 6320.0)]
    public void A_real_pair_is_repaired_to_a_plausible_distance_and_the_original_time(
        string originalName,
        string trackName,
        double expectedTrackLength)
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
        // repaired distance came in 0.8 % under the chosen length.
        Assert.InRange(
            result.Report.RepairedDistanceMeters,
            result.Report.UsedTrackLengthMeters * 0.97,
            result.Report.UsedTrackLengthMeters * 1.03);

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
