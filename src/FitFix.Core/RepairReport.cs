namespace FitFix.Core;

/// <summary>
/// The stretch of the activity a fragment redrew (AD-27): when it begins and ends, in seconds from
/// the activity's start, and the metres the fragment contributed across it. The span the question of
/// Story 5.2 is about — "we redrew these 320 seconds and these 1.2 km" — and the one the interface
/// shows before the file is anybody's.
/// <para>
/// Carried by the report rather than re-derived from <see cref="Alignment.Reach"/> by each shell
/// (AD-16): the CLI prints it in a sentence and the result screen states it in a callout, and two
/// readings of the same alignment are two numbers free to disagree about one repair.
/// </para>
/// </summary>
/// <param name="FromSeconds">Where the redrawn stretch begins, s from the activity's start.</param>
/// <param name="ToSeconds">Where it ends, s from the activity's start.</param>
/// <param name="DistanceMeters">
/// The metres the fragment holds between its two attach points — its own whole length, because a
/// fragment is used whole or not at all (AD-17, AD-27). Not the metres the repair's total gained:
/// what the watch measured before and after the span is the athlete's own either way.
/// </param>
public readonly record struct RedrawnSpan(
    double FromSeconds,
    double ToSeconds,
    double DistanceMeters);

/// <summary>
/// The report of one repair — what a shell shows as the "before and after" preview (PRD §4.3).
/// Every number here is settled when the report is built, most of them copied from the summary
/// the core computed (AD-16): the report carries finished values and derives none, so the CLI, the
/// API DTO and the preview cannot disagree about the same run (AD-14).
/// </summary>
public sealed record RepairReport
{
    /// <summary>
    /// The distance the device itself recorded — the "before" the athlete saw in Strava — or
    /// <c>null</c> when no record carried a distance at all. Null rather than zero, like
    /// <see cref="ElevationGainMeters"/>: a file that measured nothing has not measured zero
    /// metres, and a shell describing the same file before the repair says the same thing (AD-15).
    /// </summary>
    public required double? OriginalDistanceMeters { get; init; }

    /// <summary>
    /// The distance of the repaired activity, m — a <em>sum</em>, and legitimately equal to
    /// neither <see cref="TrackLengthMeters"/> nor <see cref="UsedTrackLengthMeters"/> (AD-26).
    /// The measured stretches are measured along the watch's own positions, the estimated ones
    /// along the track, and the two are added: real GPS jitter inflates a measured stretch by a
    /// percent or two, which is the accepted price of keeping the positions the watch got right.
    /// A reader who finds this number beside the track's length should not take the difference
    /// for a defect.
    /// </summary>
    public required double RepairedDistanceMeters { get; init; }

    /// <summary>
    /// The whole length of the reference track as the user drew or exported it, m — whatever the
    /// repair then decided to use. Reported in both modes, because the athlete cannot choose
    /// between two lengths they are never shown (AD-17).
    /// </summary>
    public required double TrackLengthMeters { get; init; }

    /// <summary>
    /// The part of that track the repair actually used, m — the length the distance series ends
    /// on. Below <see cref="TrackLengthMeters"/> when the track outruns what the Original's own
    /// time makes plausible and nobody has confirmed the track, in which case
    /// <c>TRACK_LONGER_THAN_ACTIVITY</c> is among the <see cref="Warnings"/> (AD-17). Reported
    /// always, so a shell showing the result of a confirmed repair can still say which of the two
    /// lengths it ended on.
    /// </summary>
    public required double UsedTrackLengthMeters { get; init; }

    public required double ElapsedSeconds { get; init; }

    /// <summary>
    /// How much of the elapsed time the athlete stood still, from the Original's own pause
    /// intervals (AD-7). Without it a shell shows a pace over the whole clock beside a file that
    /// states a shorter timer time, and the two contradict each other in front of the user.
    /// </summary>
    public required double PausedSeconds { get; init; }

    /// <summary>
    /// The time the written file claims as its <c>total_timer_time</c>, taken from the summary the
    /// core settled (AD-16). Elapsed time minus the pauses in the ordinary case — but a pause list
    /// that swallows the whole activity is ignored there, and this is the number the file states
    /// rather than the subtraction a reader would make for itself (FR-6, AD-14).
    /// </summary>
    public required double MovingSeconds { get; init; }

