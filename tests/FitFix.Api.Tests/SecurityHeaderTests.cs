using System.Net;
using System.Text.RegularExpressions;
using FitFix.Core.Diagnostics;

namespace FitFix.Api.Tests;

/// <summary>
/// The headers of the public-beta plan (§2.4), and the two ways they can be wrong without
/// anyone noticing: a header that reaches the page but not a problem document, and a
/// content-security policy that forbids the very basemap the page draws.
/// </summary>
public class SecurityHeaderTests
{
    [Fact]
    public async Task Every_answer_carries_the_headers()
    {
        using var host = new ApiHost();

        using var response = await host.Client.GetAsync("/health");

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Single(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal(SecurityHeaders.ReferrerPolicy, Single(response, "Referrer-Policy"));
        Assert.Equal(SecurityHeaders.PermissionsPolicy, Single(response, "Permissions-Policy"));
        Assert.Equal("same-origin", Single(response, "Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", Single(response, "Cross-Origin-Resource-Policy"));
    }

    [Fact]
    public async Task A_problem_document_carries_them_too()
    {
        using var host = new ApiHost();

        // The response is cleared when a problem document is written (AD-22), so headers set
        // before the failure would be lost with it. This is the assertion that says they are not.
        using var response = await host.Client.GetAsync("/api/nonsense");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Single(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task The_referrer_policy_still_lets_the_tile_server_see_where_the_map_is_drawn()
    {
        using var host = new ApiHost();

        using var response = await host.Client.GetAsync("/health");

        // The OpenStreetMap tile policy expects a Referer and names a restrictive referrer
        // policy among the things it does not allow. `no-referrer` here would make the map's
        // traffic the kind that gets blocked without notice.
        var policy = Single(response, "Referrer-Policy");
        Assert.DoesNotContain("no-referrer", policy, StringComparison.Ordinal);
        Assert.Equal("strict-origin-when-cross-origin", policy);
    }

    [Fact]
    public void The_policy_allows_the_basemap_the_page_actually_loads()
    {
        var routeMap = RepositoryFile("web/src/components/RouteMap.tsx");

        // No checkout of the frontend, nothing to compare against — the same silence the
        // private fixtures keep.
        if (routeMap is null)
            return;

        var tileUrl = Regex.Match(routeMap, @"https://[^'""{\s]+", RegexOptions.None, TimeSpan.FromSeconds(5));
        Assert.True(tileUrl.Success, "No tile URL was found in RouteMap.tsx, so the policy cannot be checked against it.");

        var host = new Uri(tileUrl.Value).GetLeftPart(UriPartial.Authority);

        // The page and the policy have to name the same host. Moving one without the other
        // leaves a map that loads nothing and a browser console nobody reads.
        Assert.Equal(SecurityHeaders.MapTileHost, host);
        Assert.Contains(host, SecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_http_request_is_not_told_to_remember_https()
    {
        using var host = new ApiHost();

        using var response = await host.Client.GetAsync("/health");

        // Every local deployment is plain HTTP. A year of HSTS from one of them would lock a
        // browser out of the service it was testing.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task A_request_forwarded_as_https_is()
    {
        using var host = new ApiHost(settings: [$"{TrustedProxies.ConfigurationKey}={TrustedProxies.Any}"]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(SecurityHeaders.StrictTransportSecurity, Single(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task The_scheme_is_not_taken_from_an_untrusted_peer()
    {
        using var host = new ApiHost();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await host.Client.SendAsync(request);

        // Nothing is configured to trust a forwarded header, so the request is what it is: HTTP.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task The_service_does_not_announce_which_server_it_is()
    {
        using var host = new ApiHost();

        using var response = await host.Client.GetAsync("/health");

        Assert.False(response.Headers.Contains("Server"));
    }

    private static string Single(HttpResponseMessage response, string name)
    {
        Assert.True(
            response.Headers.TryGetValues(name, out var values),
            $"The response carried no {name} header.");

        return Assert.Single(values!);
    }

    /// <summary>
    /// A file of the repository, found by walking up from the test binary to the solution. The
    /// frontend is not an input of this project, so the walk may come back empty — in the image,
    /// for instance — and the test says nothing rather than failing on an absence.
    /// </summary>
    internal static string? RepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "FitFix.slnx")))
                continue;

            var file = Path.Combine(directory.FullName, relativePath);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }

        return null;
    }
}
