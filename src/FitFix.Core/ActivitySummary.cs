using FitFix.Core.Diagnostics;

namespace FitFix.Core;

/// <summary>
/// Everything one stretch of the repaired activity states about itself: the activity as a whole,
/// or one lap of it (AD-16). Every value is settled when the summary is built — there is no
/// computed property here and there must never be one, because a value a reader derives on the
/// way out is a second owner of that number, and the FIT writer, the report and the API DTO would
/// each derive it their own way (AD-14).
/// <para>
/// Averages are taken over <em>moving</em> time and over the records the athlete was moving for:
/// a wait at a traffic light neither dilutes the average heart rate nor lengthens the denominator
/// of the average speed, which is how Strava reads the same file (FR-5, AD-7). A field is
/// <c>null</c> when the stretch holds nothing to state it from — no heart rate was recorded, no
/// altitude came with the track, or the whole lap was a pause — because absent and zero are
/// different claims about an activity.
/// </para>
/// </summary>
public sealed record IntervalSummary
{
    /// <summary>When the stretch began and ended — the lap's own boundaries, or the activity's.</summary>
    public required DateTime Start { get; init; }

    /// <inheritdoc cref="Start"/>
    public required DateTime End { get; init; }

    /// <summary>
    /// The records the boundaries fall on, so a reader that needs the position the stretch was
    /// opened and closed at reads it at the very same index the distance was measured across
    /// (AD-24). Two adjacent laps share the record between them: it closes the earlier one and
    /// opens the later, which is what makes the distances telescope to the activity's total
    /// instead of counting a metre twice or losing it.
    /// </summary>
    public required int FirstRecord { get; init; }

    /// <inheritdoc cref="FirstRecord"/>
    public required int LastRecord { get; init; }

    /// <summary>Metres covered between those two records (FR-6).</summary>
    public required double DistanceMeters { get; init; }

    /// <summary>The stretch's own span on the clock, pauses included (FR-6).</summary>
    public required double ElapsedSeconds { get; init; }

    /// <summary>
    /// That span less the pauses inside it — the <c>total_timer_time</c> the file states (FR-5,
    /// FR-6). Zero for a lap that was nothing but standing still.
    /// </summary>
    public required double MovingSeconds { get; init; }

    /// <summary>
    /// <see cref="DistanceMeters"/> over <see cref="MovingSeconds"/>, and <c>null</c> when nothing
    /// moved: a stretch with no moving second has no average speed, and writing zero would state
    /// a speed the athlete was never at.
    /// </summary>
    public required double? AverageSpeedMetersPerSecond { get; init; }

    /// <summary>
    /// The fastest plausible moving second of the stretch, from the repaired speed series, and
    /// <c>null</c> when the stretch holds no such record. Records inside a pause are skipped, so
    /// the maximum is a speed the athlete actually ran at; so is a record faster than the sport's
    /// own cap in <see cref="AlignmentOptions.MaxSpeedMetersPerSecond"/>, because the forced finish
    /// of AD-8 pays a whole shortfall at the last record and that surplus is not a sprint.
    /// </summary>
    public required double? MaximumSpeedMetersPerSecond { get; init; }

    /// <summary>
    /// The pace, s/km — moving seconds over kilometres covered, the same two numbers the file
    /// states. <c>null</c> when there is no pace to state: nothing covered, or nothing moving.
    /// Settled here so that no shell has to choose a denominator for itself (AD-16).
    /// </summary>
    public required double? PaceSecondsPerKilometer { get; init; }

    /// <summary>
    /// Average and maximum heart rate over the moving records of the stretch. A heart rate of zero
    /// is a dropped reading rather than a measurement and counts for neither.
    /// </summary>
    public required byte? AverageHeartRate { get; init; }

    /// <inheritdoc cref="AverageHeartRate"/>
    public required byte? MaximumHeartRate { get; init; }

    /// <summary>
    /// Average and maximum power over the moving records. Zero counts here — a cyclist freewheels
    /// at zero watts and that is part of their average — but the zero the device derived from a
    /// dead GPS is already gone from the series by then (FR-6).
    /// </summary>
    public required ushort? AveragePower { get; init; }

    /// <inheritdoc cref="AveragePower"/>
    public required ushort? MaximumPower { get; init; }

