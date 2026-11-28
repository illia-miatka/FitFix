using System.Net;

namespace FitFix.Api;

/// <summary>
/// Which peers may speak for a client. Behind a terminating proxy the socket's address is the
/// proxy's, so every request would land in one rate-limiting partition and the limit would stop
/// being per client at all; <c>X-Forwarded-For</c> is what carries the real address. It is also
/// a header anyone can write, so it is read only from a peer a deployment named.
/// </summary>
/// <remarks>
/// Unset means no forwarded header is read and the socket's own address stands. That is the
/// right default for <c>docker compose up</c> on a laptop, and it fails closed: a deployment
/// that forgot to configure its proxy gets one shared partition — a limit that is too strict,
/// never one a client can step around by sending a header.
/// </remarks>
public static class TrustedProxies
{
    /// <summary>The configuration key: a comma-separated list, or <see cref="Any"/>.</summary>
    public const string ConfigurationKey = "FITFIX_TRUSTED_PROXIES";

    /// <summary>
    /// The configuration key for how many hops of <c>X-Forwarded-For</c> the trusted peer
    /// appends. See <see cref="Trusted.Hops"/> for why a deployment has to say.
    /// </summary>
    public const string HopsConfigurationKey = "FITFIX_FORWARD_HOPS";

    /// <summary>
    /// Trusts whichever peer the request arrived from. Correct only where the container cannot
    /// be reached except through the platform's proxy — a Fly.io machine on its private network,
    /// a container published on loopback behind Caddy — because there the peer is always that
    /// proxy. A container with a port open to the internet must name addresses instead, or a
    /// client could write its own <c>X-Forwarded-For</c> and own a partition per request.
    /// </summary>
    public const string Any = "any";

    /// <summary>
    /// How many hops are read when a deployment does not say: one. A proxy that terminates TLS
    /// itself appends the address it accepted the connection from, so the last entry is the one
    /// it wrote and everything left of it is whatever the client claimed.
    /// </summary>
    public const int DefaultHops = 1;

    /// <summary>
    /// The most hops a deployment may ask for. Each hop read is an entry trusted, and a number
    /// larger than the chain in front of the service would start reading the caller's own
    /// entries — the one mistake here that a client can exploit.
    /// </summary>
    public const int MaxHops = 4;

    /// <summary>
    /// What the deployment named, split into the addresses and networks to trust.
    /// <see cref="Trusted.Any"/> is true for <see cref="Any"/>, and
    /// <see cref="Trusted.None"/> is true when nothing was configured or nothing parsed.
    /// </summary>
    public static Trusted From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hops = Hops(configuration);
        var configured = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(configured))
            return new Trusted([], [], AnyPeer: false, Hops: hops);

        if (configured.Trim().Equals(Any, StringComparison.OrdinalIgnoreCase))
            return new Trusted([], [], AnyPeer: true, Hops: hops);

        List<IPAddress> addresses = [];
        List<IPNetwork> networks = [];

        foreach (var entry in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A prefix length makes it a network, its absence a single address. An entry that is
            // neither is dropped rather than refused: the same reasoning as UploadLimit's, and a
            // dropped entry can only narrow what is trusted.
            if (entry.Contains('/', StringComparison.Ordinal))
            {
                if (IPNetwork.TryParse(entry, out var network))
                    networks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                addresses.Add(address);
            }
        }

        return new Trusted(addresses, networks, AnyPeer: false, Hops: hops);
    }

    private static int Hops(IConfiguration configuration) =>
        int.TryParse(configuration[HopsConfigurationKey], out var hops) && hops is >= 1 and <= MaxHops
            ? hops
            : DefaultHops;

    /// <summary>The peers a deployment named, in the shape the forwarded-headers options want.</summary>
    /// <param name="Addresses">Single proxy addresses.</param>
    /// <param name="Networks">Proxy networks, written as CIDR.</param>
    /// <param name="AnyPeer">Whether every peer is trusted (<see cref="Any"/>).</param>
    /// <param name="Hops">
    /// How many entries of <c>X-Forwarded-For</c> to read, from the right. One is right for a
    /// proxy that appends a single address. Google's front end appends two — the client's
    /// address and its own forwarding rule's — so a service behind it reads two, and its own
    /// documentation says the entries before those two are not verified and must not be
    /// trusted. Reading fewer than the chain appends leaves the limit partitioned on a proxy;
    /// reading more starts believing the caller.
    /// </param>
    public sealed record Trusted(
        IReadOnlyList<IPAddress> Addresses,
        IReadOnlyList<IPNetwork> Networks,
        bool AnyPeer,
        int Hops)
    {
        /// <summary>True when no peer is trusted, so no forwarded header is read.</summary>
        public bool None => !AnyPeer && Addresses.Count == 0 && Networks.Count == 0;
    }
}
