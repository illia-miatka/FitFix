using Dynastream.Fit;
using FitFix.Core.Diagnostics;

namespace FitFix.Core.Fit;

/// <summary>
/// Читає Оригінал: record-записи (час + сенсори) і вид спорту з session (PRD FR-1), plus the
/// <c>timer</c> events that say where the activity was paused (AD-7), the <c>lap</c> messages
/// that say how the athlete split it (AD-24) and the <c>device_info</c> messages that say whether
/// anything but the GPS was measuring movement (AD-17).
/// </summary>
public static class FitActivityReader
{
    public static OriginalActivity Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var records = new List<ActivityRecord>();
        var timerEvents = new List<(System.DateTime Timestamp, EventType Type)>();
        var laps = new List<LapInterval>();
        var sport = SportKind.Other;
        var speedSensors = new List<System.DateTime>();

        FitDecoder.Decode(stream, broadcaster =>
        {
            broadcaster.RecordMesgEvent += (_, e) =>
            {
                var mesg = (RecordMesg)e.mesg;
                var timestamp = mesg.GetTimestamp();
                if (timestamp is null)
                    return;

                records.Add(new ActivityRecord
                {
                    Timestamp = System.DateTime.SpecifyKind(timestamp.GetDateTime(), DateTimeKind.Utc),
                    Latitude = FitDecoder.ToDegrees(mesg.GetPositionLat()),
                    Longitude = FitDecoder.ToDegrees(mesg.GetPositionLong()),
                    Altitude = mesg.GetEnhancedAltitude() ?? mesg.GetAltitude(),
                    Distance = mesg.GetDistance(),
                    Speed = mesg.GetEnhancedSpeed() ?? mesg.GetSpeed(),
                    HeartRate = mesg.GetHeartRate(),
                    Power = mesg.GetPower(),
                    Cadence = mesg.GetCadence(),
                    Temperature = mesg.GetTemperature()
                });
            };

            broadcaster.SessionMesgEvent += (_, e) =>
            {
                var mesg = (SessionMesg)e.mesg;
                sport = MapSport(mesg.GetSport());
            };

            // The timer events are the only thing that says the activity was paused (AD-7).
            // Everything else about a pause is derived from the intervals built out of them.
            broadcaster.EventMesgEvent += (_, e) =>
            {
                var mesg = (EventMesg)e.mesg;
                if (mesg.GetEvent() != Event.Timer)
                    return;

                var timestamp = mesg.GetTimestamp();
                if (timestamp is null)
                    return;

                if (mesg.GetEventType() is { } type && (type == EventType.Start || IsStop(type)))
                    timerEvents.Add((
                        System.DateTime.SpecifyKind(timestamp.GetDateTime(), DateTimeKind.Utc), type));
            };

            // The file is the only thing that says where the laps were (AD-24). A lap message
            // states its span as start_time and timestamp, the latter being its end. A lap with
            // no end is skipped rather than guessed at — a boundary invented from an elapsed time
            // would move a press the athlete made — but one that has an end and no start keeps
            // the end, which is a cut point in its own right.
            broadcaster.LapMesgEvent += (_, e) =>
            {
                var mesg = (LapMesg)e.mesg;
                if (mesg.GetTimestamp() is not { } to)
                    return;

                var end = Utc(to);
                laps.Add(new LapInterval(mesg.GetStartTime() is { } from ? Utc(from) : end, end));
            };

            // The only thing in the file that says a sensor other than the GPS was measuring
            // movement (AD-17). One such message inside this activity's span is enough: a watch
            // writes one per paired sensor, and a footpod that was there for part of the activity
            // was still measuring. The timestamps are kept rather than a flag, because the span is
            // a property of the records and is not known until they have all been read.
            broadcaster.DeviceInfoMesgEvent += (_, e) =>
            {
                var mesg = (DeviceInfoMesg)e.mesg;
                if (mesg.GetTimestamp() is { } at && DeclaresSpeedSource(mesg))
                    speedSensors.Add(Utc(at));
            };
        });

        if (records.Count == 0)
            throw new InvalidFitFileException(
                DiagnosticCodes.FitHasNoRecords, "The FIT file carries no record messages, so it is not an activity.");

