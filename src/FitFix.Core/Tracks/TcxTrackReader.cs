using System.Globalization;
using System.Xml.Linq;

namespace FitFix.Core.Tracks;

/// <summary>Читає Еталонний трек із TCX (Activities або Courses) (PRD FR-2).</summary>
public static class TcxTrackReader
{
    public static IReadOnlyList<TrackPoint> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = XDocument.Load(stream);

        var points = document.Descendants()
            .Where(e => e.Name.LocalName == "Trackpoint")
            .Select(ToTrackPoint)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToArray();

        if (points.Length < 2)
            throw new InvalidTrackFileException("У TCX менше 2 точок з координатами.");

        return points;
    }

    private static TrackPoint? ToTrackPoint(XElement element)
    {
        var position = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Position");
        if (position is null)
            return null;

        var latitude = ParseChild(position, "LatitudeDegrees");
        var longitude = ParseChild(position, "LongitudeDegrees");
        if (latitude is null || longitude is null)
            return null;

        return new TrackPoint(new GeoPoint(latitude.Value, longitude.Value), ParseChild(element, "AltitudeMeters"));
    }

    private static double? ParseChild(XElement parent, string localName)
    {
        var raw = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
