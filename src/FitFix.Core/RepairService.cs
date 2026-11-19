namespace FitFix.Core;

/// <summary>
/// Ядро FitFix: бере час і сенсори з Оригіналу, геометрію з Еталонного треку
/// і будує Відновлену активність (PRD FR-5, FR-6).
/// </summary>
public static class RepairService
{
    /// <summary>Вікно, на якому перевіряємо, чи рухалась дистанція самого пристрою, с.</summary>
    private const double DeviceDistanceStallWindowSeconds = 10;

    /// <summary>Напівширина вікна для оцінки швидкості, записів.</summary>
    private const int SpeedWindowRecords = 3;

    public static RepairResult Repair(OriginalActivity original, ReferenceTrack track, AlignmentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(track);

        options ??= AlignmentOptions.ForSport(original.Sport);
        var alignment = Aligner.Align(original, track, options);

        var records = original.Records;
        var times = new double[records.Count];
        for (var i = 0; i < records.Count; i++)
            times[i] = original.SecondsFromStart(records[i]);

        var distances = InterpolateDistances(times, alignment.Anchors);
        Smooth(distances, times, alignment.MeasuredUntilSeconds, options);
        EnforceMonotonicity(distances, track.Length);

        var speeds = ComputeSpeeds(distances, times);
        var altitudeOffset = ComputeAltitudeOffset(original, track);

        var repaired = new List<RepairedRecord>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var source = records[i];
            var position = track.At(distances[i]);

            repaired.Add(new RepairedRecord
            {
                Timestamp = source.Timestamp,
                Latitude = position.Position.Latitude,
                Longitude = position.Position.Longitude,
                Altitude = position.Altitude is { } altitude ? altitude + altitudeOffset : source.Altitude,
                Distance = distances[i],
                Speed = speeds[i],
                HeartRate = source.HeartRate,
                Power = KeepPower(records, i, times, source.Power),
                Cadence = source.Cadence,
                Temperature = source.Temperature
            });
        }

        var report = new RepairReport
        {
            OriginalDistanceMeters = original.RecordedDistance ?? 0,
            RepairedDistanceMeters = distances[^1],
            TrackLengthMeters = track.Length,
            ElapsedSeconds = original.ElapsedSeconds,
            MeasuredUntilSeconds = alignment.MeasuredUntilSeconds,
            AnchorCount = alignment.Anchors.Count,
            GpsStallAtSeconds = alignment.GpsStallAtSeconds,
            Warnings = alignment.Warnings
        };

        return new RepairResult
        {
            Records = repaired,
            Report = report,
            Alignment = alignment,
            Sport = original.Sport
        };
    }

    /// <summary>Лінійна інтерполяція «час → дистанція» між Якорями.</summary>
    private static double[] InterpolateDistances(double[] times, IReadOnlyList<Anchor> anchors)
    {
        var anchorTimes = anchors.Select(a => a.TimeSeconds).ToArray();
        var result = new double[times.Length];

        for (var i = 0; i < times.Length; i++)
        {
            var index = UpperBound(anchorTimes, times[i]) - 1;
            index = Math.Clamp(index, 0, anchors.Count - 2);

            var from = anchors[index];
            var to = anchors[index + 1];
            var span = to.TimeSeconds - from.TimeSeconds;
            var u = span > 0 ? (times[i] - from.TimeSeconds) / span : 0;
            result[i] = from.TrackDistance + u * (to.TrackDistance - from.TrackDistance);
        }

        return result;
    }

    /// <summary>
    /// Симетричне ковзне середнє: вузьке у Надійній зоні, широке в Розрахованій,
    /// де лінійна інтерполяція дає злами на Якорях.
    /// </summary>
    private static void Smooth(double[] distances, double[] times, double measuredUntil, AlignmentOptions options)
    {
        var raw = (double[])distances.Clone();

        for (var i = 0; i < distances.Length; i++)
        {
            var halfWidth = times[i] <= measuredUntil
                ? options.SmoothingSeconds
                : options.EstimatedZoneSmoothingSeconds;

            var low = i;
            while (low > 0 && times[i] - times[low - 1] <= halfWidth)
                low--;

            var high = i;
            while (high < distances.Length - 1 && times[high + 1] - times[i] <= halfWidth)
                high++;

            // Симетричне вікно — щоб згладжування не зсувало дистанцію вперед або назад.
            var reach = Math.Min(i - low, high - i);
            if (reach == 0)
                continue;

            var sum = 0.0;
            for (var j = i - reach; j <= i + reach; j++)
                sum += raw[j];
            distances[i] = sum / (2 * reach + 1);
        }
    }

    /// <summary>Гарантує FR-5: функція «час → дистанція» монотонно неспадна, фініш = кінець треку.</summary>
    private static void EnforceMonotonicity(double[] distances, double trackLength)
    {
        var running = 0.0;
        for (var i = 0; i < distances.Length; i++)
        {
            running = Math.Clamp(Math.Max(running, distances[i]), 0, trackLength);
            distances[i] = running;
        }

        distances[^1] = trackLength;
    }

    private static double[] ComputeSpeeds(double[] distances, double[] times)
    {
        var speeds = new double[distances.Length];
        for (var i = 0; i < distances.Length; i++)
        {
            var from = Math.Max(0, i - SpeedWindowRecords);
            var to = Math.Min(distances.Length - 1, i + SpeedWindowRecords);
            var dt = times[to] - times[from];
            speeds[i] = dt > 0 ? Math.Max(0, (distances[to] - distances[from]) / dt) : 0;
        }

        return speeds;
    }

    /// <summary>Висота береться з треку зі зсувом, що вирівнює старт з Оригіналом (FR-6).</summary>
    private static double ComputeAltitudeOffset(OriginalActivity original, ReferenceTrack track)
    {
        var originalStart = original.Records.FirstOrDefault(r => r.Altitude is not null)?.Altitude;
        if (originalStart is null || track.StartAltitude is null)
            return 0;
        return originalStart.Value - track.StartAltitude.Value;
    }

    /// <summary>
    /// Нульова потужність, яку пристрій вивів із зупиненої GPS-дистанції, не переноситься (FR-6).
    /// Критерій локальний: власна дистанція пристрою не зросла за останні 10 с.
    /// </summary>
    private static ushort? KeepPower(IReadOnlyList<ActivityRecord> records, int index, double[] times, ushort? power)
    {
        if (power is not 0)
            return power;

        var current = records[index].Distance;
        if (current is null)
            return power;

        for (var j = index - 1; j >= 0; j--)
        {
            if (times[index] - times[j] > DeviceDistanceStallWindowSeconds)
                break;
            if (records[j].Distance is { } earlier && current.Value - earlier > 0.5)
                return power;
        }

        return null;
    }

    private static int UpperBound(double[] sorted, double value)
    {
        var index = Array.BinarySearch(sorted, value);
        if (index < 0)
            return ~index;
        while (index < sorted.Length && sorted[index] <= value)
            index++;
        return index;
    }
}
