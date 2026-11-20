using Dynastream.Fit;
using FitFix.Core.Diagnostics;
using FitDateTime = Dynastream.Fit.DateTime;

namespace FitFix.Core.Fit;

/// <summary>
/// Записує Відновлену активність у FIT так, щоб її прийняли Strava й Garmin Connect:
/// file_id → event(start) → records → event(stop_all) → lap × M → session → activity (PRD FR-6,
/// AD-19). The laps are the Original's own (AD-24): the writer transcribes the partition the
/// model derived and never invents a boundary of its own.
/// </summary>
public static class FitActivityWriter
{
    /// <summary>
    /// One lap as it will be written: the interval the model derived, the repaired records its
    /// boundaries fall on, and the three numbers read off them. Assembled once, before anything
    /// is encoded, so that the laps cannot disagree with each other about where a boundary is —
    /// a lap's end index is the next lap's start index, which is what makes the distances
    /// telescope to the activity's total instead of counting a metre twice or losing it (FR-6).
    /// </summary>
    private readonly record struct WrittenLap(
        LapInterval Interval,
        int FirstRecord,
        int FirstSensorRecord,
        int LastRecord,
        float Elapsed,
        float Timer,
        float Distance,
        ushort MessageIndex);

    /// <summary>
    /// The most laps a FIT file can carry. <c>message_index</c> is a 16-bit field whose top bits
    /// are reserved — one marks a selected message, three more are reserved outright — leaving
    /// twelve bits, so 4096 is the last index a reader can be relied on to see. A file past that
    /// is refused with a code rather than written with indices that collide (AD-18).
    /// </summary>
    private const int MaxLaps = 4096;

    /// <summary>
    /// Writes the repaired activity. <paramref name="utcOffsetSeconds"/> is stated by the caller
    /// rather than defaulted, so no entry point can inherit a silent zero (AD-10); Story 1.10
    /// takes the real offset from the original activity.
    /// </summary>
    public static void Write(Stream stream, RepairResult result, int utcOffsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(result);

        var records = result.Records;

        // Derived from user data, so it carries a code rather than surfacing as an argument error (AD-18).
        if (records.Count < 2)
            throw new MalformedArgumentException(
                DiagnosticCodes.ActivityTooFewRecords,
                $"Writing a FIT activity needs at least 2 records ('{nameof(result)}').");

        var start = new FitDateTime(records[0].Timestamp);
        var end = new FitDateTime(records[^1].Timestamp);
        var elapsed = (float)(records[^1].Timestamp - records[0].Timestamp).TotalSeconds;

        // Timer time is elapsed time minus what the athlete stood still for (FR-6, AD-7). The
        // pauses come from the Original the repair worked from, the one model of a pause there is;
        // elapsed time stays the Original's own span (±1 s). Pauses that swallow the whole
        // activity are an event stream that cannot be true — a timer time of zero beside a full
        // distance reads as an infinite pace — so they are ignored rather than written.
        var paused = result.CleanedOriginal.PausedSeconds;
        var pausesAreTrustworthy = paused < elapsed;
        var timer = pausesAreTrustworthy ? (float)(elapsed - paused) : elapsed;
        var totalDistance = (float)records[^1].Distance;

        // The laps the Original recorded, as a partition of the activity (AD-24). An Original with
        // no lap message yields exactly one lap covering everything, which is the file this writer
        // produced before laps existed. The laps are told whether the pause list can be believed,
        // rather than each deciding for itself: a per-lap fallback would leave the laps' timer
        // times summing to the elapsed time while the session states a shorter one.
        var laps = LapsOf(result, pausesAreTrustworthy);

        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(stream);

        encoder.Write(BuildFileId(end));
        encoder.Write(BuildTimerEvent(start, EventType.Start));

        foreach (var record in records)
            encoder.Write(BuildRecord(record));

        encoder.Write(BuildTimerEvent(end, EventType.StopAll));

        for (var index = 0; index < laps.Count; index++)
            encoder.Write(BuildLap(result, laps[index], lastOfTheActivity: index == laps.Count - 1));

        encoder.Write(BuildSession(result, start, end, elapsed, timer, totalDistance, laps.Count));
        encoder.Write(BuildActivity(end, timer, utcOffsetSeconds));

        encoder.Close();
    }

