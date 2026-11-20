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
    /// The activity with the timer events its own pauses imply: a <c>start</c> at the first
    /// record, a <c>stop</c>/<c>start</c> pair around every pause, and a <c>stop_all</c> at the
    /// last record. An activity nobody paused is written exactly as it was before pauses existed —
    /// no event messages at all — so no number pinned on the pause-free fixture can move.
    /// </summary>
    public static byte[] WriteActivity(OriginalActivity activity) =>
        WriteActivity(activity, TimerEventsFor(activity));

    /// <summary>The activity with exactly the timer events stated, in the order given.</summary>
    public static byte[] WriteActivity(OriginalActivity activity, IReadOnlyList<TimerEvent> timerEvents)
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

        // Records and events in one timestamp-ordered stream, as a watch writes them. Events come
        // first on a tie, so the start of the activity precedes its first record.
        var stream = timerEvents
            .Select(e => (e.Timestamp, OnTie: 0, Mesg: (Mesg)BuildTimerEvent(e)))
            .Concat(activity.Records.Select(r => (r.Timestamp, OnTie: 1, Mesg: (Mesg)BuildRecord(r))))
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.OnTie);

        foreach (var entry in stream)
            encoder.Write(entry.Mesg);

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
