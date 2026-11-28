using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FitFix.Api.Contracts;
using FitFix.Core.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace FitFix.Api.Tests;

/// <summary>
/// The public-beta plan's per-client limits. Three things can invert quietly here and each has a
/// test: a limit that stops being per client (every request in one partition, or every request in
/// its own), a limit that reaches the page or the platform's health probe, and a refusal that
/// leaves as something other than the problem document of AD-22.
/// </summary>
/// <remarks>
/// Over real HTTP, like every suite here, because the partition key is the connection's remote
/// address and an in-memory transport has none.
/// </remarks>
public class RateLimitTests
{
    /// <summary>One permit per window, so the second request of a test is the refused one.</summary>
    private const string OnePermitEach =
        $"{RateLimits.Keys.InspectPermits}=1";

    /// <summary>A window long enough that no test waits for it to pass.</summary>
    private const string LongWindow =
        $"{RateLimits.Keys.InspectWindowSeconds}=600";

    [Fact]
    public async Task A_client_past_its_window_is_refused_with_the_shared_problem_document()
    {
        using var host = new ApiHost(settings: [OnePermitEach, LongWindow]);

        using var first = await host.Client.SendAsync(Inspect());
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);

        using var refused = await host.Client.SendAsync(Inspect());

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(ProblemDetailsFactory.MediaType, refused.Content.Headers.ContentType?.MediaType);

        var problem = JsonSerializer.Deserialize<ProblemResponse>(
            await refused.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(problem);
        Assert.Equal(DiagnosticCodes.TooManyRequests, problem.Code);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.Equal(ProblemDetailsFactory.TypePrefix + DiagnosticCodes.TooManyRequests, problem.Type);
    }