    /// <inheritdoc cref="Write"/>
    public static void WriteFile(string path, RepairResult result, int utcOffsetSeconds)
    {
        using var stream = System.IO.File.Create(path);
        Write(stream, result, utcOffsetSeconds);
    }

    /// <inheritdoc cref="Write"/>
    public static byte[] WriteToArray(RepairResult result, int utcOffsetSeconds)
    {
        using var buffer = new MemoryStream();
        Write(buffer, result, utcOffsetSeconds);
        return buffer.ToArray();
    }

    private static FileIdMesg BuildFileId(FitDateTime created)
    {
        var mesg = new FileIdMesg();
        mesg.SetType(Dynastream.Fit.File.Activity);
        mesg.SetManufacturer(Manufacturer.Development);
        mesg.SetProduct(0);
        mesg.SetSerialNumber(0x12345678);
        mesg.SetTimeCreated(created);
        return mesg;
    }

    private static EventMesg BuildTimerEvent(FitDateTime timestamp, EventType type)
    {
        var mesg = new EventMesg();
        mesg.SetTimestamp(timestamp);
        mesg.SetEvent(Event.Timer);
        mesg.SetEventType(type);
        return mesg;
    }

    private static RecordMesg BuildRecord(RepairedRecord record)
    {
        var mesg = new RecordMesg();
        mesg.SetTimestamp(new FitDateTime(record.Timestamp));
        mesg.SetPositionLat(FitDecoder.ToSemicircles(record.Latitude));
        mesg.SetPositionLong(FitDecoder.ToSemicircles(record.Longitude));
        mesg.SetDistance((float)record.Distance);
        mesg.SetEnhancedSpeed((float)record.Speed);
        mesg.SetSpeed((float)record.Speed);

        if (record.Altitude is { } altitude)
        {
            mesg.SetEnhancedAltitude((float)altitude);
            mesg.SetAltitude((float)altitude);
        }

        if (record.HeartRate is { } heartRate)
            mesg.SetHeartRate(heartRate);
        if (record.Power is { } power)
            mesg.SetPower(power);
        if (record.Cadence is { } cadence)
            mesg.SetCadence(cadence);
        if (record.Temperature is { } temperature)
            mesg.SetTemperature(temperature);

        return mesg;
    }

    /// <summary>
    /// The laps to write, in time order with <c>message_index</c> counting from zero. Boundaries
    /// come from the model's partition — where the athlete pressed the button (AD-24) — while the
    /// distance of each lap is read off the repaired series at those boundaries, because the
    /// Original's own lap distance was measured by the GPS that failed. Elapsed time is the lap's
    /// own span, so the laps' elapsed times sum to the activity's; timer time is that span less
    /// the pauses that fall inside it (FR-5, AD-7), so the laps' timer times sum to the session's.
    /// A lap that is nothing but a pause therefore states a timer time of zero, which is what
    /// standing still for a whole lap is. When <paramref name="pausesAreTrustworthy"/> is false the
    /// pause list is ignored outright, exactly as the session ignores it.
    /// </summary>
    /// <remarks>
    /// The repaired series carries one record per record of <see cref="RepairResult.CleanedOriginal"/>,
    /// at the same timestamps, so a record index means the same thing in both — which is why the
    /// boundaries are resolved by the model's own <see cref="OriginalActivity.NearestRecordIndex"/>
    /// and the distances read out of <see cref="RepairResult.Records"/> at the result.
    /// </remarks>
    private static IReadOnlyList<WrittenLap> LapsOf(RepairResult result, bool pausesAreTrustworthy)
    {
        var original = result.CleanedOriginal;
        var records = result.Records;
        var partition = original.LapPartition();

        if (partition.Count > MaxLaps)
            throw new MalformedArgumentException(
                DiagnosticCodes.ActivityTooManyLaps,
                $"The activity has {partition.Count} laps, and a FIT file can index {MaxLaps} " +
                $"('{nameof(result)}').");

        var laps = new List<WrittenLap>(partition.Count);
        var first = 0;

        for (var index = 0; index < partition.Count; index++)
        {
            var interval = partition[index];

            // Each lap begins on the record the previous one ended on, so the laps chain through
            // the series by construction and the shared record cannot be read at two different
            // indices. The last lap always ends on the last record; the clamp on the others is
            // belt and braces — the partition already drops a cut no record separates from the
            // previous boundary — but it moves a boundary by one record rather than writing a lap
            // of no distance should that ever stop holding.
            var last = index == partition.Count - 1
                ? records.Count - 1
                : Math.Clamp(original.NearestRecordIndex(interval.End), first + 1, records.Count - 1);

            var elapsed = (float)(interval.End - interval.Start).TotalSeconds;
            var paused = pausesAreTrustworthy
                ? original.PausedSecondsBefore(original.SecondsFromStart(interval.End))
                    - original.PausedSecondsBefore(original.SecondsFromStart(interval.Start))
                : 0;

            laps.Add(new WrittenLap(
                interval,
                first,
                // The shared record belongs to the earlier lap's sensor window and not to both:
                // the distances telescope across it, so counting its heart rate in two laps would
                // be the one value paid for twice.
                index == 0 ? first : first + 1,
                last,
                elapsed,
                (float)Math.Max(0, elapsed - paused),
                (float)(records[last].Distance - records[first].Distance),
                (ushort)index));

            first = last;
        }

        return laps;
    }

