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
/// One lap of the activity, read from the Original's own <c>lap</c> messages (AD-24): the span
/// between two presses of the lap button, or between two automatic splits. The interval says
/// where the lap was in time and nothing else — its distance is recomputed from the repaired
/// series, because the Original's own lap distance was measured by the GPS that failed (FR-6).
/// A lap whose <c>start_time</c> the file never stated is carried as a point, with
/// <see cref="Start"/> equal to <see cref="End"/>: its end is a boundary the athlete made, and
/// dropping the whole lap would throw that boundary away to avoid inventing a beginning.
/// </summary>
public readonly record struct LapInterval(DateTime Start, DateTime End);

/// <summary>
/// Оригінал — джерело часу й сенсорів. A record, so cleaning copies it with <c>with</c> and
/// every member comes along by itself — the pauses of Story 1.5 and the laps of Story 1.6
/// included.
/// </summary>
public sealed record OriginalActivity
{
    public required IReadOnlyList<ActivityRecord> Records { get; init; }
    public SportKind Sport { get; init; } = SportKind.Other;

    private readonly IReadOnlyList<PauseInterval> pauses = [];
    private readonly IReadOnlyList<LapInterval> laps = [];

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

    /// <summary>
    /// The laps the Original recorded (AD-24), with the file's own boundaries: a lap is where the
    /// athlete pressed the button, and nothing else in the model may move it. Ordered by start
    /// and then by end in the init accessor, exactly as <see cref="Pauses"/> is normalised there,
    /// so every reader sees one order; a backwards interval is dropped, because it describes no
    /// stretch of the activity. Overlapping, repeated and zero-length laps are <em>kept</em> as
    /// they were read, unlike pauses: a device that writes them is describing presses that really
    /// happened, and reconciling them is the job of <see cref="LapPartition"/>, the one place that
    /// has to produce a tiling. Clipping to the activity's span happens there too, for the same
    /// reason it does for pauses: the span is a property of <see cref="Records"/>, which an init
    /// accessor cannot depend on having seen.
    /// Empty is the honest default — a file with no <c>lap</c> message had no laps.
    /// </summary>
    public IReadOnlyList<LapInterval> Laps
    {
        get => laps;
        init => laps = Normalise(value);
    }

    /// <summary>
    /// The laps as a partition of the activity: every interval the written file will carry, in
    /// time order, tiling the span from the first record to the last exactly once — no gap, no
    /// overlap (FR-6). Both the <em>starts</em> and the <em>ends</em> of the Original's laps are
    /// cut points, so a lap that does not begin where the activity did keeps its own beginning and
    /// the unlapped stretch before it becomes an interval of its own; cutting at ends alone would
    /// hand back boundaries the athlete never pressed while the arithmetic still added up. The
    /// first interval starts at the activity's start and the last runs to its end, which is what
    /// makes the lap distances sum to the total whatever the device wrote: cuts outside the span
    /// are dropped rather than trusted, repeated cuts cut once, and a lap reaching past the last
    /// record contributes no cut at all.
    /// <para>
    /// A cut is also dropped when no record separates it from the previous boundary — two lap
    /// messages a fraction of a second apart, a manual press beside an auto-lap — because the
    /// interval between them would hold a single record and be written as a lap of no distance
    /// and no pace. Resolving the cuts against the records rather than against an assumed
    /// recording rate is what makes that true for smart recording as well as for 1 Hz.
    /// </para>
    /// An activity with no laps is one lap covering everything; an activity with no records, or
    /// with one, has no laps at all — there is no span to partition, and the alternative is an
    /// index out of range where this project's rule is a declared code or nothing (AD-18).
    /// <para>
    /// A method rather than a property: it sorts and allocates on every call, so a caller that
    /// needs it twice should hold the result.
    /// </para>
    /// </summary>
    public IReadOnlyList<LapInterval> LapPartition()
    {
        if (Records.Count < 2)
            return [];

        var start = StartTime;
        var end = EndTime;

        // Both boundaries of every lap, in order. Laps are ordered by start, so the ends of laps
        // nested inside others are not in order and the whole set has to be sorted.
        var cuts = Laps
            .SelectMany(lap => (DateTime[])[lap.Start, lap.End])
            .Where(cut => cut > start && cut < end)
            .Distinct()
            .Order();

        var partition = new List<LapInterval>();
        var from = start;
        var fromRecord = 0;

        foreach (var cut in cuts)
        {
            var record = NearestRecordIndex(cut);

            // The cut has to leave a record on each side of it: one that is the previous
            // boundary's own record closes an interval of no distance, and one that is the last
            // record leaves the final interval with nothing of its own.
            if (record <= fromRecord || record >= Records.Count - 1)
                continue;

            partition.Add(new LapInterval(from, cut));
            from = cut;
            fromRecord = record;
        }

        partition.Add(new LapInterval(from, end));
        return partition;
    }

