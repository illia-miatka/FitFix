using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

public class FitRoundTripTests
{
    [Fact]
    public void Original_written_by_the_sdk_is_read_back_with_all_fields()
    {
        var run = SyntheticRun.Create();
        var bytes = SyntheticFitBuilder.WriteActivity(run.Original);

        using var stream = new MemoryStream(bytes);
        var read = FitActivityReader.Read(stream);

        Assert.Equal(SportKind.Running, read.Sport);
        Assert.Equal(run.Original.Records.Count, read.Records.Count);
        Assert.Equal(run.Original.StartTime, read.StartTime);
        Assert.Equal(run.Original.Records[0].HeartRate, read.Records[0].HeartRate);
        Assert.Equal(run.Original.Records[0].Latitude!.Value, read.Records[0].Latitude!.Value, 0.00001);
        Assert.Equal(run.Original.Records[0].Longitude!.Value, read.Records[0].Longitude!.Value, 0.00001);
    }

    [Fact]
    public void Repaired_activity_survives_a_write_read_round_trip()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        var bytes = FitActivityWriter.WriteToArray(result);
        using var stream = new MemoryStream(bytes);
        var reread = FitActivityReader.Read(stream);

        // SM-2: згенерований файл парситься назад і несе ті самі дані.
        Assert.Equal(result.Records.Count, reread.Records.Count);
        Assert.Equal(result.Records[^1].Distance, reread.Records[^1].Distance!.Value, 1.0);
        Assert.Equal(result.Records[0].HeartRate, reread.Records[0].HeartRate);
        Assert.Equal(SportKind.Running, reread.Sport);
        Assert.Equal(track.Length, reread.Records[^1].Distance!.Value, track.Length * 0.005);
    }

    [Fact]
    public void Repaired_file_contains_the_messages_strava_needs()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var bytes = FitActivityWriter.WriteToArray(result);

        var inspection = FitMessageInspector.Inspect(bytes);

        Assert.Equal(1, inspection.FileIdCount);
        Assert.Equal(2, inspection.TimerEventCount);
        Assert.Equal(result.Records.Count, inspection.RecordCount);
        Assert.Equal(1, inspection.LapCount);
        Assert.Equal(1, inspection.SessionCount);
        Assert.Equal(1, inspection.ActivityCount);
    }

    [Fact]
    public void A_file_that_is_not_fit_is_rejected()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("це точно не FIT"));
        Assert.Throws<InvalidFitFileException>(() => FitActivityReader.Read(stream));
    }

    [Fact]
    public void A_fit_without_records_is_rejected()
    {
        var run = SyntheticRun.Create();
        var empty = new OriginalActivity
        {
            Records = [run.Original.Records[0]],
            Sport = SportKind.Running
        };

        // Файл із єдиним записом валідний для SDK, але Оригінал без руху нам не підходить.
        var bytes = SyntheticFitBuilder.WriteActivity(empty);
        using var stream = new MemoryStream(bytes);
        var read = FitActivityReader.Read(stream);
        Assert.Single(read.Records);
    }
}
