namespace FitFix.Core;

/// <summary>Якір: час від початку Оригіналу ↔ дистанція вздовж Еталонного треку.</summary>
public readonly record struct Anchor(double TimeSeconds, double TrackDistance, double DeviationMeters);

/// <summary>Коди попереджень Звіту (PRD §4.1, §4.3).</summary>
public static class RepairWarnings
{
    public const string NoOriginalGps = "NO_ORIGINAL_GPS";
    public const string TrackFarFromOriginal = "TRACK_FAR_FROM_ORIGINAL";
    public const string GpsStallDetected = "GPS_STALL_DETECTED";
    public const string FewAnchors = "FEW_ANCHORS";
}

/// <summary>Результат Вирівнювання: Якорі, межа Надійної зони і попередження.</summary>
public sealed class Alignment
{
    public required IReadOnlyList<Anchor> Anchors { get; init; }

    /// <summary>Час останнього Якоря — далі позиція розрахована (Розрахована зона).</summary>
    public required double MeasuredUntilSeconds { get; init; }

    /// <summary>Час, на якому детектор вирішив, що GPS Оригіналу завис; null — не виявлено.</summary>
    public double? GpsStallAtSeconds { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
