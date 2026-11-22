using Dynastream.Fit;
using FitFix.Core;
using FitDateTime = Dynastream.Fit.DateTime;

namespace FitFix.Core.Tests.Fixtures;

/// <summary>
/// Пише синтетичний FIT напряму через Garmin FIT SDK — щоб тестувати читачів
/// FitFix незалежно від власного записувача.
/// </summary>
public static class SyntheticFitBuilder
{
    /// <summary>
    /// One <c>timer</c> event, so a fixture can state a sequence the pause intervals of
    /// <see cref="OriginalActivity.Pauses"/> cannot express — a stop nothing reopens, a start
    /// nothing preceded, two stops before one start.
    /// </summary>
    public readonly record struct TimerEvent(System.DateTime Timestamp, EventType Type);

    /// <summary>
    /// The lap distance every written <c>lap</c> message claims: the device's own measurement,
    /// which the broken GPS made nonsense of. Deliberately absurd — no lap of the synthetic run
    /// is anywhere near it — so a repaired file that quoted it would fail outright.
    /// </summary>
    public const float DeviceLapDistanceMeters = 42_195;

    /// <summary>
    /// The activity with the timer events its own pauses imply: a <c>start</c> at the first
    /// record, a <c>stop</c>/<c>start</c> pair around every pause, and a <c>stop_all</c> at the
    /// last record. Its <see cref="OriginalActivity.Laps"/> are written as <c>lap</c> messages,
    /// exactly as stated — overlaps and all, since a device can write those too. An activity
    /// nobody paused and nobody lapped is written exactly as it was before pauses and laps
    /// existed — no event and no lap messages at all — so no number pinned on that fixture can
    /// move.
    /// </summary>
    public static byte[] WriteActivity(OriginalActivity activity) =>
        WriteActivity(activity, TimerEventsFor(activity));

    /// <summary>
    /// One <c>lap</c> message exactly as stated, so a fixture can express a lap
    /// <see cref="LapInterval"/> cannot — a lap the file gave no <c>start_time</c>, or none at
    /// all, which is the only way to reach the reader's "a lap with no end" branch.
    /// </summary>
    public readonly record struct RawLap(System.DateTime? Start, System.DateTime? End);

    /// <summary>
    /// The activity with exactly the <c>lap</c> messages stated, whatever boundaries they are
    /// missing, instead of the ones its <see cref="OriginalActivity.Laps"/> imply.
    /// </summary>
    public static byte[] WriteActivityWithRawLaps(OriginalActivity activity, IReadOnlyList<RawLap> laps) =>
        WriteActivity(activity, TimerEventsFor(activity), laps);

    /// <summary>The activity with exactly the timer events stated, in the order given.</summary>
    public static byte[] WriteActivity(OriginalActivity activity, IReadOnlyList<TimerEvent> timerEvents) =>
        WriteActivity(activity, timerEvents, LapsOf(activity), []);

    /// <summary>
    /// One <c>device_info</c> message: what the sensor speaks and which profile it reports, the
    /// two fields together, because the FIT subfield that names the profile is resolved by the
    /// <c>source_type</c> beside it (AD-17). Stated as raw values rather than as a FitFix notion
    /// of a sensor, so a fixture can write what a watch writes — including the combinations the
    /// reader must <em>not</em> read as a speed source.
    /// </summary>
    public readonly record struct DeclaredDevice(SourceType Source, byte DeviceType);

    /// <summary>
    /// An ANT+ sensor of the given profile, as a watch pairs one — the profile numbers are the
    /// constants of <see cref="AntplusDeviceType"/>.
    /// </summary>
    public static DeclaredDevice Antplus(byte profile) => new(SourceType.Antplus, profile);

    /// <summary>
    /// The same over Bluetooth low energy, whose profile numbers are different ones — the
    /// constants of <see cref="BleDeviceType"/>.
    /// </summary>
    public static DeclaredDevice Ble(byte profile) => new(SourceType.BluetoothLowEnergy, profile);

