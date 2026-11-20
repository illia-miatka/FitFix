using Dynastream.Fit;
using FitFix.Core;

namespace FitFix.Core.Tests.Fixtures;

/// <summary>Рахує повідомлення у FIT-файлі — щоб перевірити структуру, а не лише дані.</summary>
public static class FitMessageInspector
{
    /// <summary>
    /// How many messages of each kind the file holds, and <see cref="MessageOrder"/>: the message
    /// types in the order they were written, with a run of the same type collapsed to one entry.
    /// Counting alone cannot tell a file that writes its session before its laps from one that
    /// does not, and AD-19 pins the order as well as the sums.
    /// </summary>
    public sealed record Counts(
        int FileIdCount,
        int RecordCount,
        int TimerEventCount,
        int LapCount,
        int SessionCount,
        int ActivityCount,
        IReadOnlyList<string> MessageOrder);

    /// <summary>
    /// What one summary message states: its durations, its distance, the span it claims, its
    /// <c>message_index</c>, its averages and the positions it was opened and closed at. Null
    /// means the field is absent. Durations and average speed are read together because they have
    /// to agree: an average speed derived from a duration the message does not state is the
    /// contradiction worth failing on. The sensor averages and the positions are read because a
    /// lap that quoted the whole activity's would be indistinguishable from a correct one without
    /// them.
    /// </summary>
    public sealed record Summary
    {
        public float? ElapsedSeconds { get; init; }
        public float? TimerSeconds { get; init; }
        public float? AvgSpeedMetersPerSecond { get; init; }
        public float? DistanceMeters { get; init; }
        public System.DateTime? Start { get; init; }
        public System.DateTime? End { get; init; }
        public ushort? MessageIndex { get; init; }
        public byte? AvgHeartRate { get; init; }
        public byte? MaxHeartRate { get; init; }
        public ushort? AvgPower { get; init; }
        public ushort? MaxPower { get; init; }
        public byte? AvgCadence { get; init; }
        public GeoPoint? StartPosition { get; init; }
        public GeoPoint? EndPosition { get; init; }
        public LapTrigger? Trigger { get; init; }
    }

    /// <summary>
    /// The timing of a written file: every lap in the order it was written, then the session and
    /// the activity's timer time — <c>activity</c> has no elapsed time of its own in FIT. The
    /// laps are a list rather than one value because Story 1.6 writes several, and a wrong one
    /// has to fail rather than be overwritten by the next. <paramref name="SessionNumLaps"/> is
    /// the count the session claims, which has to be the number of laps that precede it.
    /// </summary>
    public sealed record Timing(
        IReadOnlyList<Summary> Laps,
        Summary Session,
        float? ActivityTimerSeconds,
        ushort? SessionNumLaps = null);