    [Fact]
    public async Task The_refusal_says_how_long_to_wait()
    {
        using var host = new ApiHost(settings: [OnePermitEach, LongWindow]);

        using var first = await host.Client.SendAsync(Inspect());
        using var refused = await host.Client.SendAsync(Inspect());

        // The window is the wait, and a `Retry-After` of zero would read as "ask again now".
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value.TotalSeconds, 1, 600);
    }

    [Fact]
    public async Task The_refusal_is_logged_by_its_code_and_without_the_client_address()
    {
        using var host = new ApiHost(settings: [OnePermitEach, LongWindow]);

        using var first = await host.Client.SendAsync(Inspect());
        using var refused = await host.Client.SendAsync(Inspect());

        var lines = host.Logs.Messages
            .Where(message => message.Contains(DiagnosticCodes.TooManyRequests, StringComparison.Ordinal))
            .ToList();

        // The request line is the record of a refusal (AD-9), so there has to be one.
        Assert.NotEmpty(lines);

        // And the partition key stays a partition key. An address is personal data on the same
        // footing as a coordinate, and the request line has no field for it.
        Assert.All(lines, line => Assert.DoesNotContain(host.BaseAddress.Host, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_forwarded_address_is_ignored_where_no_proxy_is_trusted()
    {
        using var host = new ApiHost(settings: [OnePermitEach, LongWindow]);

        // Two requests claiming to come from two clients. Nothing is configured to trust a
        // forwarded address, so they are one client — the connection's — and the limit holds.
        using var first = await host.Client.SendAsync(Inspect(forwardedFor: "203.0.113.7"));
        using var second = await host.Client.SendAsync(Inspect(forwardedFor: "203.0.113.8"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task A_forwarded_address_partitions_the_limit_where_the_peer_is_trusted()
    {
        using var host = new ApiHost(settings:
        [
            OnePermitEach,
            LongWindow,
            $"{TrustedProxies.ConfigurationKey}={TrustedProxies.Any}"
        ]);

        // The same two requests behind a trusted proxy: now they are two clients, each with its
        // own permit, which is the whole point of reading the header at all.
        using var first = await host.Client.SendAsync(Inspect(forwardedFor: "203.0.113.7"));
        using var second = await host.Client.SendAsync(Inspect(forwardedFor: "203.0.113.8"));
        using var third = await host.Client.SendAsync(Inspect(forwardedFor: "203.0.113.7"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task The_health_probe_is_never_refused()
    {
        using var host = new ApiHost(settings: [OnePermitEach, LongWindow]);

        // The platform polls it on a schedule of its own, and a machine declared unhealthy
        // because a stranger is uploading is a service that takes itself down.
        for (var i = 0; i < 5; i++)
        {
            using var response = await host.Client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_page_is_never_refused()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "fitfix-ratelimit-webroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><html lang=\"uk\"><body></body></html>");

        try
        {
            using var host = new ApiHost(webRoot: webRoot, settings: [OnePermitEach, LongWindow]);

            // A static file is answered before the limiter is reached, which is what keeps a
            // reload from costing a permit somebody needs for a repair.
            for (var i = 0; i < 5; i++)
            {
                using var response = await host.Client.GetAsync("/");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public void A_misconfigured_limit_falls_back_to_the_default_instead_of_opening_the_gate()
    {
        var limits = RateLimits.From(Configuration(
            (RateLimits.Keys.InspectPermits, "plenty"),
            (RateLimits.Keys.RepairPermits, "0"),
            (RateLimits.Keys.RepairWindowSeconds, "-5"),
            (RateLimits.Keys.Concurrency, "")));

        Assert.Equal(RateLimits.Defaults.InspectPermits, limits.InspectPermits);
        Assert.Equal(RateLimits.Defaults.RepairPermits, limits.RepairPermits);
        Assert.Equal(TimeSpan.FromSeconds(RateLimits.Defaults.RepairWindowSeconds), limits.RepairWindow);
        Assert.Equal(RateLimits.Defaults.Concurrency, limits.Concurrency);
    }

    [Fact]
    public void A_configured_limit_is_the_one_that_takes_effect()
    {
        var limits = RateLimits.From(Configuration(
            (RateLimits.Keys.InspectPermits, "7"),
            (RateLimits.Keys.InspectWindowSeconds, "30"),
            (RateLimits.Keys.RepairPermits, "3"),
            (RateLimits.Keys.RepairWindowSeconds, "90"),
            (RateLimits.Keys.Concurrency, "1"),
            // Zero is a deployment's to choose: refuse rather than queue.
            (RateLimits.Keys.Queue, "0")));

        Assert.Equal(7, limits.InspectPermits);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.InspectWindow);
        Assert.Equal(3, limits.RepairPermits);
        Assert.Equal(TimeSpan.FromSeconds(90), limits.RepairWindow);
        Assert.Equal(1, limits.Concurrency);
        Assert.Equal(0, limits.Queue);
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("any", false, true)]
    [InlineData("ANY", false, true)]
    [InlineData("nonsense", true, false)]
    public void Which_peers_may_speak_for_a_client_is_what_the_deployment_said(
        string? configured,
        bool none,
        bool anyPeer)
    {
        var trusted = TrustedProxies.From(Configuration((TrustedProxies.ConfigurationKey, configured)));

        Assert.Equal(none, trusted.None);
        Assert.Equal(anyPeer, trusted.AnyPeer);
    }

    [Fact]
    public void A_named_proxy_is_read_as_an_address_and_a_named_network_as_a_network()
    {
        var trusted = TrustedProxies.From(Configuration(
            (TrustedProxies.ConfigurationKey, "10.0.0.4, 172.16.0.0/12, not-an-address, ::1")));

        Assert.False(trusted.None);
        Assert.False(trusted.AnyPeer);
        Assert.Equal(["10.0.0.4", "::1"], trusted.Addresses.Select(address => address.ToString()));
        Assert.Single(trusted.Networks);
        Assert.Equal(12, trusted.Networks[0].PrefixLength);
    }

    /// <summary>
    /// A request to <c>/api/inspect</c> that the limiter counts and the handler then refuses:
    /// the limiter runs on the matched endpoint, before anything reads a body, so a request with
    /// nothing in it costs a permit exactly as a real upload does. That is what makes it usable
    /// here — no FIT file is needed to establish that counting works.
    /// </summary>
    private static HttpRequestMessage Inspect(string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, InspectEndpoint.Route)
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent("original", Encoding.UTF8), InspectEndpoint.RoleField }
            }
        };

        if (forwardedFor is not null)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);

        return request;
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