    /// <summary>
    /// The activity with a <c>device_info</c> message per declared device, written where a watch
    /// writes them — right after <c>file_id</c>, before the records. Every fixture that states
    /// none is written exactly as it was before <c>device_info</c> was read at all, so no number
    /// pinned on one of them can move.
    /// </summary>
    public static byte[] WriteActivityWithDevices(
        OriginalActivity activity,
        IReadOnlyList<DeclaredDevice> devices) =>
        WriteActivity(activity, TimerEventsFor(activity), LapsOf(activity), devices);

    private static IReadOnlyList<RawLap> LapsOf(OriginalActivity activity) =>
        [.. activity.Laps.Select(lap => new RawLap(lap.Start, lap.End))];

    private static byte[] WriteActivity(
        OriginalActivity activity,
        IReadOnlyList<TimerEvent> timerEvents,
        IReadOnlyList<RawLap> laps) =>
        WriteActivity(activity, timerEvents, laps, []);

    private static byte[] WriteActivity(
        OriginalActivity activity,
        IReadOnlyList<TimerEvent> timerEvents,
        IReadOnlyList<RawLap> laps,
        IReadOnlyList<DeclaredDevice> devices)
    {
        using var buffer = new MemoryStream();
        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(buffer);

        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Activity);
        fileId.SetManufacturer(Manufacturer.Garmin);
        fileId.SetProduct(3121);
        fileId.SetSerialNumber(1234u);
        fileId.SetTimeCreated(new FitDateTime(activity.StartTime));
        encoder.Write(fileId);

        // The paired sensors, as a watch declares them at the top of the file. device_index 0 is
        // the watch itself, so the sensors start at 1.
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var mesg = new DeviceInfoMesg();
            mesg.SetTimestamp(new FitDateTime(activity.StartTime));
            mesg.SetDeviceIndex((byte)(index + 1));
            mesg.SetSourceType(device.Source);

            // The SDK resolves device_type's subfield from source_type, so the profile has to be
            // set through the accessor that matches what the sensor speaks — exactly the
            // resolution the reader relies on (AD-17).
            if (device.Source == SourceType.Antplus)
                mesg.SetAntplusDeviceType(device.DeviceType);
            else if (device.Source == SourceType.BluetoothLowEnergy)
                mesg.SetBleDeviceType(device.DeviceType);
            else
                mesg.SetDeviceType(device.DeviceType);