    private static LapMesg BuildLap(RepairResult result, WrittenLap lap, bool lastOfTheActivity)
    {
        var mesg = new LapMesg();
        mesg.SetTimestamp(new FitDateTime(lap.Interval.End));
        mesg.SetStartTime(new FitDateTime(lap.Interval.Start));
        mesg.SetTotalElapsedTime(lap.Elapsed);
        mesg.SetTotalTimerTime(lap.Timer);
        mesg.SetTotalDistance(lap.Distance);
        mesg.SetSport(MapSport(result.Sport));
        mesg.SetEvent(Event.Lap);
        mesg.SetEventType(EventType.Stop);
        mesg.SetMessageIndex(lap.MessageIndex);

        // Why the lap ended, which Strava reads to tell a split the athlete pressed from an
        // automatic one. The last lap ends with the session; for the others the Original's own
        // trigger is not carried in the model, and a press is the honest reading of a boundary
        // somebody put there.
        mesg.SetLapTrigger(lastOfTheActivity ? LapTrigger.SessionEnd : LapTrigger.Manual);

        var records = result.Records;
        mesg.SetStartPositionLat(FitDecoder.ToSemicircles(records[lap.FirstRecord].Latitude));
        mesg.SetStartPositionLong(FitDecoder.ToSemicircles(records[lap.FirstRecord].Longitude));
        mesg.SetEndPositionLat(FitDecoder.ToSemicircles(records[lap.LastRecord].Latitude));
        mesg.SetEndPositionLong(FitDecoder.ToSemicircles(records[lap.LastRecord].Longitude));

        ApplyAverages(records, lap.FirstSensorRecord, lap.LastRecord, lap.Timer, lap.Distance,
            (speed, avgHr, maxHr, avgPower, maxPower, avgCadence) =>
            {
                mesg.SetAvgSpeed(speed);
                mesg.SetEnhancedAvgSpeed(speed);
                if (avgHr is not null) mesg.SetAvgHeartRate(avgHr);
                if (maxHr is not null) mesg.SetMaxHeartRate(maxHr);
                if (avgPower is not null) mesg.SetAvgPower(avgPower);
                if (maxPower is not null) mesg.SetMaxPower(maxPower);
                if (avgCadence is not null) mesg.SetAvgCadence(avgCadence);
            });

        return mesg;
    }

