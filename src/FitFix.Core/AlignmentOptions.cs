namespace FitFix.Core;

/// <summary>Пороги Вирівнювання. Значення за замовчуванням — з PRD §4.2 і §9.</summary>
public sealed record AlignmentOptions
{
    /// <summary>Максимальне відхилення GPS-точки Оригіналу від треку, щоб стати Якорем (FR-4).</summary>
    public double AnchorToleranceMeters { get; init; } = 20;

    /// <summary>Максимальна правдоподібна швидкість між Якорями, м/с (залежить від виду спорту).</summary>
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

    public static AlignmentOptions ForSport(SportKind sport) => sport switch
    {
        SportKind.Running => new AlignmentOptions { MaxSpeedMetersPerSecond = 7 },
        SportKind.Cycling => new AlignmentOptions { MaxSpeedMetersPerSecond = 25 },
        _ => new AlignmentOptions { MaxSpeedMetersPerSecond = 15 }
    };
}