    /// <summary>
    /// The index of the record nearest an instant in time, with a tie going to the earlier
    /// record. A lap boundary need not fall on a record — under smart recording most of them fall
    /// between two — so this is where a boundary is resolved to a record, once: the partition
    /// drops cuts that resolve onto a boundary already taken, and the FIT writer reads each lap's
    /// distance at the very same index, so two adjacent laps share one record and the distances
    /// telescope to the activity's total instead of counting a metre twice or losing it.
    /// </summary>
    public int NearestRecordIndex(DateTime instant)
    {
        var low = 0;
        var high = Records.Count - 1;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (Records[mid].Timestamp < instant)
                low = mid + 1;
            else
                high = mid;
        }

        // `low` is the first record at or after the instant; the one before it may be closer.
        if (low > 0 && instant - Records[low - 1].Timestamp <= Records[low].Timestamp - instant)
            return low - 1;

        return low;
    }

    public DateTime StartTime => Records[0].Timestamp;
    public DateTime EndTime => Records[^1].Timestamp;
    public double ElapsedSeconds => (EndTime - StartTime).TotalSeconds;

    /// <summary>
    /// Whether any record still carries a position. The one place that question is answered: the
    /// aligner asks it before it looks for anchors and reports <c>NO_ORIGINAL_GPS</c>, and a shell
    /// asks it to say whether the repair has anything to anchor the track with. Asked of a cleaned
    /// series it is the honest answer about usable GPS — a series cleaned down to nothing says
    /// false, exactly as one that never had a position does (AD-25).
    /// </summary>
    public bool HasPosition => Records.Any(record => record.Position is not null);

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

    /// <summary>
    /// The laps ordered by start and then by end, with the backwards ones dropped: an interval
    /// that ends before it began describes no stretch of the activity. A lap that ends where it
    /// began is kept — it is how a lap the file gave no start is carried, and its end is still a
    /// boundary. Nothing is merged — see <see cref="Laps"/>.
    /// </summary>
    private static IReadOnlyList<LapInterval> Normalise(IReadOnlyList<LapInterval>? intervals)
    {
        if (intervals is null || intervals.Count == 0)
            return [];

        return [.. intervals.Where(lap => lap.End >= lap.Start).OrderBy(lap => lap.Start).ThenBy(lap => lap.End)];
    }

    /// <summary>The activity's total paused seconds, clipped to its own span (FR-6).</summary>
    public double PausedSeconds => PausedSecondsBefore(ElapsedSeconds);

    /// <summary>
    /// The activity with a pause list that cannot be true struck out: one that leaves no moving
    /// second at all, while the records plainly cover a distance. A moving time of zero beside a
    /// full distance reads as an infinite pace, and spreading the distance over a measure that
    /// never advances leaves every record at the start and the whole track owed at the finish.
    /// <para>
    /// The one owner of that disbelief (AD-7): the repair applies it before it computes a single
    /// moving second, and the summary applies it to whatever activity it is handed, so no caller
    /// can decide it differently. An activity with a believable pause list — or none — is returned
    /// as it is.
    /// </para>
    /// </summary>
    public OriginalActivity WithoutImpossiblePauses() =>
        Pauses.Count == 0 || PausedSeconds < ElapsedSeconds ? this : this with { Pauses = [] };

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