    /// <summary>
    /// Average and maximum cadence over the moving records. A cadence of zero is standing still
    /// rather than a measurement of running, so it counts for neither.
    /// </summary>
    public required byte? AverageCadence { get; init; }

    /// <inheritdoc cref="AverageCadence"/>
    public required byte? MaximumCadence { get; init; }

    /// <summary>
    /// The sum of the rises and the sum of the falls in the repaired altitude series across the
    /// stretch — the climb and the descent, not the net change, and neither is subtracted from the
    /// other. The altitude comes from the reference track, which is already smooth, so no
    /// hysteresis is applied: a threshold here would be a tuning number living outside
    /// <see cref="AlignmentOptions"/> (AD-10). Both are <c>null</c> when no record of the stretch
    /// carried an altitude at all — a track without elevation states no climb, rather than a flat
    /// zero the user would read as a measurement.
    /// </summary>
    public required double? ElevationGainMeters { get; init; }

    /// <inheritdoc cref="ElevationGainMeters"/>
    public required double? ElevationLossMeters { get; init; }
}

/// <summary>
/// The one owner of every summary number the repaired activity states (AD-16): the activity, each
/// of its laps, the paces and the measured share, computed once in the core and handed over as
/// data. The FIT writer transcribes these values, the report carries them and the API DTO copies
/// them across; none of them performs arithmetic over a series or chooses a denominator, so FitFix
/// and Strava cannot show two different average heart rates for one file.
/// </summary>
public sealed record ActivitySummary
{
    /// <summary>The whole activity, as the <c>session</c> message will state it.</summary>
    public required IntervalSummary Total { get; init; }

    /// <summary>
    /// One summary per lap the activity will carry, in time order — the Original's own laps, or a
    /// single lap covering everything when it had none (AD-24). Their distances, moving seconds
    /// and elevation gains sum to <see cref="Total"/>, because adjacent laps share the record
    /// between them. Empty only for an activity too short to partition, which the writer refuses
    /// outright (AD-18).
    /// </summary>
    public required IReadOnlyList<IntervalSummary> Laps { get; init; }

    /// <summary>
    /// The share of the activity whose position was measured by anchors rather than interpolated
    /// between them (FR-4): the measured <see cref="Alignment.Zones"/> summed over the elapsed
    /// time. A sum and not a prefix — an activity whose GPS returned for its last kilometre has
    /// that kilometre among the measured ones. Settled here, with the elapsed time it is a share
    /// of, so a shell never divides one of the two by something else.
    /// </summary>
    public required double MeasuredFraction { get; init; }

    /// <summary>
    /// The pace the athlete saw before the repair, s/km: the distance the device itself recorded
    /// over <em>the Original's own moving time</em> — its span less its own pauses, the time the
    /// watch was counting. Neither number comes from the repaired series, because this is the
    /// "before" of the before-and-after and has to stay what Strava showed. <c>null</c> when the
    /// device recorded no distance at all.
    /// </summary>
    public required double? OriginalPaceSecondsPerKilometer { get; init; }

