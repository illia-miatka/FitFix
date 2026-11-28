namespace FitFix.Core;

/// <summary>Якір: час від початку Оригіналу ↔ дистанція вздовж Еталонного треку.</summary>
public readonly record struct Anchor(double TimeSeconds, double TrackDistance, double DeviationMeters);

/// <summary>What the repair knew about the position over one stretch of the activity (FR-4).</summary>
public enum ZoneKind
{
    /// <summary>
    /// The Original's own GPS established the position: anchors close enough together in time that
    /// the stretch between them is the track the athlete was measured on — the Надійна зона.
    /// </summary>
    Measured,

    /// <summary>
    /// The position was interpolated across a gap no anchor spoke for — the Розрахована зона. The
    /// distance there is the repair's estimate, not a measurement.
    /// </summary>
    Estimated
}

/// <summary>
/// One stretch of the activity, in seconds from its start, and what the repair knew about the
/// position across it. Half-open in effect: a record on <see cref="ToSeconds"/> belongs to this
/// zone rather than to the next, so the two kinds can share a boundary without either claiming the
/// second twice.
/// </summary>
/// <param name="Kind">Measured or estimated.</param>
/// <param name="FromSeconds">When the stretch begins, s from the activity's start.</param>
/// <param name="ToSeconds">When it ends, s from the activity's start.</param>
public readonly record struct TrustZone(ZoneKind Kind, double FromSeconds, double ToSeconds);

/// <summary>
/// Which stretch of the activity the Еталонний трек reached, and whether its two ends are the
/// activity's own (AD-27). The track is a <em>stretch</em> of the route and need not cover the whole
/// of it, so these are the two assumptions the aligner used to make and now has to establish: that
/// the activity starts where the track starts, and that it finishes where the track finishes.
/// <para>
/// Read by the repair rather than re-derived from <see cref="Alignment.Anchors"/>: outside this span
/// the track holds nothing at all, which is what the smoothing and the geometry of a blind second
/// have to know, and an anchor list cannot say it. Stories 5.2 and 5.3 read the same values to ask
/// before a fragment is used and to show which stretch it redrew.
/// </para>
/// </summary>
/// <param name="OpensAtTrackStart">
/// The track's span begins where the activity does, so the athlete starts on its first metre. True
/// of a whole track by definition, and of a fragment covering the activity's own first kilometres —
/// the head fragment, whose start AD-27 falls back to the activity's start because no true position
/// survives before it. False for a fragment that begins later: the seconds before it are the
/// watch's own.
/// </param>
/// <param name="ClosesAtTrackEnd">
/// The track's span ends where the activity does, so the athlete finishes on its last metre (AD-17,
/// AD-27) — a whole track, or a fragment with no true position surviving after it. False for a
/// fragment the activity outlived: the finish is then the athlete's own last position.
/// </param>
/// <param name="FromSeconds">
/// When the track's own span begins, s from the activity's start.
/// </param>
/// <param name="ToSeconds">
/// When it ends, s from the activity's start. Outside <c>[FromSeconds, ToSeconds]</c> the track
/// contributes nothing at all: not a metre of distance and not a coordinate (AD-26).
/// </param>
public readonly record struct TrackReach(
    bool OpensAtTrackStart,
    bool ClosesAtTrackEnd,
    double FromSeconds,
    double ToSeconds);

/// <summary>Результат Вирівнювання: Якорі, Зони довіри і попередження.</summary>
public sealed class Alignment
{
    public required IReadOnlyList<Anchor> Anchors { get; init; }

    /// <summary>
    /// What the anchors established about the track's own reach into the activity (AD-27).
    /// </summary>
    public required TrackReach Reach { get; init; }

    /// <summary>
    /// Which stretches of the activity the position was measured over and which were interpolated,
    /// in time order, tiling the alignment's own span exactly once — no gap, no overlap, and no two
    /// adjacent zones of the same kind (FR-4, FR-8). Derived from <see cref="Anchors"/>: a stretch
    /// between two of them is measured while they are closer together in time than
    /// <see cref="AlignmentOptions.EstimatedZoneGapSeconds"/>, and estimated when the position had
    /// to be carried across a longer gap.
    /// <para>
    /// A list rather than a single boundary, because real GPS drops out, returns and drops again: a
    /// "measured until" would call the kilometre whose signal came back estimated, and the athlete
    /// looking at the preview could not tell which stretches to trust.
    /// </para>
    /// </summary>
    public required IReadOnlyList<TrustZone> Zones { get; init; }

    /// <summary>
    /// Час останнього Якоря — the last second any anchor spoke for. Not the boundary of trust,
    /// which <see cref="Zones"/> owns: it is what the finish is pinned against (AD-17) and how much
    /// of the activity the plausible distance still has to cover.
    /// </summary>
    public required double MeasuredUntilSeconds { get; init; }

    /// <summary>
    /// How far along the track the Original's own GPS established the athlete had gone, m — the
    /// distance of the last measured Якір, before any finish was pinned to it. The floor under the
    /// chosen track length (AD-17): a length below this would have the series reach its finish in
    /// the middle of the measured zone and then stand still, which is the one outcome worse than an
    /// inflated pace. It stays the measured distance after the finish is pinned, which is why it is
    /// a value of its own rather than <c>Anchors[^1].TrackDistance</c>.
    /// </summary>
    public required double MeasuredDistanceMeters { get; init; }

    /// <summary>Час, на якому детектор вирішив, що GPS Оригіналу завис; null — не виявлено.</summary>
    public double? GpsStallAtSeconds { get; init; }

    /// <summary>
    /// How far the Original's first position lay from the nearest point of the whole reference
    /// track, m — the distance <c>TRACK_FAR_FROM_ORIGINAL</c> is raised about. Deliberately not the
    /// search's closest approach: a loop that crosses the athlete's route anywhere comes within a
    /// metre of some position while nothing attaches, and "0.8 m" beside "far" would contradict the
    /// caveat. Set only together with that warning: a number beside no warning would invite a shell
    /// to invent a caveat of its own (FR-3, AD-16).
    /// </summary>
    public double? StartToTrackMeters { get; init; }

    /// <summary>Warning codes, every one declared in <c>DiagnosticCodes</c> (AD-18).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
