using System.Reflection;
using System.Text;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;
using FitFix.Core.Tracks;

namespace FitFix.Core.Tests;

/// <summary>
/// The code registry is a contract the core, the API shell and the UI all read (AD-18, AD-22),
/// so its severities, its completeness and the codes on real refusals are all pinned here.
/// </summary>
public class DiagnosticCodesTests
{
    private const string OnePointGpx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
        <trk><trkseg><trkpt lat="50.3" lon="30.4"><ele>180</ele></trkpt></trkseg></trk>
        </gpx>
        """;

    private const string MalformedGpx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="test">
        <trk><trkseg><trkpt lat="50.3" lon="30.4"/></trkseg></trk>
        </gpxx>
        """;

    private const string OnePointTcx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TrainingCenterDatabase xmlns="http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2">
        <Courses><Course><Track><Trackpoint>
        <Position><LatitudeDegrees>50.3</LatitudeDegrees><LongitudeDegrees>30.4</LongitudeDegrees></Position>
        </Trackpoint></Track></Course></Courses>
        </TrainingCenterDatabase>
        """;

    private static IReadOnlyList<string> DeclaredConstants() =>
    [
        .. typeof(DiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
    ];

    [Fact]
    public void Severities_the_acceptance_criteria_pin()
    {
        Assert.Equal(DiagnosticSeverity.Blocking, DiagnosticCodes.SeverityOf(DiagnosticCodes.TrackFarFromOriginal));
        Assert.Equal(DiagnosticSeverity.Info, DiagnosticCodes.SeverityOf(DiagnosticCodes.NoOriginalGps));
    }

    [Fact]
    public void A_stalled_gps_and_few_anchors_do_not_withhold_the_download()
    {
        // The case this service exists for, and a milder version of no anchors at all:
        // marking either blocking would treat a better input more harshly than a worse one.
        Assert.Equal(DiagnosticSeverity.Info, DiagnosticCodes.SeverityOf(DiagnosticCodes.GpsStallDetected));
        Assert.Equal(DiagnosticSeverity.Info, DiagnosticCodes.SeverityOf(DiagnosticCodes.FewAnchors));
        Assert.False(DiagnosticCodes.IsBlocking(DiagnosticCodes.GpsStallDetected));
        Assert.False(DiagnosticCodes.IsBlocking(DiagnosticCodes.FewAnchors));
    }

    [Fact]
    public void An_over_long_track_blocks_the_download()
    {
        // AD-17: confirming this one does not merely open the gate, it changes the result.
        Assert.True(DiagnosticCodes.IsBlocking(DiagnosticCodes.TrackLongerThanActivity));
    }

    [Fact]
    public void Every_declared_code_has_exactly_one_severity()
    {
        var constants = DeclaredConstants();

        Assert.NotEmpty(constants);
        Assert.Equal(constants.Count, constants.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(constants.Count, DiagnosticCodes.All.Count);
        Assert.All(constants, code => Assert.True(DiagnosticCodes.IsDeclared(code), code));
        Assert.All(DiagnosticCodes.All, code => Assert.False(string.IsNullOrWhiteSpace(code)));
    }

    [Fact]
    public void The_shell_codes_of_ad_22_come_from_the_registry()
    {
        Assert.True(DiagnosticCodes.IsDeclared(DiagnosticCodes.FileTooLarge));
        Assert.True(DiagnosticCodes.IsDeclared(DiagnosticCodes.InternalError));
    }

    [Fact]
    public void The_existing_warning_codes_keep_their_strings()
    {
        Assert.Equal("NO_ORIGINAL_GPS", DiagnosticCodes.NoOriginalGps);
        Assert.Equal("TRACK_FAR_FROM_ORIGINAL", DiagnosticCodes.TrackFarFromOriginal);
        Assert.Equal("GPS_STALL_DETECTED", DiagnosticCodes.GpsStallDetected);
        Assert.Equal("FEW_ANCHORS", DiagnosticCodes.FewAnchors);
    }

    [Fact]
    public void An_undeclared_code_has_no_severity()
    {
        Assert.False(DiagnosticCodes.IsDeclared("NOT_A_REAL_CODE"));
        Assert.Throws<KeyNotFoundException>(() => DiagnosticCodes.SeverityOf("NOT_A_REAL_CODE"));
    }

    [Fact]
    public void Every_exception_declared_in_the_core_derives_from_the_base_type()
    {
        var offenders = typeof(DiagnosticCodes).Assembly.GetTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t))
            .Where(t => !typeof(FitFixException).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void A_file_that_is_not_fit_refuses_with_a_declared_code()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("definitely not a FIT file"));

        var exception = Assert.Throws<InvalidFitFileException>(() => FitActivityReader.Read(stream));

        Assert.Equal(DiagnosticCodes.NotAFitFile, exception.Code);
        Assert.True(DiagnosticCodes.IsDeclared(exception.Code));
    }

    [Fact]
    public void A_track_with_one_point_refuses_with_a_code_not_an_argument_error()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OnePointGpx));

        var exception = Assert.Throws<InvalidTrackFileException>(() => TrackReader.ReadReferenceTrack(stream, new AlignmentOptions()));

        Assert.Equal(DiagnosticCodes.TrackTooFewPoints, exception.Code);
        Assert.True(DiagnosticCodes.IsDeclared(exception.Code));
    }

    [Fact]
    public void A_tcx_with_one_point_refuses_with_a_declared_code()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OnePointTcx));

        var exception = Assert.Throws<InvalidTrackFileException>(() => TrackReader.ReadReferenceTrack(stream, new AlignmentOptions()));

        Assert.Equal(DiagnosticCodes.TrackTooFewPoints, exception.Code);
    }

    [Fact]
    public void A_fit_track_with_one_point_refuses_with_a_declared_code()
    {
        var run = SyntheticRun.Create();
        var bytes = SyntheticFitBuilder.WriteTrack([run.TrackPoints[0]], run.Original.StartTime);
        using var stream = new MemoryStream(bytes);

        var exception = Assert.Throws<InvalidTrackFileException>(() => FitTrackReader.Read(stream));

        Assert.Equal(DiagnosticCodes.TrackTooFewPoints, exception.Code);
    }

    [Fact]
    public void A_track_in_no_known_format_refuses_with_a_declared_code()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello,world\n1,2\n"));

        var exception = Assert.Throws<InvalidTrackFileException>(() => TrackReader.ReadReferenceTrack(stream, new AlignmentOptions()));

        Assert.Equal(DiagnosticCodes.TrackFormatUnknown, exception.Code);
    }

    [Fact]
    public void A_track_that_is_not_well_formed_xml_refuses_with_a_declared_code()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MalformedGpx));

        var exception = Assert.Throws<InvalidTrackFileException>(() => TrackReader.ReadReferenceTrack(stream, new AlignmentOptions()));

        Assert.Equal(DiagnosticCodes.TrackFormatUnknown, exception.Code);
        Assert.IsType<System.Xml.XmlException>(exception.InnerException);
    }

    [Fact]
    public void A_fit_file_that_fails_its_integrity_check_refuses_with_a_declared_code()
    {
        var run = SyntheticRun.Create();
        var bytes = SyntheticFitBuilder.WriteActivity(run.Original);

        // The header still says FIT, so IsFIT passes and the CRC over the body does not.
        bytes[^1] ^= 0xFF;
        using var stream = new MemoryStream(bytes);

        var exception = Assert.Throws<InvalidFitFileException>(() => FitActivityReader.Read(stream));

        Assert.Equal(DiagnosticCodes.FitIntegrityFailed, exception.Code);
    }

    [Fact]
    public void A_track_without_two_distinct_points_refuses_with_a_code()
    {
        var duplicated = new TrackPoint(new GeoPoint(50.3, 30.4), 180);

        var exception = Assert.Throws<MalformedArgumentException>(
            () => ReferenceTrack.FromPoints([duplicated, duplicated], new AlignmentOptions()));

        // Its own code: the points existed and the de-duplication collapsed them.
        Assert.Equal(DiagnosticCodes.TrackWithoutDistinctPoints, exception.Code);
    }

    [Fact]
    public void Writing_an_activity_without_two_records_refuses_with_a_code()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);
        var single = new RepairResult
        {
            Records = [result.Records[0]],
            Report = result.Report,
            Alignment = result.Alignment,
            Sport = result.Sport
        };

        var exception = Assert.Throws<MalformedArgumentException>(() => FitActivityWriter.WriteToArray(single, utcOffsetSeconds: 0));

        Assert.Equal(DiagnosticCodes.ActivityTooFewRecords, exception.Code);
    }

    [Fact]
    public void An_activity_whose_records_share_one_timestamp_refuses_with_a_code()
    {
        var run = SyntheticRun.Create();
        var frozen = new OriginalActivity
        {
            Records = [.. run.Original.Records.Take(5).Select(r => r with { Timestamp = run.Original.StartTime })],
            Sport = SportKind.Running
        };

        // Without the refusal the interpolation is left with a single anchor and the activity
        // surfaces as a bare ArgumentException, which the CLI then reports as a bad path.
        var exception = Assert.Throws<MalformedArgumentException>(
            () => RepairService.Repair(frozen, run.ReferenceTrack));

        Assert.Equal(DiagnosticCodes.ActivityWithoutElapsedTime, exception.Code);
        Assert.True(DiagnosticCodes.IsDeclared(exception.Code));
    }

    [Fact]
    public void Warnings_a_repair_raises_are_declared_codes()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.All(result.Report.Warnings, code => Assert.True(DiagnosticCodes.IsDeclared(code), code));
    }
}
