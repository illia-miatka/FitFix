using System.Globalization;

namespace FitFix.Api;

/// <summary>
/// The port the service listens on when the platform picks it rather than the image.
/// </summary>
/// <remarks>
/// <para>
/// Cloud Run's container contract says it injects <c>PORT</c> and that the container "must
/// listen for requests on <c>0.0.0.0</c> on the port to which requests are sent" — requests are
/// sent to 8080 by default, but the port is a setting of the service, and a container listening
/// on the wrong one is a deployment that never becomes ready. ASP.NET Core does not read
/// <c>PORT</c> on its own; it reads <c>ASPNETCORE_HTTP_PORTS</c>, which the image sets to 8080.
/// </para>
/// <para>
/// So <c>PORT</c> wins where it is set, and nothing changes anywhere else: no platform in this
/// repository's own deployments sets it, and a checkout without it keeps the image's 8080.
/// </para>
/// </remarks>
public static class ListenPort
{
    /// <summary>The variable the platform injects.</summary>
    public const string ConfigurationKey = "PORT";

    /// <summary>
    /// Every address, not loopback: the request arrives from the platform's own network, and a
    /// service bound to 127.0.0.1 inside a container is a service nothing can reach.
    /// </summary>
    public const string AnyAddress = "0.0.0.0";

    /// <summary>
    /// The URL to listen on, or null when the platform named no port — which leaves the image's
    /// own <c>ASPNETCORE_HTTP_PORTS</c> in charge. A value that is not a usable port number is
    /// ignored for the same reason <see cref="UploadLimit"/> ignores a typo: falling back to the
    /// image's port leaves a service that works, where honouring nonsense leaves one that does
    /// not start.
    /// </summary>
    public static string? From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return int.TryParse(configuration[ConfigurationKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            && port is > 0 and <= 65535
                ? $"http://{AnyAddress}:{port.ToString(CultureInfo.InvariantCulture)}"
                : null;
    }
}
