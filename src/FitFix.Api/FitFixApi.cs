using System.Diagnostics;
using System.Threading.RateLimiting;
using FitFix.Api.Contracts;
using FitFix.Core.Diagnostics;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;

namespace FitFix.Api;

/// <summary>
/// The service itself: the upload bound, the failure contract (Story 2.1), the health endpoint,
/// the inspect endpoint (Story 2.2), the repair endpoint (Story 2.3) and the built SPA beside
/// them on one origin (Story 2.8). <c>Program</c> runs it; the HTTP tests host it with probe
/// endpoints of their own, so the pipeline they exercise is this one and not a copy of it.
/// </summary>
public static class FitFixApi
{
    /// <summary>What the request log calls a request that matched no endpoint.</summary>
    private const string UnmatchedRoute = "(unmatched)";

    /// <summary>
    /// The one prefix that is a contract rather than a page (AD-12). Everything under it
    /// answers as itself, including a route that matches nothing.
    /// </summary>
    private const string ApiPrefix = "/api";

    /// <summary>
    /// The SPA's entry document, and the only file an unknown path is ever answered with.
    /// </summary>
    private const string IndexFile = "index.html";

    /// <summary>
    /// What it calls one refused before routing ran. It is not "(unmatched)": nothing looked
    /// for a match, and the endpoint of whatever used this connection before is not this
    /// request's route.
    /// </summary>
    private const string NotRoutedRoute = "(not routed)";

    /// <summary>Keeps a log line short enough to read, and bounded whatever a request sends.</summary>
    private const int MaxLoggedLength = 48;

    /// <summary>The global limiter's partition for everything it does not bound: the page.</summary>
    private const string UnlimitedPartition = "unlimited";

    /// <summary>
    /// The rate-limiting partition of a request whose peer has no address. It is a name and not
    /// an address, and it is the only partition key that ever appears in this file.
    /// </summary>
    private const string UnknownClient = "unknown";

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

        AddClientAddress(builder);
        AddRateLimiting(builder);

