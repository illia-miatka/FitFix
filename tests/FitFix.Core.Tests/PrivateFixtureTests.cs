using FitFix.Core;
using FitFix.Core.Fit;
using FitFix.Core.Tracks;

namespace FitFix.Core.Tests;

/// <summary>
/// Тести на реальній парі файлів із кейсу 19.11.2026. Файли приватні (GPS-дані),
/// тому лежать у tests/fixtures/private і не входять у репозиторій — без них тест пропускається.
/// </summary>
public class PrivateFixtureTests
{
    private const string OriginalName = "Run_4k_Chabany.fit";
    private const string TrackName = "FFF.fit";

    /// <summary>SM-1: очікувана довжина правильного треку, м.</summary>
    private const double ExpectedTrackLength = 6537;

    [Fact]
    public void Real_case_is_repaired_to_the_expected_distance_and_time()
    {
        var directory = FindPrivateFixtures();
        var originalPath = Path.Combine(directory, OriginalName);
        var trackPath = Path.Combine(directory, TrackName);

        // xunit 2.9 не вміє динамічно пропускати тест — виходимо тихо, як домовлено в CLAUDE.md.
        if (!File.Exists(originalPath) || !File.Exists(trackPath))
            return;

        var original = FitActivityReader.ReadFile(originalPath);
        var track = TrackReader.ReadReferenceTrackFile(trackPath);
        var result = RepairService.Repair(original, track);

        Assert.Equal(ExpectedTrackLength, track.Length, ExpectedTrackLength * 0.01);
        Assert.Equal(track.Length, result.Report.RepairedDistanceMeters, track.Length * 0.005);
        Assert.Equal(original.ElapsedSeconds, result.Report.ElapsedSeconds, 1.0);

        // Надійна зона має покривати хоча б половину активності, інакше алгоритм втратив якорі.
        Assert.True(
            result.Report.MeasuredFraction > 0.5,
            $"Надійна зона лише {result.Report.MeasuredFraction:P0}");

        for (var i = 1; i < result.Records.Count; i++)
            Assert.True(result.Records[i].Distance >= result.Records[i - 1].Distance);

        // Файл має читатись назад (SM-2).
        var bytes = FitActivityWriter.WriteToArray(result);
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
