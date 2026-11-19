using Dynastream.Fit;

namespace FitFix.Core.Fit;

/// <summary>Читає Оригінал: record-записи (час + сенсори) і вид спорту з session (PRD FR-1).</summary>
public static class FitActivityReader
{
    public static OriginalActivity Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var records = new List<ActivityRecord>();
        var sport = SportKind.Other;

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
        });

        if (records.Count == 0)
            throw new InvalidFitFileException("Це не схоже на FIT-активність: немає record-повідомлень.");

        records.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return new OriginalActivity { Records = records, Sport = sport };
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
