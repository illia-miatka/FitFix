namespace FitFix.Api.Contracts;

/// <summary>
/// The two lengths a repair may end on, as the request names one (AD-17). Tokens of the protocol
/// rather than text for a person, so they are matched exactly: a mode nobody understood must be
/// refused, because guessing either way decides for the athlete something only they can decide.
/// An absent mode is <see cref="Plausible"/> — the conservative length, which is the only default
/// a request can inherit without having asked anybody.
/// </summary>
public static class TrackLengthModes
{
    /// <summary>End on the part of the track the Original's own time makes plausible.</summary>
    public const string Plausible = "plausible";

    /// <summary>
    /// End on the track's whole length, because the user has confirmed the track is right. The
    /// client sends this after it has shown the <c>TRACK_LONGER_THAN_ACTIVITY</c> warning and the
    /// user has answered it (AD-22).
    /// </summary>
    public const string Full = "full";
}

/// <summary>
/// What the request says it knows about how much of the route the track covers (AD-27). Tokens of
/// the protocol, matched exactly for the same reason the length modes are: a mode nobody understood
/// is refused, because both of the ways to guess it decide something only the athlete can. An absent
/// mode is <see cref="Detect"/> — the service answers the question and asks before acting on its
/// answer, which is the only coverage a request that has asked nobody anything may inherit.
/// </summary>
public static class TrackCoverageModes
{
    /// <summary>
    /// Let the service decide. A track it recognises as a stretch of the route is repaired as one
    /// and the answer carries <c>TRACK_IS_A_FRAGMENT</c> as a blocking warning.
    /// </summary>
    public const string Detect = "detect";

    /// <summary>
    /// The user has confirmed the stretch, so the same repair comes back without that warning. The
    /// client sends this after it has shown the warning and the span beside it (AD-22).
    /// </summary>
    public const string Fragment = "fragment";

    /// <summary>
    /// The track is the whole route and must never be read as a piece of one — the answer a lap
    /// course gets when the athlete knows better than the detector.
    /// </summary>
    public const string Whole = "whole";
}

/// <summary>
/// The answer to <c>POST /api/repair</c>: everything the result screen and the later preview need
/// about one repair — the summaries before and after, the two track lengths, the trust zones, the
/// per-record series, the warnings with their severities and the repaired file itself. Nothing is
/// stored, so one request yields both the numbers and the file.
/// <para>
/// Hand-written, like every other body (AD-14), and holding base quantities only: <strong>no
/// pace, no measured fraction, no difference between a before and an after</strong>. The core
/// computes all three for the CLI and the DTO still may not carry them — a UI that divides the
/// distance it was given by the moving seconds it was given can never disagree with the file,
/// while a second number travelling beside them can. Units are SI throughout and time is UTC
/// ISO-8601 (AD-6); enums travel as strings.
/// </para>
/// </summary>
/// <param name="Before">The activity as the device recorded it — the "before" the athlete saw.</param>
/// <param name="After">The repaired activity, as the written file states it.</param>
/// <param name="Track">Both lengths of the reference track (AD-17).</param>
/// <param name="Zones">
/// Which stretches of the activity the position was measured over and which were interpolated, in
/// time order, tiling the elapsed time exactly once (FR-4, FR-8).
/// </param>
/// <param name="Redrawn">
/// The stretch a fragment of the route redrew, or null when the track covered the whole activity
/// (AD-27).
/// </param>
/// <param name="Series">Every per-record series, indexed by the Original's records (AD-5).</param>
/// <param name="Warnings">
/// Every warning of the report, each with the severity that decides the download gate (AD-22).
/// </param>
/// <param name="FitBase64">
/// The repaired FIT file, base64-encoded. Inside the JSON beside the report, because nothing is
/// kept server-side to fetch later (AD-3): one request, both the numbers and the file.
/// </param>
public sealed record RepairResponse(
    OriginalSummaryResponse Before,
    RepairedSummaryResponse After,
    TrackLengthsResponse Track,
    IReadOnlyList<TrustZoneResponse> Zones,
    RedrawnSpanResponse? Redrawn,
    RepairSeriesResponse Series,
    IReadOnlyList<RepairWarningResponse> Warnings,
    string FitBase64);

