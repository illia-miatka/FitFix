using System.Net.Sockets;
using System.Text;
using FitFix.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FitFix.Api.Tests;

/// <summary>
/// The API's own pipeline, running on Kestrel at a port the operating system picks. Kestrel is
/// the point of it: the 413 this story exists for is Kestrel counting the bytes of a body as a
/// handler reads them, and no in-memory stand-in has that counter.
/// </summary>
public sealed class ApiHost : IDisposable
{
    private readonly WebApplication _app;

    /// <param name="maxUploadBytes">
    /// The raw value of <c>FITFIX_MAX_UPLOAD_BYTES</c>, including one that does not parse — the
    /// fallback to the default is part of the contract. Null leaves it unset, as a deployment
    /// that configures nothing does.
    /// </param>
    /// <param name="withProbes">
    /// False hosts exactly what production hosts, with no endpoint added for a test's sake.
    /// </param>
    /// <param name="webRoot">
    /// Where the built SPA sits (Story 2.8). Null leaves the web root at its convention,
    /// <c>wwwroot</c> under the content root, which no test output has — the state every suite
    /// but the static one runs in.
    /// </param>
    /// <param name="settings">
    /// Anything else a deployment would configure, as <c>KEY=value</c> pairs — the rate limits,
    /// the trusted proxies. Passed the same way as the upload bound, so a suite can hold a
    /// limiter to two requests without waiting for sixty.
    /// </param>
    /// <param name="useGivenUrls">
    /// False lets the application choose its own address, for the one suite that is about the
    /// port a platform named. Every other suite takes a port from the operating system.
    /// </param>
    public ApiHost(
        string? maxUploadBytes = null,
        bool withProbes = true,
        string? webRoot = null,
        IEnumerable<string>? settings = null,
        bool useGivenUrls = true)
    {
        // Configuration travels as command-line arguments: no environment variable to leak
        // into another test running beside this one. The content root is the output directory,
        // where the build drops the API's own appsettings.json — the log levels under test are
        // the ones production runs, not a set invented here.
        List<string> args = [$"--contentRoot={AppContext.BaseDirectory}"];
        if (maxUploadBytes is not null)
            args.Add($"--{UploadLimit.ConfigurationKey}={maxUploadBytes}");
        if (webRoot is not null)
            args.Add($"--webroot={webRoot}");
        if (settings is not null)
            args.AddRange(settings.Select(setting => "--" + setting));

        var builder = FitFixApi.CreateBuilder([.. args]);
        if (useGivenUrls)
            builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Only the collecting provider: the console would print a test's deliberate failures.
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CollectingLoggerProvider(Logs));

        _app = FitFixApi.Build(builder, withProbes ? Probes.Map : null);
        _app.StartAsync().GetAwaiter().GetResult();

        var address = _app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("The host started without binding an address, so no request can reach it.");

        BaseAddress = new Uri(address);
        Client = new HttpClient { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Everything the hosted application logged, for the AD-9 assertions.</summary>
    public LogSink Logs { get; } = new();

    public Uri BaseAddress { get; }

    public HttpClient Client { get; }

    /// <summary>
    /// Sends a request <c>HttpClient</c> will not send — one whose path keeps its percent
    /// encoding, or that announces a body it never delivers — and reads the whole answer. The
    /// request has to ask for <c>Connection: close</c>, or the read waits for the keep-alive
    /// timeout; half-closing the socket instead makes Kestrel abandon the request unanswered.
    /// </summary>
    public async Task<string> SendRawAsync(string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(BaseAddress.Host, BaseAddress.Port);

        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Sends a request and leaves the connection open, so the test says when the client goes
    /// away rather than racing the server to it.
    /// </summary>
    public async Task<RawConnection> SendWithoutClosingAsync(string request)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(BaseAddress.Host, BaseAddress.Port);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(request));
            return new RawConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Waits for a log line to appear, for everything recorded after the client has been
    /// answered — or, when the client has gone, instead of answering it. The bound is generous
    /// on purpose: a loaded machine delays the line, it does not change what gets logged.
    /// </summary>
    public async Task<bool> WaitForLogAsync(string fragment, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (Logs.Text.Contains(fragment, StringComparison.Ordinal))
                return true;
            if (DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(25);
        }
    }

    public void Dispose()
    {
        Client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// A connection held open, so a test can abandon it at a moment of its own choosing — which is
/// the only way to play a client that goes away mid-upload without racing the server.
/// </summary>
public sealed class RawConnection(TcpClient client) : IDisposable
{
    /// <summary>
    /// Leaves without a word: linger zero makes the close a reset rather than an orderly
    /// shutdown, which is what a browser tab closing mid-upload looks like from here.
    /// </summary>
    public void Abort()
    {
        client.Client.LingerState = new LingerOption(enable: true, seconds: 0);
        client.Client.Close();
    }

    public void Dispose() => client.Dispose();
}
