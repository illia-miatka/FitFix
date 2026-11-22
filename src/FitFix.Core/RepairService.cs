using FitFix.Core.Diagnostics;

namespace FitFix.Core;

/// <summary>
/// Ядро FitFix: бере час і сенсори з Оригіналу, геометрію з Еталонного треку
/// і будує Відновлену активність (PRD FR-5, FR-6).
/// </summary>
public static class RepairService
{
    /// <summary>
    /// Repairs the activity with the thresholds that suit its own sport. The overload exists so
    /// that a caller with nothing to say about the thresholds says so in the signature, instead
    /// of omitting an optional argument and silently getting different numbers (AD-10).
    /// </summary>
    public static RepairResult Repair(OriginalActivity original, ReferenceTrack track)
    {
        ArgumentNullException.ThrowIfNull(original);

        return Repair(original, track, AlignmentOptions.ForSport(original.Sport));
    }

    /// <summary>Repairs the activity with the given thresholds, the only tuning source (AD-10).</summary>
    public static RepairResult Repair(OriginalActivity original, ReferenceTrack track, AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(options);

        // No elapsed time means no time axis to spread the track over: the interpolation would
        // be left with a single anchor. User data, so it refuses with a code (AD-18).
        if (original.ElapsedSeconds <= 0)
            throw new MalformedArgumentException(
                DiagnosticCodes.ActivityWithoutElapsedTime,
                $"The activity's records span no time at all ('{nameof(original)}').");

        // A pause list that leaves the activity no moving second cannot be true, and it is struck
        // out here — before a single moving second is computed — so the interpolation, the
        // smoothing, the summary and the report all read one answer (AD-7). Without it the
        // distance would be spread over a measure that never advances, and the whole track would
        // fall due at the finish.
        var believable = original.WithoutImpossiblePauses();

        // Cleaning comes next, so the compatibility check (FR-3), the anchor search and every
        // series a shell will show read the same cleaned original (AD-25). It is done here and
        // nowhere else, which is how every entry point inherits it.
        var (cleaned, discardedPositions) = GpsCleaner.Clean(believable, options);

        // The alignment works from the cleaned series; the stall detector inside it reads the
        // original as it arrived, because blanking a burst would otherwise look like a stall.
        var alignment = Aligner.Align(cleaned, believable, track, options);

        var records = cleaned.Records;
        var times = new double[records.Count];
        var movingTimes = new double[records.Count];
        var paused = new bool[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            times[i] = cleaned.SecondsFromStart(records[i]);
            movingTimes[i] = cleaned.MovingSecondsFromStart(times[i]);
            paused[i] = cleaned.IsPaused(records[i].Timestamp);
        }

        // Distance is spread over moving time, never over the clock: inside a pause the measure
        // stops advancing, so a record there keeps the distance of the record that opened the
        // pause and the wait earns no metres (FR-5, AD-7). With no pauses this is the identity.
        var anchorMovingTimes = alignment.Anchors
            .Select(anchor => cleaned.MovingSecondsFromStart(anchor.TimeSeconds))
            .ToArray();

        var distances = InterpolateDistances(movingTimes, anchorMovingTimes, alignment.Anchors);
        Smooth(distances, times, PauseBlocks(cleaned), alignment.MeasuredUntilSeconds, options);
        EnforceMonotonicity(distances, times, track.Length, options);

        // Speed is a distance over the time the athlete was moving for, never over the clock, and
        // a record a pause covers states no speed at all (FR-5, AD-7).
        var speeds = ComputeSpeeds(distances, movingTimes, paused, options);
        var altitudeOffset = ComputeAltitudeOffset(cleaned, track);

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
                Power = KeepPower(records, i, times, source.Power, options),
                Cadence = source.Cadence,
                Temperature = source.Temperature
            });
        }

        // Every summary number is settled here, once, for the activity and for each of its laps
        // (AD-16). The report below and the FIT writer both read these values; neither computes
        // one of its own, which is what keeps the file and the preview from stating two different
        // average heart rates for the same run.
        var summary = ActivitySummary.Of(repaired, cleaned, alignment, options);
        var total = summary.Total;

        var report = new RepairReport
        {
            // The device's own distance field, which cleaning never touches: the "before" number
            // has to stay what the user saw in Strava, and stay absent when the watch recorded
            // no distance at all.
            OriginalDistanceMeters = original.RecordedDistance,
            RepairedDistanceMeters = total.DistanceMeters,
            TrackLengthMeters = track.Length,
            // The span of the series the file was written from, so the report cannot state one
            // elapsed time while the session message states another.
            ElapsedSeconds = total.ElapsedSeconds,
            // Both from the same activity, the one whose pauses the whole pipeline believed:
            // taking the moving seconds from the summary and the paused ones from a list it
            // disbelieved would have the two add up past the elapsed time.
            PausedSeconds = cleaned.PausedSeconds,
            MovingSeconds = total.MovingSeconds,
            MeasuredUntilSeconds = alignment.MeasuredUntilSeconds,
            MeasuredFraction = summary.MeasuredFraction,
            OriginalPaceSecondsPerKm = summary.OriginalPaceSecondsPerKilometer,
            RepairedPaceSecondsPerKm = total.PaceSecondsPerKilometer,
            AnchorCount = alignment.Anchors.Count,
            GpsStallAtSeconds = alignment.GpsStallAtSeconds,
            DiscardedPositionCount = discardedPositions,
            // The laps the writer will transcribe, counted here so the report and the file agree
            // on how many there are (AD-24).
            LapCount = summary.Laps.Count,
            ElevationGainMeters = total.ElevationGainMeters,
            ElevationLossMeters = total.ElevationLossMeters,
            Warnings = WarningsOf(alignment, discardedPositions)
        };

        return new RepairResult
        {
            Records = repaired,
            Report = report,
            Summary = summary,
            Alignment = alignment,
            CleanedOriginal = cleaned,
            Sport = original.Sport
        };
    }


    /// <summary>
    /// The codes the report carries: the alignment's own, preceded by <c>POSITIONS_DISCARDED</c>
    /// when cleaning threw anything away (AD-18). Cleaning runs before the alignment, so its code
    /// comes first, and a shell can say why the map lost a point instead of leaving
    /// <c>NO_ORIGINAL_GPS</c> as the only hint after a full discard.
    /// </summary>
    private static IReadOnlyList<string> WarningsOf(Alignment alignment, int discardedPositions) =>
        discardedPositions > 0
            ? [DiagnosticCodes.PositionsDiscarded, .. alignment.Warnings]
            : alignment.Warnings;

    /// <summary>
    /// Linear interpolation of distance between anchors, over the measure
    /// <paramref name="times"/> and <paramref name="anchorTimes"/> are stated in — moving
    /// seconds, so that a pause holds the distance flat (FR-5). Both the bucket and the fraction
    /// inside it are taken from <paramref name="anchorTimes"/>: an anchor's own
    /// <see cref="Anchor.TimeSeconds"/> is elapsed time, and mixing the two measures would
    /// divide moving seconds by an elapsed span and leave every record after a pause short.
    /// <paramref name="anchors"/> is read for distance alone.
    /// </summary>
    private static double[] InterpolateDistances(
        double[] times,
        double[] anchorTimes,
        IReadOnlyList<Anchor> anchors)
    {
        var result = new double[times.Length];

        for (var i = 0; i < times.Length; i++)
        {
            var index = UpperBound(anchorTimes, times[i]) - 1;
            index = Math.Clamp(index, 0, anchors.Count - 2);

            var span = anchorTimes[index + 1] - anchorTimes[index];
            var u = span > 0 ? (times[i] - anchorTimes[index]) / span : 0;
            result[i] = anchors[index].TrackDistance
                + u * (anchors[index + 1].TrackDistance - anchors[index].TrackDistance);
        }

        return result;
    }

    /// <summary>
    /// Which stretch between pause boundaries each record falls in, so a smoothing window can be
    /// clipped at the nearest boundary instead of carrying movement into a pause (AD-21). A
    /// boundary is a pause's own start or end, clipped to the activity: the record that opened a
    /// pause goes with the pause — it already holds the pause's distance — and the record that
    /// ends one opens the stretch that follows. An activity with no pauses is one stretch, which
    /// is what leaves the numbers of a pause-free file exactly where they were.
    /// </summary>
    private static int[] PauseBlocks(OriginalActivity activity)
    {
        var records = activity.Records;
        var blocks = new int[records.Count];
        if (activity.Pauses.Count == 0)
            return blocks;

        for (var i = 0; i < records.Count; i++)
            blocks[i] = activity.PauseSegment(records[i].Timestamp);

        return blocks;
    }

    /// <summary>
    /// Симетричне ковзне середнє: вузьке у Надійній зоні, широке в Розрахованій,
    /// де лінійна інтерполяція дає злами на Якорях. The window is clipped at pause boundaries,
    /// so the records before a pause are smoothed against their own side of it and no record
    /// inside one gains distance from the movement outside (AD-21). Monotonicity is verified
    /// after this, never instead of it (AD-8).
    /// </summary>
    private static void Smooth(
        double[] distances,
        double[] times,
        int[] blocks,
        double measuredUntil,
        AlignmentOptions options)
    {
        var raw = (double[])distances.Clone();

        for (var i = 0; i < distances.Length; i++)
        {
            var halfWidth = times[i] <= measuredUntil
                ? options.SmoothingSeconds
                : options.EstimatedZoneSmoothingSeconds;

            var low = i;
            while (low > 0 && times[i] - times[low - 1] <= halfWidth && blocks[low - 1] == blocks[i])
                low--;

            var high = i;
            while (high < distances.Length - 1 && times[high + 1] - times[i] <= halfWidth && blocks[high + 1] == blocks[i])
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

    /// <summary>
    /// Гарантує FR-5: функція «час → дистанція» монотонно неспадна, фініш = кінець треку.
    /// The finish is pinned to the track length, so whatever the series was short by is paid at
    /// the last record — which is how a wrongly scaled interpolation can look like a correct
    /// total. The payment is therefore bounded by what the athlete could cover in that last
    /// interval: anything more is a defect upstream, and it says so instead of shipping a
    /// teleport at the finish line (AD-8).
    /// </summary>
    private static void EnforceMonotonicity(
        double[] distances,
        double[] times,
        double trackLength,
        AlignmentOptions options)
    {
        var running = 0.0;
        for (var i = 0; i < distances.Length; i++)
        {
            running = Math.Clamp(Math.Max(running, distances[i]), 0, trackLength);
            distances[i] = running;
        }

        var last = distances.Length - 1;
        var shortfall = trackLength - distances[last];
        var interval = last > 0 ? times[last] - times[last - 1] : 0;
        var plausible = options.MaxSpeedMetersPerSecond * interval + options.SpeedToleranceMeters;

        // Derived from the activity and the track the user offered, so it carries a declared code
        // rather than a bare exception a shell could only report as an internal error (AD-18).
        if (shortfall > plausible)
            throw new MalformedArgumentException(
                DiagnosticCodes.DistanceSeriesImplausible,
                $"The repaired series ends {shortfall:F1} m short of the track, and the last " +
                $"{interval:F0} s cannot cover more than {plausible:F1} m: the distance series is wrong, " +
                "not the finish.");

        distances[last] = trackLength;
    }

    /// <summary>
    /// The repaired speed of each record, over a window of
    /// <see cref="AlignmentOptions.SpeedWindowHalfWidthRecords"/> records either side.
    /// <paramref name="movingTimes"/> is moving seconds rather than the clock, so a window that
    /// reaches across a pause divides its metres by the seconds the athlete was actually moving
    /// and the records beside a pause keep the athlete's own pace (FR-5, AD-7).
    /// <para>
    /// A record a pause covers states a speed of zero, whatever its window spans. The window of a
    /// record just inside a pause still reaches the moving records outside it, and its moving
    /// seconds collapse to the one or two the window has left there — a full window of metres over
    /// a fraction of the time, which reads as the athlete sprinting while standing at a light.
    /// Zero is the only honest number for a second nothing moved in.
    /// </para>
    /// </summary>
    private static double[] ComputeSpeeds(
        double[] distances,
        double[] movingTimes,
        bool[] paused,
        AlignmentOptions options)
    {
        var speeds = new double[distances.Length];
        for (var i = 0; i < distances.Length; i++)
        {
            if (paused[i])
                continue;

            var from = Math.Max(0, i - options.SpeedWindowHalfWidthRecords);
            var to = Math.Min(distances.Length - 1, i + options.SpeedWindowHalfWidthRecords);
            var dt = movingTimes[to] - movingTimes[from];
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
    /// Zero power the device derived from a frozen GPS distance is not carried over (FR-6).
    /// The criterion is local: the device's own distance did not grow by
    /// <see cref="AlignmentOptions.DeviceDistanceGrowthMeters"/> within
    /// <see cref="AlignmentOptions.DeviceDistanceStallWindowSeconds"/>.
    /// </summary>
    private static ushort? KeepPower(
        IReadOnlyList<ActivityRecord> records,
        int index,
        double[] times,
        ushort? power,
        AlignmentOptions options)
    {
        if (power is not 0)
            return power;

        var current = records[index].Distance;
        if (current is null)
            return power;

        for (var j = index - 1; j >= 0; j--)
        {
            if (times[index] - times[j] > options.DeviceDistanceStallWindowSeconds)
                break;
            if (records[j].Distance is { } earlier && current.Value - earlier > options.DeviceDistanceGrowthMeters)
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