/// <summary>
/// What the device itself recorded. The durations are not repeated here: the repair keeps the
/// Original's own clock, so <see cref="RepairedSummaryResponse.ElapsedSeconds"/> and
/// <see cref="RepairedSummaryResponse.MovingSeconds"/> are the Original's too — which is also the
/// denominator the core uses for the pace before the repair, so a UI that divides
/// <see cref="RecordedDistanceMeters"/> by those seconds states the very number the CLI prints.
/// </summary>
/// <param name="Sport">
/// The sport the file's <c>session</c> message named, as a string (AD-14). It decides the repair's
/// speed thresholds, so it says why the file was judged the way it was.
/// </param>
/// <param name="StartedAt">
/// The timestamp of the first record, in UTC — the instant the seconds of the zones and the series
/// are counted from.
/// </param>
/// <param name="RecordCount">
/// How many records the activity holds, which is the length of every series in
/// <see cref="RepairSeriesResponse"/> (AD-5).
/// </param>
/// <param name="RecordedDistanceMeters">
/// The distance the device recorded, or null when no record carried one. Null rather than zero: a
/// file that measured nothing has not measured zero metres, and the inspect endpoint says the same
/// thing about the same file (AD-15).
/// </param>
/// <param name="DiscardedPositionCount">
/// How many positions were physically impossible and were thrown away before anything read them
/// (AD-25). The count, not only the <c>POSITIONS_DISCARDED</c> warning: the user is told how much
/// was discarded, not merely that something was.
/// </param>
public sealed record OriginalSummaryResponse(
    string Sport,
    DateTime StartedAt,
    int RecordCount,
    double? RecordedDistanceMeters,
    int DiscardedPositionCount);

/// <summary>
/// The repaired activity, every value copied from the summary the core settled once (AD-16) — the
/// same values the <c>session</c> message of <see cref="RepairResponse.FitBase64"/> states, so the
/// screen and the file cannot disagree about one run. A field is null when the activity holds
/// nothing to state it from: no heart rate was recorded, no altitude came with the track, nothing
/// moved. Absent and zero are different claims.
/// </summary>
/// <param name="DistanceMeters">Metres the repaired series covers.</param>
/// <param name="ElapsedSeconds">The Original's own span, first record to last, pauses included.</param>
/// <param name="MovingSeconds">
/// That span less the pauses — the <c>total_timer_time</c> the file states (FR-5, FR-6), and the
/// denominator of <see cref="AverageSpeedMetersPerSecond"/>.
/// </param>
/// <param name="PausedSeconds">How much of the elapsed time the athlete stood still (AD-7).</param>
/// <param name="AverageSpeedMetersPerSecond">Null when nothing moved.</param>
/// <param name="MaximumSpeedMetersPerSecond">
/// The fastest plausible moving second, with the records inside a pause and those above the sport's
/// own cap skipped (AD-8).
/// </param>
/// <param name="AverageHeartRate">Over the moving records; a reading of zero is a dropout, not a measurement.</param>
/// <param name="MaximumHeartRate"><inheritdoc cref="AverageHeartRate"/></param>
/// <param name="AveragePower">
/// Over the moving records, zero included — a cyclist freewheels at zero watts — but the zero the
/// device derived from a dead GPS is already gone from the series (FR-6).
/// </param>
/// <param name="MaximumPower"><inheritdoc cref="AveragePower"/></param>
/// <param name="AverageCadence">Over the moving records; zero is standing still, not a measurement.</param>
/// <param name="MaximumCadence"><inheritdoc cref="AverageCadence"/></param>
/// <param name="ElevationGainMeters">
/// The climb and the descent of the repaired altitude series, neither subtracted from the other.
/// Both null for a track that carries no altitude: a flat zero would read as measured flat ground.
/// </param>
/// <param name="ElevationLossMeters"><inheritdoc cref="ElevationGainMeters"/></param>
/// <param name="LapCount">
/// How many laps the repaired file carries — the Original's own, or one when it had none (AD-24).
/// Without it a screen cannot tell the athlete that their eight by four hundred came through,
/// which is the whole point of reading the laps (FR-6).
/// </param>
public sealed record RepairedSummaryResponse(
    double DistanceMeters,
    double ElapsedSeconds,
    double MovingSeconds,
    double PausedSeconds,
    double? AverageSpeedMetersPerSecond,
    double? MaximumSpeedMetersPerSecond,
    int? AverageHeartRate,
    int? MaximumHeartRate,
    int? AveragePower,
    int? MaximumPower,
    int? AverageCadence,
    int? MaximumCadence,
    double? ElevationGainMeters,
    double? ElevationLossMeters,
    int LapCount);

