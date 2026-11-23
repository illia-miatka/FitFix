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

        var bytes = FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0);
        using var stream = new MemoryStream(bytes);
        var reread = FitActivityReader.Read(stream);

        // SM-2: згенерований файл парситься назад і несе ті самі дані.
        Assert.Equal(result.Records.Count, reread.Records.Count);
        Assert.Equal(result.Records[^1].Distance, reread.Records[^1].Distance!.Value, 1.0);
        Assert.Equal(result.Records[0].HeartRate, reread.Records[0].HeartRate);
        Assert.Equal(SportKind.Running, reread.Sport);

        // The file states the repaired distance, which is a sum of the two sources and sits a
        // little past the track's own length (AD-26) — the writer transcribes it rather than
        // ending the series on the track (AD-16).
        Assert.Equal(
            track.Length + AlignmentOptionsTests.ExpectedMeasuredZoneSurplus,
            reread.Records[^1].Distance!.Value,
            0.01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repaired_file_contains_the_messages_strava_needs(bool lapped)
    {
        // AD-19: file_id → timer start → records → timer stop_all → lap × M → session → activity.
        // M is the number of laps the Original had, and one when it had none (AD-24), so the
        // structure is pinned for both shapes rather than only for the unlapped one.
        var run = lapped
            ? SyntheticRun.Create().WithLapsEndingAt(300, 1500)
            : SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var bytes = FitActivityWriter.WriteToArray(result, utcOffsetSeconds: 0);

        var inspection = FitMessageInspector.Inspect(bytes);

        Assert.Equal(1, inspection.FileIdCount);
        Assert.Equal(2, inspection.TimerEventCount);
        Assert.Equal(result.Records.Count, inspection.RecordCount);
        // Derived from the partition the model produced, not restated by hand beside the
        // boundaries above, so the two cannot drift apart.
        Assert.Equal(run.Original.LapPartition().Count, inspection.LapCount);
        Assert.Equal(1, inspection.SessionCount);
        Assert.Equal(1, inspection.ActivityCount);

        // The order, not only the counts: a file writing its session before its laps carries the
        // very same six numbers and does not import.
        Assert.Equal(
            ["file_id", "event", "record", "event", "lap", "session", "activity"],
            inspection.MessageOrder);
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
