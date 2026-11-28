using System.Text.Json;
using FitFix.Api.Contracts;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace FitFix.Api.Tests;

/// <summary>
/// One contract test per response DTO (AD-14), plus the two decisions the shell makes on its
/// own: which status and code a failure carries, and how large a body may be (AD-13, AD-22).
/// </summary>
public class ContractShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_problem_document_has_four_members_and_no_others()
    {
        var problem = ProblemDetailsFactory.Create(StatusCodes.Status413PayloadTooLarge, DiagnosticCodes.FileTooLarge);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(problem, Web));
        var members = document.RootElement.EnumerateObject().Select(member => member.Name).Order().ToArray();

        Assert.Equal(["code", "status", "title", "type"], members);
        Assert.Equal("FILE_TOO_LARGE", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(413, document.RootElement.GetProperty("status").GetInt32());
        // The title names the problem, which is what the code is: a reason phrase would read
        // the same for every refusal sharing a status.
        Assert.Equal("FILE_TOO_LARGE", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:fitfix:problem:FILE_TOO_LARGE", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void The_health_body_carries_one_member()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new HealthResponse("ok"), Web));

        Assert.Equal(["status"], document.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void The_original_inspection_body_carries_its_nine_members()
    {
        var inspection = new OriginalInspectionResponse(
            Role: FileRole.Original,
            Sport: "running",
            StartedAt: new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc),
            ElapsedSeconds: 2160,
            RecordedDistanceMeters: 3911,
            RecordCount: 2161,
            HasPosition: true,
            DiscardedPositionCount: 1,
            Codes: [DiagnosticCodes.PositionsDiscarded]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(inspection, Web));
        var root = document.RootElement;

        Assert.Equal(
            ["codes", "discardedPositionCount", "elapsedSeconds", "hasPosition", "recordCount",
             "recordedDistanceMeters", "role", "sport", "startedAt"],
            root.EnumerateObject().Select(member => member.Name).Order());

        // The role discriminates the answer, and the sport is a string rather than the enum's
        // number (AD-14). No pace, no fraction, no delta: the interface derives those (AD-6).
        Assert.Equal("original", root.GetProperty("role").GetString());
        Assert.Equal("running", root.GetProperty("sport").GetString());
        Assert.Equal("2026-11-19T15:30:00Z", root.GetProperty("startedAt").GetString());

        // The codes are the registry's, so the dropzone reads them out of the same dictionary the
        // result screen uses (AD-18).
        var codes = root.GetProperty("codes").EnumerateArray().Select(code => code.GetString()!);
        Assert.All(codes, code => Assert.True(DiagnosticCodes.IsDeclared(code), code));
    }

    [Fact]
    public void The_track_inspection_body_carries_its_five_members()
    {
        var inspection = new TrackInspectionResponse(
            Role: FileRole.Track,
            LengthMeters: 6537.2,
            PointCount: 1308,
            Format: "gpx",
            HasAltitude: false);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(inspection, Web));
        var root = document.RootElement;

        Assert.Equal(
            ["format", "hasAltitude", "lengthMeters", "pointCount", "role"],
            root.EnumerateObject().Select(member => member.Name).Order());

        Assert.Equal("track", root.GetProperty("role").GetString());
        Assert.Equal("gpx", root.GetProperty("format").GetString());
        Assert.False(root.GetProperty("hasAltitude").GetBoolean());
    }

    [Fact]
    public void The_repair_body_carries_its_nine_members_and_names_every_enum_as_a_string()
    {
        var repair = new RepairResponse(
            Before: new OriginalSummaryResponse(
                Sport: "running",
                StartedAt: new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc),
                RecordCount: 2161,
                RecordedDistanceMeters: 3911,
                DiscardedPositionCount: 1),
            After: new RepairedSummaryResponse(
                DistanceMeters: 6537.2,
                ElapsedSeconds: 2160,
                MovingSeconds: 2160,
                PausedSeconds: 0,
                AverageSpeedMetersPerSecond: 3.02,
                MaximumSpeedMetersPerSecond: 3.4,
                AverageHeartRate: 154,
                MaximumHeartRate: 159,
                AveragePower: 200,
                MaximumPower: 210,
                AverageCadence: 85,
                MaximumCadence: 85,
                ElevationGainMeters: 40,
                ElevationLossMeters: 40,
                LapCount: 1),
            Track: new TrackLengthsResponse(LengthMeters: 6537.2, UsedLengthMeters: 6537.2),
            Zones: [new TrustZoneResponse(ZoneKindName.Measured, 0, 1300)],
            Redrawn: new RedrawnSpanResponse(FromSeconds: 1612, ToSeconds: 1976, DistanceMeters: 1234),
            StartToTrackMeters: 1340,
            Series: new RepairSeriesResponse(
                SecondsFromStart: [0, 1],
                Original: new OriginalSeriesResponse(
                    Latitude: [50.3333, null],
                    Longitude: [30.3833, null],
                    DistanceMeters: [0, null],
                    SpeedMetersPerSecond: [3, null]),
                Repaired: new RepairedSeriesResponse(
                    Latitude: [50.3333, 50.3334],
                    Longitude: [30.3833, 30.3834],
                    AltitudeMeters: [182.8, null],
                    DistanceMeters: [0, 3],
                    SpeedMetersPerSecond: [3, 3],
                    HeartRate: [150, null],
                    Power: [200, null],
                    Cadence: [85, null],
                    TemperatureCelsius: [4, null])),
            Warnings: [new RepairWarningResponse(DiagnosticCodes.GpsStallDetected, SeverityName.Info)],
            FitBase64: "RklU");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(repair, Web));
        var root = document.RootElement;

        Assert.Equal(
            ["after", "before", "fitBase64", "redrawn", "series", "startToTrackMeters", "track", "warnings", "zones"],
            root.EnumerateObject().Select(member => member.Name).Order());

        // The redrawn stretch is three base quantities and no sentence (AD-14, AD-27).
        Assert.Equal(
            ["distanceMeters", "fromSeconds", "toSeconds"],
            root.GetProperty("redrawn").EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal(1612, root.GetProperty("redrawn").GetProperty("fromSeconds").GetDouble());

        Assert.Equal(
            ["discardedPositionCount", "recordCount", "recordedDistanceMeters", "sport", "startedAt"],
            root.GetProperty("before").EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal("2026-11-19T15:30:00Z", root.GetProperty("before").GetProperty("startedAt").GetString());

        Assert.Equal(
            ["averageCadence", "averageHeartRate", "averagePower", "averageSpeedMetersPerSecond",
             "distanceMeters", "elapsedSeconds", "elevationGainMeters", "elevationLossMeters",
             "lapCount", "maximumCadence", "maximumHeartRate", "maximumPower",
             "maximumSpeedMetersPerSecond", "movingSeconds", "pausedSeconds"],
            root.GetProperty("after").EnumerateObject().Select(member => member.Name).Order());

        Assert.Equal(
            ["lengthMeters", "usedLengthMeters"],
            root.GetProperty("track").EnumerateObject().Select(member => member.Name).Order());

        Assert.Equal(
            ["original", "repaired", "secondsFromStart"],
            root.GetProperty("series").EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal(
            ["distanceMeters", "latitude", "longitude", "speedMetersPerSecond"],
            root.GetProperty("series").GetProperty("original")
                .EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal(
            ["altitudeMeters", "cadence", "distanceMeters", "heartRate", "latitude", "longitude",
             "power", "speedMetersPerSecond", "temperatureCelsius"],
            root.GetProperty("series").GetProperty("repaired")
                .EnumerateObject().Select(member => member.Name).Order());

        // A gap is null rather than a missing element or a zero (AD-5), and the arrays stay the
        // same length as each other.
        var latitude = root.GetProperty("series").GetProperty("original").GetProperty("latitude");
        Assert.Equal(2, latitude.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, latitude[1].ValueKind);

        // Both enums as strings, never as the numbers behind them (AD-14).
        var zone = root.GetProperty("zones")[0];
        Assert.Equal(
            ["fromSeconds", "kind", "toSeconds"],
            zone.EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal("measured", zone.GetProperty("kind").GetString());

        var warning = root.GetProperty("warnings")[0];
        Assert.Equal(["code", "severity"], warning.EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal("info", warning.GetProperty("severity").GetString());
        Assert.True(DiagnosticCodes.IsDeclared(warning.GetProperty("code").GetString()));

        // A whole track redrew no stretch, and the member is still there, stated as null: a client
        // that reads `redrawn` has to be able to tell "nothing was redrawn" from an older server
        // (AD-5's rule for a series, and the same claim about a value).
        using var whole = JsonDocument.Parse(JsonSerializer.Serialize(repair with { Redrawn = null }, Web));
        Assert.Equal(
            ["after", "before", "fitBase64", "redrawn", "series", "startToTrackMeters", "track", "warnings", "zones"],
            whole.RootElement.EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal(JsonValueKind.Null, whole.RootElement.GetProperty("redrawn").ValueKind);

        // Metres of a measurement, no sentence; null when nothing was far (AD-14).
        Assert.Equal(1340, root.GetProperty("startToTrackMeters").GetDouble());
        using var near = JsonDocument.Parse(JsonSerializer.Serialize(repair with { StartToTrackMeters = null }, Web));
        Assert.Equal(JsonValueKind.Null, near.RootElement.GetProperty("startToTrackMeters").ValueKind);
    }

    [Fact]
    public void The_two_endpoints_name_a_sport_the_same_way()
    {
        // The dropzone and the result screen read one answer out of one dictionary: a sport spelt
        // two ways would be two names for one file (AD-15).
        Assert.Equal("running", SportName.Of(SportKind.Running));
        Assert.Equal("cycling", SportName.Of(SportKind.Cycling));
        Assert.Equal("other", SportName.Of(SportKind.Other));
    }

    [Fact]
    public void Every_severity_and_zone_kind_the_core_has_can_be_named()
    {
        // The contract must be able to state whatever the core models, or a repair would leave as
        // the shell's own failure over a value that is perfectly valid.
        Assert.All(
            Enum.GetValues<DiagnosticSeverity>(),
            severity => Assert.False(string.IsNullOrEmpty(SeverityName.Of(severity))));
        Assert.All(
            Enum.GetValues<ZoneKind>(),
            kind => Assert.False(string.IsNullOrEmpty(ZoneKindName.Of(kind))));
    }

    [Fact]
    public void A_code_the_registry_does_not_know_becomes_the_shells_own_failure()
    {
        // Rather than forwarding a code the UI dictionary has no text for (AD-18).
        var problem = ProblemDetailsFactory.Create(StatusCodes.Status400BadRequest, "NOT_A_REAL_CODE");

        Assert.Equal(DiagnosticCodes.InternalError, problem.Code);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
    }

    [Fact]
    public void Every_code_the_factory_can_be_given_is_declared()
    {
        Assert.All(
            DiagnosticCodes.All,
            code => Assert.Equal(code, ProblemDetailsFactory.Create(FailureStatus.ForCode(code), code).Code));
    }

    [Fact]
    public void A_broken_invariant_leaves_as_500_and_bad_input_as_400()
    {
        // These three document themselves as invariants of the repair, not faults in the files
        // the user chose, so answering them 400 would blame the athlete for our bug.
        Assert.Equal(StatusCodes.Status500InternalServerError, FailureStatus.ForCode(DiagnosticCodes.SummarySeriesMismatch));
        Assert.Equal(StatusCodes.Status500InternalServerError, FailureStatus.ForCode(DiagnosticCodes.SummaryWithoutLaps));
        Assert.Equal(StatusCodes.Status500InternalServerError, FailureStatus.ForCode(DiagnosticCodes.DistanceSeriesImplausible));

        Assert.Equal(StatusCodes.Status400BadRequest, FailureStatus.ForCode(DiagnosticCodes.NotAFitFile));
        Assert.Equal(StatusCodes.Status400BadRequest, FailureStatus.ForCode(DiagnosticCodes.TrackFormatUnknown));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, FailureStatus.ForCode(DiagnosticCodes.FileTooLarge));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, DiagnosticCodes.RequestMalformed)]
    [InlineData(StatusCodes.Status404NotFound, DiagnosticCodes.RouteNotFound)]
    [InlineData(StatusCodes.Status405MethodNotAllowed, DiagnosticCodes.MethodNotAllowed)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, DiagnosticCodes.FileTooLarge)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, DiagnosticCodes.MediaTypeNotSupported)]
    [InlineData(StatusCodes.Status408RequestTimeout, DiagnosticCodes.RequestMalformed)]
    [InlineData(StatusCodes.Status500InternalServerError, DiagnosticCodes.InternalError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, DiagnosticCodes.InternalError)]
    public void A_status_the_framework_settled_still_carries_a_declared_code(int status, string expected)
    {
        var code = FailureStatus.ForStatus(status);

        Assert.Equal(expected, code);
        Assert.True(DiagnosticCodes.IsDeclared(code), code);
    }

    [Theory]
    [InlineData(null, UploadLimit.DefaultBytes)]
    [InlineData("", UploadLimit.DefaultBytes)]
    [InlineData("twenty megabytes", UploadLimit.DefaultBytes)]
    [InlineData("0", UploadLimit.DefaultBytes)]
    [InlineData("-1", UploadLimit.DefaultBytes)]
    [InlineData("1048576", 1048576L)]
    // Above the ceiling an upload could no longer be held in memory in full, so the ceiling
    // stands in rather than a part spilling to a temporary file.
    [InlineData("9999999999", UploadLimit.MaxBytes)]
    public void The_upload_limit_is_configuration_with_a_default_behind_it(string? configured, long expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [UploadLimit.ConfigurationKey] = configured })
            .Build();

        Assert.Equal(expected, UploadLimit.From(configuration));
    }

    [Fact]
    public void The_default_limit_is_twenty_megabytes()
    {
        Assert.Equal(20L * 1024 * 1024, UploadLimit.DefaultBytes);
    }
}
