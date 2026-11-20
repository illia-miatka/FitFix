using Dynastream.Fit;

namespace FitFix.Core.Tests.Fixtures;

/// <summary>Рахує повідомлення у FIT-файлі — щоб перевірити структуру, а не лише дані.</summary>
public static class FitMessageInspector
{
    public sealed record Counts(
        int FileIdCount,
        int RecordCount,
        int TimerEventCount,
        int LapCount,
        int SessionCount,
        int ActivityCount);

    /// <summary>
    /// The durations and the average speed one summary message states. Null means the field is
    /// absent. They are read together because they have to agree: an average speed derived from
    /// a duration the message does not state is the contradiction worth failing on.
    /// </summary>
    public sealed record Summary(float? ElapsedSeconds, float? TimerSeconds, float? AvgSpeedMetersPerSecond);

    /// <summary>
    /// The timing of a written file: every lap in the order it was written, then the session and
    /// the activity's timer time — <c>activity</c> has no elapsed time of its own in FIT. The
    /// laps are a list rather than one value because Story 1.6 writes several, and a wrong one
    /// has to fail rather than be overwritten by the next.
    /// </summary>
    public sealed record Timing(
        IReadOnlyList<Summary> Laps,
        Summary Session,
        float? ActivityTimerSeconds);

    public static Timing InspectTiming(byte[] bytes)
    {
        var laps = new List<Summary>();
        var session = new Summary(null, null, null);
        float? activityTimer = null;

        Read(bytes, broadcaster =>
        {
            broadcaster.LapMesgEvent += (_, e) =>
            {
                var mesg = (LapMesg)e.mesg;
                laps.Add(new Summary(
                    mesg.GetTotalElapsedTime(),
                    mesg.GetTotalTimerTime(),
                    mesg.GetEnhancedAvgSpeed() ?? mesg.GetAvgSpeed()));
            };
            broadcaster.SessionMesgEvent += (_, e) =>
            {
                var mesg = (SessionMesg)e.mesg;
                session = new Summary(
                    mesg.GetTotalElapsedTime(),
                    mesg.GetTotalTimerTime(),
                    mesg.GetEnhancedAvgSpeed() ?? mesg.GetAvgSpeed());
            };
            broadcaster.ActivityMesgEvent += (_, e) =>
                activityTimer = ((ActivityMesg)e.mesg).GetTotalTimerTime();
        });

        return new Timing(laps, session, activityTimer);
    }

    public static Counts Inspect(byte[] bytes)
    {
        int fileId = 0, records = 0, timerEvents = 0, laps = 0, sessions = 0, activities = 0;

        Read(bytes, broadcaster =>
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
        });

        return new Counts(fileId, records, timerEvents, laps, sessions, activities);
    }

    private static void Read(byte[] bytes, Action<MesgBroadcaster> subscribe)
    {
        var decoder = new Decode();
        var broadcaster = new MesgBroadcaster();
        decoder.MesgEvent += broadcaster.OnMesg;
        decoder.MesgDefinitionEvent += broadcaster.OnMesgDefinition;
        subscribe(broadcaster);

        using var stream = new MemoryStream(bytes);
        decoder.Read(stream);
    }
}
