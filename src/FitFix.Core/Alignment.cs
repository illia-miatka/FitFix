namespace FitFix.Core;

/// <summary>Якір: час від початку Оригіналу ↔ дистанція вздовж Еталонного треку.</summary>
public readonly record struct Anchor(double TimeSeconds, double TrackDistance, double DeviationMeters);

/// <summary>Результат Вирівнювання: Якорі, межа Надійної зони і попередження.</summary>
public sealed class Alignment
{
    public required IReadOnlyList<Anchor> Anchors { get; init; }

    /// <summary>Час останнього Якоря — далі позиція розрахована (Розрахована зона).</summary>
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

    /// <summary>Warning codes, every one declared in <c>DiagnosticCodes</c> (AD-18).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
