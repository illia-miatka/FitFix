using System.Globalization;
using System.Xml.Linq;
using FitFix.Core.Diagnostics;

namespace FitFix.Core.Tracks;

/// <summary>Читає Еталонний трек із GPX — track (trkpt) або route (rtept) (PRD FR-2).</summary>
public static class GpxTrackReader
{
    public static IReadOnlyList<TrackPoint> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = LoadGpx(stream);

        // Ігноруємо namespace: GPX буває 1.0 і 1.1, а експортери додають свої простори.
        var points = document.Descendants()
            .Where(e => e.Name.LocalName is "trkpt" or "rtept")
            .Select(ToTrackPoint)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToArray();

        if (points.Length < 2)
            throw new InvalidTrackFileException(
                DiagnosticCodes.TrackTooFewPoints, "The GPX file carries fewer than 2 positioned points.");

        return points;
    }

    /// <summary>Broken XML is a refusal with a code, not an exception without one (AD-18).</summary>
    private static XDocument LoadGpx(Stream stream)
    {
        try
        {
            return XDocument.Load(stream);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new InvalidTrackFileException(
                DiagnosticCodes.TrackFormatUnknown, "The GPX file is not well-formed XML.", exception);
        }
    }

    private static TrackPoint? ToTrackPoint(XElement element)
    {
        var latitude = ParseAttribute(element, "lat");
        var longitude = ParseAttribute(element, "lon");
        if (latitude is null || longitude is null)
            return null;

        var elevation = element.Elements().FirstOrDefault(e => e.Name.LocalName == "ele")?.Value;
        double? altitude = double.TryParse(elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

        return new TrackPoint(new GeoPoint(latitude.Value, longitude.Value), altitude);
    }

    private static double? ParseAttribute(XElement element, string name)
    {
        var raw = element.Attribute(name)?.Value;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