    private static SessionMesg BuildSession(
        RepairResult result,
        FitDateTime start,
        FitDateTime end,
        float elapsed,
        float timer,
        float distance,
        int lapCount)
    {
        var mesg = new SessionMesg();
        mesg.SetTimestamp(end);
        mesg.SetStartTime(start);
        mesg.SetTotalElapsedTime(elapsed);
        mesg.SetTotalTimerTime(timer);
        mesg.SetTotalDistance(distance);
        mesg.SetSport(MapSport(result.Sport));
        mesg.SetSubSport(SubSport.Generic);
        mesg.SetEvent(Event.Session);
        mesg.SetEventType(EventType.Stop);
        mesg.SetMessageIndex(0);
        mesg.SetFirstLapIndex(0);
        // How many lap messages precede this session. A count of one beside several laps is a file
        // that contradicts itself, and Strava reads this field to find them.
        mesg.SetNumLaps((ushort)lapCount);
        mesg.SetTrigger(SessionTrigger.ActivityEnd);

        var records = result.Records;
        mesg.SetStartPositionLat(FitDecoder.ToSemicircles(records[0].Latitude));
        mesg.SetStartPositionLong(FitDecoder.ToSemicircles(records[0].Longitude));

        ApplyAverages(records, 0, records.Count - 1, timer, distance,
            (speed, avgHr, maxHr, avgPower, maxPower, avgCadence) =>
            {
                mesg.SetAvgSpeed(speed);
                mesg.SetEnhancedAvgSpeed(speed);
                if (avgHr is not null) mesg.SetAvgHeartRate(avgHr);
                if (maxHr is not null) mesg.SetMaxHeartRate(maxHr);
                if (avgPower is not null) mesg.SetAvgPower(avgPower);
                if (maxPower is not null) mesg.SetMaxPower(maxPower);
                if (avgCadence is not null) mesg.SetAvgCadence(avgCadence);
            });

        return mesg;
    }

    private static ActivityMesg BuildActivity(FitDateTime end, float timer, int utcOffsetSeconds)
    {
        var mesg = new ActivityMesg();
        mesg.SetTimestamp(end);
        mesg.SetTotalTimerTime(timer);
        mesg.SetNumSessions(1);
        mesg.SetType(Activity.Manual);
        mesg.SetEvent(Event.Activity);
        mesg.SetEventType(EventType.Stop);
        mesg.SetLocalTimestamp((uint)(end.GetTimeStamp() + utcOffsetSeconds));
        return mesg;
    }

    /// <summary>
    /// The averages a summary message carries, over the records from <paramref name="from"/> to
    /// <paramref name="to"/> inclusive — a lap's own stretch, or the whole activity for the
    /// session. Speed is distance over <paramref name="timer"/> — moving time, the same number
    /// the message states as its <c>total_timer_time</c>, so the file cannot claim one duration
    /// and an average derived from another. A lap averaged over the whole activity would describe
    /// the run rather than the interval, which is worse than saying nothing.
    /// The sensor averages are Story 1.7's to revisit (AD-16).
    /// </summary>
    private static void ApplyAverages(
        IReadOnlyList<RepairedRecord> records,
        int from,
        int to,
        float timer,
        float distance,
        Action<float, byte?, byte?, ushort?, ushort?, byte?> apply)
    {
        // One pass over the records themselves: a lap's window is a slice of a series the caller
        // already holds, so copying it out to filter it three times is work nobody asked for.
        int heartRateSum = 0, heartRates = 0, maxHeartRate = 0;
        int powerSum = 0, powers = 0, maxPower = 0;
        int cadenceSum = 0, cadences = 0;

        for (var index = from; index <= to; index++)
        {
            var record = records[index];

            if (record.HeartRate is > 0 and { } heartRate)
            {
                heartRateSum += heartRate;
                heartRates++;
                maxHeartRate = Math.Max(maxHeartRate, heartRate);
            }

            if (record.Power is { } power)
            {
                powerSum += power;
                powers++;
                maxPower = Math.Max(maxPower, power);
            }

            if (record.Cadence is > 0 and { } cadence)
            {
                cadenceSum += cadence;
                cadences++;
            }
        }

        apply(
            timer > 0 ? distance / timer : 0,
            heartRates > 0 ? (byte)Math.Round((double)heartRateSum / heartRates) : null,
            heartRates > 0 ? (byte)maxHeartRate : null,
            powers > 0 ? (ushort)Math.Round((double)powerSum / powers) : null,
            powers > 0 ? (ushort)maxPower : null,
            cadences > 0 ? (byte)Math.Round((double)cadenceSum / cadences) : null);
    }

    private static Sport MapSport(SportKind sport) => sport switch
    {
        SportKind.Running => Sport.Running,
        SportKind.Cycling => Sport.Cycling,
        _ => Sport.Generic
    };
}
