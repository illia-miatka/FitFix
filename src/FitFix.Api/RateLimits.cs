using System.Globalization;

namespace FitFix.Api;

/// <summary>
/// How many requests one client may make, and how many repairs the service runs at once.
/// Configuration rather than constants, like <see cref="UploadLimit"/>: a public deployment
/// tightens or loosens these without a rebuild, and no number lives in a signature (AD-10's
/// rule, applied to the shell).
/// </summary>
/// <remarks>
/// The numbers these default to are for a beta on one small machine. <c>/api/inspect</c> is
/// cheap and the page calls it on every drop, so its window is generous; <c>/api/repair</c>
/// reads two files, rewrites a series and writes a FIT, synchronously inside the request
/// (AD-11), so its window is narrow and a concurrency bound sits across both — a 1 vCPU
/// machine running four repairs at once finishes none of them quickly.
/// </remarks>
public sealed record RateLimits
{
    /// <summary>The policy name <c>/api/inspect</c> is limited under.</summary>
    public const string InspectPolicy = "fitfix-inspect";

    /// <summary>The policy name <c>/api/repair</c> is limited under.</summary>
    public const string RepairPolicy = "fitfix-repair";

    /// <summary>Requests to <c>/api/inspect</c> one client may make per window.</summary>
    public required int InspectPermits { get; init; }

    /// <summary>The window those permits are counted in.</summary>
    public required TimeSpan InspectWindow { get; init; }

    /// <summary>Requests to <c>/api/repair</c> one client may make per window.</summary>
    public required int RepairPermits { get; init; }

    /// <summary>The window those permits are counted in.</summary>
    public required TimeSpan RepairWindow { get; init; }

    /// <summary>How many <c>/api</c> requests are served at once, across every client.</summary>
    public required int Concurrency { get; init; }

    /// <summary>How many wait for a permit before the next one is refused outright.</summary>
    public required int Queue { get; init; }

    /// <summary>
    /// The limits a deployment configured, with the defaults standing in for anything missing
    /// or unusable. A typo must not open the gate and must not close it either, which is the
    /// rule <see cref="UploadLimit"/> already follows.
    /// </summary>
    public static RateLimits From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new RateLimits
        {
            InspectPermits = Count(configuration, Keys.InspectPermits, Defaults.InspectPermits),
            InspectWindow = Window(configuration, Keys.InspectWindowSeconds, Defaults.InspectWindowSeconds),
            RepairPermits = Count(configuration, Keys.RepairPermits, Defaults.RepairPermits),
            RepairWindow = Window(configuration, Keys.RepairWindowSeconds, Defaults.RepairWindowSeconds),
            Concurrency = Count(configuration, Keys.Concurrency, Defaults.Concurrency),
            // The only one that may be zero: a deployment can refuse to queue at all.
            Queue = Count(configuration, Keys.Queue, Defaults.Queue, allowZero: true)
        };
    }

    private static int Count(IConfiguration configuration, string key, int fallback, bool allowZero = false) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        && (value > 0 || (allowZero && value == 0))
            ? value
            : fallback;

    private static TimeSpan Window(IConfiguration configuration, string key, int fallbackSeconds) =>
        TimeSpan.FromSeconds(Count(configuration, key, fallbackSeconds));

    /// <summary>The configuration keys, read from the environment like every other setting.</summary>
    public static class Keys
    {
        public const string InspectPermits = "FITFIX_RATE_INSPECT_PERMITS";
        public const string InspectWindowSeconds = "FITFIX_RATE_INSPECT_WINDOW_SECONDS";
        public const string RepairPermits = "FITFIX_RATE_REPAIR_PERMITS";
        public const string RepairWindowSeconds = "FITFIX_RATE_REPAIR_WINDOW_SECONDS";
        public const string Concurrency = "FITFIX_RATE_CONCURRENCY";
        public const string Queue = "FITFIX_RATE_QUEUE";
    }

    /// <summary>What a deployment that configures nothing gets.</summary>
    public static class Defaults
    {
        /// <summary>A drop inspects one file, and the page has two zones to fill.</summary>
        public const int InspectPermits = 60;

        public const int InspectWindowSeconds = 60;

        /// <summary>Enough to repair the same run several times over while reading the result.</summary>
        public const int RepairPermits = 20;

        public const int RepairWindowSeconds = 300;

        /// <summary>Two at once on one shared vCPU; the third waits rather than slows the first two.</summary>
        public const int Concurrency = 2;

        public const int Queue = 4;
    }
}
