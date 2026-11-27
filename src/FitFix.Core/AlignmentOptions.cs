namespace FitFix.Core;

/// <summary>Пороги Вирівнювання. Значення за замовчуванням — з PRD §4.2 і §9.</summary>
public sealed record AlignmentOptions
{
    /// <summary>Максимальне відхилення GPS-точки Оригіналу від треку, щоб стати Якорем (FR-4).</summary>
    public double AnchorToleranceMeters { get; init; } = 20;

    /// <summary>
    /// Highest plausible speed between two anchors, m/s. The default is the running cap, which
    /// is why <see cref="ForSport"/> leaves running alone: the number has one home.
    /// </summary>
    public double MaxSpeedMetersPerSecond { get; init; } = 7;

    /// <summary>Допуск до оцінки швидкості, щоб не відкидати Якорі через шум GPS, м.</summary>
    public double SpeedToleranceMeters { get; init; } = 3;

    /// <summary>
    /// The shortest time base a position may be judged impossible over, s. Cleaning asks whether
    /// <see cref="MaxSpeedMetersPerSecond"/> could cover the step in <c>max(dt, this)</c>, so a
    /// single second of GPS jitter — 15 m between two 1 Hz records of a 5 m/s runner — is never
    /// called impossible, while a sustained impossible speed still is (AD-25).
    /// </summary>
    public double ImpossibleSpeedJudgedOverSeconds { get; init; } = 5;

    /// <summary>
    /// How long a reference with unresolved garbage behind it may vouch for, s. While cleaning is
    /// still throwing positions away, the allowance stops growing with the elapsed time at this
    /// bound, so a run of garbage cannot widen it until the next teleport fits inside (AD-25).
    /// Its own threshold on purpose: <see cref="LongGapSeconds"/> governs the anchor stage's
    /// minimum-speed gate, and retuning that gate must not retune cleaning.
    /// </summary>
    public double StaleReferenceVouchesForSeconds { get; init; } = 30;

    /// <summary>
    /// How many of the following positions vote on whether a position can be trusted as the
    /// reference everything else is judged against (AD-25). A leading position has nothing behind
    /// it, so this many positions that are somewhere else are asked whether it can be reached from
    /// them; otherwise a cold-start fix would be confirmed by its own repetitions and condemn the
    /// real route. It also bounds how long a run of discards may go on before the reference counts
    /// as left behind and a new one is seated by the same vote.
    /// </summary>
    public int LeadingAgreementPositions { get; init; } = 3;

    /// <summary>Якщо між Якорями пройшло стільки часу, вимагаємо мінімальну швидкість (відсікає завислий GPS).</summary>
    public double LongGapSeconds { get; init; } = 30;

    /// <summary>Мінімальна швидкість на довгому розриві, м/с.</summary>
    public double LongGapMinSpeedMetersPerSecond { get; init; } = 1.8;

    /// <summary>Скільки сегментів назад дозволено дивитись під час проєкції (шум GPS).</summary>
    public int SearchWindowBackward { get; init; } = 3;

    /// <summary>Скільки сегментів уперед дозволено дивитись під час проєкції.</summary>
    public int SearchWindowForward { get; init; } = 40;

    /// <summary>
    /// The longest a stretch between two consecutive Якорі may last and still be called measured,
    /// s (FR-4). Above it the position between them was carried across a gap the Original's own GPS
    /// said nothing about, so the stretch is a Розрахована зона — which is how a dropout that later
    /// recovers shows up as its own estimated zone instead of condemning everything after it.
    /// <para>
    /// Its own threshold and not <see cref="LongGapSeconds"/>, which decides whether an anchor is
    /// accepted at all: a watch under smart recording writes a point every twenty seconds and is
    /// measured throughout, and retuning the anchor gate must not reclassify its whole route.
    /// </para>
    /// </summary>
    public double EstimatedZoneGapSeconds { get; init; } = 30;

    /// <summary>
    /// Напівширина згладжування в Розрахованій зоні, с. Only the estimated zones are smoothed:
    /// a measured position is data rather than an estimate, so there is no width for one (AD-26).
    /// </summary>
    public double EstimatedZoneSmoothingSeconds { get; init; } = 60;

    /// <summary>Радіус, у якому позиція вважається незмінною (детектор завислого GPS), м.</summary>
    public double StallRadiusMeters { get; init; } = 15;

    /// <summary>Скільки часу позиція має не змінюватись, щоб вважати GPS завислим, с.</summary>
    public double StallMinimumSeconds { get; init; } = 90;

