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

    /// <summary>Якщо між Якорями пройшло стільки часу, вимагаємо мінімальну швидкість (відсікає завислий GPS).</summary>
    public double LongGapSeconds { get; init; } = 30;

    /// <summary>Мінімальна швидкість на довгому розриві, м/с.</summary>
    public double LongGapMinSpeedMetersPerSecond { get; init; } = 1.8;

    /// <summary>Скільки сегментів назад дозволено дивитись під час проєкції (шум GPS).</summary>
    public int SearchWindowBackward { get; init; } = 3;

    /// <summary>Скільки сегментів уперед дозволено дивитись під час проєкції.</summary>
    public int SearchWindowForward { get; init; } = 40;

    /// <summary>Напівширина згладжування в Надійній зоні, с.</summary>
    public double SmoothingSeconds { get; init; } = 15;

    /// <summary>Напівширина згладжування в Розрахованій зоні, с.</summary>
    public double EstimatedZoneSmoothingSeconds { get; init; } = 60;

    /// <summary>Радіус, у якому позиція вважається незмінною (детектор завислого GPS), м.</summary>
    public double StallRadiusMeters { get; init; } = 15;

    /// <summary>Скільки часу позиція має не змінюватись, щоб вважати GPS завислим, с.</summary>
    public double StallMinimumSeconds { get; init; } = 90;

    /// <summary>Поріг «трек явно не про цю активність» (FR-3), м.</summary>
    public double TrackFarFromOriginalMeters { get; init; } = 2_000;

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
