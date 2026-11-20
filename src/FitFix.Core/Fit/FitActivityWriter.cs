using Dynastream.Fit;
using FitFix.Core.Diagnostics;
using FitDateTime = Dynastream.Fit.DateTime;

namespace FitFix.Core.Fit;

/// <summary>
/// Записує Відновлену активність у FIT так, щоб її прийняли Strava й Garmin Connect:
/// file_id → event(start) → records → event(stop_all) → lap → session → activity (PRD FR-6).
/// </summary>
public static class FitActivityWriter
{
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
        var timer = paused < elapsed ? (float)(elapsed - paused) : elapsed;
        var totalDistance = (float)records[^1].Distance;

        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(stream);

        encoder.Write(BuildFileId(end));
        encoder.Write(BuildTimerEvent(start, EventType.Start));

        foreach (var record in records)
            encoder.Write(BuildRecord(record));

        encoder.Write(BuildTimerEvent(end, EventType.StopAll));
        encoder.Write(BuildLap(result, start, end, elapsed, timer, totalDistance));
        encoder.Write(BuildSession(result, start, end, elapsed, timer, totalDistance));
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

    private static LapMesg BuildLap(
        RepairResult result,
        FitDateTime start,
        FitDateTime end,
        float elapsed,
        float timer,
        float distance)
    {
        var mesg = new LapMesg();
        mesg.SetTimestamp(end);
        mesg.SetStartTime(start);
        mesg.SetTotalElapsedTime(elapsed);
        mesg.SetTotalTimerTime(timer);
        mesg.SetTotalDistance(distance);
        mesg.SetSport(MapSport(result.Sport));
        mesg.SetEvent(Event.Lap);
        mesg.SetEventType(EventType.Stop);
        mesg.SetMessageIndex(0);

        var records = result.Records;
        mesg.SetStartPositionLat(FitDecoder.ToSemicircles(records[0].Latitude));
        mesg.SetStartPositionLong(FitDecoder.ToSemicircles(records[0].Longitude));
        mesg.SetEndPositionLat(FitDecoder.ToSemicircles(records[^1].Latitude));
        mesg.SetEndPositionLong(FitDecoder.ToSemicircles(records[^1].Longitude));

        ApplyAverages(result, timer, distance,
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
        float distance)
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
        mesg.SetNumLaps(1);
        mesg.SetTrigger(SessionTrigger.ActivityEnd);

        var records = result.Records;
        mesg.SetStartPositionLat(FitDecoder.ToSemicircles(records[0].Latitude));
        mesg.SetStartPositionLong(FitDecoder.ToSemicircles(records[0].Longitude));

        ApplyAverages(result, timer, distance,
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
    /// The averages a summary message carries. Speed is distance over
    /// <paramref name="timer"/> — moving time, the same number the message states as its
    /// <c>total_timer_time</c>, so the file cannot claim one duration and an average derived from
    /// another. The sensor averages are Story 1.7's to revisit.
    /// </summary>
    private static void ApplyAverages(
        RepairResult result,
        float timer,
        float distance,
        Action<float, byte?, byte?, ushort?, ushort?, byte?> apply)
    {
        var records = result.Records;
        var heartRates = records.Where(r => r.HeartRate is > 0).Select(r => (int)r.HeartRate!.Value).ToArray();
        var powers = records.Where(r => r.Power is not null).Select(r => (int)r.Power!.Value).ToArray();
        var cadences = records.Where(r => r.Cadence is > 0).Select(r => (int)r.Cadence!.Value).ToArray();

        apply(
            timer > 0 ? distance / timer : 0,
            heartRates.Length > 0 ? (byte)Math.Round(heartRates.Average()) : null,
            heartRates.Length > 0 ? (byte)heartRates.Max() : null,
            powers.Length > 0 ? (ushort)Math.Round(powers.Average()) : null,
            powers.Length > 0 ? (ushort)powers.Max() : null,
            cadences.Length > 0 ? (byte)Math.Round(cadences.Average()) : null);
    }

    private static Sport MapSport(SportKind sport) => sport switch
    {
        SportKind.Running => Sport.Running,
        SportKind.Cycling => Sport.Cycling,
        _ => Sport.Generic
    };
}
