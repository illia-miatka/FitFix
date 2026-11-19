namespace FitFix.Core;

/// <summary>Якір: час від початку Оригіналу ↔ дистанція вздовж Еталонного треку.</summary>
public readonly record struct Anchor(double TimeSeconds, double TrackDistance, double DeviationMeters);

/// <summary>Результат Вирівнювання: Якорі, межа Надійної зони і попередження.</summary>
public sealed class Alignment
{
    public required IReadOnlyList<Anchor> Anchors { get; init; }

    /// <summary>Час останнього Якоря — далі позиція розрахована (Розрахована зона).</summary>
    public required double MeasuredUntilSeconds { get; init; }

    /// <summary>Час, на якому детектор вирішив, що GPS Оригіналу завис; null — не виявлено.</summary>
    public double? GpsStallAtSeconds { get; init; }

    /// <summary>Warning codes, every one declared in <c>DiagnosticCodes</c> (AD-18).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