    /// <summary>
    /// Поріг «трек явно не про цю активність» (FR-3), м. Judged against the closest the anchor
    /// search ever came to the track, never against the Original's first position alone: a track
    /// covering the last five kilometres of a fifty-kilometre run starts 45 km from where the
    /// activity did, and a warning that is false for every fragment teaches people to ignore
    /// warnings (AD-27). Past this distance nothing on the track attaches at all and the repair is
    /// refused instead.
    /// </summary>
    public double TrackFarFromOriginalMeters { get; init; } = 2_000;

    /// <summary>
    /// How near one of a fragment's own two endpoints the activity must come for that endpoint to
    /// attach there, m (AD-27). A fragment is attached by its ends and not by a chain, so this is
    /// the threshold the whole mechanism turns on: too small and a route drawn by hand attaches
    /// nowhere, too large and a lap's own closing metres offer the same place twice.
    /// <para>
    /// Generous next to <see cref="AnchorToleranceMeters"/>, which judges a position against the
    /// nearest point of the whole track: an endpoint is one fixed point, the watch's fix near it
    /// carries its own error, and a route drawn in an editor after the run puts its ends where the
    /// athlete pressed the button only to the nearest block. Measured on the 23.11.2026 files, where
    /// the two ends of a real fragment attach 7 m and 44 m from the athlete's own positions.
    /// </para>
    /// <para>
    /// Offering the same place twice is not a hazard this number can remove — a lap route's first and
    /// last hundred metres run along the same street, so the fragment's end is 54 m from where the
    /// activity started as well as 44 m from where it passed at the finish — and it is not asked to:
    /// which pair of places the fragment attaches between is settled by the order they are run in
    /// and by the failure the span claims to cover.
    /// </para>
    /// </summary>
    public double TrackEndAttachmentMeters { get; init; } = 100;

    /// <summary>
    /// Minimum spacing between two consecutive reference-track points, m. A point closer than
    /// this to the previous one is dropped as a duplicate while the track is built (FR-2).
    /// </summary>
    public double TrackMinSpacingMeters { get; init; } = 0.3;

    /// <summary>
    /// How far back the device's own recorded distance is examined to decide whether the device
    /// was still measuring movement, s. Governs which zero power values are carried over (FR-6).
    /// </summary>
    public double DeviceDistanceStallWindowSeconds { get; init; } = 10;

    /// <summary>
    /// How much the device's own recorded distance must grow inside that window to count as
    /// movement, m. Below it the zero power the device derived from a dead GPS is dropped (FR-6).
    /// </summary>
    public double DeviceDistanceGrowthMeters { get; init; } = 0.5;

    /// <summary>
    /// Half-width of the window the repaired speed of a record is estimated over, in records.
    /// </summary>
    public int SpeedWindowHalfWidthRecords { get; init; } = 3;

    /// <summary>
    /// Fewer anchors than this make the alignment too thin to trust, and the repair reports
    /// <c>FEW_ANCHORS</c> (FR-4). The first and the last anchor are counted too.
    /// </summary>
    public int MinimumAnchorCount { get; init; } = 4;

    /// <summary>
    /// The smallest divergence between the track's whole length and the plausible distance that
    /// may be reported, m — the floor under <see cref="TrackLengthToleranceFraction"/> (AD-17).
    /// Inside the tolerance the whole length is taken as it is: a declared footpod and a correctly
    /// drawn route disagree by a percent or two, and a blocking warning on that noise would teach
    /// the athlete to click through the one case it exists for — a lap drawn one time too many,
    /// which is hundreds of metres at the very least.
    /// </summary>
    public double TrackLengthToleranceMeters { get; init; } = 200;

    /// <summary>
    /// The share of the plausible distance the two lengths may differ by, on top of the floor
    /// above: the tolerance is <c>max(floor, fraction × plausible)</c>. A fraction and not metres
    /// alone, because a sensor's disagreement with a drawn route scales with the route — a flat
    /// 200 m would warn about every 100 km ride with a wheel sensor, where 1–2 km of divergence is
    /// ordinary, and warning about all of them is the same as warning about none (AD-17).
    /// </summary>
    public double TrackLengthToleranceFraction { get; init; } = 0.02;

    /// <summary>
    /// The thresholds for one sport. Running is the record's own profile — stating its speed cap
    /// again here would be a second place for the same number to live, free to drift from the
    /// first — so only the other sports override anything.
    /// </summary>
    public static AlignmentOptions ForSport(SportKind sport) => sport switch
    {
        SportKind.Running => new AlignmentOptions(),
        SportKind.Cycling => new AlignmentOptions { MaxSpeedMetersPerSecond = 25 },
        _ => new AlignmentOptions { MaxSpeedMetersPerSecond = 15 }
    };
}
