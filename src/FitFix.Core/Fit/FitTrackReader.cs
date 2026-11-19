using Dynastream.Fit;
using FitFix.Core.Diagnostics;

namespace FitFix.Core.Fit;

/// <summary>Читає Еталонний трек із FIT (активність або course) — лише геометрія (PRD FR-2).</summary>
public static class FitTrackReader
{
    public static IReadOnlyList<TrackPoint> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var points = new List<TrackPoint>();

        FitDecoder.Decode(stream, broadcaster =>
        {
            broadcaster.RecordMesgEvent += (_, e) =>
            {
                var mesg = (RecordMesg)e.mesg;
                var latitude = FitDecoder.ToDegrees(mesg.GetPositionLat());
                var longitude = FitDecoder.ToDegrees(mesg.GetPositionLong());
                if (latitude is null || longitude is null)
                    return;

                points.Add(new TrackPoint(
                    new GeoPoint(latitude.Value, longitude.Value),
                    mesg.GetEnhancedAltitude() ?? mesg.GetAltitude()));
            };
        });

        if (points.Count < 2)
            throw new InvalidTrackFileException(
                DiagnosticCodes.TrackTooFewPoints, "The FIT track carries fewer than 2 positioned points.");

        return points;
    }

    public static IReadOnlyList<TrackPoint> ReadFile(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return Read(stream);
    }
}