    /// <summary>
    /// Measures the repaired series. <paramref name="records"/> is the repaired activity and
    /// <paramref name="original"/> the cleaned Original it was built from: one carries the
    /// geometry, the distances and the speeds, the other the pauses, the laps and the device's own
    /// distance, and a record index means the same thing in both — which is why they are measured
    /// together here rather than anywhere a reader of the result might try to do it again.
    /// <paramref name="alignment"/> says how much of the activity was measured and
    /// <paramref name="options"/> holds the sport's speed cap, the only threshold this measurement
    /// reads (AD-10).
    /// <para>
    /// A pause list that cannot be true is struck out first by
    /// <see cref="OriginalActivity.WithoutImpossiblePauses"/>, the one owner of that disbelief, so
    /// the averages and the durations here agree with the rest of the pipeline.
    /// </para>
    /// </summary>
    /// <exception cref="MalformedArgumentException">
    /// The series holds no record (<see cref="DiagnosticCodes.SummaryWithoutRecords"/>), or it is
    /// not the one the activity was measured from
    /// (<see cref="DiagnosticCodes.SummarySeriesMismatch"/>).
    /// </exception>
    public static ActivitySummary Of(
        IReadOnlyList<RepairedRecord> records,
        OriginalActivity original,
        Alignment alignment,
        AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(options);

        if (records.Count == 0)
            throw new MalformedArgumentException(
                DiagnosticCodes.SummaryWithoutRecords,
                $"There is no record to summarise ('{nameof(records)}').");

        // The two series describe the same activity at the same timestamps, so an index is read in
        // both. A mismatch would silently measure one lap's heart rate across another's records.
        if (records.Count != original.Records.Count)
            throw new MalformedArgumentException(
                DiagnosticCodes.SummarySeriesMismatch,
                $"The repaired series has {records.Count} records and the Original "
                    + $"{original.Records.Count}: they must be the same activity.");

        var activity = original.WithoutImpossiblePauses();

        var partition = activity.LapPartition();
        var laps = new List<IntervalSummary>(partition.Count);
        var first = 0;

        for (var index = 0; index < partition.Count; index++)
        {
            var interval = partition[index];

            // Each lap ends on the record the next one begins at; the last ends on the last
            // record. The bounds degrade rather than throw: a boundary that would land on or
            // before the previous one is moved a record forward, and one past the series is pulled
            // back to its end, which yields a lap of no distance where the partition has already
            // run out of records instead of an exception the user cannot act on.
            var last = index == partition.Count - 1
                ? records.Count - 1
                : Math.Min(
                    Math.Max(activity.NearestRecordIndex(interval.End), first + 1),
                    records.Count - 1);

            laps.Add(Measure(
                records,
                activity,
                options,
                interval,
                first,
                // The shared record belongs to the earlier lap's sensor window and not to both:
                // the distances telescope across it, so counting its heart rate in two laps would
                // be the one value paid for twice.
                index == 0 ? first : first + 1,
                last));

            first = last;
        }

        var total = Measure(
            records,
            activity,
            options,
            new LapInterval(records[0].Timestamp, records[^1].Timestamp),
            0,
            0,
            records.Count - 1);

        return new ActivitySummary
        {
            Total = total,
            Laps = laps,
            MeasuredFraction = total.ElapsedSeconds > 0
                ? MeasuredSeconds(alignment.Zones) / total.ElapsedSeconds
                : 0,
            OriginalPaceSecondsPerKilometer = Pace(
                original.RecordedDistance ?? 0,
                activity.MovingSecondsFromStart(activity.ElapsedSeconds))
        };
    }

    /// <summary>
    /// How many seconds of the activity the position was measured over: the measured zones added
    /// up. Every one of them, because the trust the athlete is shown is the sum of the stretches
    /// that were measured and not the first of them (FR-8).
    /// </summary>
    private static double MeasuredSeconds(IReadOnlyList<TrustZone> zones)
    {
        var measured = 0.0;
        foreach (var zone in zones)
        {
            if (zone.Kind == ZoneKind.Measured)
                measured += zone.ToSeconds - zone.FromSeconds;
        }

        return measured;
    }

