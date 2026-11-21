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