/// <summary>
/// The two lengths of AD-17, reported in both modes: the athlete cannot choose between two numbers
/// they are never shown. They are equal whenever the track was taken whole — the client compares
/// them itself rather than reading a flag or a difference the server computed.
/// </summary>
/// <param name="LengthMeters">The whole reference track, after de-duplication.</param>
/// <param name="UsedLengthMeters">
/// The part of it the repair ended on, which is what <see cref="RepairedSummaryResponse.DistanceMeters"/>
/// reaches.
/// </param>
public sealed record TrackLengthsResponse(double LengthMeters, double UsedLengthMeters);

/// <summary>
/// One stretch of the activity and what the repair knew about the position across it. In seconds
/// from <see cref="OriginalSummaryResponse.StartedAt"/>, the same measure the series use, so a
/// chart can shade the estimated stretches without converting anything.
/// </summary>
/// <param name="Kind"><c>measured</c> or <c>estimated</c> (AD-14).</param>
/// <param name="FromSeconds">When the stretch begins.</param>
/// <param name="ToSeconds">
/// When it ends. A record landing on it belongs to this zone rather than to the next, so two kinds
/// share a boundary without either claiming the second twice.
/// </param>
public sealed record TrustZoneResponse(string Kind, double FromSeconds, double ToSeconds);

/// <summary>
/// The stretch a fragment of the route redrew (AD-27), in the same seconds the zones and the series
/// are stated in. Base quantities and no sentence (AD-14): the interface writes "we redrew these
/// five minutes and this 1.2 km" out of these three numbers, because a server that wrote the
/// sentence would be writing Ukrainian outside the dictionary (AD-18).
/// </summary>
/// <param name="FromSeconds">
/// Where the redrawn stretch begins, counted from <see cref="OriginalSummaryResponse.StartedAt"/>.
/// </param>
/// <param name="ToSeconds">Where it ends, in the same measure.</param>
/// <param name="DistanceMeters">
/// The metres the fragment holds across the span — its own whole length, because a fragment is used
/// whole or not at all. The boundaries match the estimated zone of <see cref="RepairResponse.Zones"/>
/// exactly, so a client may shade one from the other.
/// </param>
public sealed record RedrawnSpanResponse(double FromSeconds, double ToSeconds, double DistanceMeters);

/// <summary>
/// One warning of the report and what it does to the download (AD-22).
/// </summary>
/// <param name="Code">A code declared in <see cref="FitFix.Core.Diagnostics.DiagnosticCodes"/>, which the UI dictionary keys its text on (AD-18).</param>
/// <param name="Severity"><c>blocking</c> or <c>info</c>: whether the file waits for an explicit confirmation.</param>
public sealed record RepairWarningResponse(string Code, string Severity);

