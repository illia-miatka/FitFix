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
    /// <c>message_index</c>, its averages, its climb and the positions it was opened and closed
    /// at. Null means the field is absent. Durations and average speed are read together because
    /// they have to agree: an average speed derived from a duration the message does not state is
    /// the contradiction worth failing on. The sensor averages and the positions are read because
    /// a lap that quoted the whole activity's would be indistinguishable from a correct one
    /// without them.
    /// </summary>
    /// <remarks>
    /// Every value here goes through a <c>Measured…</c> reader. A FIT encoder writes one field
    /// definition for all the <c>lap</c> messages of a file, so a field one lap states and another
    /// does not is written as that field's invalid sentinel rather than left out of the message.
    /// Read raw, a lap that states no average heart rate at all would come back claiming 255 bpm,
    /// and a test asserting the field is absent would be asserting nothing. Each field is
    /// therefore compared against the sentinel of <em>its own</em> FIT base type: 255 for a
    /// <c>uint8</c> heart rate, 65535 for a <c>uint16</c> power, 4294967295 for a <c>uint32</c>
    /// distance or duration — a shared threshold would read a real 18-hour ride or a 70 km route
    /// as an absent field.
    /// </remarks>
    public sealed record Summary
    {
        public float? ElapsedSeconds { get; init; }
        public float? TimerSeconds { get; init; }
        public float? AvgSpeedMetersPerSecond { get; init; }
        public float? MaxSpeedMetersPerSecond { get; init; }
        public float? DistanceMeters { get; init; }
        public System.DateTime? Start { get; init; }
        public System.DateTime? End { get; init; }
        public ushort? MessageIndex { get; init; }
        public byte? AvgHeartRate { get; init; }
        public byte? MaxHeartRate { get; init; }
        public ushort? AvgPower { get; init; }
        public ushort? MaxPower { get; init; }
        public byte? AvgCadence { get; init; }
        public byte? MaxCadence { get; init; }
        public ushort? ElevationGainMeters { get; init; }
        public ushort? ElevationLossMeters { get; init; }
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
                    ElapsedSeconds = MeasuredAsUInt32(mesg.GetTotalElapsedTime()),
                    TimerSeconds = MeasuredAsUInt32(mesg.GetTotalTimerTime()),
                    AvgSpeedMetersPerSecond = MeasuredAsUInt32(mesg.GetEnhancedAvgSpeed())
                        ?? MeasuredAsUInt16(mesg.GetAvgSpeed()),
                    MaxSpeedMetersPerSecond = MeasuredAsUInt32(mesg.GetEnhancedMaxSpeed())
                        ?? MeasuredAsUInt16(mesg.GetMaxSpeed()),
                    DistanceMeters = MeasuredAsUInt32(mesg.GetTotalDistance()),
                    Start = Utc(mesg.GetStartTime()),
                    End = Utc(mesg.GetTimestamp()),
                    MessageIndex = mesg.GetMessageIndex(),
                    AvgHeartRate = Measured(mesg.GetAvgHeartRate()),
                    MaxHeartRate = Measured(mesg.GetMaxHeartRate()),
                    AvgPower = Measured(mesg.GetAvgPower()),
                    MaxPower = Measured(mesg.GetMaxPower()),
                    AvgCadence = Measured(mesg.GetAvgCadence()),
                    MaxCadence = Measured(mesg.GetMaxCadence()),
                    ElevationGainMeters = Measured(mesg.GetTotalAscent()),
                    ElevationLossMeters = Measured(mesg.GetTotalDescent()),
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
                    ElapsedSeconds = MeasuredAsUInt32(mesg.GetTotalElapsedTime()),
                    TimerSeconds = MeasuredAsUInt32(mesg.GetTotalTimerTime()),
                    AvgSpeedMetersPerSecond = MeasuredAsUInt32(mesg.GetEnhancedAvgSpeed())
                        ?? MeasuredAsUInt16(mesg.GetAvgSpeed()),
                    MaxSpeedMetersPerSecond = MeasuredAsUInt32(mesg.GetEnhancedMaxSpeed())
                        ?? MeasuredAsUInt16(mesg.GetMaxSpeed()),
                    DistanceMeters = MeasuredAsUInt32(mesg.GetTotalDistance()),
                    Start = Utc(mesg.GetStartTime()),
                    End = Utc(mesg.GetTimestamp()),
                    MessageIndex = mesg.GetMessageIndex(),
                    AvgHeartRate = Measured(mesg.GetAvgHeartRate()),
                    MaxHeartRate = Measured(mesg.GetMaxHeartRate()),
                    AvgPower = Measured(mesg.GetAvgPower()),
                    MaxPower = Measured(mesg.GetMaxPower()),
                    AvgCadence = Measured(mesg.GetAvgCadence()),
                    MaxCadence = Measured(mesg.GetMaxCadence()),
                    ElevationGainMeters = Measured(mesg.GetTotalAscent()),
                    ElevationLossMeters = Measured(mesg.GetTotalDescent()),
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

    /// <summary>
    /// The value of a <c>uint8</c> field a message actually stated, and null for one it left to
    /// the encoder's invalid sentinel — see the remarks on <see cref="Summary"/>. A heart rate or
    /// a cadence of 255 is that sentinel, and no athlete produces one.
    /// </summary>
    private static byte? Measured(byte? value) => value is null or byte.MaxValue ? null : value;

    /// <summary>
    /// The same for a <c>uint16</c> field — a power, a climb or a descent of 65535.
    /// </summary>
    private static ushort? Measured(ushort? value) => value is null or ushort.MaxValue ? null : value;

    /// <summary>
    /// The same for a scaled <c>uint16</c> field the SDK hands back as a <c>float</c>: an unset
    /// <c>avg_speed</c> or <c>max_speed</c> comes back as its raw 65535, which is 65 km/s and
    /// therefore no speed.
    /// </summary>
    private static float? MeasuredAsUInt16(float? value) =>
        value is null || value == ushort.MaxValue ? null : value;

    /// <summary>
    /// The same for a scaled <c>uint32</c> field: an unset duration, distance or enhanced speed
    /// comes back as its raw 4294967295. The sentinel is that field's own, not a shared ceiling —
    /// 65535 would be a real total for an 18-hour ride or a 70 km route, and reading those as
    /// absent would fail the equality between the summary and the file it was written to.
    /// </summary>
    private static float? MeasuredAsUInt32(float? value) =>
        value is null || value >= uint.MaxValue ? null : value;

    private static GeoPoint? Position(int? latitude, int? longitude) =>
        latitude is { } lat && longitude is { } lon
            ? new GeoPoint(lat * Geo.SemicirclesToDegrees, lon * Geo.SemicirclesToDegrees)
            : null;

    private static System.DateTime? Utc(Dynastream.Fit.DateTime? timestamp) =>
        timestamp is null
            ? null
            : System.DateTime.SpecifyKind(timestamp.GetDateTime(), System.DateTimeKind.Utc);
}