    public static Timing InspectTiming(byte[] bytes)
    {
        var laps = new List<Summary>();
        var session = new Summary();
        float? activityTimer = null;
        ushort? numLaps = null;

        Read(bytes, broadcaster =>
        {
            broadcaster.LapMesgEvent += (_, e) =>
            {
                var mesg = (LapMesg)e.mesg;
                laps.Add(new Summary
                {
                    ElapsedSeconds = mesg.GetTotalElapsedTime(),
                    TimerSeconds = mesg.GetTotalTimerTime(),
                    AvgSpeedMetersPerSecond = mesg.GetEnhancedAvgSpeed() ?? mesg.GetAvgSpeed(),
                    DistanceMeters = mesg.GetTotalDistance(),
                    Start = Utc(mesg.GetStartTime()),
                    End = Utc(mesg.GetTimestamp()),
                    MessageIndex = mesg.GetMessageIndex(),
                    AvgHeartRate = mesg.GetAvgHeartRate(),
                    MaxHeartRate = mesg.GetMaxHeartRate(),
                    AvgPower = mesg.GetAvgPower(),
                    MaxPower = mesg.GetMaxPower(),
                    AvgCadence = mesg.GetAvgCadence(),
                    StartPosition = Position(mesg.GetStartPositionLat(), mesg.GetStartPositionLong()),
                    EndPosition = Position(mesg.GetEndPositionLat(), mesg.GetEndPositionLong()),
                    Trigger = mesg.GetLapTrigger()
                });
            };
            broadcaster.SessionMesgEvent += (_, e) =>
            {
                var mesg = (SessionMesg)e.mesg;
                session = new Summary
                {
                    ElapsedSeconds = mesg.GetTotalElapsedTime(),
                    TimerSeconds = mesg.GetTotalTimerTime(),
                    AvgSpeedMetersPerSecond = mesg.GetEnhancedAvgSpeed() ?? mesg.GetAvgSpeed(),
                    DistanceMeters = mesg.GetTotalDistance(),
                    Start = Utc(mesg.GetStartTime()),
                    End = Utc(mesg.GetTimestamp()),
                    MessageIndex = mesg.GetMessageIndex(),
                    AvgHeartRate = mesg.GetAvgHeartRate(),
                    MaxHeartRate = mesg.GetMaxHeartRate(),
                    AvgPower = mesg.GetAvgPower(),
                    MaxPower = mesg.GetMaxPower(),
                    AvgCadence = mesg.GetAvgCadence(),
                    StartPosition = Position(mesg.GetStartPositionLat(), mesg.GetStartPositionLong())
                };
                numLaps = mesg.GetNumLaps();
            };
            broadcaster.ActivityMesgEvent += (_, e) =>
                activityTimer = ((ActivityMesg)e.mesg).GetTotalTimerTime();
        });

        return new Timing(laps, session, activityTimer, numLaps);
    }

    public static Counts Inspect(byte[] bytes)
    {
        int fileId = 0, records = 0, timerEvents = 0, laps = 0, sessions = 0, activities = 0;
        var order = new List<string>();

        Read(
            bytes,
            broadcaster =>
            {
                broadcaster.FileIdMesgEvent += (_, _) => fileId++;
                broadcaster.RecordMesgEvent += (_, _) => records++;
                broadcaster.LapMesgEvent += (_, _) => laps++;
                broadcaster.SessionMesgEvent += (_, _) => sessions++;
                broadcaster.ActivityMesgEvent += (_, _) => activities++;
                broadcaster.EventMesgEvent += (_, e) =>
                {
                    if (((EventMesg)e.mesg).GetEvent() == Event.Timer)
                        timerEvents++;
                };
            },
            name =>
            {
                if (order.Count == 0 || order[^1] != name)
                    order.Add(name);
            });

        return new Counts(fileId, records, timerEvents, laps, sessions, activities, order);
    }

    private static void Read(byte[] bytes, Action<MesgBroadcaster> subscribe, Action<string>? onMessage = null)
    {
        var decoder = new Decode();
        var broadcaster = new MesgBroadcaster();
        decoder.MesgEvent += broadcaster.OnMesg;
        decoder.MesgDefinitionEvent += broadcaster.OnMesgDefinition;
        if (onMessage is not null)
            decoder.MesgEvent += (_, e) => onMessage(NameOf(e.mesg.Num));
        subscribe(broadcaster);

        using var stream = new MemoryStream(bytes);
        decoder.Read(stream);
    }

    private static string NameOf(ushort num) => num switch
    {
        MesgNum.FileId => "file_id",
        MesgNum.Event => "event",
        MesgNum.Record => "record",
        MesgNum.Lap => "lap",
        MesgNum.Session => "session",
        MesgNum.Activity => "activity",
        _ => $"#{num}"
    };

    private static GeoPoint? Position(int? latitude, int? longitude) =>
        latitude is { } lat && longitude is { } lon
            ? new GeoPoint(lat * Geo.SemicirclesToDegrees, lon * Geo.SemicirclesToDegrees)
            : null;

    private static System.DateTime? Utc(Dynastream.Fit.DateTime? timestamp) =>
        timestamp is null
            ? null
            : System.DateTime.SpecifyKind(timestamp.GetDateTime(), System.DateTimeKind.Utc);
}
