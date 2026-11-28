using System.Net;
using System.Text.Json;
using FitFix.Api.Contracts;
using FitFix.Core.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace FitFix.Api.Tests;

/// <summary>
/// The timeouts of the public-beta plan (§2.3). A handler that never finishes holds a
/// connection, a thread and a rate-limiting permit for as long as the client will wait, so what
/// matters here is that it ends — and that it ends as the problem document of AD-22 with a code
/// of its own, rather than as the apology <c>INTERNAL_ERROR</c> would be.
/// </summary>
public class RequestTimeoutTests
{
    [Fact]
    public async Task A_handler_that_does_not_finish_is_answered_anyway()
    {
        using var host = new ApiHost(settings: [$"{RequestTimeouts.Keys.DefaultSeconds}=1"]);

        using var response = await host.Client.GetAsync("/probe/never-finishes");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(ProblemDetailsFactory.MediaType, response.Content.Headers.ContentType?.MediaType);

        var problem = JsonSerializer.Deserialize<ProblemResponse>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(problem);
        Assert.Equal(DiagnosticCodes.RequestTimedOut, problem.Code);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, problem.Status);
    }

    [Fact]
    public async Task The_timeout_is_logged_as_itself_and_not_as_a_failure_of_ours()
    {
        using var host = new ApiHost(settings: [$"{RequestTimeouts.Keys.DefaultSeconds}=1"]);

        using var response = await host.Client.GetAsync("/probe/never-finishes");

        Assert.Contains($"code {DiagnosticCodes.RequestTimedOut}", host.Logs.Text, StringComparison.Ordinal);

        // A timeout is not a broken invariant, so nothing about it belongs at error level: a
        // public deployment whose log fills with errors over slow requests stops being read.
        Assert.DoesNotContain(DiagnosticCodes.InternalError, host.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_answers_in_time_is_left_alone()
    {
        using var host = new ApiHost(settings: [$"{RequestTimeouts.Keys.DefaultSeconds}=30"]);

        using var response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void A_misconfigured_timeout_falls_back_to_the_default()
    {
        var timeouts = RequestTimeouts.From(Configuration(
            (RequestTimeouts.Keys.DefaultSeconds, "soon"),
            (RequestTimeouts.Keys.RepairSeconds, "0")));

        Assert.Equal(TimeSpan.FromSeconds(RequestTimeouts.Defaults.DefaultSeconds), timeouts.Default);
        Assert.Equal(TimeSpan.FromSeconds(RequestTimeouts.Defaults.RepairSeconds), timeouts.Repair);
    }

    [Fact]
    public void A_configured_timeout_is_the_one_that_takes_effect()
    {
        var timeouts = RequestTimeouts.From(Configuration(
            (RequestTimeouts.Keys.DefaultSeconds, "15"),
            (RequestTimeouts.Keys.RepairSeconds, "45")));

        Assert.Equal(TimeSpan.FromSeconds(15), timeouts.Default);
        Assert.Equal(TimeSpan.FromSeconds(45), timeouts.Repair);
    }

    [Fact]
    public void A_repair_is_given_longer_than_everything_else_by_default()
    {
        // The repair reads two files and writes a third inside the request (AD-11). A default
        // that applied to it as well would turn the slowest honest repair into a refusal.
        Assert.True(RequestTimeouts.Defaults.RepairSeconds > RequestTimeouts.Defaults.DefaultSeconds);
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
