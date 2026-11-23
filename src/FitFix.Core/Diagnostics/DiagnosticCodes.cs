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
    /// Positions of the original were physically impossible and were discarded before anything
    /// read them (AD-25). The count is in the report; this code is what tells a shell to say so,
    /// and what distinguishes an original cleaned down to nothing from one that never had GPS.
    /// </summary>
    public const string PositionsDiscarded = "POSITIONS_DISCARDED";

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

    /// <summary>
    /// The activity's laps outnumber what a FIT file can index. <c>message_index</c> reserves its
    /// high bits, leaving twelve for the number itself, so a file with more laps than that would
    /// silently reuse indices — a refusal with a code beats a file nobody can read back (AD-18).
    /// </summary>
    public const string ActivityTooManyLaps = "ACTIVITY_TOO_MANY_LAPS";

    /// <summary>
    /// The repaired series handed to the summary, or to the writer beside a summary, is not the
    /// one the activity was measured from: a different number of records, or a boundary index
    /// past its end. Every summary number is read at a record index (AD-16), so one mismatched
    /// pair would quote one lap's heart rate against another's records.
    /// </summary>
    public const string SummarySeriesMismatch = "SUMMARY_SERIES_MISMATCH";

    /// <summary>The series to summarise holds no record at all, so there is nothing to measure.</summary>
    public const string SummaryWithoutRecords = "SUMMARY_WITHOUT_RECORDS";

    /// <summary>
    /// The summary carries no lap, so the file would hold no <c>lap</c> message and claim
    /// <c>num_laps = 0</c> — a shape Strava may refuse (AD-19). An activity nobody lapped is one
    /// lap covering everything, never none.
    /// </summary>
    public const string SummaryWithoutLaps = "SUMMARY_WITHOUT_LAPS";

    /// <summary>
    /// The interpolated distance series ends further short of the track than the activity's last
    /// interval could plausibly cover, so pinning the finish to the track length (AD-8) would
    /// write a teleport across the line. A fault in the data the repair was given, which is why
    /// it carries a code rather than a bare exception (AD-18).
    /// </summary>
    public const string DistanceSeriesImplausible = "DISTANCE_SERIES_IMPLAUSIBLE";

    // --- Errors raised by the shell, declared here so AD-22 reads its codes from one source.
    //
    // These describe the transport, not the user's data: the request never reached a point
    // where an activity or a track was read, so nothing about the files is being claimed. Their
    // severity is not a download gate either — there is no repaired file to withhold — and
    // `Blocking` is only the registry's zero value doing no harm. Story 2.7 still owes each one
    // a Ukrainian text, because the interface shows whatever code it is handed (AD-18).

    /// <summary>The upload exceeded the shell's size limit (HTTP 413).</summary>
    public const string FileTooLarge = "FILE_TOO_LARGE";

    /// <summary>An unexpected failure the shell could not attribute to the input (HTTP 500).</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>
    /// The request reached no endpoint (HTTP 404). Declared here because every error body
    /// carries a code from this registry (AD-22): a bare 404 with an empty body would leave
    /// the interface nothing to look up.
    /// </summary>
    public const string RouteNotFound = "ROUTE_NOT_FOUND";

    /// <summary>
    /// The request could not be parsed, or stopped before the body it announced had arrived
    /// (HTTP 400) — an aborted upload, most often. A failure of the request, not of the
    /// service, so it must not be reported as one.
    /// </summary>
    public const string RequestMalformed = "REQUEST_MALFORMED";

    /// <summary>The path exists but not for this HTTP method (HTTP 405).</summary>
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";

    /// <summary>The endpoint cannot read a body in the media type offered (HTTP 415).</summary>
    public const string MediaTypeNotSupported = "MEDIA_TYPE_NOT_SUPPORTED";

    /// <summary>
    /// The client went away before the answer was written. Nothing can be sent any more, so
    /// this code exists for the log line rather than for a response body.
    /// </summary>
    public const string ClientDisconnected = "CLIENT_DISCONNECTED";

    /// <summary>
    /// An inspect request named no role the service knows (HTTP 400): no <c>role</c> at all, an
    /// empty one, or one naming neither <c>original</c> nor <c>track</c>. A FIT file is valid as
    /// an original and as a track, so the role is required and never guessed (AD-15) — all three
    /// mistakes are the same mistake to the person who made it, and one code is what the UI
    /// dictionary needs to say so.
    /// </summary>
    public const string InspectRoleInvalid = "INSPECT_ROLE_INVALID";

    /// <summary>
    /// The request carried no file at all (HTTP 400). Its own code rather than
    /// <see cref="RequestMalformed"/>: the registry is the interface's whole vocabulary, so a
    /// dropzone can only say "choose a file" if a code says that is what happened.
    /// </summary>
    public const string FileMissing = "FILE_MISSING";

    /// <summary>
    /// The request carried more than one file (HTTP 400). Inspect describes one file in one role,
    /// so two of them are refused rather than answered about whichever arrived first — and the
    /// user is told which of their drops was the problem.
    /// </summary>
    public const string TooManyFiles = "TOO_MANY_FILES";

    /// <summary>
    /// A repair request named a track-length mode the service does not know (HTTP 400): neither
    /// <c>plausible</c> nor <c>full</c>. Its own code because guessing is the one thing AD-17
    /// forbids here — a misspelt mode read as "trust the track" would hand the athlete a distance
    /// they never confirmed, and one read as the conservative mode would silently ignore the
    /// confirmation they did give. Refusing says which of the two happened.
    /// </summary>
    public const string TrackLengthModeInvalid = "TRACK_LENGTH_MODE_INVALID";

    private static readonly Dictionary<string, DiagnosticSeverity> Severities = new(StringComparer.Ordinal)
    {
        // Warnings. A stalled GPS is the normal case this service exists for, and few anchors
        // is a milder version of no anchors at all — marking either blocking would treat a
        // better input more harshly than a worse one.
        [NoOriginalGps] = DiagnosticSeverity.Info,
        [GpsStallDetected] = DiagnosticSeverity.Info,
        [FewAnchors] = DiagnosticSeverity.Info,
        // Throwing away an impossible point improves the result rather than casting doubt on it,
        // so it tells the user what happened and leaves the download alone.
        [PositionsDiscarded] = DiagnosticSeverity.Info,
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
        [ActivityTooManyLaps] = DiagnosticSeverity.Blocking,
        [SummarySeriesMismatch] = DiagnosticSeverity.Blocking,
        [SummaryWithoutRecords] = DiagnosticSeverity.Blocking,
        [SummaryWithoutLaps] = DiagnosticSeverity.Blocking,
        [DistanceSeriesImplausible] = DiagnosticSeverity.Blocking,
        // Transport codes. Blocking says nothing here: no repaired file exists to gate.
        [FileTooLarge] = DiagnosticSeverity.Blocking,
        [InternalError] = DiagnosticSeverity.Blocking,
        [RouteNotFound] = DiagnosticSeverity.Blocking,
        [RequestMalformed] = DiagnosticSeverity.Blocking,
        [MethodNotAllowed] = DiagnosticSeverity.Blocking,
        [MediaTypeNotSupported] = DiagnosticSeverity.Blocking,
        [ClientDisconnected] = DiagnosticSeverity.Blocking,
        [InspectRoleInvalid] = DiagnosticSeverity.Blocking,
        [FileMissing] = DiagnosticSeverity.Blocking,
        [TooManyFiles] = DiagnosticSeverity.Blocking,
        [TrackLengthModeInvalid] = DiagnosticSeverity.Blocking
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
