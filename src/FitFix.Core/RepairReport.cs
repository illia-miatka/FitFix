namespace FitFix.Core;

/// <summary>Звіт про виправлення — те, що UI показує у прев'ю «До / Після» (PRD §4.3).</summary>
public sealed record RepairReport
{
    public required double OriginalDistanceMeters { get; init; }
    public required double RepairedDistanceMeters { get; init; }
    public required double TrackLengthMeters { get; init; }
    public required double ElapsedSeconds { get; init; }

    /// <summary>До цього часу позиція виміряна Якорями, далі — розрахована.</summary>
    public required double MeasuredUntilSeconds { get; init; }

    public required int AnchorCount { get; init; }
    public double? GpsStallAtSeconds { get; init; }

    /// <summary>
    /// How many GPS positions of the Original were physically impossible and thrown away before
    /// anything read them (AD-25). The user is told how much was discarded, not only that
    /// something was.
    /// </summary>
    public required int DiscardedPositionCount { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public double MeasuredFraction => ElapsedSeconds > 0 ? MeasuredUntilSeconds / ElapsedSeconds : 0;

    public double? OriginalPaceSecondsPerKm => OriginalDistanceMeters > 0
        ? ElapsedSeconds / (OriginalDistanceMeters / 1000.0)
        : null;

    public double RepairedPaceSecondsPerKm => RepairedDistanceMeters > 0
        ? ElapsedSeconds / (RepairedDistanceMeters / 1000.0)
        : 0;
}

/// <summary>Відновлена активність: записи + Звіт.</summary>
public sealed class RepairResult
{
    public required IReadOnlyList<RepairedRecord> Records { get; init; }
    public required RepairReport Report { get; init; }
    public required Alignment Alignment { get; init; }

    /// <summary>
    /// The Original the repair actually worked from: every record and sensor value of the
    /// uploaded file, with the impossible positions already gone (AD-25). A shell's "before"
    /// map and its zones read this, never the raw series, so a discarded point is drawn nowhere.
    /// </summary>
    public required OriginalActivity CleanedOriginal { get; init; }

    public SportKind Sport { get; init; }
}