    /// <summary>
    /// Which stretches of the activity the position was measured over and which were interpolated,
    /// in time order, tiling the elapsed time exactly once (FR-4, FR-8). The model a preview draws
    /// and the athlete reads: GPS drops out, returns and drops again, so a single "measured until"
    /// would call the kilometre whose signal came back estimated.
    /// </summary>
    public required IReadOnlyList<TrustZone> Zones { get; init; }

    /// <summary>
    /// The stretch a fragment of the route redrew, or <c>null</c> when the track covered the whole
    /// activity and nothing was redrawn from a piece of it (AD-27). Present in both coverage modes
    /// that use a fragment: confirming one changes who was asked, not what was done, so a shell
    /// showing a confirmed repair can still say which seconds came from the drawing.
    /// </summary>
    public required RedrawnSpan? RedrawnSpan { get; init; }

    /// <summary>
    /// The share of the activity whose position was measured by anchors — the measured
    /// <see cref="Zones"/> summed over the elapsed time, not a prefix of it. Settled when the
    /// report is built, like every other value here: a computed property is a second owner of the
    /// number and the DTO would then be free to divide differently (AD-14).
    /// </summary>
    public required double MeasuredFraction { get; init; }

    /// <summary>
    /// The pace before the repair, s/km: the distance the device recorded over the Original's own
    /// moving time, both of them the watch's own numbers, so the "before" stays what the user saw
    /// in Strava (AD-16). <c>null</c> when the device recorded no distance.
    /// </summary>
    public required double? OriginalPaceSecondsPerKm { get; init; }

    /// <summary>
    /// The pace after the repair, s/km, over the moving seconds the written file states — the same
    /// denominator as its average speed, so a shell cannot show a pace the file contradicts.
    /// <c>null</c> when there is no pace to state: nothing covered, or nothing moving.
    /// </summary>
    public required double? RepairedPaceSecondsPerKm { get; init; }

    public required int AnchorCount { get; init; }
    public double? GpsStallAtSeconds { get; init; }

    /// <summary>
    /// How many GPS positions of the Original were physically impossible and thrown away before
    /// anything read them (AD-25). The user is told how much was discarded, not only that
    /// something was.
    /// </summary>
    public required int DiscardedPositionCount { get; init; }

    /// <summary>
    /// How many laps the repaired activity carries (AD-24) — the Original's own, or one when it
    /// had none. Without it a shell cannot tell the athlete that their eight by four hundred came
    /// through, which is the whole point of reading the laps at all (FR-6).
    /// </summary>
    public required int LapCount { get; init; }

    /// <summary>
    /// The climb and the descent of the repaired activity, m — the sums of the rises and of the
    /// falls in the track's own altitude series (AD-16), which the file states as
    /// <c>total_ascent</c> and <c>total_descent</c>. Both are <c>null</c> for a track that carries
    /// no altitude: a flat zero there would read as a measurement of flat ground.
    /// </summary>
    public required double? ElevationGainMeters { get; init; }

    /// <inheritdoc cref="ElevationGainMeters"/>
    public required double? ElevationLossMeters { get; init; }

    /// <summary>
    /// How far the Original's first position lay from the nearest point of the reference track, m,
    /// when <c>TRACK_FAR_FROM_ORIGINAL</c> is among the <see cref="Warnings"/>; null otherwise. The
    /// one number a shell needs to say how wrong the track probably is (FR-3, AD-16).
    /// </summary>
    public required double? StartToTrackMeters { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// The repaired activity: the records, the report, the summary every number in it was settled in,
/// the alignment it was built from and the Original it was built out of.
/// </summary>
public sealed class RepairResult
{
    public required IReadOnlyList<RepairedRecord> Records { get; init; }
    public required RepairReport Report { get; init; }

    /// <summary>
    /// Every summary number the activity and its laps state, computed once by the core (AD-16).
    /// The FIT writer transcribes these values and the API DTO copies them; neither averages,
    /// sums or divides over a series of its own, so two entry points cannot disagree about the
    /// same run.
    /// </summary>
    public required ActivitySummary Summary { get; init; }

    public required Alignment Alignment { get; init; }

    /// <summary>
    /// The Original the repair actually worked from: every record and sensor value of the
    /// uploaded file, with the impossible positions already gone (AD-25). A shell's "before"
    /// map and its zones read this, never the raw series, so a discarded point is drawn nowhere.
    /// </summary>
    public required OriginalActivity CleanedOriginal { get; init; }

    public SportKind Sport { get; init; }
}
