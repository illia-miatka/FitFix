using System.Text.Json;
using FitFix.Api.Contracts;
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
