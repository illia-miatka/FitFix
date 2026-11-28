using System.Globalization;
using System.Text.Json;
using FitFix.Core.Diagnostics;

namespace FitFix.Api.Contracts;

/// <summary>
/// The one place a failure becomes a response body (AD-22). Every refusal the shell can
/// produce — a domain exception, an over-sized upload, an unexpected throw, a request that
/// reached no endpoint — leaves through here, so the interface has exactly one shape to read
/// and exactly one member to key its dictionary on.
/// </summary>
public static class ProblemDetailsFactory
{
    /// <summary>The media type RFC 9457 defines for a problem document.</summary>
    public const string MediaType = "application/problem+json";

    /// <summary>The URN prefix of <see cref="ProblemResponse.Type"/>; a URN needs nothing hosted to resolve.</summary>
    public const string TypePrefix = "urn:fitfix:problem:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds the body for a status and a code. A code the registry does not know would reach
    /// the UI dictionary with no text and no severity (AD-18), so it is not forwarded: the
    /// response becomes the shell's own failure instead, which is what it is. The caller logs
    /// the code of the returned document rather than the one it asked for, or the log would
    /// describe a response nobody received.
    /// </summary>
    public static ProblemResponse Create(int status, string code)
    {
        if (!DiagnosticCodes.IsDeclared(code))
        {
            code = DiagnosticCodes.InternalError;
            status = StatusCodes.Status500InternalServerError;
        }

        return new ProblemResponse(
            Type: TypePrefix + code,
            // RFC 9457 wants a title that names the problem, and the code is its name. The
            // reason phrase would read the same for every refusal sharing a status.
            Title: code,
            Status: status,
            Code: code);
    }

    /// <summary>
    /// Writes the problem document. Returns false when it could not be sent — the response had
    /// already started, or the client is no longer there to receive it. Both are reported, not
    /// thrown: this is called from inside failure handling, where a second exception would
    /// escape the handler and take the first one's answer with it.
    /// </summary>
    /// <param name="retryAfterSeconds">
    /// How long the client should wait, for the refusals where waiting is the whole answer —
    /// a rate limit, and nothing else so far. It is set here rather than by the caller because
    /// the response is cleared below, which would take any header the caller had set with it.
    /// </param>
    public static async Task<bool> TryWriteAsync(HttpContext context, ProblemResponse problem, int? retryAfterSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(problem);

        if (context.Response.HasStarted)
            return false;

        try
        {
            // Clear what a handler had set before it failed: a half-written set of headers
            // would otherwise describe a body that is no longer being sent.
            context.Response.Clear();
            context.Response.StatusCode = problem.Status;

            if (retryAfterSeconds is { } seconds)
                context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

            // CancellationToken.None on purpose: an aborted request must not turn the write
            // into a cancellation the caller then reports as a second, unrelated failure.
            await context.Response.WriteAsJsonAsync(problem, SerializerOptions, MediaType, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection went away mid-write. Nothing left to tell the client.
            return false;
        }
    }
}
