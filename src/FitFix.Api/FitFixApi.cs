using System.Diagnostics;
using FitFix.Api.Contracts;
using FitFix.Core.Diagnostics;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace FitFix.Api;

/// <summary>
/// The service itself: the upload bound, the failure contract (Story 2.1), the health endpoint
/// and the inspect endpoint (Story 2.2). <c>Program</c> runs it; the HTTP tests host it with
/// probe endpoints of their own, so the pipeline they exercise is this one and not a copy of it.
/// The repair endpoint arrives in Story 2.3, the built SPA in Story 2.8.
/// </summary>
public static class FitFixApi
{
    /// <summary>What the request log calls a request that matched no endpoint.</summary>
    private const string UnmatchedRoute = "(unmatched)";

    /// <summary>
    /// What it calls one refused before routing ran. It is not "(unmatched)": nothing looked
    /// for a match, and the endpoint of whatever used this connection before is not this
    /// request's route.
    /// </summary>
    private const string NotRoutedRoute = "(not routed)";

    /// <summary>Keeps a log line short enough to read, and bounded whatever a request sends.</summary>
    private const int MaxLoggedLength = 48;

    /// <summary>
    /// The builder with the input bound applied (AD-13): Kestrel's own ceiling, the multipart
    /// reader's and the threshold past which an uploaded part would be buffered to a temporary
    /// file, all three from <c>FITFIX_MAX_UPLOAD_BYTES</c> — so the form reader's 128 MB default
    /// cannot sit above the number a deployment chose, and nothing uploaded reaches the disk.
    /// </summary>
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var maxUploadBytes = UploadLimit.From(builder.Configuration);

        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadBytes);

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxUploadBytes;
            options.BufferBodyLengthLimit = maxUploadBytes;
            // An uploaded part larger than this threshold is buffered to a temporary file, and
            // the default is 64 KB — every real activity. Nothing uploaded may touch the disk
            // (AD-3), and Story 2.8's container has a read-only filesystem to write it to, so
            // the threshold is the upload bound itself: a part that fits the limit fits memory.
            // UploadLimit keeps that bound inside int, which is what makes this a safe cast.
            options.MemoryBufferThreshold = (int)maxUploadBytes;
        });

        return builder;
    }

    /// <summary>
    /// The application: the failure handler, the health endpoint, <c>POST /api/inspect</c>, and
    /// whatever <paramref name="additionalEndpoints"/> adds. Production adds none — the probes
    /// that raise each kind of failure on demand belong to the test host, not to the shipped
    /// binary.
    /// </summary>
    public static WebApplication Build(
        WebApplicationBuilder builder,
        Action<IEndpointRouteBuilder>? additionalEndpoints = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var app = builder.Build();
        var maxUploadBytes = UploadLimit.From(app.Configuration);

        // One category for the request line, so a deployment can filter it on its own. AD-9
        // governs what it may carry: duration, input size and codes, never a coordinate, a
        // file's contents or an uploaded file's name.
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FitFix.Api.Requests");

        // UploadLimit.From swallows a typo on purpose, so the value that took effect is stated
        // once rather than left to be guessed from a refusal.
        logger.LogInformation("The upload limit is {MaxUploadBytes} bytes.", maxUploadBytes);

        // The failure handler, outermost on purpose: it covers the endpoints, the routing that
        // matches none, and the body-size refusal Kestrel raises while a handler is reading.
        app.Use((context, next) => HandleAsync(context, next, logger, maxUploadBytes));

        app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")));

        // Story 2.2. Its own refusals travel through the handler above like every other, so the
        // endpoint states what is wrong with a code and never writes a status of its own.
        InspectEndpoint.Map(app);

        additionalEndpoints?.Invoke(app);

        return app;
    }

    private static async Task HandleAsync(HttpContext context, RequestDelegate next, ILogger logger, long maxUploadBytes)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var inputBytes = context.Request.ContentLength;
        var method = Loggable(context.Request.Method);
        var routed = false;
        string? code = null;

        // The route is only this request's once routing has run for it.
        string Route() => routed ? MatchedRoute(context) : NotRoutedRoute;

        try
        {
            if (!TryBoundBody(context, maxUploadBytes))
            {
                // A body with no ceiling at all is worse than a refusal, and silence about it
                // worse still: the bound is the one thing this shell is responsible for.
                logger.LogError("The body bound of {MaxUploadBytes} bytes could not be applied to the request.", maxUploadBytes);
                code = await RefuseAsync(context, StatusCodes.Status500InternalServerError, DiagnosticCodes.InternalError, logger);
            }
            else if (inputBytes > maxUploadBytes)
            {
                // Content-Length already says the body is too large, so nothing needs reading
                // to refuse it, and no handler sees a request it would only fail on (AD-13).
                code = await RefuseAsync(context, StatusCodes.Status413PayloadTooLarge, DiagnosticCodes.FileTooLarge, logger);
            }
            else
            {
                routed = true;
                await next(context);
                code = await RewriteBodilessFailureAsync(context, logger);
            }
        }
        // Kestrel's own refusals, each carrying the status it chose: 413 for a body past the
        // bound, 400 for one that stopped before the length it announced. An upload that ends
        // early is the most ordinary client failure there is, not a fault of the service.
        catch (BadHttpRequestException exception)
        {
            code = await RefuseAsync(context, exception.StatusCode, FailureStatus.ForStatus(exception.StatusCode), logger);
        }
        // What the form reader raises, for its size limit and for a malformed form alike: a
        // multipart header with no boundary, more form fields than the count limit, a part that
        // ends mid-header. Only a body that actually reached the bound is a size refusal — the
        // reader's own length limit is the same number (AD-13), and Kestrel, which counts the
        // bytes as they arrive, refuses anything past it before this. Everything below the bound
        // is a request the service could not parse, and telling its sender that their file is too
        // large would send them looking for a smaller one.
        catch (InvalidDataException)
        {
            code = inputBytes >= maxUploadBytes
                ? await RefuseAsync(context, StatusCodes.Status413PayloadTooLarge, DiagnosticCodes.FileTooLarge, logger)
                : await RefuseAsync(context, StatusCodes.Status400BadRequest, DiagnosticCodes.RequestMalformed, logger);
        }
        // The client left. Nothing can be written any more, and nothing is wrong here.
        catch (Exception exception) when (IsClientGone(context, exception))
        {
            code = DiagnosticCodes.ClientDisconnected;
            logger.LogInformation("The client left before {Method} {Route} was answered.", method, Route());
        }
        // Every core refusal carries a code from the registry (AD-18), so one catch covers them
        // all. The status comes from the code, not from the type: the same exception type is
        // raised over a malformed file and over a series the repair itself got wrong. The
        // exception's own text stays here — the human sentence lives in the UI dictionary (AD-4).
        catch (FitFixException exception)
        {
            var status = FailureStatus.ForCode(exception.Code);

            // A broken invariant of ours must reach whoever reads the logs, not only the user.
            if (status >= StatusCodes.Status500InternalServerError)
                logger.LogError(exception, "A broken invariant surfaced as {Code}.", exception.Code);

            code = await RefuseAsync(context, status, exception.Code, logger);
        }
        catch (Exception exception)
        {
            // The one place the exception is recorded, and only in the log: the response says
            // INTERNAL_ERROR and nothing else about it.
            logger.LogError(exception, "Unexpected failure on {Method} {Route}.", method, Route());
            code = await RefuseAsync(context, StatusCodes.Status500InternalServerError, DiagnosticCodes.InternalError, logger);
        }
        finally
        {
            logger.LogInformation(
                "{Method} {Route} -> {Status} in {DurationMs:F1} ms, input {InputBytes} bytes, code {Code}",
                method,
                // The matched route, never the request's own path: a path arrives
                // percent-decoded, so it could write lines of its own into the log (AD-9), and
                // in Story 2.2 it could carry a file name.
                Route(),
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                // A body with no declared length counts as none: the number is what the
                // request announced, and AD-9 allows exactly that much about the input.
                inputBytes ?? 0,
                code ?? "-");
        }
    }

    /// <summary>
    /// Writes the problem document and returns the code that actually left, which is not
    /// always the one asked for: an undeclared code becomes the shell's own failure, and the
    /// log must describe the response the client received.
    /// </summary>
    private static async Task<string> RefuseAsync(HttpContext context, int status, string code, ILogger logger)
    {
        var problem = ProblemDetailsFactory.Create(status, code);

        if (!await ProblemDetailsFactory.TryWriteAsync(context, problem))
            logger.LogWarning("The response had already started; {Code} could not be sent as a body.", problem.Code);

        return problem.Code;
    }

    /// <summary>
    /// Gives a failure status the framework settled by itself a body with a code in it — a
    /// wrong method, a media type an endpoint cannot read, a path that matched nothing.
    /// Returns null when there is nothing to rewrite.
    /// </summary>
    private static async Task<string?> RewriteBodilessFailureAsync(HttpContext context, ILogger logger)
    {
        var response = context.Response;

        if (response.HasStarted || response.StatusCode < StatusCodes.Status400BadRequest || response.ContentLength > 0)
            return null;

        // A 404 an endpoint chose is its own answer: a resource missing inside Story 2.3, or
        // Story 2.8's index.html fallback. Only routing's own 404, where nothing matched at
        // all, belongs to this handler.
        if (response.StatusCode == StatusCodes.Status404NotFound && context.GetEndpoint() is not null)
            return null;

        return await RefuseAsync(context, response.StatusCode, FailureStatus.ForStatus(response.StatusCode), logger);
    }

    /// <summary>
    /// Narrows this request's body bound to the configured limit. Kestrel already carries the
    /// same number server-wide; this establishes it for the request as well, and reports
    /// false when it could not be established at all.
    /// </summary>
    private static bool TryBoundBody(HttpContext context, long maxUploadBytes)
    {
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maxUploadBytes;
            return true;
        }

        // A request carrying no body at all needs no bound.
        if (context.Request.ContentLength is null or 0 && context.Request.Headers.TransferEncoding.Count == 0)
            return true;

        // Already bounded at least as tightly by whoever made it read-only.
        return feature?.MaxRequestBodySize is { } bound && bound <= maxUploadBytes;
    }

    private static bool IsClientGone(HttpContext context, Exception exception) =>
        exception is ConnectionResetException
        || (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested);

    /// <summary>The route pattern the request matched, which the application wrote, not the client.</summary>
    private static string MatchedRoute(HttpContext context) => context.GetEndpoint() switch
    {
        RouteEndpoint route => Loggable(route.RoutePattern.RawText ?? UnmatchedRoute),
        { DisplayName: { } name } => Loggable(name),
        _ => UnmatchedRoute
    };

    /// <summary>
    /// Strips every control character, so nothing a request chose can open a line of its own
    /// in the log. AD-9 makes that log the one trustworthy record of a refusal.
    /// </summary>
    private static string Loggable(string value) =>
        new([.. value.Take(MaxLoggedLength).Select(character => char.IsControl(character) ? '?' : character)]);
}