            encoder.Write(mesg);
        }

        // Records and events in one timestamp-ordered stream, as a watch writes them. Events come
        // first on a tie, so the start of the activity precedes its first record.
        var stream = timerEvents
            .Select(e => (e.Timestamp, OnTie: 0, Mesg: (Mesg)BuildTimerEvent(e)))
            .Concat(activity.Records.Select(r => (r.Timestamp, OnTie: 1, Mesg: (Mesg)BuildRecord(r))))
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.OnTie);

        foreach (var entry in stream)
            encoder.Write(entry.Mesg);

        // The laps the athlete marked, as a watch writes them: start_time and timestamp are the
        // span, and total_distance is the one the device measured with the GPS that failed —
        // exactly the number the repair must not believe (AD-24). It is written as a value no
        // repaired lap could hold, so a writer that copied it across would be caught rather than
        // flattered by a plausible-looking figure.
        var lapIndex = 0;
        foreach (var lap in laps)
        {
            var mesg = new LapMesg();
            mesg.SetMessageIndex((ushort)lapIndex++);
            if (lap.Start is { } from)
                mesg.SetStartTime(new FitDateTime(from));
            if (lap.End is { } to)
                mesg.SetTimestamp(new FitDateTime(to));
            if (lap.Start is { } spanFrom && lap.End is { } spanTo)
            {
                mesg.SetTotalElapsedTime((float)(spanTo - spanFrom).TotalSeconds);
                mesg.SetTotalTimerTime((float)(spanTo - spanFrom).TotalSeconds);
            }

            mesg.SetTotalDistance(DeviceLapDistanceMeters);
            mesg.SetEvent(Event.Lap);
            mesg.SetEventType(EventType.Stop);
            encoder.Write(mesg);
        }

        var session = new SessionMesg();
        session.SetTimestamp(new FitDateTime(activity.EndTime));
        session.SetStartTime(new FitDateTime(activity.StartTime));
        session.SetSport(activity.Sport == SportKind.Running ? Sport.Running : Sport.Cycling);
        session.SetTotalElapsedTime((float)activity.ElapsedSeconds);
        session.SetTotalTimerTime((float)activity.ElapsedSeconds);
        session.SetTotalDistance((float)(activity.RecordedDistance ?? 0));
        encoder.Write(session);

        var activityMesg = new ActivityMesg();
        activityMesg.SetTimestamp(new FitDateTime(activity.EndTime));
        activityMesg.SetNumSessions(1);
        activityMesg.SetType(Dynastream.Fit.Activity.Manual);
        encoder.Write(activityMesg);

        encoder.Close();
        return buffer.ToArray();
    }

    private static IReadOnlyList<TimerEvent> TimerEventsFor(OriginalActivity activity)
    {
        if (activity.Pauses.Count == 0)
            return [];

        var events = new List<TimerEvent> { new(activity.StartTime, EventType.Start) };
        foreach (var pause in activity.Pauses)
        {
            events.Add(new TimerEvent(pause.Start, EventType.Stop));
            events.Add(new TimerEvent(pause.End, EventType.Start));
        }

        events.Add(new TimerEvent(activity.EndTime, EventType.StopAll));
        return events;
    }

    private static EventMesg BuildTimerEvent(TimerEvent timerEvent)
    {
        var mesg = new EventMesg();
        mesg.SetTimestamp(new FitDateTime(timerEvent.Timestamp));
        mesg.SetEvent(Event.Timer);
        mesg.SetEventType(timerEvent.Type);
        return mesg;
    }

    private static RecordMesg BuildRecord(ActivityRecord record)
    {
        var mesg = new RecordMesg();
        mesg.SetTimestamp(new FitDateTime(record.Timestamp));
        if (record.Latitude is { } latitude)
            mesg.SetPositionLat((int)Math.Round(latitude / Geo.SemicirclesToDegrees));
        if (record.Longitude is { } longitude)
            mesg.SetPositionLong((int)Math.Round(longitude / Geo.SemicirclesToDegrees));
        if (record.Altitude is { } altitude)
            mesg.SetEnhancedAltitude((float)altitude);
        if (record.Distance is { } distance)
            mesg.SetDistance((float)distance);
        if (record.Speed is { } speed)
            mesg.SetEnhancedSpeed((float)speed);
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
    /// A well-formed FIT file with no <c>record</c> message at all: it passes the header check and
    /// the integrity check and is still not an activity, which is the one way to reach
    /// <c>FIT_HAS_NO_RECORDS</c> from the outside.
    /// </summary>
    public static byte[] WriteWithoutRecords(System.DateTime timeCreated)
    {
        using var buffer = new MemoryStream();
        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(buffer);

        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Activity);
        fileId.SetManufacturer(Manufacturer.Garmin);
        fileId.SetProduct(3121);
        fileId.SetSerialNumber(1234u);
        fileId.SetTimeCreated(new FitDateTime(timeCreated));
        encoder.Write(fileId);

        var activityMesg = new ActivityMesg();
        activityMesg.SetTimestamp(new FitDateTime(timeCreated));
        activityMesg.SetNumSessions(1);
        activityMesg.SetType(Dynastream.Fit.Activity.Manual);
        encoder.Write(activityMesg);

        encoder.Close();
        return buffer.ToArray();
    }

    /// <summary>Трек у вигляді FIT-активності (саме так виглядає експорт з онлайн-редактора).</summary>
    public static byte[] WriteTrack(IReadOnlyList<TrackPoint> points, System.DateTime startTime)
    {
        var records = points.Select((point, i) => new ActivityRecord
        {
            Timestamp = startTime.AddSeconds(i),
            Latitude = point.Position.Latitude,
            Longitude = point.Position.Longitude,
            Altitude = point.Altitude
        }).ToArray();

        return WriteActivity(new OriginalActivity { Records = records, Sport = SportKind.Running });
    }
}
