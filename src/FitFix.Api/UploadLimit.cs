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
    /// The highest limit a deployment can ask for. An upload is held in memory in full, and the
    /// form reader's in-memory threshold is an <see cref="int"/>: past this a part would spill to
    /// a temporary file, which breaks AD-3 and crashes under a read-only filesystem. A deployment
    /// that asks for more gets this, rather than silently getting disk buffering.
    /// </summary>
    public const long MaxBytes = int.MaxValue;

    /// <summary>
    /// The configured limit, or <see cref="DefaultBytes"/> when the value is missing, not a
    /// number, or not positive, clamped to <see cref="MaxBytes"/>. A typo in a deployment
    /// variable must not open the gate altogether, and it must not close it either, so the
    /// default stands in silently.
    /// </summary>
    public static long From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration[ConfigurationKey];
        return long.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) && bytes > 0
            ? Math.Min(bytes, MaxBytes)
            : DefaultBytes;
    }
}
