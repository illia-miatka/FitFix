using System.Globalization;

namespace FitFix.Api;

/// <summary>
/// The only input bound the shell enforces: how many bytes a request body may carry
/// (AD-13). It is configuration rather than a constant, so a deployment can lower it
/// without a rebuild, and content type and file extension decide nothing — the core
/// recognises the format from the bytes.
/// </summary>
public static class UploadLimit
{
    /// <summary>The configuration key, read from the environment like every other setting.</summary>
    public const string ConfigurationKey = "FITFIX_MAX_UPLOAD_BYTES";

    /// <summary>20 MB: comfortably above a long activity and far below anything worth buffering.</summary>
    public const long DefaultBytes = 20L * 1024 * 1024;

    /// <summary>
    /// The configured limit, or <see cref="DefaultBytes"/> when the value is missing, not a
    /// number, or not positive. A typo in a deployment variable must not open the gate
    /// altogether, and it must not close it either, so the default stands in silently.
    /// </summary>
    public static long From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration[ConfigurationKey];
        return long.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) && bytes > 0
            ? bytes
            : DefaultBytes;
    }
}
