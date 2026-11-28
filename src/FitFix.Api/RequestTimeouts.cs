using System.Globalization;

namespace FitFix.Api;

/// <summary>
/// How long a request may take before the service stops waiting for itself. Kestrel's own
/// defaults already cover a client that sends its body too slowly; what nothing bounded until
/// now is a handler that does not finish — and on a public URL an answer that never comes holds
/// a permit, a thread and a connection for as long as the client is willing to wait.
/// </summary>
/// <remarks>
/// Configuration with defaults, like <see cref="UploadLimit"/> and <see cref="RateLimits"/>.
/// The repair's own budget is about three seconds, so the number here is "something is wrong",
/// not a target.
/// </remarks>
public sealed record RequestTimeouts
{
    /// <summary>The policy name <c>/api/repair</c> runs under.</summary>
    public const string RepairPolicy = "fitfix-repair-timeout";

    /// <summary>What every request gets unless it names a policy of its own.</summary>
    public required TimeSpan Default { get; init; }

    /// <summary>What a repair gets: two files read, a series rewritten, a FIT written.</summary>
    public required TimeSpan Repair { get; init; }

    /// <summary>
    /// The timeouts a deployment configured, with the defaults standing in for anything missing
    /// or unusable.
    /// </summary>
    public static RequestTimeouts From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new RequestTimeouts
        {
            Default = Seconds(configuration, Keys.DefaultSeconds, Defaults.DefaultSeconds),
            Repair = Seconds(configuration, Keys.RepairSeconds, Defaults.RepairSeconds)
        };
    }

    private static TimeSpan Seconds(IConfiguration configuration, string key, int fallback) =>
        TimeSpan.FromSeconds(
            int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : fallback);

    /// <summary>The configuration keys, read from the environment like every other setting.</summary>
    public static class Keys
    {
        public const string DefaultSeconds = "FITFIX_TIMEOUT_SECONDS";
        public const string RepairSeconds = "FITFIX_TIMEOUT_REPAIR_SECONDS";
    }

    /// <summary>What a deployment that configures nothing gets.</summary>
    public static class Defaults
    {
        /// <summary>Everything that is not a repair answers in milliseconds or not at all.</summary>
        public const int DefaultSeconds = 30;

        /// <summary>Four times the longest repair anyone has measured, and then some.</summary>
        public const int RepairSeconds = 120;
    }
}
