using System.Net;
using System.Text;
using System.Text.Json;
using FitFix.Api.Contracts;
using FitFix.Core.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitFix.Api.Tests;

/// <summary>
/// Story 2.1: every failure leaves the service as a problem document carrying a code from the
/// core's registry (AD-22), and nothing of an exception travels with it (AD-4). The requests
/// here go over a socket to a real Kestrel, because the refusals that matter are the ones the
/// server raises rather than the ones a handler writes.
/// </summary>
public class FailureContractTests : IDisposable
{
    /// <summary>What a leak would look like: an uploaded name, file contents, a coordinate.</summary>
    private const string UploadedFileName = "chabany-secret-run.fit";
    private const string UploadedContentMarker = "SECRET_FILE_CONTENT";
    private const string UploadedCoordinate = "50.3421,30.4567";

    /// <summary>
    /// How long a log line may take to appear. Only the whole suite sharing a machine makes it
    /// matter, and then a wait beats a sleep: the line either comes or the guarantee is broken.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The logger this service writes its own lines with (AD-9).</summary>
    private const string OwnLogger = "FitFix.Api.Requests";

    // Started on first use, so a test that needs a different upload limit never starts it.
    private readonly Lazy<ApiHost> _host = new(() => new ApiHost());

    private ApiHost Host => _host.Value;

    public void Dispose()
    {
        if (_host.IsValueCreated)
            _host.Value.Dispose();
    }

