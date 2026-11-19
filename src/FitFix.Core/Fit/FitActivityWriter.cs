using Dynastream.Fit;
using FitDateTime = Dynastream.Fit.DateTime;

namespace FitFix.Core.Fit;

/// <summary>
/// Записує Відновлену активність у FIT так, щоб її прийняли Strava й Garmin Connect:
/// file_id → event(start) → records → event(stop_all) → lap → session → activity (PRD FR-6).
/// </summary>
public static class FitActivityWriter
{
    public static void Write(Stream stream, RepairResult result, int utcOffsetSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(result);

        var records = result.Records;
        if (records.Count < 2)
            throw new ArgumentException("Для запису потрібно щонайменше 2 записи.", nameof(result));

        var start = new FitDateTime(records[0].Timestamp);
        var end = new FitDateTime(records[^1].Timestamp);
        var elapsed = (float)(records[^1].Timestamp - records[0].Timestamp).TotalSeconds;
        var totalDistance = (float)records[^1].Distance;

        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(stream);

        encoder.Write(BuildFileId(end));
        encoder.Write(BuildTimerEvent(start, EventType.Start));

        foreach (var record in records)
            encoder.Write(BuildRecord(record));

        encoder.Write(BuildTimerEvent(end, EventType.StopAll));
        encoder.Write(BuildLap(result, start, end, elapsed, totalDistance));
        encoder.Write(BuildSession(result, start, end, elapsed, totalDistance));
        encoder.Write(BuildActivity(end, elapsed, utcOffsetSeconds));

        encoder.Close();
    }

    public static void WriteFile(string path, RepairResult result, int utcOffsetSeconds = 0)
    {
        using var stream = System.IO.File.Create(path);
        Write(stream, result, utcOffsetSeconds);
    }

    public static byte[] WriteToArray(RepairResult result, int utcOffsetSeconds = 0)
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

    private static LapMesg BuildLap(RepairResult result, FitDateTime start, FitDateTime end, float elapsed, float distance)
    {
        var mesg = new LapMesg();
        mesg.SetTimestamp(end);
        mesg.SetStartTime(start);
        mesg.SetTotalElapsedTime(elapsed);
        mesg.SetTotalTimerTime(elapsed);
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

        ApplyAverages(result, elapsed, distance,
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

    private static SessionMesg BuildSession(RepairResult result, FitDateTime start, FitDateTime end, float elapsed, float distance)
    {
        var mesg = new SessionMesg();
        mesg.SetTimestamp(end);
        mesg.SetStartTime(start);
        mesg.SetTotalElapsedTime(elapsed);
        mesg.SetTotalTimerTime(elapsed);
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

        ApplyAverages(result, elapsed, distance,
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

    private static ActivityMesg BuildActivity(FitDateTime end, float elapsed, int utcOffsetSeconds)
    {
        var mesg = new ActivityMesg();
        mesg.SetTimestamp(end);
        mesg.SetTotalTimerTime(elapsed);
        mesg.SetNumSessions(1);
        mesg.SetType(Activity.Manual);
        mesg.SetEvent(Event.Activity);
        mesg.SetEventType(EventType.Stop);
        mesg.SetLocalTimestamp((uint)(end.GetTimeStamp() + utcOffsetSeconds));
        return mesg;
    }

    private static void ApplyAverages(
        RepairResult result,
        float elapsed,
        float distance,
        Action<float, byte?, byte?, ushort?, ushort?, byte?> apply)
    {
        var records = result.Records;
        var heartRates = records.Where(r => r.HeartRate is > 0).Select(r => (int)r.HeartRate!.Value).ToArray();
        var powers = records.Where(r => r.Power is not null).Select(r => (int)r.Power!.Value).ToArray();
        var cadences = records.Where(r => r.Cadence is > 0).Select(r => (int)r.Cadence!.Value).ToArray();

        apply(
            elapsed > 0 ? distance / elapsed : 0,
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
