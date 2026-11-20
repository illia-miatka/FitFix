namespace FitFix.Core;

/// <summary>Вид спорту — від нього залежать пороги швидкості (PRD FR-4).</summary>
public enum SportKind
{
    Running,
    Cycling,
    Other
}

/// <summary>Один record-запис Оригіналу: час, позиція (може бути відсутня) і сенсори.</summary>
public sealed record ActivityRecord
{
    public required DateTime Timestamp { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Altitude { get; init; }
    public double? Distance { get; init; }
    public double? Speed { get; init; }
    public byte? HeartRate { get; init; }
    public ushort? Power { get; init; }
    public byte? Cadence { get; init; }
    public sbyte? Temperature { get; init; }

    public GeoPoint? Position => Latitude is { } lat && Longitude is { } lon ? new GeoPoint(lat, lon) : null;
}

/// <summary>
/// Оригінал — джерело часу й сенсорів. A record, so cleaning copies it with <c>with</c> and
/// every member a later story adds — the laps of Story 1.6 — comes along by itself.
/// </summary>
public sealed record OriginalActivity
{
    public required IReadOnlyList<ActivityRecord> Records { get; init; }
    public SportKind Sport { get; init; } = SportKind.Other;

    public DateTime StartTime => Records[0].Timestamp;
    public DateTime EndTime => Records[^1].Timestamp;
    public double ElapsedSeconds => (EndTime - StartTime).TotalSeconds;

    /// <summary>Дистанція, яку записав пристрій (те, що бачить користувач у Strava до виправлення).</summary>
    public double? RecordedDistance => Records.LastOrDefault(r => r.Distance is not null)?.Distance;

    public double SecondsFromStart(ActivityRecord record) => (record.Timestamp - StartTime).TotalSeconds;
}

/// <summary>Точка Еталонного треку до нормалізації.</summary>
public sealed record TrackPoint(GeoPoint Position, double? Altitude);

/// <summary>Один виправлений запис — результат роботи ядра.</summary>
public sealed record RepairedRecord
{
    public required DateTime Timestamp { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public double? Altitude { get; init; }
    public required double Distance { get; init; }
    public required double Speed { get; init; }
    public byte? HeartRate { get; init; }
    public ushort? Power { get; init; }
    public byte? Cadence { get; init; }
    public sbyte? Temperature { get; init; }
}
