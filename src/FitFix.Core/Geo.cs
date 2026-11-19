namespace FitFix.Core;

/// <summary>Географічна точка у градусах.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>
/// Локальна планарна (equirectangular) геометрія. На масштабах однієї активності
/// похибка відносно геодезичної формули — частки проміле, а проєкція на сегмент
/// лишається простою і швидкою.
/// </summary>
public static class Geo
{
    public const double MetersPerDegreeLatitude = 111_320.0;

    /// <summary>Переводить semicircles (одиниці FIT) у градуси.</summary>
    public const double SemicirclesToDegrees = 180.0 / 2147483648.0;

    public static double Distance(GeoPoint a, GeoPoint b)
    {
        var midLatitude = (a.Latitude + b.Latitude) / 2.0;
        var dy = (a.Latitude - b.Latitude) * MetersPerDegreeLatitude;
        var dx = (a.Longitude - b.Longitude) * MetersPerDegreeLatitude * Math.Cos(midLatitude * Math.PI / 180.0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Проєкція точки <paramref name="point"/> на відрізок [<paramref name="start"/>, <paramref name="end"/>].
    /// Повертає відстань до відрізка в метрах і параметр t ∈ [0, 1] вздовж нього.
    /// </summary>
    public static (double Deviation, double T) ProjectOnSegment(GeoPoint point, GeoPoint start, GeoPoint end)
    {
        var cosLatitude = Math.Cos(start.Latitude * Math.PI / 180.0);
        var bx = (end.Longitude - start.Longitude) * MetersPerDegreeLatitude * cosLatitude;
        var by = (end.Latitude - start.Latitude) * MetersPerDegreeLatitude;
        var px = (point.Longitude - start.Longitude) * MetersPerDegreeLatitude * cosLatitude;
        var py = (point.Latitude - start.Latitude) * MetersPerDegreeLatitude;

        var lengthSquared = bx * bx + by * by;
        if (lengthSquared <= 0)
            return (Math.Sqrt(px * px + py * py), 0.0);

        var t = Math.Clamp((px * bx + py * by) / lengthSquared, 0.0, 1.0);
        var dx = px - t * bx;
        var dy = py - t * by;
        return (Math.Sqrt(dx * dx + dy * dy), t);
    }
}
