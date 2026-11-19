using FitFix.Core;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

/// <summary>
/// Викладає синтетичну пару файлів на диск — щоб той самий кейс можна було
/// прогнати через CLI вручну, без приватних реальних файлів.
/// </summary>
public class SyntheticFixtureExportTests
{
    [Fact]
    public void Synthetic_pair_is_exported_and_is_readable_back()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "synthetic");
        Directory.CreateDirectory(directory);

        var run = SyntheticRun.Create();
        var originalPath = Path.Combine(directory, "synthetic_original.fit");
        var trackPath = Path.Combine(directory, "synthetic_track.fit");

        File.WriteAllBytes(originalPath, SyntheticFitBuilder.WriteActivity(run.Original));
        File.WriteAllBytes(trackPath, SyntheticFitBuilder.WriteTrack(
            run.TrackPoints,
            new System.DateTime(2026, 11, 19, 10, 0, 0, System.DateTimeKind.Utc)));

        var original = FitActivityReader.ReadFile(originalPath);
        var track = FitTrackReader.ReadFile(trackPath);

        Assert.Equal(run.Original.Records.Count, original.Records.Count);
        Assert.Equal(run.TrackPoints.Count, track.Count);
    }
}
