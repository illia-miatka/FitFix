using System.Globalization;
using System.Text;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Tests.Fixtures;
using FitFix.Core.Tracks;

namespace FitFix.Core.Tests;

public class TrackReaderTests
{
    private static Stream AsStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static string ToGpx(IReadOnlyList<TrackPoint> points)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">""");
        builder.AppendLine("<trk><trkseg>");
        foreach (var point in points)
        {
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                """<trkpt lat="{0:F7}" lon="{1:F7}"><ele>{2:F1}</ele></trkpt>""",
                point.Position.Latitude, point.Position.Longitude, point.Altitude ?? 0));
        }
        builder.AppendLine("</trkseg></trk></gpx>");
        return builder.ToString();
    }

    private static string ToTcx(IReadOnlyList<TrackPoint> points)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<TrainingCenterDatabase xmlns="http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2">""");
        builder.AppendLine("""<Activities><Activity Sport="Running"><Lap><Track>""");
        foreach (var point in points)
        {
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "<Trackpoint><Position><LatitudeDegrees>{0:F7}</LatitudeDegrees><LongitudeDegrees>{1:F7}</LongitudeDegrees></Position><AltitudeMeters>{2:F1}</AltitudeMeters></Trackpoint>",
                point.Position.Latitude, point.Position.Longitude, point.Altitude ?? 0));
        }
        builder.AppendLine("</Track></Lap></Activity></Activities></TrainingCenterDatabase>");
        return builder.ToString();
    }

    [Fact]
    public void Gpx_is_detected_by_content_and_gives_the_same_length_as_fit()
    {
        var run = SyntheticRun.Create();

        using var gpx = AsStream(ToGpx(run.TrackPoints));
        var fromGpx = TrackReader.ReadPoints(gpx, out var format);

        Assert.Equal(TrackFormat.Gpx, format);
        Assert.Equal(
            run.ReferenceTrack.Length,
            ReferenceTrack.FromPoints(fromGpx, SyntheticRun.Options).Length,
            run.ReferenceTrack.Length * 0.001);
    }

    [Fact]
    public void Tcx_is_detected_by_content()
    {
        var run = SyntheticRun.Create();

        using var tcx = AsStream(ToTcx(run.TrackPoints));
        var points = TrackReader.ReadPoints(tcx, out var format);

        Assert.Equal(TrackFormat.Tcx, format);
        Assert.Equal(run.TrackPoints.Count, points.Count);
    }

    [Fact]
    public void Fit_is_detected_by_content()
    {
        var run = SyntheticRun.Create();
        var bytes = SyntheticFitBuilder.WriteTrack(run.TrackPoints, new System.DateTime(2026, 11, 19, 10, 0, 0, System.DateTimeKind.Utc));

        using var stream = new MemoryStream(bytes);
        var points = TrackReader.ReadPoints(stream, out var format);

        Assert.Equal(TrackFormat.Fit, format);
        Assert.Equal(run.TrackPoints.Count, points.Count);
    }

    [Fact]
    public void A_gpx_renamed_to_fit_is_still_read_as_gpx()
    {
        var run = SyntheticRun.Create();
        using var stream = AsStream(ToGpx(run.TrackPoints));

        // FR-2: формат визначається за вмістом, а не за розширенням.
        var track = TrackReader.ReadReferenceTrack(stream, SyntheticRun.Options);
        Assert.True(track.Length > 6000);
    }

    [Fact]
    public void A_track_with_one_point_is_rejected()
    {
        var run = SyntheticRun.Create();
        using var stream = AsStream(ToGpx([run.TrackPoints[0]]));

        Assert.Throws<InvalidTrackFileException>(
            () => TrackReader.ReadReferenceTrack(stream, SyntheticRun.Options));
    }

    [Fact]
    public void An_unknown_format_is_rejected()
    {
        using var stream = AsStream("hello,world\n1,2\n");
        Assert.Throws<InvalidTrackFileException>(
            () => TrackReader.ReadReferenceTrack(stream, new AlignmentOptions()));
    }

    [Fact]
    public void Repair_works_the_same_from_a_gpx_track()
    {
        var run = SyntheticRun.Create();

        using var gpx = AsStream(ToGpx(run.TrackPoints));
        var trackFromGpx = TrackReader.ReadReferenceTrack(gpx, SyntheticRun.Options);

        var fromGpx = RepairService.Repair(run.Original, trackFromGpx);
        var fromFit = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Equal(fromFit.Report.RepairedDistanceMeters, fromGpx.Report.RepairedDistanceMeters, 5.0);
        Assert.Equal(fromFit.Report.MeasuredUntilSeconds, fromGpx.Report.MeasuredUntilSeconds, 5.0);
    }
}
