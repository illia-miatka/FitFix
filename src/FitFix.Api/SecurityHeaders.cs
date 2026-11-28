namespace FitFix.Api;

/// <summary>
/// The headers a browser needs to be told, once the service is somewhere strangers can reach
/// it: what may be loaded (and from where), that the content type is not to be guessed at, that
/// the page is not to be framed, and — on HTTPS — that it is only ever to be fetched over HTTPS.
/// </summary>
/// <remarks>
/// <para>
/// TLS is terminated in front of the container (a platform's router, or Caddy on a host), so the
/// service keeps speaking HTTP on its one port and there is deliberately no redirect here: a
/// redirect to HTTPS behind a proxy that already terminated it is how a loop is made. Whether a
/// request arrived over HTTPS is known only through the forwarded headers, which are read from
/// the peers <see cref="TrustedProxies"/> names.
/// </para>
/// <para>
/// One header is not set to its strictest value, and that is on purpose: see
/// <see cref="ReferrerPolicy"/>.
/// </para>
/// </remarks>
public static class SecurityHeaders
{
    /// <summary>
    /// Where the basemap comes from, named in the policy so that the one thing the page fetches
    /// from outside this origin is written down rather than implied. It must stay equal to the
    /// tile URL in <c>web/src/components/RouteMap.tsx</c>, which a test checks.
    /// </summary>
    public const string MapTileHost = "https://tile.openstreetmap.org";

    /// <summary>
    /// A year, which is what a browser is asked to remember about this host. Only ever sent on a
    /// request that arrived over HTTPS: a deployment reached over plain HTTP, which is every
    /// local one, must not be able to lock itself out of itself.
    /// </summary>
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    /// <summary>
    /// Deliberately not <c>no-referrer</c>. The OpenStreetMap tile usage policy expects a valid
    /// <c>Referer</c> on tile requests and names a restrictive referrer policy among the things
    /// that are not allowed; stripping it would make our map the kind of traffic that gets
    /// blocked without notice. This value sends the origin and never the path, which is all the
    /// tile server is being told, and nothing at all over a downgrade.
    /// </summary>
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";

    /// <summary>
    /// What the page may load. <c>'unsafe-inline'</c> appears for styles alone, because Leaflet
    /// positions tiles, panes and markers with <c>style</c> attributes, which is what
    /// <c>style-src</c> governs; scripts have no such allowance. Everything else is this origin,
    /// except the basemap's tiles and the <c>data:</c> URLs Leaflet's own images use.
    /// </summary>
    public static readonly string ContentSecurityPolicy = string.Join("; ",
        "default-src 'self'",
        $"img-src 'self' data: {MapTileHost}",
        "style-src 'self' 'unsafe-inline'",
        "script-src 'self'",
        "connect-src 'self'",
        "font-src 'self'",
        "object-src 'none'",
        "base-uri 'none'",
        // Nothing on this origin is meant to be embedded anywhere, and the service posts no
        // form to anyone: the uploads travel as fetch requests to its own API.
        "frame-ancestors 'none'",
        "form-action 'none'");

    /// <summary>
    /// Nothing the page does needs a device or a position. The route being drawn is the one in
    /// the file, never the one the browser is standing on.
    /// </summary>
    public const string PermissionsPolicy = "geolocation=(), camera=(), microphone=()";

    /// <summary>
    /// Writes the headers on every answer, the problem documents of AD-22 included.
    /// </summary>
    /// <remarks>
    /// As the response starts, and not here and now: writing a problem document clears the
    /// response first, so anything set before a failure would leave with it. A callback runs
    /// after that clearing and before the first byte, which is the one moment that holds for
    /// an answer and for a refusal alike.
    /// </remarks>
    public static Task WriteAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        context.Response.OnStarting(static state =>
        {
            var starting = (HttpContext)state;
            var headers = starting.Response.Headers;

            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = ReferrerPolicy;
            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            if (starting.Request.IsHttps)
                headers.StrictTransportSecurity = StrictTransportSecurity;

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
