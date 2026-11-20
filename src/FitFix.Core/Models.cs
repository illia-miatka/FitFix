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
/// A stretch of the activity the timer was stopped for, read from the Original's own
/// <c>timer stop</c> / <c>timer start</c> events (AD-7). Distance never grows inside one, which
/// is what keeps a wait at a traffic light from earning metres (FR-5). One model of a pause:
/// there is no per-record "was moving" flag anywhere, and no second place a pause can come from.
/// </summary>
public readonly record struct PauseInterval(DateTime Start, DateTime End);

/// <summary>
/// Оригінал — джерело часу й сенсорів. A record, so cleaning copies it with <c>with</c> and
/// every member a later story adds — the laps of Story 1.6 — comes along by itself.
/// </summary>
public sealed record OriginalActivity
{
    public required IReadOnlyList<ActivityRecord> Records { get; init; }
    public SportKind Sport { get; init; } = SportKind.Other;

    private readonly IReadOnlyList<PauseInterval> pauses = [];

    /// <summary>
    /// The stretches the timer was stopped for (AD-7) — always ordered by time, never
    /// overlapping, never of zero length, whatever order and overlap they were set in: the
    /// intervals are normalised here, once, so that every reader of them sees the same pauses and
    /// no consumer has to defend itself against a malformed list. Empty is the honest default: an
    /// activity nobody paused, for which moving time is elapsed time exactly.
    /// </summary>
    public IReadOnlyList<PauseInterval> Pauses
    {
        get => pauses;
        init => pauses = Normalise(value);
    }

    public DateTime StartTime => Records[0].Timestamp;
    public DateTime EndTime => Records[^1].Timestamp;
    public double ElapsedSeconds => (EndTime - StartTime).TotalSeconds;

    /// <summary>Дистанція, яку записав пристрій (те, що бачить користувач у Strava до виправлення).</summary>
    public double? RecordedDistance => Records.LastOrDefault(r => r.Distance is not null)?.Distance;

    public double SecondsFromStart(ActivityRecord record) => SecondsFromStart(record.Timestamp);

    public double SecondsFromStart(DateTime instant) => (instant - StartTime).TotalSeconds;

    /// <summary>
    /// Seconds of pause that fall before <paramref name="secondsFromStart"/>. The intervals are
    /// already ordered and merged (see <see cref="Pauses"/>), so this is a sum of the stretches
    /// clipped to the activity's own span: a <c>timer stop</c> recorded before the first record
    /// counts only from the start, and no second is counted twice.
    /// </summary>
    public double PausedSecondsBefore(double secondsFromStart)
    {
        var limit = Math.Clamp(secondsFromStart, 0, ElapsedSeconds);
        var paused = 0.0;

        foreach (var pause in Pauses)
        {
            var from = Math.Max(SecondsFromStart(pause.Start), 0);
            var to = Math.Min(SecondsFromStart(pause.End), limit);
            if (to > from)
                paused += to - from;
        }

        return paused;
    }

    /// <summary>
    /// Whether an instant falls inside a pause, boundaries included. The record that opens a
    /// pause and the one that ends it count as part of it: they sit where the athlete stood
    /// still, which is what a reader of immobility needs to know (AD-7).
    /// </summary>
    public bool IsPaused(DateTime instant)
    {
        foreach (var pause in Pauses)
        {
            if (instant >= pause.Start && instant <= pause.End)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Which stretch between pause boundaries an instant falls in. The pauses cut the activity
    /// into alternating moving and standing stretches; a window that may not cross a pause — the
    /// smoothing window of AD-21, or a window measuring immobility — compares this number for its
    /// two ends instead of deriving the boundaries again. Zero for an activity with no pauses.
    /// </summary>
    public int PauseSegment(DateTime instant)
    {
        var segment = 0;
        foreach (var pause in Pauses)
        {
            if (instant >= pause.Start)
                segment++;
            if (instant >= pause.End)
                segment++;
        }

        return segment;
    }

    /// <summary>
    /// The intervals ordered by start and merged where they touch or overlap, with empty and
    /// backwards ones dropped. The one place a pause list is put in order, so the moving-time
    /// arithmetic and the smoothing boundaries can never disagree about where a pause is (AD-7).
    /// </summary>
    private static IReadOnlyList<PauseInterval> Normalise(IReadOnlyList<PauseInterval>? intervals)
    {
        if (intervals is null || intervals.Count == 0)
            return [];

        var ordered = intervals.Where(p => p.End > p.Start).OrderBy(p => p.Start).ToArray();
        if (ordered.Length == 0)
            return [];

        var merged = new List<PauseInterval> { ordered[0] };
        foreach (var pause in ordered.Skip(1))
        {
            var open = merged[^1];
            if (pause.Start <= open.End)
                merged[^1] = new PauseInterval(open.Start, pause.End > open.End ? pause.End : open.End);
            else
                merged.Add(pause);
        }

        return merged;
    }

    /// <summary>The activity's total paused seconds, clipped to its own span (FR-6).</summary>
    public double PausedSeconds => PausedSecondsBefore(ElapsedSeconds);

    /// <summary>
    /// The seconds of movement that preceded an instant: elapsed seconds minus the pauses before
    /// it. Inside a pause the number stops advancing, which is how distance spread over this
    /// measure stays flat across a pause by construction rather than by a special case (FR-5).
    /// With no pauses it is the identity.
    /// </summary>
    public double MovingSecondsFromStart(double secondsFromStart) =>
        secondsFromStart - PausedSecondsBefore(secondsFromStart);

    /// <inheritdoc cref="MovingSecondsFromStart(double)"/>
    public double MovingSecondsFromStart(ActivityRecord record) =>
        MovingSecondsFromStart(SecondsFromStart(record));
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
