namespace FitFix.Core;

/// <summary>Позиція вздовж Еталонного треку на заданій кумулятивній дистанції.</summary>
public readonly record struct TrackPosition(GeoPoint Position, double? Altitude);

/// <summary>
/// Еталонний трек: дедуплікована послідовність точок із кумулятивною дистанцією.
/// Часові мітки джерела ігноруються (PRD FR-2) — трек це суто геометрія.
/// </summary>
public sealed class ReferenceTrack
{
    private readonly GeoPoint[] _points;
    private readonly double?[] _altitudes;
    private readonly double[] _cumulative;

    private ReferenceTrack(GeoPoint[] points, double?[] altitudes, double[] cumulative)
    {
        _points = points;
        _altitudes = altitudes;
        _cumulative = cumulative;
    }

    public IReadOnlyList<GeoPoint> Points => _points;
    public IReadOnlyList<double> Cumulative => _cumulative;

    /// <summary>Повна довжина треку в метрах.</summary>
    public double Length => _cumulative[^1];

    public int SegmentCount => _points.Length - 1;

    public double? StartAltitude => _altitudes[0];

    /// <summary>
    /// Будує трек, відкидаючи точки, що стоять ближче ніж <paramref name="minSpacingMeters"/>
    /// до попередньої (годинники й редактори часто дублюють координати).
    /// </summary>
    public static ReferenceTrack FromPoints(IEnumerable<TrackPoint> source, double minSpacingMeters = 0.3)
    {
        ArgumentNullException.ThrowIfNull(source);

        var points = new List<GeoPoint>();
        var altitudes = new List<double?>();

        foreach (var point in source)
        {
            if (points.Count > 0 && Geo.Distance(point.Position, points[^1]) <= minSpacingMeters)
                continue;
            points.Add(point.Position);
            altitudes.Add(point.Altitude);
        }

        if (points.Count < 2)
            throw new ArgumentException("Еталонний трек має містити принаймні 2 різні точки.", nameof(source));

        var cumulative = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
            cumulative[i] = cumulative[i - 1] + Geo.Distance(points[i - 1], points[i]);

        return new ReferenceTrack([.. points], [.. altitudes], cumulative);
    }

    /// <summary>Позиція на треку за кумулятивною дистанцією (лінійна інтерполяція вздовж сегмента).</summary>
    public TrackPosition At(double distance)
    {
        var clamped = Math.Clamp(distance, 0, Length);
        var segment = FindSegment(clamped);
        var segmentLength = _cumulative[segment + 1] - _cumulative[segment];
        var u = segmentLength > 0 ? (clamped - _cumulative[segment]) / segmentLength : 0;

        var a = _points[segment];
        var b = _points[segment + 1];
        var position = new GeoPoint(
            a.Latitude + u * (b.Latitude - a.Latitude),
            a.Longitude + u * (b.Longitude - a.Longitude));

        double? altitude = _altitudes[segment] is { } altA && _altitudes[segment + 1] is { } altB
            ? altA + u * (altB - altA)
            : _altitudes[segment];

        return new TrackPosition(position, altitude);
    }

    /// <summary>
    /// Шукає найближчу точку треку до <paramref name="point"/> у вікні сегментів
    /// [<paramref name="fromSegment"/>, <paramref name="toSegment"/>]. Вікно робить пошук
    /// монотонним: на треку-петлі ми не стрибаємо назад на те саме місце.
    /// </summary>
    public TrackProjection Project(GeoPoint point, int fromSegment, int toSegment)
    {
        var start = Math.Max(0, fromSegment);
        var end = Math.Min(toSegment, SegmentCount - 1);

        var best = new TrackProjection(double.MaxValue, 0, start);
        for (var k = start; k <= end; k++)
        {
            var (deviation, t) = Geo.ProjectOnSegment(point, _points[k], _points[k + 1]);
            if (deviation >= best.Deviation)
                continue;
            var segmentLength = _cumulative[k + 1] - _cumulative[k];
            best = new TrackProjection(deviation, _cumulative[k] + t * segmentLength, k);
        }

        return best;
    }

    /// <summary>Найменша відстань від точки до всього треку — для перевірки сумісності (FR-3).</summary>
    public double MinDeviationToWholeTrack(GeoPoint point) => Project(point, 0, SegmentCount - 1).Deviation;

    private int FindSegment(double distance)
    {
        var index = Array.BinarySearch(_cumulative, distance);
        if (index < 0)
            index = ~index - 1;
        return Math.Clamp(index, 0, SegmentCount - 1);
    }
}

/// <summary>Результат проєкції: відхилення в метрах, дистанція вздовж треку, індекс сегмента.</summary>
public readonly record struct TrackProjection(double Deviation, double TrackDistance, int SegmentIndex);
