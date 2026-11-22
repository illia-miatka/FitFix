using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FitFix.Api.Contracts;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Api.Tests;

/// <summary>
/// Story 2.2: <c>POST /api/inspect</c> says what the core read from one file, in the role the
/// request named. Over real HTTP, because a contract is an HTTP shape: the parts of the request,
/// the status of a refusal and the members of the body are the thing under test.
/// </summary>
public class InspectTests : IDisposable
{
    /// <summary>The form field the file travels in; the endpoint reads the part, not its name.</summary>
    private const string FileField = "file";

    /// <summary>A name that must never reach a log, and a lie about the format besides (AD-9, AD-13).</summary>
    private const string UploadedFileName = "chabany-secret-run.fit";

    private readonly Lazy<ApiHost> _host = new(() => new ApiHost());

    private ApiHost Host => _host.Value;

    public void Dispose()
    {
        if (_host.IsValueCreated)
            _host.Value.Dispose();
    }

    [Fact]
    public async Task An_original_is_described_with_the_numbers_the_core_read()
    {
        var bytes = SyntheticFitBuilder.WriteActivity(SyntheticRun.Create().Original);

        // The same bytes through the core directly: the answer must quote these numbers, not
        // numbers of its own (AD-15).
        using var read = new MemoryStream(bytes);
        var activity = FitActivityReader.Read(read);

        using var response = await InspectAsync(bytes, FileRole.Original);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        Assert.Equal(FileRole.Original, body.GetProperty("role").GetString());
        Assert.Equal("running", body.GetProperty("sport").GetString());
        Assert.Equal(
            activity.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            body.GetProperty("startedAt").GetString());
        Assert.Equal(activity.ElapsedSeconds, body.GetProperty("elapsedSeconds").GetDouble());
        Assert.Equal(activity.RecordedDistance, body.GetProperty("recordedDistanceMeters").GetDouble());
        Assert.Equal(activity.Records.Count, body.GetProperty("recordCount").GetInt32());

        // The run's GPS freezes halfway, which leaves every position plausible: there is one to
        // anchor with, nothing was thrown away, and so there is nothing to report about it.
        Assert.True(body.GetProperty("hasPosition").GetBoolean());
        Assert.Equal(0, body.GetProperty("discardedPositionCount").GetInt32());
        Assert.Empty(Codes(body));
    }

    [Theory]
    [InlineData("fit")]
    [InlineData("gpx")]
    [InlineData("tcx")]
    public async Task A_track_is_described_in_every_format_the_core_recognises(string format)
    {
        var run = SyntheticRun.Create();
        var expected = run.ReferenceTrack;

        using var response = await InspectAsync(TrackBytes(run, format, withAltitude: true), FileRole.Track);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FileRole.Track, body.GetProperty("role").GetString());
        Assert.Equal(format, body.GetProperty("format").GetString());
        Assert.Equal(expected.Points.Count, body.GetProperty("pointCount").GetInt32());
        Assert.True(body.GetProperty("hasAltitude").GetBoolean());