        records.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return new OriginalActivity
        {
            Records = records,
            Sport = sport,
            Pauses = PausesOf(timerEvents, records[^1].Timestamp),
            // A FIT file may hold more than one session, and a course or a previous workout
            // leaves lap messages of its own. A lap whose end lies outside this activity's
            // records belongs to something else and must not cut this activity's laps.
            Laps = [.. laps.Where(lap =>
                lap.End >= records[0].Timestamp && lap.End <= records[^1].Timestamp)],
            // Clipped to this activity's span, exactly as the laps above are and for the same
            // reason: a FIT file may hold more than one session, and a footpod paired for a ride
            // earlier in the file says nothing about the run being read here (AD-17). A
            // `device_info` with no timestamp cannot be placed and is therefore not counted —
            // towards "no sensor", which is the safe direction for this flag.
            DeclaresSpeedSensor = speedSensors.Exists(at =>
                at >= records[0].Timestamp && at <= records[^1].Timestamp)
        };
    }

    /// <summary>
    /// Whether one <c>device_info</c> message declares a sensor that measures movement without a
    /// GPS fix: a footpod, a wheel speed sensor or a power meter (AD-17). Everything else — a heart
    /// rate strap, a watch whose only speed came from its own GPS — is not one, and a speed the
    /// device derived from GPS must never become the source, because after the failure it is zero.
    /// <para>
    /// The profile is read through the SDK's own subfield resolution, which answers only when the
    /// message states what the sensor speaks (<c>source_type</c> of <c>antplus</c> or
    /// <c>bluetooth_low_energy</c>): a bare <c>device_type</c> of 124 on a message that says
    /// nothing else could as easily be a local sensor index, and reading it as a footpod would hand
    /// the chosen length over to a device distance nothing has vouched for. Erring towards "no
    /// sensor" is the safe direction — the fallback is a generous cap, while the mistake in the
    /// other direction trimmed a real 6.5 km run to the 3.9 km a dead GPS froze at.
    /// </para>
    /// </summary>
    private static bool DeclaresSpeedSource(DeviceInfoMesg mesg) =>
        (mesg.GetAntplusDeviceType() is { } antplus && IsAntplusSpeedSource(antplus))
        || (mesg.GetBleDeviceType() is { } ble && IsBleSpeedSource(ble));

    /// <inheritdoc cref="DeclaresSpeedSource"/>
    private static bool IsAntplusSpeedSource(byte deviceType) =>
        deviceType == (byte)AntplusDeviceType.StrideSpeedDistance
        || deviceType == (byte)AntplusDeviceType.BikeSpeed
        || deviceType == (byte)AntplusDeviceType.BikeSpeedCadence
        || deviceType == (byte)AntplusDeviceType.BikePower;

    /// <inheritdoc cref="DeclaresSpeedSource"/>
    private static bool IsBleSpeedSource(byte deviceType) =>
        deviceType == (byte)BleDeviceType.Footpod
        || deviceType == (byte)BleDeviceType.BikeSpeed
        || deviceType == (byte)BleDeviceType.BikeSpeedCadence
        || deviceType == (byte)BleDeviceType.BikePower;

    private static System.DateTime Utc(Dynastream.Fit.DateTime timestamp) =>
        System.DateTime.SpecifyKind(timestamp.GetDateTime(), DateTimeKind.Utc);

    /// <summary>
    /// Every event type that stops the timer. A watch writes <c>stop_disable</c> or
    /// <c>stop_disable_all</c> when the athlete presses stop and <c>stop</c> or <c>stop_all</c>
    /// when it pauses itself; a pause is a pause either way, so reading only two of the four
    /// would leave genuinely paused files behaving as if nothing had been read at all.
    /// </summary>
    private static bool IsStop(EventType type) =>
        type is EventType.Stop or EventType.StopAll or EventType.StopDisable or EventType.StopDisableAll;

    /// <summary>
    /// The pauses the timer events describe, ordered by time (AD-7). A stop opens a pause and the
    /// next <c>start</c> closes it; a second stop before that start extends the one already open
    /// rather than opening a nested second one, and a <c>start</c> with no stop before it closes
    /// nothing. A stop nothing reopens is where the activity ended — unless records keep arriving
    /// after it, which is a watch that was stopped and went on writing: then the pause runs to
    /// the last record, because those seconds are standing still just the same.
    /// </summary>
    private static IReadOnlyList<PauseInterval> PausesOf(
        List<(System.DateTime Timestamp, EventType Type)> timerEvents,
        System.DateTime lastRecord)
    {
        if (timerEvents.Count == 0)
            return [];

        var pauses = new List<PauseInterval>();
        System.DateTime? openedAt = null;

        // OrderBy is stable, so a stop and a start sharing one timestamp keep the order the file
        // put them in — the only thing that can tell them apart.
        foreach (var (timestamp, type) in timerEvents.OrderBy(e => e.Timestamp))
        {
            if (IsStop(type))
            {
                openedAt ??= timestamp;
                continue;
            }

            if (openedAt is { } from && timestamp > from)
                pauses.Add(new PauseInterval(from, timestamp));

            openedAt = null;
        }

        // A stop at the last record is the end of the activity and earns no interval; one the
        // records outlive is a pause that lasts as long as they do.
        if (openedAt is { } unclosed && lastRecord > unclosed)
            pauses.Add(new PauseInterval(unclosed, lastRecord));

        return pauses;
    }

    public static OriginalActivity ReadFile(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return Read(stream);
    }

    private static SportKind MapSport(Sport? sport) => sport switch
    {
        Sport.Running => SportKind.Running,
        Sport.Cycling => SportKind.Cycling,
        _ => SportKind.Other
    };
}
