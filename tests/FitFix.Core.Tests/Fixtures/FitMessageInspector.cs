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

    public static Counts Inspect(byte[] bytes)
    {
        int fileId = 0, records = 0, timerEvents = 0, laps = 0, sessions = 0, activities = 0;

        var decoder = new Decode();
        var broadcaster = new MesgBroadcaster();
        decoder.MesgEvent += broadcaster.OnMesg;
        decoder.MesgDefinitionEvent += broadcaster.OnMesgDefinition;

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

        using var stream = new MemoryStream(bytes);
        decoder.Read(stream);

        return new Counts(fileId, records, timerEvents, laps, sessions, activities);
    }
}
