using FitFix.Core;

namespace FitFix.Core.Tests.Fixtures;

/// <summary>
/// Синтетична пробіжка, що повторює реальний кейс (CLAUDE.md): GPS коректний
/// першу частину активності, далі позиція «замерзає», хоча пульс і каденс показують рух.
/// Реальні файли приватні (tests/fixtures/private), тому базові тести — на цій фікстурі.
/// </summary>
public sealed class SyntheticRun
{
    public const double StartLatitude = 50.3333;
    public const double StartLongitude = 30.3833;

    /// <summary>Швидкість спортсмена, м/с (≈5:33/км).</summary>
    public const double SpeedMetersPerSecond = 3.0;

    /// <summary>Тривалість активності, с.</summary>
    public const int DurationSeconds = 2160;

    /// <summary>Момент, коли GPS зависає, с.</summary>
    public const int GpsFailureSeconds = 1300;

    /// <summary>Висота Оригіналу на старті, м.</summary>
    public const double OriginalStartAltitude = 182.8;

    /// <summary>Зсув висоти в треку (як у FFF.fit: −400.8 м).</summary>
    public const double TrackAltitudeOffset = -400.8;

    public required IReadOnlyList<TrackPoint> TrackPoints { get; init; }
    public required OriginalActivity Original { get; init; }
    public required double TrueRouteLength { get; init; }

    public ReferenceTrack ReferenceTrack => ReferenceTrack.FromPoints(TrackPoints);

    public static SyntheticRun Create(bool withGpsFailure = true, bool withAnyGps = true)
    {
        var route = BuildRectangleLoop(widthMeters: 1500, heightMeters: 1750, stepMeters: 5);
        var cumulative = Cumulative(route);
        var length = cumulative[^1];

        var trackPoints = route
            .Select((point, i) => new TrackPoint(point, OriginalStartAltitude + TrackAltitudeOffset + Hill(cumulative[i], length)))
            .ToArray();

        var noise = new Random(42);
        var records = new List<ActivityRecord>(DurationSeconds + 1);
        var startTime = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);

        GeoPoint? frozenPosition = null;
        double frozenDistance = 0;

        for (var second = 0; second <= DurationSeconds; second++)
        {
            var travelled = Math.Min(second * SpeedMetersPerSecond, length);
            var truePosition = PositionAt(route, cumulative, travelled);

            double? latitude = null;
            double? longitude = null;
            double? distance = null;
            double? speed = null;

            if (withAnyGps)
            {
                if (withGpsFailure && second >= GpsFailureSeconds)
                {
                    // GPS завис: позиція і дистанція пристрою стоять на місці.
                    frozenPosition ??= Jitter(truePosition, noise, 3);
                    frozenDistance = frozenDistance == 0 ? travelled : frozenDistance;
                    latitude = frozenPosition.Value.Latitude;
                    longitude = frozenPosition.Value.Longitude;
                    distance = frozenDistance;
                    speed = 0;
                }
                else
                {
                    var measured = Jitter(truePosition, noise, 3);
                    latitude = measured.Latitude;
                    longitude = measured.Longitude;
                    distance = travelled;
                    speed = SpeedMetersPerSecond;
                }
            }

            var inFailure = withGpsFailure && second >= GpsFailureSeconds;

            records.Add(new ActivityRecord
            {
                Timestamp = startTime.AddSeconds(second),
                Latitude = latitude,
                Longitude = longitude,
                Altitude = OriginalStartAltitude + Hill(travelled, length),
                Distance = distance,
                Speed = speed,
                HeartRate = (byte)(150 + second % 10),
                // Пристрій рахував потужність із GPS-швидкості — після збою вона нульова.
                Power = (ushort)(inFailure && withAnyGps ? 0 : 200),
                Cadence = 85,
                Temperature = 4
            });
        }

        return new SyntheticRun
        {
            TrackPoints = trackPoints,
            TrueRouteLength = length,
            Original = new OriginalActivity { Records = records, Sport = SportKind.Running }
        };
    }

    /// <summary>Прямокутна петля, дискретизована з заданим кроком.</summary>
    private static GeoPoint[] BuildRectangleLoop(double widthMeters, double heightMeters, double stepMeters)
    {
        var corners = new (double X, double Y)[]
        {
            (0, 0),
            (widthMeters, 0),
            (widthMeters, heightMeters),
            (0, heightMeters),
            (0, 0)
        };

        var points = new List<GeoPoint> { Offset(0, 0) };
        for (var i = 0; i < corners.Length - 1; i++)
        {
            var (x0, y0) = corners[i];
            var (x1, y1) = corners[i + 1];
            var segmentLength = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            var steps = (int)Math.Ceiling(segmentLength / stepMeters);
            for (var s = 1; s <= steps; s++)
            {
                var t = (double)s / steps;
                points.Add(Offset(x0 + t * (x1 - x0), y0 + t * (y1 - y0)));
            }
        }

        return [.. points];
    }

    private static GeoPoint Offset(double eastMeters, double northMeters)
    {
        var latitude = StartLatitude + northMeters / Geo.MetersPerDegreeLatitude;
        var longitude = StartLongitude
            + eastMeters / (Geo.MetersPerDegreeLatitude * Math.Cos(StartLatitude * Math.PI / 180.0));
        return new GeoPoint(latitude, longitude);
    }

    private static double[] Cumulative(GeoPoint[] points)
    {
        var cumulative = new double[points.Length];
        for (var i = 1; i < points.Length; i++)
            cumulative[i] = cumulative[i - 1] + Geo.Distance(points[i - 1], points[i]);
        return cumulative;
    }

    private static GeoPoint PositionAt(GeoPoint[] points, double[] cumulative, double distance)
    {
        var index = Array.BinarySearch(cumulative, distance);
        if (index < 0)
            index = ~index - 1;
        index = Math.Clamp(index, 0, points.Length - 2);

        var segment = cumulative[index + 1] - cumulative[index];
        var u = segment > 0 ? (distance - cumulative[index]) / segment : 0;
        return new GeoPoint(
            points[index].Latitude + u * (points[index + 1].Latitude - points[index].Latitude),
            points[index].Longitude + u * (points[index + 1].Longitude - points[index].Longitude));
    }

    private static GeoPoint Jitter(GeoPoint point, Random random, double metersRadius)
    {
        var dy = (random.NextDouble() * 2 - 1) * metersRadius;
        var dx = (random.NextDouble() * 2 - 1) * metersRadius;
        return new GeoPoint(
            point.Latitude + dy / Geo.MetersPerDegreeLatitude,
            point.Longitude + dx / (Geo.MetersPerDegreeLatitude * Math.Cos(point.Latitude * Math.PI / 180.0)));
    }

    /// <summary>Невеликий профіль висоти, щоб перевірити перенесення альтитуди.</summary>
    private static double Hill(double distance, double length) =>
        20 * Math.Sin(Math.PI * distance / Math.Max(1, length));
}
