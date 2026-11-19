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
    public static byte[] WriteActivity(OriginalActivity activity)
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

        foreach (var record in activity.Records)
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