/// <summary>
/// Every per-record series of the repair. <strong>All of them are exactly as long as
/// <see cref="OriginalSummaryResponse.RecordCount"/></strong>, index <c>i</c> always means the
/// <c>i</c>-th record of the Original, and a value the activity does not have is <c>null</c>
/// rather than a missing element (AD-5). Nothing is thinned.
/// <para>
/// Wasteful on purpose. Equal lengths with nulls in the gaps is what lets a client line a chart up
/// with a map without working out which record each entry belongs to; a server that thinned them
/// would decide for the client what resolution it may have, and one that thinned one series and
/// not another would put a hover on the wrong point (AD-23). Thinning happens in the client, for
/// rendering, and a thinned point keeps its original index.
/// </para>
/// </summary>
/// <param name="SecondsFromStart">
/// Each record's own second, from <see cref="OriginalSummaryResponse.StartedAt"/> — the axis the
/// zones are stated in. A watch under smart recording leaves gaps here, which is why the series
/// carry it rather than letting a client assume one record a second.
/// </param>
/// <param name="Original">The series the device recorded, with its impossible positions gone (AD-25).</param>
/// <param name="Repaired">The series the written file carries.</param>
public sealed record RepairSeriesResponse(
    IReadOnlyList<double> SecondsFromStart,
    OriginalSeriesResponse Original,
    RepairedSeriesResponse Repaired);

/// <summary>
/// What the device recorded, as the repair read it: the cleaned series, never the raw one, so a
/// position the repair discarded is drawn nowhere (AD-25). This is the dashed track and the second
/// pace line of the preview (FR-8, FR-9), which is why it carries geometry and distance and no
/// sensors: those are the same readings the repaired series states.
/// </summary>
/// <param name="Latitude">Degrees, null for a record the watch gave no fix for or whose fix was discarded.</param>
/// <param name="Longitude"><inheritdoc cref="Latitude"/></param>
/// <param name="DistanceMeters">The device's own odometer, null where no record carried one.</param>
/// <param name="SpeedMetersPerSecond">The device's own speed, null where no record carried one.</param>
public sealed record OriginalSeriesResponse(
    IReadOnlyList<double?> Latitude,
    IReadOnlyList<double?> Longitude,
    IReadOnlyList<double?> DistanceMeters,
    IReadOnlyList<double?> SpeedMetersPerSecond);

/// <summary>
/// The repaired activity, record by record — exactly what <see cref="RepairResponse.FitBase64"/>
/// holds, so a preview drawn from these series is a preview of the file that will be uploaded.
/// </summary>
/// <param name="Latitude">
/// Degrees, from the reference track. Never null: every record of the repaired file carries a
/// position, which is what the repair exists to produce.
/// </param>
/// <param name="Longitude"><inheritdoc cref="Latitude"/></param>
/// <param name="AltitudeMeters">
/// The track's own altitude, offset to meet the Original's at the start; the Original's own where
/// the track carries none, and null where neither had one.
/// </param>
/// <param name="DistanceMeters">Monotonically non-decreasing, ending on <see cref="TrackLengthsResponse.UsedLengthMeters"/>.</param>
/// <param name="SpeedMetersPerSecond">Zero inside a pause, whatever the window around it spans (FR-5).</param>
/// <param name="HeartRate">The Original's own readings, carried through untouched.</param>
/// <param name="Power">
/// The Original's own, except where the device derived a zero from its dead GPS — those are null,
/// as they are in the written file (FR-6).
/// </param>
/// <param name="Cadence"><inheritdoc cref="HeartRate"/></param>
/// <param name="TemperatureCelsius"><inheritdoc cref="HeartRate"/></param>
public sealed record RepairedSeriesResponse(
    IReadOnlyList<double> Latitude,
    IReadOnlyList<double> Longitude,
    IReadOnlyList<double?> AltitudeMeters,
    IReadOnlyList<double> DistanceMeters,
    IReadOnlyList<double> SpeedMetersPerSecond,
    IReadOnlyList<int?> HeartRate,
    IReadOnlyList<int?> Power,
    IReadOnlyList<int?> Cadence,
    IReadOnlyList<int?> TemperatureCelsius);