    /// <summary>
    /// One stretch, in one pass. <paramref name="firstSensorRecord"/> is where the sensor window
    /// opens, which is one record after <paramref name="firstRecord"/> for every lap but the
    /// first; the distance and the elevation are measured from <paramref name="firstRecord"/>
    /// itself, so both telescope across the shared record.
    /// </summary>
    private static IntervalSummary Measure(
        IReadOnlyList<RepairedRecord> records,
        OriginalActivity activity,
        AlignmentOptions options,
        LapInterval interval,
        int firstRecord,
        int firstSensorRecord,
        int lastRecord)
    {
        var elapsed = (interval.End - interval.Start).TotalSeconds;
        var moving = activity.MovingSecondsFromStart(activity.SecondsFromStart(interval.End))
            - activity.MovingSecondsFromStart(activity.SecondsFromStart(interval.Start));
        moving = moving > 0 ? moving : 0;

        var distance = records[lastRecord].Distance - records[firstRecord].Distance;

        // Sums over a long ride: a power near the top of its `ushort` across more than about
        // thirty-two thousand records overruns an `int`, and the average would wrap to a number
        // no athlete produced.
        long heartRateSum = 0, powerSum = 0, cadenceSum = 0;
        int heartRates = 0, powers = 0, cadences = 0;
        int maximumHeartRate = 0, maximumPower = 0, maximumCadence = 0;
        double? maximumSpeed = null;

        for (var index = firstSensorRecord; index <= lastRecord; index++)
        {
            var record = records[index];

            // The record that opens a pause and the one that ends it count as part of it: they sit
            // where the athlete stood still, and their sensor values describe standing rather than
            // running (AD-7).
            if (activity.IsPaused(record.Timestamp))
                continue;

            // A second faster than the sport can go is the forced finish or a defect upstream, not
            // the athlete's fastest second (AD-8, AD-10).
            if (record.Speed <= options.MaxSpeedMetersPerSecond
                && (maximumSpeed is null || record.Speed > maximumSpeed))
            {
                maximumSpeed = record.Speed;
            }

            if (record.HeartRate is > 0 and { } heartRate)
            {
                heartRateSum += heartRate;
                heartRates++;
                maximumHeartRate = Math.Max(maximumHeartRate, heartRate);
            }

            if (record.Power is { } power)
            {
                powerSum += power;
                powers++;
                maximumPower = Math.Max(maximumPower, power);
            }

            if (record.Cadence is > 0 and { } cadence)
            {
                cadenceSum += cadence;
                cadences++;
                maximumCadence = Math.Max(maximumCadence, cadence);
            }
        }

        var (gain, loss) = Elevation(records, firstRecord, lastRecord);

        return new IntervalSummary
        {
            Start = interval.Start,
            End = interval.End,
            FirstRecord = firstRecord,
            LastRecord = lastRecord,
            DistanceMeters = distance,
            ElapsedSeconds = elapsed,
            MovingSeconds = moving,
            AverageSpeedMetersPerSecond = moving > 0 ? distance / moving : null,
            MaximumSpeedMetersPerSecond = maximumSpeed,
            PaceSecondsPerKilometer = Pace(distance, moving),
            AverageHeartRate = heartRates > 0 ? (byte)Math.Round((double)heartRateSum / heartRates) : null,
            MaximumHeartRate = heartRates > 0 ? (byte)maximumHeartRate : null,
            AveragePower = powers > 0 ? (ushort)Math.Round((double)powerSum / powers) : null,
            MaximumPower = powers > 0 ? (ushort)maximumPower : null,
            AverageCadence = cadences > 0 ? (byte)Math.Round((double)cadenceSum / cadences) : null,
            MaximumCadence = cadences > 0 ? (byte)maximumCadence : null,
            ElevationGainMeters = gain,
            ElevationLossMeters = loss
        };
    }

    /// <summary>
    /// Seconds per kilometre, and <c>null</c> when there is no pace to state: a stretch that
    /// covered nothing, or one that moved for no second, has no pace, and a zero or an infinity
    /// there is a number a shell would format and show.
    /// </summary>
    private static double? Pace(double distanceMeters, double movingSeconds) =>
        distanceMeters > 0 && movingSeconds > 0
            ? movingSeconds / (distanceMeters / 1000.0)
            : null;

    /// <summary>
    /// The sum of the rises and the sum of the falls between consecutive altitudes of the stretch.
    /// A record without an altitude is stepped over rather than read as a descent to zero, and
    /// nothing is subtracted: a loop that climbs and returns has a climb and a descent, not a net
    /// change of nothing.
    /// <para>
    /// The walk starts from the last altitude known at or before <paramref name="firstRecord"/>,
    /// not from the first one inside the stretch: a lap boundary that falls on a record carrying
    /// no altitude would otherwise lose the rise across it — counted by neither the lap that ends
    /// there nor the one that begins — and the laps would stop summing to the activity.
    /// </para>
    /// Both are null when no record of the stretch carried an altitude at all.
    /// </summary>
    private static (double? Gain, double? Loss) Elevation(
        IReadOnlyList<RepairedRecord> records,
        int firstRecord,
        int lastRecord)
    {
        double? previous = null;
        for (var index = firstRecord; index >= 0; index--)
        {
            if (records[index].Altitude is { } seed)
            {
                previous = seed;
                break;
            }
        }

        var gain = 0.0;
        var loss = 0.0;
        var measured = false;

        for (var index = firstRecord; index <= lastRecord; index++)
        {
            if (records[index].Altitude is not { } altitude)
                continue;

            measured = true;

            if (previous is { } earlier)
            {
                if (altitude > earlier)
                    gain += altitude - earlier;
                else
                    loss += earlier - altitude;
            }

            previous = altitude;
        }

        return measured ? (gain, loss) : (null, null);
    }
}
