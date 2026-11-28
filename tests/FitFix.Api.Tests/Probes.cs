using FitFix.Core.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FitFix.Api.Tests;

/// <summary>
/// Endpoints that raise each kind of failure on demand. They live here, not in the API: an
/// endpoint that throws when asked has no business in a shipped binary, and a flag that every
/// test sets would mean no test ever observes the host production runs.
/// </summary>
internal static class Probes
{
    /// <summary>The text of every probe failure: developer-facing English that must stay server-side.</summary>
    public const string Marker = "Probe failure: developer-facing text that must never reach a response.";

    /// <summary>
    /// Said once the handler is parked on a body that has not fully arrived. A test that wants
    /// to play a client going away waits for this: a reset sent before the server has read the
    /// request takes the request with it — the bytes are still unread in the socket, so nothing
    /// is parsed, nothing is logged, and the test would be waiting on a request that never
    /// happened.
    /// </summary>
    public const string ParkedOnBody = "The probe is parked on the request body.";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Reads the body to the end and reports how many bytes arrived: the request the upload
        // limit is measured against, from both sides of it.
        endpoints.MapPost("/probe/read", async (HttpRequest request) => Results.Ok(await DrainAsync(request)));

        // Raises one failure of each kind, after reading the body — so a probe can also carry
        // an upload whose name and contents must not turn up in any log (AD-9).
        endpoints.MapPost("/probe/fail", async (HttpRequest request, string? kind) =>
        {
            await DrainAsync(request);
            throw Failure(kind);
        });

        // Reads what has arrived, says so, and then waits for the rest — which lets a test
        // decide the moment the client goes away instead of racing the server for it.
        endpoints.MapPost("/probe/read-and-park", async (HttpRequest request, ILoggerFactory loggers) =>
        {
            var logger = loggers.CreateLogger("FitFix.Api.Tests.Probes");
            var buffer = new byte[8192];

            var read = await request.Body.ReadAsync(buffer);
            logger.LogInformation("{Announcement} It has {Count} of its bytes.", ParkedOnBody, read);

            while (read > 0)
                read = await request.Body.ReadAsync(buffer);

            return Results.Ok();
        });

        // Writes and flushes before it throws, which is the one case where no body can replace
        // the one already on its way.
        endpoints.MapPost("/probe/write-then-fail", async (HttpContext context) =>
        {
            await context.Response.WriteAsync("partial");
            await context.Response.Body.FlushAsync();
            throw new InvalidOperationException(Marker);
        });

        // Takes a JSON body, so a body in another media type is refused by the framework
        // itself rather than by anything written here.
        endpoints.MapPost("/probe/json", (ProbePayload payload) => Results.Ok(payload.Value));

        // An endpoint answering 404 itself: a resource that does not exist, which is a
        // different answer from a path that matched nothing, and Story 2.8's index.html
        // fallback will be a third.
        endpoints.MapGet("/probe/missing-resource", () => Results.NotFound());

        // A handler that does not finish, which is the only way to observe a request timeout.
        // It waits on the request's own cancellation rather than on a clock, so the test is
        // over the moment the timeout fires and never a second later.
        endpoints.MapGet("/probe/never-finishes", async (HttpContext context) =>
        {
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
            return Results.Ok();
        });
    }

    private static Exception Failure(string? kind) => kind switch
    {
        // A refusal over the user's data.
        "domain" => new MalformedArgumentException(DiagnosticCodes.TrackFormatUnknown, Marker),
        // A refusal whose code describes a broken invariant of ours, not a bad file.
        "invariant" => new MalformedArgumentException(DiagnosticCodes.SummarySeriesMismatch, Marker),
        // What the multipart reader raises past its own limits.
        "form" => new InvalidDataException(Marker),
        _ => new InvalidOperationException(Marker)
    };

    private static async Task<long> DrainAsync(HttpRequest request)
    {
        var buffer = new byte[8192];
        long received = 0;
        int read;
        while ((read = await request.Body.ReadAsync(buffer)) > 0)
            received += read;
        return received;
    }
}

/// <summary>A body the JSON parameter of <c>/probe/json</c> binds to.</summary>
internal sealed record ProbePayload(string Value);
