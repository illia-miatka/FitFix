namespace FitFix.Core.Diagnostics;

/// <summary>
/// What a code means for the download gate (AD-22). It answers one question only:
/// may the user download the result without confirming first?
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// The shell withholds the repaired file until the user confirms explicitly. This is the
    /// zero value on purpose: a severity nobody set must close the gate, not open it.
    /// </summary>
    Blocking,

    /// <summary>Shown to the user and nothing else; the repaired file stays available.</summary>
    Info
}

/// <summary>
/// The single place where every warning and error code is declared together with its
/// severity (AD-18). Domain exceptions carry a code from here, the warning list of a
/// <see cref="Alignment"/> holds codes from here, and the shell fills
/// <c>ProblemDetails.code</c> from here (AD-22). Human-readable text lives only in the
/// UI dictionary (<c>i18n/uk.ts</c>) and in the CLI report — never in this assembly.
/// </summary>
public static class DiagnosticCodes
{
    // --- Warnings: the repair succeeded, but the result needs a caveat (PRD §4.1, §4.3).

    /// <summary>The original carries no GPS position at all, so nothing anchors the track.</summary>
    public const string NoOriginalGps = "NO_ORIGINAL_GPS";

    /// <summary>The track starts too far from the original's first position to be the same route (FR-3).</summary>
    public const string TrackFarFromOriginal = "TRACK_FAR_FROM_ORIGINAL";

    /// <summary>The original's GPS stopped moving while the activity continued — the case this service exists for.</summary>
    public const string GpsStallDetected = "GPS_STALL_DETECTED";

    /// <summary>Too few anchors survived, so most of the distance is estimated rather than measured.</summary>
    public const string FewAnchors = "FEW_ANCHORS";

    /// <summary>
    /// The track is longer than the elapsed time can plausibly cover (AD-17). Confirming it
    /// changes the result: the client repeats the request with <c>trackLengthMode=full</c>.
    /// </summary>
    public const string TrackLongerThanActivity = "TRACK_LONGER_THAN_ACTIVITY";

    // --- Errors: the core refuses the input (AD-18).

    /// <summary>The bytes offered as a FIT file are not a FIT file.</summary>
    public const string NotAFitFile = "NOT_A_FIT_FILE";

    /// <summary>The FIT file failed the SDK's integrity check.</summary>
    public const string FitIntegrityFailed = "FIT_INTEGRITY_FAILED";

    /// <summary>The FIT file is well-formed but carries no record messages, so it is not an activity.</summary>
    public const string FitHasNoRecords = "FIT_HAS_NO_RECORDS";

    /// <summary>The track file holds fewer than two positioned points, so it has no geometry.</summary>
    public const string TrackTooFewPoints = "TRACK_TOO_FEW_POINTS";

    /// <summary>
    /// The track file had points, but they all collapsed into one place once points closer
    /// than the de-duplication spacing were dropped — a different problem from
    /// <see cref="TrackTooFewPoints"/>, and a different fix for the user.
    /// </summary>
    public const string TrackWithoutDistinctPoints = "TRACK_WITHOUT_DISTINCT_POINTS";

    /// <summary>The track file's format could not be recognised; FIT, GPX and TCX are supported.</summary>
    public const string TrackFormatUnknown = "TRACK_FORMAT_UNKNOWN";

    /// <summary>The activity to write holds fewer than two records, so it has no duration.</summary>
    public const string ActivityTooFewRecords = "ACTIVITY_TOO_FEW_RECORDS";

    /// <summary>
    /// Every record of the activity carries the same timestamp, so the activity has no elapsed
    /// time to spread the track over. A bad file, not a bad argument (AD-18).
    /// </summary>
    public const string ActivityWithoutElapsedTime = "ACTIVITY_WITHOUT_ELAPSED_TIME";

    // --- Errors raised by the shell, declared here so AD-22 reads its codes from one source.

    /// <summary>The upload exceeded the shell's size limit (HTTP 413).</summary>
    public const string FileTooLarge = "FILE_TOO_LARGE";

    /// <summary>An unexpected failure the shell could not attribute to the input (HTTP 500).</summary>
    public const string InternalError = "INTERNAL_ERROR";

    private static readonly Dictionary<string, DiagnosticSeverity> Severities = new(StringComparer.Ordinal)
    {
        // Warnings. A stalled GPS is the normal case this service exists for, and few anchors
        // is a milder version of no anchors at all — marking either blocking would treat a
        // better input more harshly than a worse one.
        [NoOriginalGps] = DiagnosticSeverity.Info,
        [GpsStallDetected] = DiagnosticSeverity.Info,
        [FewAnchors] = DiagnosticSeverity.Info,
        [TrackFarFromOriginal] = DiagnosticSeverity.Blocking,
        [TrackLongerThanActivity] = DiagnosticSeverity.Blocking,

        // Errors always withhold the result: there is no file to download.
        [NotAFitFile] = DiagnosticSeverity.Blocking,
        [FitIntegrityFailed] = DiagnosticSeverity.Blocking,
        [FitHasNoRecords] = DiagnosticSeverity.Blocking,
        [TrackTooFewPoints] = DiagnosticSeverity.Blocking,
        [TrackWithoutDistinctPoints] = DiagnosticSeverity.Blocking,
        [TrackFormatUnknown] = DiagnosticSeverity.Blocking,
        [ActivityTooFewRecords] = DiagnosticSeverity.Blocking,
        [ActivityWithoutElapsedTime] = DiagnosticSeverity.Blocking,
        [FileTooLarge] = DiagnosticSeverity.Blocking,
        [InternalError] = DiagnosticSeverity.Blocking
    };

    /// <summary>Every declared code. The UI dictionary's key set must equal this set (AD-18).</summary>
    public static IReadOnlyCollection<string> All { get; } = [.. Severities.Keys];

    /// <summary>True when <paramref name="code"/> is declared here; false for null.</summary>
    public static bool IsDeclared(string? code) => code is not null && Severities.ContainsKey(code);

    /// <summary>
    /// The severity of a declared code. An undeclared code is a programming error, not a
    /// user-facing refusal, so this throws rather than guessing a severity.
    /// </summary>
    public static DiagnosticSeverity SeverityOf(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Severities.TryGetValue(code, out var severity)
            ? severity
            : throw new KeyNotFoundException($"Code '{code}' is not declared in DiagnosticCodes.");
    }

    /// <summary>True when the code withholds the download until the user confirms (AD-22).</summary>
    public static bool IsBlocking(string code) => SeverityOf(code) == DiagnosticSeverity.Blocking;
}
