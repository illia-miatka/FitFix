using System.Net;
using Microsoft.Extensions.Configuration;

namespace FitFix.Api.Tests;

/// <summary>
/// What a managed platform decides for the service rather than the image: the port it is given,
/// and how many entries of <c>X-Forwarded-For</c> the front end in front of it appends. Both are
/// configuration, because both differ per platform and getting either wrong is silent — a port
/// nothing can reach looks like a service that will not start, and a hop count that is too low
/// turns the per-client rate limit into one shared bucket.
/// </summary>
public class PlatformContractTests
{
    [Fact]
    public void A_platform_that_names_a_port_is_listened_to()
    {
        var url = ListenPort.From(Configuration((ListenPort.ConfigurationKey, "8081")));

        // Every address, not loopback: Cloud Run's contract says the container must not listen
        // on 127.0.0.1.
        Assert.Equal($"http://{ListenPort.AnyAddress}:8081", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eight thousand")]
    [InlineData("0")]
    [InlineData("70000")]
    public void Anything_that_is_not_a_port_leaves_the_image_in_charge(string? configured)
    {
        // Null here means the image's own ASPNETCORE_HTTP_PORTS stands. Honouring nonsense
        // instead would leave a container that never becomes ready.
        Assert.Null(ListenPort.From(Configuration((ListenPort.ConfigurationKey, configured))));
    }

    [Fact]
    public async Task The_service_answers_on_the_port_the_platform_named()
    {
        var port = FreePort();

        using var host = new ApiHost(
            useGivenUrls: false,
            settings: [$"{ListenPort.ConfigurationKey}={port}"]);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void One_hop_is_read_unless_the_deployment_says_otherwise()
    {
        var trusted = TrustedProxies.From(Configuration((TrustedProxies.ConfigurationKey, TrustedProxies.Any)));

        Assert.Equal(TrustedProxies.DefaultHops, trusted.Hops);
    }

    [Fact]
    public void A_front_end_that_appends_two_entries_is_read_two_deep()
    {
        // Google's front end appends the client's address and its forwarding rule's, in that
        // order, and says the entries before those two are not verified. Two hops from the
        // right is therefore exactly the client, whatever the caller put in front of it.
        var trusted = TrustedProxies.From(Configuration(
            (TrustedProxies.ConfigurationKey, TrustedProxies.Any),
            (TrustedProxies.HopsConfigurationKey, "2")));

        Assert.Equal(2, trusted.Hops);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("99")]
    [InlineData("lots")]
    public void A_hop_count_that_would_start_believing_the_caller_is_refused(string configured)
    {
        // Each hop read is an entry trusted. A number larger than the chain in front of the
        // service reads the caller's own entries, which is the one mistake here somebody can
        // exploit, so an unusable value falls back to one rather than to what was asked for.
        var trusted = TrustedProxies.From(Configuration(
            (TrustedProxies.ConfigurationKey, TrustedProxies.Any),
            (TrustedProxies.HopsConfigurationKey, configured)));

        Assert.Equal(TrustedProxies.DefaultHops, trusted.Hops);
    }

    [Fact]
    public async Task Two_hops_reach_past_the_front_end_and_ignore_what_the_caller_claimed()
    {
        using var host = new ApiHost(settings:
        [
            $"{TrustedProxies.ConfigurationKey}={TrustedProxies.Any}",
            $"{TrustedProxies.HopsConfigurationKey}=2",
            $"{RateLimits.Keys.InspectPermits}=1",
            $"{RateLimits.Keys.InspectWindowSeconds}=600"
        ]);

        // One client behind a front end that appended its address and its own, with a forged
        // entry in front. The forged entry must not buy a second permit.
        using var first = await host.Client.SendAsync(Inspect("198.51.100.1, 203.0.113.9, 10.0.0.1"));
        using var second = await host.Client.SendAsync(Inspect("198.51.100.2, 203.0.113.9, 10.0.0.1"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        // And a different client behind the same front end still gets its own.
        using var other = await host.Client.SendAsync(Inspect("203.0.113.10, 10.0.0.1"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }

    private static HttpRequestMessage Inspect(string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, InspectEndpoint.Route)
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent("original"), InspectEndpoint.RoleField }
            }
        };

        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return request;
    }

    /// <summary>A port the operating system has just confirmed is free, for the one test that
    /// cannot let Kestrel pick.</summary>
    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