        return builder;
    }

    /// <summary>
    /// Reads <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from the peers a deployment
    /// trusts, so that behind a terminating proxy a request's client address is the client's
    /// and its scheme is the one the browser used. The rate limiter partitions on that address
    /// and HSTS is emitted on that scheme; both are wrong without this, and both would be wrong
    /// in the other direction if the header were read from anyone (see <see cref="TrustedProxies"/>).
    /// </summary>
    private static void AddClientAddress(WebApplicationBuilder builder)
    {
        var trusted = TrustedProxies.From(builder.Configuration);

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Nothing configured, nothing read: the socket's address and scheme stand, which is
            // both what a laptop wants and the safe side of the mistake — an unconfigured
            // deployment limits everyone together rather than believing a header.
            options.ForwardedHeaders = trusted.None
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            options.ForwardLimit = TrustedProxies.ForwardLimit;

            // The framework trusts loopback by default. Which peers may speak for a client is a
            // deployment's decision here, so both lists start empty — and empty is exactly what
            // "any" means to the middleware: with nothing to check against, it reads the header
            // from whoever sent it, which is only ever right where that peer cannot be anyone
            // but the platform's proxy.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            if (trusted.AnyPeer || trusted.None)
                return;

            foreach (var address in trusted.Addresses)
                options.KnownProxies.Add(address);

            foreach (var network in trusted.Networks)
                options.KnownIPNetworks.Add(network);
        });
    }

    /// <summary>
    /// The per-client windows on <c>/api/inspect</c> and <c>/api/repair</c>, and the concurrency
    /// bound across <c>/api</c> as a whole. A repair runs inside the request (AD-11) on a machine
    /// with one shared core, so what protects a public deployment is not only how often one
    /// client may ask but how many requests are in flight at once.
    /// </summary>
    /// <remarks>
    /// A refusal leaves as a problem document with <c>TOO_MANY_REQUESTS</c>, like every other
    /// refusal (AD-22). The partition key is the client's address and it is never logged: the
    /// request line has no field for it, and an address is personal data on the same footing as
    /// a coordinate (AD-9).
    /// </remarks>
    private static void AddRateLimiting(WebApplicationBuilder builder)
    {
        var limits = RateLimits.From(builder.Configuration);

        builder.Services.AddRateLimiter(options =>
        {
            // Across every client and both endpoints. /api and nothing else: the platform polls
            // /health on a schedule of its own, and the page is a handful of cacheable files
            // that never reach a handler.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments(ApiPrefix)
                    ? RateLimitPartition.GetConcurrencyLimiter(ApiPrefix, _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = limits.Concurrency,
                        QueueLimit = limits.Queue,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    })
                    : RateLimitPartition.GetNoLimiter(UnlimitedPartition));

            options.AddPolicy(
                RateLimits.InspectPolicy,
                context => PerClient(context, limits.InspectPermits, limits.InspectWindow));

            options.AddPolicy(
                RateLimits.RepairPolicy,
                context => PerClient(context, limits.RepairPermits, limits.RepairWindow));

            options.OnRejected = RejectAsync;
        });
    }

    /// <summary>
    /// One fixed window per client address. No queue: a client past its window is told so now,
    /// because a page waiting silently for a minute is worse than a sentence it can read.
    /// </summary>
    private static RateLimitPartition<string> PerClient(HttpContext context, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            // A request with no remote address — a unix socket, a test's in-memory transport —
            // shares one partition rather than escaping the limit.
            context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });

    /// <summary>
    /// What a client past a limit receives: the shell's own problem document (AD-22) and, when
    /// the limiter knows it, how long to wait. The log line is the request line's, which carries
    /// the code and no address.
    /// </summary>
    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var problem = ProblemDetailsFactory.Create(StatusCodes.Status429TooManyRequests, DiagnosticCodes.TooManyRequests);

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window)
            // A window of less than a second still means "not now": one second, never zero.
            ? Math.Max(1, (int)Math.Ceiling(window.TotalSeconds))
            : (int?)null;

        await ProblemDetailsFactory.TryWriteAsync(context.HttpContext, problem, retryAfter);
    }

    /// <summary>
    /// The application: the failure handler, the built SPA, the health endpoint,
    /// <c>POST /api/inspect</c>, <c>POST /api/repair</c>, and whatever
    /// <paramref name="additionalEndpoints"/> adds. Production adds none — the probes
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

        // Before anything reads an address or a scheme: behind a terminating proxy both arrive
        // in a header, and from here on the request reports the client's rather than the
        // proxy's. A deployment that trusts no peer gets the socket's own, unchanged.
        app.UseForwardedHeaders();

        // The failure handler, outermost on purpose: it covers the endpoints, the routing that
        // matches none, and the body-size refusal Kestrel raises while a handler is reading.
        app.Use((context, next) => HandleAsync(context, next, logger, maxUploadBytes));

        // Story 2.8: the built SPA on the same origin as the API (AD-12). Inside the failure
        // handler, so a static file that cannot be read still leaves as a problem document.
        var webRoot = app.Environment.WebRootFileProvider;
        app.Use((context, next) => ServeSpaAsync(context, next, webRoot));
        // No UseDefaultFiles: the rewrite above already answers "/" with index.html, and where
        // there is no index.html there is no default file to find either.
        // The entry document must be revalidated on every load: it names the hashed bundle, and
        // without a Cache-Control header a browser may reuse a days-old copy by heuristic and keep
        // running the previous build after the image was replaced. The hashed assets beside it
        // change name with their content, so they stay cacheable.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
                    context.Context.Response.Headers.CacheControl = "no-cache";
            },
        });

        // After the static files, so the page is never rate-limited: a request for a hashed
        // bundle is answered above and never reaches the limiter. /health is mapped below and
        // does reach it, where the global limiter leaves it alone — it is not under /api, and
        // the platform's own probe must not be refused because a stranger is busy uploading.
        app.UseRateLimiter();

        app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")));

        // Story 2.2. Its own refusals travel through the handler above like every other, so the
        // endpoint states what is wrong with a code and never writes a status of its own.
        InspectEndpoint.Map(app).RequireRateLimiting(RateLimits.InspectPolicy);

        // Story 2.3, through the same handler: the repair's own refusals are the core's, each
        // carrying a code, and the one answer it writes itself is the successful one.
        RepairEndpoint.Map(app).RequireRateLimiting(RateLimits.RepairPolicy);

        additionalEndpoints?.Invoke(app);

        return app;
    }

    /// <summary>
    /// Hands an unknown path the SPA's entry document, so opening or reloading a deep link
    /// works (AD-12): the request is rewritten to <c>index.html</c> and the static-file
    /// middleware below serves it with its own content type and validators.
    /// </summary>
    /// <remarks>
    /// A rewrite and not a mapped fallback endpoint, on purpose. A catch-all endpoint is also a
    /// candidate for <c>/api/repair</c>, and — carrying no method metadata of its own — it wins
    /// over that endpoint when the method does not match, which would turn the method refusal
    /// of AD-22 into a page. Routing has already run by the time this is reached, because
    /// <c>WebApplication</c> puts its own routing middleware outermost, so a path that matched
    /// an endpoint is recognised here rather than guessed at.
    /// </remarks>
    private static Task ServeSpaAsync(HttpContext context, RequestDelegate next, IFileProvider webRoot)
    {
        if (ShouldServeIndex(context, webRoot))
            context.Request.Path = "/" + IndexFile;

        return next(context);
    }

    private static bool ShouldServeIndex(HttpContext context, IFileProvider webRoot)
    {
        var request = context.Request;

        // Only a navigation can be answered with a page. A POST to an unknown path is not one.
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            return false;

        // An endpoint of ours answers for itself, and so does the method refusal routing
        // settles when one matches the path but not the verb.
        if (context.GetEndpoint() is not null)
            return false;

        // /api is a contract (AD-22): an unknown route under it stays ROUTE_NOT_FOUND. A
        // client parsing a problem document must never be handed a page instead, or a typo in
        // a route becomes a mystery.
        if (request.Path.StartsWithSegments(ApiPrefix))
            return false;

        // A path naming a file is an asset request, and a missing asset is a 404: answered
        // with the page, a stale bundle reference would look like a working deployment.
        if (Path.HasExtension(request.Path.Value))
            return false;

        // No SPA in the web root — a checkout where the frontend was never built, and every
        // API test. The request then stays exactly what it is without this story: routing's
        // own 404, which the failure handler turns into ROUTE_NOT_FOUND.
        return webRoot.GetFileInfo(IndexFile).Exists;
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
                code = await RewriteBodilessFailureAsync(context, logger) ?? NameAnsweredFailure(context);
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

        // A 404 an endpoint chose is its own answer: a resource missing inside Story 2.3.
        // Only routing's own 404, where nothing matched at all, belongs to this handler.
        if (response.StatusCode == StatusCodes.Status404NotFound && context.GetEndpoint() is not null)
            return null;

        return await RefuseAsync(context, response.StatusCode, FailureStatus.ForStatus(response.StatusCode), logger);
    }

    /// <summary>
    /// Names a refusal that answered itself before this handler saw it — the rate limiter, which
    /// writes its own problem document from <c>OnRejected</c>. Without this the request line would
    /// report the status and a code of "-", and the one record of a refusal (AD-9) would not say
    /// what was refused. Returns null for anything that has not already been written, which is
    /// where <see cref="RewriteBodilessFailureAsync"/> is still the answer.
    /// </summary>
    private static string? NameAnsweredFailure(HttpContext context) =>
        context.Response.HasStarted && context.Response.StatusCode >= StatusCodes.Status400BadRequest
            ? FailureStatus.ForStatus(context.Response.StatusCode)
            : null;

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