    [Fact]
    public async Task Health_answers_200_with_the_body_a_probe_reads()
    {
        using var response = await Host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_route_answers_a_problem_document_not_an_empty_body()
    {
        using var response = await Host.Client.GetAsync("/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RouteNotFound, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_404_an_endpoint_chose_is_left_alone()
    {
        // Only routing's own 404 belongs to the failure handler. An endpoint saying a resource
        // is missing keeps its answer, and so will Story 2.8's index.html fallback.
        using var response = await Host.Client.GetAsync("/probe/missing-resource");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Contains("GET /probe/missing-resource -> 404", Host.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_method_answers_its_own_code()
    {
        using var response = await Host.Client.PostAsync("/health", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(DiagnosticCodes.MethodNotAllowed, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_body_the_endpoint_cannot_read_answers_its_own_code()
    {
        using var response = await Host.Client.PostAsync(
            "/probe/json",
            new StringContent("not json at all", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(DiagnosticCodes.MediaTypeNotSupported, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_domain_refusal_answers_400_with_the_exceptions_own_code()
    {
        using var response = await PostAsync("/probe/fail?kind=domain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TrackFormatUnknown, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_broken_invariant_is_not_the_users_fault_and_answers_500_with_its_code()
    {
        using var response = await PostAsync("/probe/fail?kind=invariant");

        // Telling the athlete their file is wrong would hide our own bug.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(DiagnosticCodes.SummarySeriesMismatch, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task Something_unexpected_answers_500_with_internal_error()
    {
        using var response = await PostAsync("/probe/fail?kind=unexpected");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(DiagnosticCodes.InternalError, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_form_the_reader_cannot_parse_is_the_requests_own_failure_not_a_size_refusal()
    {
        // The form reader raises one exception type for its size limit and for a malformed form
        // alike: a multipart header with no boundary, more form fields than the count limit. A
        // three-byte body is no size problem, and telling its sender that their file is too large
        // would send them looking for a smaller one.
        using var response = await PostAsync("/probe/fail?kind=form");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RequestMalformed, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_form_refusal_on_a_body_that_reached_the_bound_is_the_size_refusal()
    {
        // The reader's own length limit is the upload bound (AD-13), so a body that actually got
        // there is refused for its size — the one form refusal that is one.
        using var small = new ApiHost("1024");

        using var response = await small.Client.PostAsync(
            "/probe/fail?kind=form", new ByteArrayContent(new byte[1024]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(DiagnosticCodes.FileTooLarge, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public void The_form_reader_buffers_a_part_up_to_the_whole_upload_limit()
    {
        // Past MemoryBufferThreshold an uploaded part is buffered to a temporary file, and the
        // default is 64 KB — every real activity. Nothing uploaded may touch the disk (AD-3), and
        // Story 2.8's container has a read-only filesystem to write it to, so the threshold is the
        // upload bound itself: no part that passes the bound can exceed the buffer. BufferBody
        // stays off with it, or the whole body would take the same path on its own terms.
        using var app = FitFixApi
            .CreateBuilder([$"--contentRoot={AppContext.BaseDirectory}", $"--{UploadLimit.ConfigurationKey}=4096"])
            .Build();

        var form = app.Services.GetRequiredService<IOptions<FormOptions>>().Value;

        Assert.Equal(4096, form.MultipartBodyLengthLimit);
        Assert.Equal(4096, form.MemoryBufferThreshold);
        Assert.False(form.BufferBody);
    }

    [Fact]
    public async Task No_part_of_an_exception_reaches_the_body()
    {
        foreach (var kind in new[] { "domain", "invariant", "form", "unexpected" })
        {
            using var response = await PostAsync($"/probe/fail?kind={kind}");
            var body = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("Probe", body, StringComparison.Ordinal);
            Assert.DoesNotContain("developer-facing", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);

            // No detail member at all: an absent one cannot carry a sentence by accident.
            using var document = JsonDocument.Parse(body);
            Assert.False(document.RootElement.TryGetProperty("detail", out _));
            Assert.False(document.RootElement.TryGetProperty("stackTrace", out _));
        }
    }

    [Fact]
    public async Task Every_code_the_api_can_send_is_declared_in_the_registry()
    {
        using var small = new ApiHost("1024");

        var codes = new List<string>();
        foreach (var response in new[]
                 {
                     await Host.Client.GetAsync("/nope"),
                     await Host.Client.PostAsync("/health", new StringContent(string.Empty)),
                     await Host.Client.PostAsync("/probe/json", new StringContent("x", Encoding.UTF8, "text/plain")),
                     await PostAsync("/probe/fail?kind=domain"),
                     await PostAsync("/probe/fail?kind=invariant"),
                     await PostAsync("/probe/fail?kind=form"),
                     await PostAsync("/probe/fail?kind=unexpected"),
                     await small.Client.PostAsync("/probe/read", new ByteArrayContent(new byte[4096]))
                 })
        {
            using (response)
                codes.Add((await ReadProblemAsync(response)).Code);
        }

        Assert.Equal(8, codes.Count);
        Assert.All(codes, code => Assert.True(DiagnosticCodes.IsDeclared(code), code));
    }

    [Fact]
    public async Task A_body_over_the_configured_limit_is_refused_with_file_too_large()
    {
        using var small = new ApiHost("1024");

        using var response = await small.Client.PostAsync("/probe/read", new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(DiagnosticCodes.FileTooLarge, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_body_over_the_limit_that_declares_no_length_is_refused_too()
    {
        // Chunked: nothing says up front how large the body is, so only Kestrel counting the
        // bytes as the handler reads them can refuse it — the case the empty 413 comes from.
        using var small = new ApiHost("1024");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe/read")
        {
            Content = new StreamContent(new UnknownLengthStream(new byte[4096]))
        };
        request.Headers.TransferEncodingChunked = true;

        using var response = await small.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(DiagnosticCodes.FileTooLarge, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_body_under_the_configured_limit_is_accepted()
    {
        using var small = new ApiHost("4096");

        using var response = await small.Client.PostAsync("/probe/read", new ByteArrayContent(new byte[1024]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1024", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_limit_that_does_not_parse_falls_back_to_the_default()
    {
        using var nonsense = new ApiHost("twenty megabytes");

        // Above any small limit, far below the 20 MB default: accepted, so the default took
        // effect rather than nothing at all.
        using var response = await nonsense.Client.PostAsync("/probe/read", new ByteArrayContent(new byte[64 * 1024]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_body_over_the_default_limit_is_refused_with_nothing_configured()
    {
        // The default is a real bound, not a number in a constant: without this, a regression
        // that bounds nothing at all passes every other test here. The length is declared
        // rather than sent, because a refusal that needs 20 MB on the wire to prove itself is
        // a refusal that arrives too late.
        var raw = await Host.SendRawAsync(
            $"POST /probe/read HTTP/1.1\r\nHost: {Host.BaseAddress.Authority}\r\nConnection: close\r\n"
            + $"Content-Length: {UploadLimit.DefaultBytes + 1}\r\n\r\nbegun");

        Assert.Contains("413 Payload Too Large", raw, StringComparison.Ordinal);
        Assert.Contains(DiagnosticCodes.FileTooLarge, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_the_server_cannot_read_to_the_end_is_the_requests_failure_not_the_services()
    {
        // Kestrel refuses the framing itself, with a status of its own. Reporting that as our
        // bug would make INTERNAL_ERROR meaningless, since an upload that ends badly is the
        // most ordinary client failure this service will see.
        var raw = await Host.SendRawAsync(
            $"POST /probe/read HTTP/1.1\r\nHost: {Host.BaseAddress.Authority}\r\nConnection: close\r\n"
            + "Transfer-Encoding: chunked\r\n\r\nnot-a-chunk-size\r\n");

        Assert.Contains("400 Bad Request", raw, StringComparison.Ordinal);
        Assert.Contains(DiagnosticCodes.RequestMalformed, raw, StringComparison.Ordinal);

        // Information, not error: no stack trace of ours attached to a client's mistake.
        Assert.Contains($"code {DiagnosticCodes.RequestMalformed}", Host.Logs.Text, StringComparison.Ordinal);
        AssertNothingOfOursWasLoggedAsAnError();
    }

    [Fact]
    public async Task An_upload_the_client_abandons_is_recorded_as_information()
    {
        // Declares a body of 4096 bytes and delivers five of them.
        using var connection = await Host.SendWithoutClosingAsync(
            $"POST /probe/read-and-park HTTP/1.1\r\nHost: {Host.BaseAddress.Authority}\r\nContent-Length: 4096\r\n\r\nshort");

        // Only once the handler is waiting for the rest is the connection worth abandoning: a
        // reset that arrives while the request is still unread in the socket takes the request
        // with it, and then there is no request for anything to be logged about. Waiting for
        // the handler to say so puts that order under this test's control rather than the
        // machine's.
        Assert.True(await Host.WaitForLogAsync(Probes.ParkedOnBody, Patience), Host.Logs.Text);

        connection.Abort();

        // Nobody is left to answer, so all that is owed is an honest line: the client left,
        // which is information about a request, not a failure of the service.
        Assert.True(await Host.WaitForLogAsync(DiagnosticCodes.ClientDisconnected, Patience), Host.Logs.Text);
        AssertNothingOfOursWasLoggedAsAnError();
    }

    /// <summary>
    /// Nothing this service wrote itself may call a client's departure a failure of its own.
    /// Kestrel grumbling about a socket it can no longer write to is its business and arrives
    /// or not depending on how busy the machine is, which is no part of the guarantee.
    /// </summary>
    private void AssertNothingOfOursWasLoggedAsAnError() =>
        Assert.DoesNotContain(
            Host.Logs.Entries,
            entry => entry.Level >= LogLevel.Error && entry.Category == OwnLogger);

    [Fact]
    public async Task A_path_cannot_write_a_line_of_its_own_into_the_log()
    {
        // A percent-encoded newline and a bracketed level: logged decoded, this would forge a
        // line of its own, and AD-9 makes the log the one trustworthy record of a refusal.
        var raw = await Host.SendRawAsync(
            $"GET /nope%0A%5BInformation%5D%20forged HTTP/1.1\r\nHost: {Host.BaseAddress.Authority}\r\nConnection: close\r\n\r\n");

        Assert.Contains("404 Not Found", raw, StringComparison.Ordinal);

        // One message per call to the logger, and a message is the only place a value taken
        // from a request appears — so a line something forged would sit inside one of them.
        Assert.All(Host.Logs.Messages, message =>
        {
            Assert.DoesNotContain('\n', message);
            Assert.DoesNotContain('\r', message);
        });

        // The matched route is what the log records, and nothing matched.
        Assert.Contains("GET (unmatched) -> 404", Host.Logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/nope", Host.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_before_routing_claims_no_route()
    {
        using var small = new ApiHost("1024");

        // A wrong method first, so the connection has an endpoint from a request before this
        // one — exactly what a log line must not borrow.
        using var wrongMethod = await small.Client.PostAsync("/health", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);

        using var oversized = await small.Client.PostAsync("/health", new ByteArrayContent(new byte[4096]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);

        Assert.Contains("POST (not routed) -> 413", small.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_after_the_response_began_completes_and_is_warned_about()
    {
        using var response = await PostAsync("/probe/write-then-fail");

        // The response was already on its way, so the status stands and no body can replace it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already started", Host.Logs.Text, StringComparison.Ordinal);
        Assert.Contains(DiagnosticCodes.InternalError, Host.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_with_no_probes_answers_them_404_and_still_answers_health()
    {
        // Exactly what production runs: the probes are the test host's, not the binary's.
        using var production = new ApiHost(withProbes: false);

        using var probe = await production.Client.PostAsync("/probe/fail?kind=unexpected", new ByteArrayContent([1]));
        Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
        Assert.Equal(DiagnosticCodes.RouteNotFound, (await ReadProblemAsync(probe)).Code);

        using var health = await production.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task The_logs_carry_no_coordinate_no_file_content_and_no_uploaded_name()
    {
        foreach (var kind in new[] { "domain", "unexpected" })
        {
            using var content = BuildUpload();
            using var response = await Host.Client.PostAsync($"/probe/fail?kind={kind}", content);
            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError);
        }

        var text = Host.Logs.Text;

        // The request line is there — otherwise this test would pass on an empty log.
        Assert.Contains(DiagnosticCodes.TrackFormatUnknown, text, StringComparison.Ordinal);
        Assert.Contains(DiagnosticCodes.InternalError, text, StringComparison.Ordinal);

        Assert.DoesNotContain(UploadedFileName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UploadedContentMarker, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UploadedCoordinate, text, StringComparison.Ordinal);
        Assert.DoesNotContain("50.3421", text, StringComparison.Ordinal);
        Assert.DoesNotContain("30.4567", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_effective_upload_limit_is_stated_once_at_startup()
    {
        using var small = new ApiHost("4096");

        // UploadLimit.From swallows a typo on purpose, so the value that took effect is said
        // out loud rather than left to be inferred from a refusal.
        Assert.Contains("The upload limit is 4096 bytes.", small.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_failure_body_arrives_as_a_problem_document()
    {
        using var response = await PostAsync("/probe/fail?kind=domain");

        Assert.Equal(ProblemDetailsFactory.MediaType, response.Content.Headers.ContentType?.MediaType);
    }

    private static MultipartFormDataContent BuildUpload()
    {
        // A request shaped like the real upload of Story 2.3: a named file whose bytes carry a
        // coordinate. Neither the name nor the bytes may turn up anywhere in the logs (AD-9).
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes($"{UploadedContentMarker} {UploadedCoordinate}"));
        content.Add(part, "original", UploadedFileName);
        return content;
    }

    private Task<HttpResponseMessage> PostAsync(string path) =>
        Host.Client.PostAsync(path, new ByteArrayContent([1, 2, 3]));

    private static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // The four members of the contract, and no fifth one.
        Assert.Equal(4, root.EnumerateObject().Count());

        var problem = new ProblemResponse(
            Type: root.GetProperty("type").GetString()!,
            Title: root.GetProperty("title").GetString()!,
            Status: root.GetProperty("status").GetInt32(),
            Code: root.GetProperty("code").GetString()!);

        Assert.Equal(ProblemDetailsFactory.TypePrefix + problem.Code, problem.Type);
        Assert.Equal(problem.Code, problem.Title);
        Assert.Equal((int)response.StatusCode, problem.Status);
        Assert.True(DiagnosticCodes.IsDeclared(problem.Code), problem.Code);
        return problem;
    }
}

/// <summary>
/// A stream that refuses to say how long it is, so <c>HttpClient</c> sends the body chunked
/// and the server learns its size only by reading it.
/// </summary>
internal sealed class UnknownLengthStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
