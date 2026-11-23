using System.Net;
using System.Text.Json;
using FitFix.Api.Contracts;
using FitFix.Core.Diagnostics;

namespace FitFix.Api.Tests;

/// <summary>
/// Story 2.8: the built SPA and <c>/api/*</c> on one origin (AD-12). The part that can silently
/// invert is the fallback — too wide and it answers a page where the contract promises a problem
/// document (AD-22), too narrow and a reloaded deep link is a 404. Both halves are asserted here
/// over real HTTP, against a web root this suite writes itself, because no test output carries a
/// <c>wwwroot</c> and the image is the only other place one exists.
/// </summary>
public class StaticServingTests : IDisposable
{
    /// <summary>Marks the entry document, so a response can be told from any other 200.</summary>
    private const string IndexMarker = "<!-- fitfix spa entry -->";

    /// <summary>Marks the hashed asset, which must arrive as itself and never as the page.</summary>
    private const string AssetMarker = "export const marker = 'fitfix-asset';";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "fitfix-webroot-" + Guid.NewGuid().ToString("N"));
    private readonly Lazy<ApiHost> _host;

    public StaticServingTests()
    {
        // What `vite build` leaves behind: the entry document and a hashed asset beside it.
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(
            Path.Combine(_webRoot, "index.html"),
            $"<!doctype html><html lang=\"uk\"><head><title>FitFix</title></head><body>{IndexMarker}<div id=\"root\"></div></body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "index-abc123.js"), AssetMarker);

        _host = new Lazy<ApiHost>(() => new ApiHost(webRoot: _webRoot));
    }

    private ApiHost Host => _host.Value;

    public void Dispose()
    {
        if (_host.IsValueCreated)
            _host.Value.Dispose();

        if (Directory.Exists(_webRoot))
            Directory.Delete(_webRoot, recursive: true);
    }

    [Fact]
    public async Task The_root_path_serves_the_entry_document()
    {
        using var response = await Host.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(IndexMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_asset_arrives_as_itself_with_its_own_content_type()
    {
        using var response = await Host.Client.GetAsync("/assets/index-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(AssetMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_deep_link_serves_the_entry_document_with_a_200()
    {
        // The reload of a result URL. A 404 here is the browser's blank page, and a redirect
        // would lose the path the client router reads.
        using var response = await Host.Client.GetAsync("/result");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(IndexMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_asset_stays_a_404_rather_than_becoming_the_page()
    {
        // A path naming a file is a bundle reference, not a navigation: answered with the page,
        // a deployment missing half its assets would look like a working one.
        using var response = await Host.Client.GetAsync("/assets/index-gone.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RouteNotFound, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task An_unknown_api_route_answers_a_problem_document_not_the_page()
    {
        using var response = await Host.Client.GetAsync("/api/nonsense");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemDetailsFactory.MediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(DiagnosticCodes.RouteNotFound, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task A_wrong_method_on_a_real_api_route_keeps_its_own_refusal()
    {
        // The reason this is a rewrite and not a mapped fallback: a catch-all endpoint would
        // also be a candidate here and would answer instead of the method refusal.
        using var response = await Host.Client.GetAsync("/api/repair");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(DiagnosticCodes.MethodNotAllowed, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task Health_still_answers_its_own_body()
    {
        using var response = await Host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_post_to_an_unknown_path_is_not_a_navigation()
    {
        using var response = await Host.Client.PostAsync("/result", new ByteArrayContent([1, 2, 3]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RouteNotFound, (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task With_no_web_root_an_unknown_path_is_the_route_refusal_it_is_today()
    {
        // A checkout where the frontend was never built, and the state every other API suite
        // runs in: the fallback must not exist when there is no page to fall back to.
        using var bare = new ApiHost();

        using var response = await bare.Client.GetAsync("/result");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RouteNotFound, (await ReadProblemAsync(response)).Code);
        Assert.Contains("GET (unmatched) -> 404", bare.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_served_path_never_reaches_the_log()
    {
        // AD-9 again, now that a path can be answered rather than refused: the request line
        // records the matched route, and a deep link matched none.
        using var deepLink = await Host.Client.GetAsync("/result");
        using var asset = await Host.Client.GetAsync("/assets/index-abc123.js");

        Assert.DoesNotContain("/result", Host.Logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("index-abc123", Host.Logs.Text, StringComparison.Ordinal);
    }

    private static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        return new ProblemResponse(
            Type: root.GetProperty("type").GetString()!,
            Title: root.GetProperty("title").GetString()!,
            Status: root.GetProperty("status").GetInt32(),
            Code: root.GetProperty("code").GetString()!);
    }
}