        // The three formats carry the same geometry at their own precision — semicircles for FIT,
        // seven decimal places for the two XML ones — so the lengths agree to a tenth of a
        // percent rather than exactly.
        Assert.Equal(expected.Length, body.GetProperty("lengthMeters").GetDouble(), expected.Length * 0.001);
    }

    [Fact]
    public async Task The_length_at_the_dropzone_is_the_length_the_repair_spreads_the_activity_over()
    {
        // AD-15: one de-duplication, one length. The original here is a ride, so the repair reads
        // the track with the cycling profile while inspect reads it with the defaults — with a run
        // the two option sets are the same numbers and this test would prove nothing.
        var ride = SyntheticRun.CreateRide();
        var track = TrackBytes(ride, "gpx", withAltitude: true);
        var original = SyntheticFitBuilder.WriteActivity(ride.Original);

        using var response = await InspectAsync(track, FileRole.Track);
        var inspected = (await ReadJsonAsync(response)).GetProperty("lengthMeters").GetDouble();

        using var originalStream = new MemoryStream(original);
        using var trackStream = new MemoryStream(track);
        var repaired = RepairPipeline.Repair(originalStream, trackStream).Report.TrackLengthMeters;

        Assert.Equal(repaired, inspected, 0.001);
    }

    [Fact]
    public async Task A_ride_is_judged_as_a_ride()
    {
        // Every step of this file is impossible at the running cap and ordinary at the cycling
        // one, so an inspect that cleaned with the default thresholds would report a route thrown
        // away that the repair of the same bytes keeps — the disagreement AD-15 forbids.
        var ride = SyntheticRun.CreateRide();
        var bytes = SyntheticFitBuilder.WriteActivity(ride.Original);

        using var response = await InspectAsync(bytes, FileRole.Original);
        var body = await ReadJsonAsync(response);

        using var originalStream = new MemoryStream(bytes);
        using var trackStream = new MemoryStream(TrackBytes(ride, "gpx", withAltitude: true));
        var repaired = RepairPipeline.Repair(originalStream, trackStream).Report;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("cycling", body.GetProperty("sport").GetString());
        Assert.Equal(repaired.DiscardedPositionCount, body.GetProperty("discardedPositionCount").GetInt32());
        Assert.True(body.GetProperty("hasPosition").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("whatever")]
    public async Task The_role_is_never_guessed(string? role)
    {
        // A FIT file is valid as an original and as a track, so a server that guessed would
        // answer one dropzone with the other's fields (AD-15).
        var bytes = SyntheticFitBuilder.WriteActivity(SyntheticRun.Create().Original);

        using var response = await InspectAsync(bytes, role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.InspectRoleInvalid, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Two_roles_state_no_role_at_all()
    {
        var bytes = SyntheticFitBuilder.WriteActivity(SyntheticRun.Create().Original);
        using var content = Upload(bytes);
        content.Add(new StringContent(FileRole.Original), InspectEndpoint.RoleField);
        content.Add(new StringContent(FileRole.Track), InspectEndpoint.RoleField);

        using var response = await Host.Client.PostAsync(InspectEndpoint.Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.InspectRoleInvalid, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task The_content_type_and_the_file_name_decide_nothing()
    {
        // AD-13: the shell checks the size and the parts, the core reads the bytes. Both uploads
        // below lie about themselves in every way a browser can.
        var run = SyntheticRun.Create();

        using var gpx = await InspectAsync(
            TrackBytes(run, "gpx", withAltitude: true),
            FileRole.Track,
            contentType: "application/octet-stream",
            fileName: "route.fit");

        Assert.Equal(HttpStatusCode.OK, gpx.StatusCode);
        Assert.Equal("gpx", (await ReadJsonAsync(gpx)).GetProperty("format").GetString());

        using var fit = await InspectAsync(
            SyntheticFitBuilder.WriteActivity(run.Original),
            FileRole.Original,
            contentType: "text/plain",
            fileName: "activity.txt");

        Assert.Equal(HttpStatusCode.OK, fit.StatusCode);
        Assert.Equal("running", (await ReadJsonAsync(fit)).GetProperty("sport").GetString());
    }

    [Fact]
    public async Task A_track_offered_as_the_original_refuses_with_the_cores_own_code()
    {
        var gpx = TrackBytes(SyntheticRun.Create(), "gpx", withAltitude: true);

        using var response = await InspectAsync(gpx, FileRole.Original);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.NotAFitFile, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_file_with_no_geometry_offered_as_the_track_refuses_with_the_cores_own_code()
    {
        // A positioned FIT activity is a legitimate track — that is how the real case's track
        // arrived, exported as an activity — so the refusal is measured with an activity that
        // carries no position at all: a file with nothing to borrow geometry from.
        var bytes = SyntheticFitBuilder.WriteActivity(SyntheticRun.Create(withAnyGps: false).Original);

        using var response = await InspectAsync(bytes, FileRole.Track);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TrackTooFewPoints, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task An_activity_with_no_gps_says_so_instead_of_failing()
    {
        var bytes = SyntheticFitBuilder.WriteActivity(SyntheticRun.Create(withAnyGps: false).Original);

        using var response = await InspectAsync(bytes, FileRole.Original);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("hasPosition").GetBoolean());
        // Nothing was thrown away: there was nothing to throw. A dropzone can tell this file from
        // the one below, which is the distinction AD-25 asked the count for, and the codes say it
        // in the vocabulary the result screen already uses.
        Assert.Equal(0, body.GetProperty("discardedPositionCount").GetInt32());
        Assert.Equal([DiagnosticCodes.NoOriginalGps], Codes(body));
        // The device measured no distance either, and a file that measured nothing has not
        // measured zero metres.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("recordedDistanceMeters").ValueKind);
    }

    [Fact]
    public async Task An_activity_of_nothing_but_teleports_has_no_position_left()
    {
        var garbage = SyntheticRun.Create().WithNothingButGarbage().Original;
        var bytes = SyntheticFitBuilder.WriteActivity(garbage);

        using var response = await InspectAsync(bytes, FileRole.Original);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Saying "GPS present" here would promise a map the repair cannot draw (AD-25), and the
        // count is what tells the user something was thrown away before they waited for a repair.
        Assert.False(body.GetProperty("hasPosition").GetBoolean());
        Assert.Equal(garbage.Records.Count, body.GetProperty("discardedPositionCount").GetInt32());
        Assert.Equal(garbage.Records.Count, body.GetProperty("recordCount").GetInt32());

        // Cleaning runs before the alignment, so its code comes first — the order the repair's
        // own report states them in.
        Assert.Equal([DiagnosticCodes.PositionsDiscarded, DiagnosticCodes.NoOriginalGps], Codes(body));
    }

    [Fact]
    public async Task A_track_without_elevation_says_altitude_is_absent()
    {
        var run = SyntheticRun.Create();

        using var response = await InspectAsync(TrackBytes(run, "gpx", withAltitude: false), FileRole.Track);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("hasAltitude").GetBoolean());
        // Absent, not flat: the geometry is still the whole track.
        Assert.Equal(run.ReferenceTrack.Length, body.GetProperty("lengthMeters").GetDouble(), run.ReferenceTrack.Length * 0.001);
    }

    [Fact]
    public async Task Altitude_is_about_the_track_not_about_its_first_point()
    {
        // An editor that exported one point without its elevation has not exported a flat route,
        // and a reader that looked at the first point alone would hide a profile that is there.
        var run = SyntheticRun.Create();
        IReadOnlyList<TrackPoint> points =
        [
            new TrackPoint(run.TrackPoints[0].Position, null),
            .. run.TrackPoints.Skip(1)
        ];

        using var response = await InspectAsync(Encoding.UTF8.GetBytes(ToGpx(points)), FileRole.Track);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("hasAltitude").GetBoolean());
    }

    [Fact]
    public async Task Inspect_refuses_the_activity_the_repair_would_refuse()
    {
        // Every record on the same second: there is no time axis to spread a track over, and the
        // repair says so with a code. A dropzone that called this file understood would leave the
        // button to fail, which is the gap this endpoint exists to close.
        var run = SyntheticRun.Create();
        var frozen = run.Original with
        {
            Records = [.. run.Original.Records.Take(5).Select(record => record with { Timestamp = run.Original.StartTime })]
        };

        using var response = await InspectAsync(SyntheticFitBuilder.WriteActivity(frozen), FileRole.Original);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.ActivityWithoutElapsedTime, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task One_number_one_claim_about_a_file_that_recorded_no_distance()
    {
        // Absent at the dropzone and absent in the report: a zero in either place would be a
        // measurement of standing still, and two different claims about one file.
        var run = SyntheticRun.Create(withAnyGps: false);
        var bytes = SyntheticFitBuilder.WriteActivity(run.Original);

        using var response = await InspectAsync(bytes, FileRole.Original);
        var body = await ReadJsonAsync(response);

        using var originalStream = new MemoryStream(bytes);
        using var trackStream = new MemoryStream(TrackBytes(run, "gpx", withAltitude: true));
        var report = RepairPipeline.Repair(originalStream, trackStream).Report;

        Assert.Equal(JsonValueKind.Null, body.GetProperty("recordedDistanceMeters").ValueKind);
        Assert.Null(report.OriginalDistanceMeters);
    }

    [Fact]
    public async Task A_file_in_no_known_track_format_refuses_with_the_cores_own_code()
    {
        using var response = await InspectAsync(Encoding.UTF8.GetBytes("hello,world\n1,2\n"), FileRole.Track);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TrackFormatUnknown, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_track_whose_points_all_collapse_refuses_with_its_own_code()
    {
        // Two points a tenth of a metre apart: the file had points, and de-duplication left one
        // place — a different problem for the user than a file with one point in it.
        IReadOnlyList<TrackPoint> together =
        [
            new TrackPoint(SyntheticRun.Offset(0, 0), 180),
            new TrackPoint(SyntheticRun.Offset(0.1, 0), 180)
        ];

        using var response = await InspectAsync(Encoding.UTF8.GetBytes(ToGpx(together)), FileRole.Track);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TrackWithoutDistinctPoints, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_fit_file_with_no_records_refuses_with_the_cores_own_code()
    {
        var bytes = SyntheticFitBuilder.WriteWithoutRecords(SyntheticRun.StartTime);

        using var response = await InspectAsync(bytes, FileRole.Original);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.FitHasNoRecords, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_multipart_body_with_no_boundary_is_malformed_not_too_large()
    {
        // The form reader raises its size exception for this too. Telling the sender of a
        // three-byte request that their file is too large would send them looking for a
        // smaller one.
        using var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");

        using var response = await Host.Client.PostAsync(InspectEndpoint.Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RequestMalformed, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task More_form_fields_than_the_limit_is_malformed_not_too_large()
    {
        using var content = new MultipartFormDataContent();
        for (var field = 0; field < 1_100; field++)
            content.Add(new StringContent(FileRole.Original), $"field{field}");

        using var response = await Host.Client.PostAsync(InspectEndpoint.Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RequestMalformed, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_request_with_no_file_says_the_file_is_missing()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(FileRole.Original), InspectEndpoint.RoleField);

        using var response = await Host.Client.PostAsync(InspectEndpoint.Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.FileMissing, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_request_that_is_not_multipart_is_refused()
    {
        using var response = await Host.Client.PostAsync(
            InspectEndpoint.Route,
            new StringContent("""{"role":"original"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RequestMalformed, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Two_files_are_refused_rather_than_the_first_one_read()
    {
        var run = SyntheticRun.Create();
        using var content = Upload(SyntheticFitBuilder.WriteActivity(run.Original));
        content.Add(new ByteArrayContent(TrackBytes(run, "gpx", withAltitude: true)), FileField, "track.gpx");
        content.Add(new StringContent(FileRole.Original), InspectEndpoint.RoleField);

        using var response = await Host.Client.PostAsync(InspectEndpoint.Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TooManyFiles, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_file_over_the_upload_limit_leaves_as_the_size_refusal()
    {
        // Story 2.1 settled this one; it is here because inspect is the first endpoint a file
        // actually arrives at.
        using var small = new ApiHost("1024");
        using var content = Upload(SyntheticFitBuilder.WriteActivity(SyntheticRun.Create().Original));
        content.Add(new StringContent(FileRole.Original), InspectEndpoint.RoleField);

        using var response = await small.Client.PostAsync(InspectEndpoint.Route, content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(DiagnosticCodes.FileTooLarge, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_successful_inspect_logs_the_shape_of_the_file_and_nothing_of_its_contents()
    {
        // The track's own bytes are full of coordinates and its name is a give-away; AD-9 allows
        // counts, the sport and codes, and nothing else. Nothing is kept either: the file is read
        // from memory and never written anywhere, which is what the form-buffering test in
        // FailureContractTests pins.
        var run = SyntheticRun.Create();
        var gpx = TrackBytes(run, "gpx", withAltitude: true);

        using var response = await InspectAsync(gpx, FileRole.Track, fileName: UploadedFileName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var text = Host.Logs.Text;

        // The request line and the endpoint's own line, so this test cannot pass on an empty log.
        Assert.Contains($"POST {InspectEndpoint.Route} -> 200", text, StringComparison.Ordinal);
        Assert.Contains(
            $"Inspected a track: gpx, {run.ReferenceTrack.Points.Count} points", text, StringComparison.Ordinal);

        Assert.DoesNotContain(UploadedFileName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            SyntheticRun.StartLatitude.ToString("F7", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SyntheticRun.StartLongitude.ToString("F7", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.DoesNotContain("<trkpt", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_answers_only_a_post()
    {
        using var response = await Host.Client.GetAsync(InspectEndpoint.Route);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(DiagnosticCodes.MethodNotAllowed, await ReadCodeAsync(response));
    }

    /// <summary>The same geometry in each of the three formats the core recognises.</summary>
    private static byte[] TrackBytes(SyntheticRun run, string format, bool withAltitude)
    {
        var points = withAltitude
            ? run.TrackPoints
            : [.. run.TrackPoints.Select(point => new TrackPoint(point.Position, null))];

        return format switch
        {
            "fit" => SyntheticFitBuilder.WriteTrack(points, SyntheticRun.StartTime),
            "gpx" => Encoding.UTF8.GetBytes(ToGpx(points)),
            "tcx" => Encoding.UTF8.GetBytes(ToTcx(points)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown track format.")
        };
    }

    /// <summary>
    /// A GPX whose points carry an elevation only when they have one, so a track with no altitude
    /// is a track with no <c>ele</c> element rather than one flat at zero.
    /// </summary>
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
                """<trkpt lat="{0:F7}" lon="{1:F7}">{2}</trkpt>""",
                point.Position.Latitude,
                point.Position.Longitude,
                point.Altitude is { } altitude
                    ? string.Format(CultureInfo.InvariantCulture, "<ele>{0:F1}</ele>", altitude)
                    : string.Empty));
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
                "<Trackpoint><Position><LatitudeDegrees>{0:F7}</LatitudeDegrees>"
                + "<LongitudeDegrees>{1:F7}</LongitudeDegrees></Position>{2}</Trackpoint>",
                point.Position.Latitude,
                point.Position.Longitude,
                point.Altitude is { } altitude
                    ? string.Format(CultureInfo.InvariantCulture, "<AltitudeMeters>{0:F1}</AltitudeMeters>", altitude)
                    : string.Empty));
        }

        builder.AppendLine("</Track></Lap></Activity></Activities></TrainingCenterDatabase>");
        return builder.ToString();
    }

    private Task<HttpResponseMessage> InspectAsync(
        byte[] file,
        string? role,
        string contentType = "application/octet-stream",
        string fileName = "upload.bin")
    {
        var content = Upload(file, contentType, fileName);
        if (role is not null)
            content.Add(new StringContent(role), InspectEndpoint.RoleField);

        return Host.Client.PostAsync(InspectEndpoint.Route, content);
    }

    private static MultipartFormDataContent Upload(
        byte[] file,
        string contentType = "application/octet-stream",
        string fileName = "upload.bin")
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(part, FileField, fileName);
        return content;
    }

    /// <summary>The answer's codes, which must all be the registry's (AD-18).</summary>
    private static string[] Codes(JsonElement body)
    {
        var codes = body.GetProperty("codes").EnumerateArray().Select(code => code.GetString()!).ToArray();

        Assert.All(codes, code => Assert.True(DiagnosticCodes.IsDeclared(code), code));
        return codes;
    }

    /// <summary>The body as a detached element, so the document can be disposed with it.</summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(ProblemDetailsFactory.MediaType, response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadJsonAsync(response);
        var code = problem.GetProperty("code").GetString()!;

        Assert.True(DiagnosticCodes.IsDeclared(code), code);
        Assert.Equal((int)response.StatusCode, problem.GetProperty("status").GetInt32());
        return code;
    }
}
