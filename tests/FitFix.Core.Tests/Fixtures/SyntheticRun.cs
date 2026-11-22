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

    /// <summary>When the athlete stops at the traffic light, s from the start — mid-route.</summary>
    public const int PauseStartSeconds = 900;

    /// <summary>How long the wait lasts, s: the five minutes of FR-5.</summary>
    public const int PauseSeconds = 300;

    /// <summary>Висота Оригіналу на старті, м.</summary>
    public const double OriginalStartAltitude = 182.8;

    /// <summary>Зсув висоти в треку (як у FFF.fit: −400.8 м).</summary>
    public const double TrackAltitudeOffset = -400.8;

    public required IReadOnlyList<TrackPoint> TrackPoints { get; init; }
    public required OriginalActivity Original { get; init; }
    public required double TrueRouteLength { get; init; }

    /// <summary>The options every test of this fixture uses — the run is a run (AD-10).</summary>
    public static AlignmentOptions Options => AlignmentOptions.ForSport(SportKind.Running);

    public ReferenceTrack ReferenceTrack => ReferenceTrack.FromPoints(TrackPoints, Options);

    /// <summary>The 0,0 position a watch emits when it reports a fix it does not have.</summary>
    public static GeoPoint NullIsland => new(0, 0);

    /// <summary>
    /// A point <paramref name="kilometres"/> from the run's start on the given bearing (0° north,
    /// 90° east), so a spike can be placed in any direction rather than always northwards. A
    /// distance that pushes the planar offset past a pole is refused: a latitude outside ±90 is
    /// not a place, and a test that asked for one would be measuring nothing.
    /// </summary>
    public static GeoPoint FarAway(double kilometres, double bearingDegrees)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kilometres);

        var radians = bearingDegrees * Math.PI / 180.0;
        var point = Offset(
            eastMeters: kilometres * 1000 * Math.Sin(radians),
            northMeters: kilometres * 1000 * Math.Cos(radians));

        if (Math.Abs(point.Latitude) > 90)
            throw new ArgumentOutOfRangeException(
                nameof(kilometres),
                kilometres,
                $"{kilometres:F0} km on bearing {bearingDegrees:F0}° leaves the latitude range.");

        return point;
    }

    /// <summary>
    /// A point offset from the run's start, in metres. Public so a test places a position with
    /// the fixture's own geometry instead of repeating the conversion.
    /// </summary>
    public static GeoPoint Offset(double eastMeters, double northMeters)
    {
        var latitude = StartLatitude + northMeters / Geo.MetersPerDegreeLatitude;
        var longitude = StartLongitude
            + eastMeters / (Geo.MetersPerDegreeLatitude * Math.Cos(StartLatitude * Math.PI / 180.0));
        return new GeoPoint(latitude, longitude);
    }

    /// <summary>
    /// The same run with the position of one record moved somewhere no athlete could have been —
    /// a watch that teleported for a single record (AD-25). Only that record's coordinates
    /// change, so every number the suite pins on the untouched run stays comparable.
    /// </summary>
    public SyntheticRun WithTeleportAt(int recordIndex, GeoPoint destination) =>
        WithTeleportBurst(recordIndex, 1, destination);

    /// <summary>
    /// The same run with <paramref name="records"/> consecutive positions moved to
    /// <paramref name="destination"/> — the burst a watch emits while it is still looking for a
    /// fix, which is the case a single look ahead cannot tell from a real start.
    /// </summary>
    public SyntheticRun WithTeleportBurst(int firstRecordIndex, int records, GeoPoint destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstRecordIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(records, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstRecordIndex + records, Original.Records.Count);

        var spiked = Original.Records.ToArray();
        for (var index = firstRecordIndex; index < firstRecordIndex + records; index++)
        {
            // A record with no position has nothing to teleport, and a test that asked for one
            // is testing something other than what it believes.
            if (spiked[index].Position is null)
                throw new ArgumentException(
                    $"Record {index} carries no position, so it cannot hold a spike.",
                    nameof(firstRecordIndex));

            spiked[index] = spiked[index] with
            {
                Latitude = destination.Latitude,
                Longitude = destination.Longitude
            };
        }

        return new SyntheticRun
        {
            TrackPoints = TrackPoints,
            TrueRouteLength = TrueRouteLength,
            Original = Original with { Records = spiked }
        };
    }

    /// <summary>
    /// The same run with every position replaced by somewhere impossible — a watch that never
    /// found a fix and wrote garbage all the way through. Cleaning takes the lot, so the series is
    /// one that had positions and has none left, which is a different file from one that never
    /// carried a position at all (AD-25). The records, their timestamps and their sensors are the
    /// run's own, so only the coordinates differ from the untouched fixture.
    /// </summary>
    public SyntheticRun WithNothingButGarbage()
    {
        var noise = new Random(3);
        var garbage = Original.Records
            .Select(record =>
            {
                var somewhere = FarAway(500 + noise.NextDouble() * 2_000, noise.NextDouble() * 360);
                return record with { Latitude = somewhere.Latitude, Longitude = somewhere.Longitude };
            });

        return new SyntheticRun
        {
            TrackPoints = TrackPoints,
            TrueRouteLength = TrueRouteLength,
            Original = Original with { Records = [.. garbage] }
        };
    }

    /// <summary>
    /// How many times the loop has to be drawn before the reference track runs past twice what the
    /// run's own time makes plausible (AD-17): the running cap of 7 m/s over
    /// <see cref="DurationSeconds"/> allows 15.1 km, and five laps of the 6.5 km course are
    /// 32.7 km. Four laps would raise the warning too — this is the "twice too long" of the
    /// architecture decision, not the smallest track that trips it.
    /// </summary>
    public const int LoopsPastTwiceThePlausibleDistance = 5;

    /// <summary>
    /// The same run, the same athlete and the same records, with the reference track drawn
    /// <paramref name="times"/> times round the loop — the route editor's extra lap, taken far
    /// enough to be caught (AD-17). The course closes on its own start, so the repetitions join
    /// into one continuous track and the duplicate point at each seam is dropped by the track's
    /// own de-duplication; the length is therefore <paramref name="times"/> times the loop's.
    /// <para>
    /// Only the track changes, so the Original's GPS still describes the first lap and every
    /// anchor the untouched fixture finds is still found here.
    /// </para>
    /// </summary>
    public SyntheticRun WithTrackDrawn(int times)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 2);

        return new SyntheticRun
        {
            TrackPoints = [.. Enumerable.Repeat(TrackPoints, times).SelectMany(points => points)],
            TrueRouteLength = TrueRouteLength * times,
            Original = Original
        };
    }

    /// <summary>
    /// The same run with the athlete's own laps marked: one lap ending at each of
    /// <paramref name="secondsFromStart"/>, and a last lap running to the end of the activity —
    /// so three unequal laps are <c>WithLapsEndingAt(300, 1500)</c>. Only the lap intervals are
    /// added, so every number the suite pins on the unlapped run stays comparable.
    /// </summary>
    public SyntheticRun WithLapsEndingAt(params int[] secondsFromStart)
    {
        ArgumentNullException.ThrowIfNull(secondsFromStart);
        if (secondsFromStart.Length == 0)
            throw new ArgumentException("A lapped run needs at least one lap boundary.", nameof(secondsFromStart));

        var elapsed = Original.ElapsedSeconds;
        var previous = 0;
        foreach (var second in secondsFromStart)
        {
            // Boundaries out of order, or outside the activity, would be testing the partition's
            // repair of a malformed file while claiming to test an ordinary lapped run.
            if (second <= previous || second >= elapsed)
                throw new ArgumentOutOfRangeException(
                    nameof(secondsFromStart),
                    second,
                    $"Lap boundaries must rise through the activity's {elapsed:F0} s.");

            previous = second;
        }

        var start = Original.StartTime;
        var laps = new List<LapInterval>(secondsFromStart.Length + 1);
        var from = start;
        foreach (var second in secondsFromStart)
        {
            laps.Add(new LapInterval(from, start.AddSeconds(second)));
            from = start.AddSeconds(second);
        }

        laps.Add(new LapInterval(from, Original.EndTime));

        return new SyntheticRun
        {
            TrackPoints = TrackPoints,
            TrueRouteLength = TrueRouteLength,
            Original = Original with { Laps = laps }
        };
    }

    /// <summary>
    /// The same run with a heart rate and a cadence of its own in every lap — the athlete working
    /// harder in each one. Without it the fixture cannot tell a per-lap average from the whole
    /// activity's: the unmodified run's heart rate cycles through ten values and its cadence never
    /// changes, so every window averages to the same number. A record on a boundary belongs to the
    /// earlier lap, which is the lap whose sensor window the writer puts it in.
    /// </summary>
    public SyntheticRun WithSensorsPerLap()
    {
        var partition = Original.LapPartition();

        var records = Original.Records.Select(record =>
        {
            var lap = 0;
            while (lap < partition.Count - 1 && record.Timestamp > partition[lap].End)
                lap++;

            return record with
            {
                HeartRate = (byte)(HeartRatePerLap + LapHeartRateStep * lap),
                Cadence = (byte)(CadencePerLap + LapCadenceStep * lap)
            };
        });

        return new SyntheticRun
        {
            TrackPoints = TrackPoints,
            TrueRouteLength = TrueRouteLength,
            Original = Original with { Records = [.. records] }
        };
    }

    /// <summary>
    /// The same run with the heart rate of every paused record dropped to
    /// <see cref="RestingHeartRate"/> — what a watch records while the athlete waits at a light,
    /// and what tells an average over the moving records from an average over all of them. The
    /// unmodified paused run cannot: its heart rate cycles through the same ten values inside the
    /// pause as outside it, so both means round to the very same byte and a test on them would
    /// pass whichever records the average was taken over.
    /// </summary>
    public SyntheticRun WithRestingHeartRateWhilePaused()
    {
        var records = Original.Records.Select(record => Original.IsPaused(record.Timestamp)
            ? record with { HeartRate = RestingHeartRate }
            : record);

        return new SyntheticRun
        {
            TrackPoints = TrackPoints,
            TrueRouteLength = TrueRouteLength,
            Original = Original with { Records = [.. records] }
        };
    }

    /// <summary>The heart rate <see cref="WithRestingHeartRateWhilePaused"/> writes, bpm.</summary>
    public const byte RestingHeartRate = 110;

    /// <summary>Heart rate in the first lap of <see cref="WithSensorsPerLap"/>, and the step per lap.</summary>
    public const int HeartRatePerLap = 140;

    /// <inheritdoc cref="HeartRatePerLap"/>
    public const int LapHeartRateStep = 10;

    /// <summary>Cadence in the first lap of <see cref="WithSensorsPerLap"/>, and the step per lap.</summary>
    public const int CadencePerLap = 80;

    /// <inheritdoc cref="CadencePerLap"/>
    public const int LapCadenceStep = 5;

    /// <summary>
    /// The same run under smart recording: one record every <paramref name="everySeconds"/>
    /// instead of one a second, which is how a watch that writes a point when something changes
    /// records a steady run. The first and last records are always kept, so the span is the dense
    /// run's and a lap boundary between two records is the real thing rather than a fraction of a
    /// second on a 1 Hz series.
    /// </summary>
    public SyntheticRun WithRecordsEvery(int everySeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(everySeconds, 2);

        var all = Original.Records;
        var sparse = new List<ActivityRecord>((all.Count / everySeconds) + 2);
        for (var index = 0; index < all.Count; index += everySeconds)
            sparse.Add(all[index]);

        if (sparse[^1].Timestamp != all[^1].Timestamp)
            sparse.Add(all[^1]);

        return new SyntheticRun
        {
            TrackPoints = TrackPoints,
            TrueRouteLength = TrueRouteLength,
            Original = Original with { Records = sparse }
        };
    }

    /// <summary>
    /// The clock every fixture starts at. One value, so a second means the same thing in all of
    /// them and the two runs below can be compared second by second.
    /// </summary>
    public static DateTime StartTime => new(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);

    /// <summary>The route and the reference geometry both runs share.</summary>
    private sealed record Course(GeoPoint[] Points, double[] Cumulative, double Length, TrackPoint[] TrackPoints);

    /// <summary>
    /// Builds that geometry: the rectangular loop, its cumulative distances and the track points
    /// with the altitude offset of the real case. Shared, so neither run can drift onto a route
    /// the other does not know — the pinned numbers only mean something while both run this one.
    /// </summary>
    private static Course BuildCourse()
    {
        var points = BuildRectangleLoop(widthMeters: 1500, heightMeters: 1750, stepMeters: 5);
        var cumulative = Cumulative(points);
        var length = cumulative[^1];
        var trackPoints = points
            .Select((point, i) => new TrackPoint(point, OriginalStartAltitude + TrackAltitudeOffset + Hill(cumulative[i], length)))
            .ToArray();

        return new Course(points, cumulative, length, trackPoints);
    }

    /// <summary>The one seeded noise source, so the GPS jitter is the same sequence in both runs.</summary>
    private static Random Noise() => new(42);

    /// <summary>What the watch reports for a point on the course: the truth plus 3 m of jitter.</summary>
    private static GeoPoint Measured(Course course, double travelled, Random noise) =>
        Jitter(PositionAt(course.Points, course.Cumulative, travelled), noise, 3);

    public static SyntheticRun Create(bool withGpsFailure = true, bool withAnyGps = true)
    {
        var course = BuildCourse();
        var length = course.Length;

        var noise = Noise();
        var records = new List<ActivityRecord>(DurationSeconds + 1);
        var startTime = StartTime;

        GeoPoint? frozenPosition = null;
        double frozenDistance = 0;

        for (var second = 0; second <= DurationSeconds; second++)
        {
            var travelled = Math.Min(second * SpeedMetersPerSecond, length);

            double? latitude = null;
            double? longitude = null;
            double? distance = null;
            double? speed = null;

            if (withAnyGps)
            {
                if (withGpsFailure && second >= GpsFailureSeconds)
                {
                    // GPS завис: позиція і дистанція пристрою стоять на місці.
                    frozenPosition ??= Measured(course, travelled, noise);
                    frozenDistance = frozenDistance == 0 ? travelled : frozenDistance;
                    latitude = frozenPosition.Value.Latitude;
                    longitude = frozenPosition.Value.Longitude;
                    distance = frozenDistance;
                    speed = 0;
                }
                else
                {
                    var measured = Measured(course, travelled, noise);
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
            TrackPoints = course.TrackPoints,
            TrueRouteLength = length,
            Original = new OriginalActivity { Records = records, Sport = SportKind.Running }
        };
    }

    /// <summary>How long each dropout of <see cref="CreateWithGpsReturning"/> lasts, s.</summary>
    public const int GpsDropoutSeconds = 45;

    /// <summary>
    /// When the first and the second dropout of <see cref="CreateWithGpsReturning"/> begin, s from
    /// the start. Far enough apart that three measured stretches separate them, and short enough
    /// that the athlete covers less ground inside one than
    /// <see cref="AlignmentOptions.SearchWindowForward"/> segments of the track reach — otherwise
    /// the greedy anchor chain could not pick the route back up and the "recovery" would be a
    /// dropout that never ends.
    /// </summary>
    public const int FirstGpsDropoutSeconds = 400;

    /// <inheritdoc cref="FirstGpsDropoutSeconds"/>
    public const int SecondGpsDropoutSeconds = 1000;

    /// <summary>
    /// Whether <see cref="CreateWithGpsReturning"/> wrote no position for this second. The ends of
    /// a dropout are fixes: that is where the anchors on either side of the gap are measured, and
    /// the gap between them is <see cref="GpsDropoutSeconds"/> exactly.
    /// </summary>
    public static bool WithoutAFixAt(int second) =>
        (second > FirstGpsDropoutSeconds && second < FirstGpsDropoutSeconds + GpsDropoutSeconds)
        || (second > SecondGpsDropoutSeconds && second < SecondGpsDropoutSeconds + GpsDropoutSeconds);

    /// <summary>
    /// The same route and athlete as <see cref="Create(bool, bool)"/>, with the GPS dropping out
    /// twice and coming back both times: the watch writes records throughout — the sensors, the
    /// clock and its own distance are unbroken — but no position at all inside the two windows
    /// <see cref="WithoutAFixAt"/> describes. The real failure mode, and the one a single
    /// "measured until" cannot describe: the signal returns, and the kilometres after the first
    /// dropout were measured as honestly as the ones before it (FR-8).
    /// </summary>
    /// <remarks>
    /// Nothing freezes here, so <see cref="GpsStallDetector"/> finds no stall: the positions either
    /// side of a dropout are 135 m apart. A stall would discard every anchor after it and the
    /// fixture would have one measured stretch rather than three.
    /// </remarks>
    public static SyntheticRun CreateWithGpsReturning()
    {
        var course = BuildCourse();
        var length = course.Length;

        var noise = Noise();
        var records = new List<ActivityRecord>(DurationSeconds + 1);

        for (var second = 0; second <= DurationSeconds; second++)
        {
            var travelled = Math.Min(second * SpeedMetersPerSecond, length);
            // Drawn every second, dropout or not, so the jitter is the same sequence as the other
            // fixtures' and a position here is the position the honest run reported.
            var measured = Measured(course, travelled, noise);
            var withoutAFix = WithoutAFixAt(second);

            records.Add(new ActivityRecord
            {
                Timestamp = StartTime.AddSeconds(second),
                Latitude = withoutAFix ? null : measured.Latitude,
                Longitude = withoutAFix ? null : measured.Longitude,
                Altitude = OriginalStartAltitude + Hill(travelled, length),
                Distance = travelled,
                Speed = SpeedMetersPerSecond,
                HeartRate = (byte)(150 + second % 10),
                Power = 200,
                Cadence = 85,
                Temperature = 4
            });
        }

        return new SyntheticRun
        {
            TrackPoints = course.TrackPoints,
            TrueRouteLength = length,
            Original = new OriginalActivity { Records = records, Sport = SportKind.Running }
        };
    }

    /// <summary>The cyclist's speed of <see cref="CreateRide"/>, m/s — 54 km/h.</summary>
    public const double RideSpeedMetersPerSecond = 15;

    /// <summary>How often the ride's watch writes a record, s.</summary>
    public const int RideSecondsBetweenRecords = 10;

    /// <summary>
    /// The same course ridden rather than run: a record every ten seconds, 150 m apart. Nothing in
    /// it is impossible at the cycling cap of 25 m/s and every step of it is impossible at the
    /// running cap of 7 m/s, so a reader that judged this file by the running profile would throw
    /// its whole route away while one that judged it by its own sport keeps every position. That
    /// is what makes it the fixture for any claim about per-sport thresholds: with a run,
    /// <see cref="AlignmentOptions.ForSport"/> and the defaults are the same numbers.
    /// </summary>
    public static SyntheticRun CreateRide()
    {
        var course = BuildCourse();
        var noise = Noise();
        var records = new List<ActivityRecord>();

        for (var second = 0; second * RideSpeedMetersPerSecond <= course.Length; second += RideSecondsBetweenRecords)
        {
            var travelled = second * RideSpeedMetersPerSecond;
            var measured = Measured(course, travelled, noise);

            records.Add(new ActivityRecord
            {
                Timestamp = StartTime.AddSeconds(second),
                Latitude = measured.Latitude,
                Longitude = measured.Longitude,
                Altitude = OriginalStartAltitude + Hill(travelled, course.Length),
                Distance = travelled,
                Speed = RideSpeedMetersPerSecond,
                HeartRate = (byte)(140 + second % 10),
                Power = 250,
                Cadence = 90,
                Temperature = 4
            });
        }

        return new SyntheticRun
        {
            TrackPoints = course.TrackPoints,
            TrueRouteLength = course.Length,
            Original = new OriginalActivity { Records = records, Sport = SportKind.Cycling }
        };
    }

    /// <summary>
    /// The same route and the same athlete as <see cref="Create(bool, bool)"/>, with the timer stopped for
    /// <paramref name="pauseSeconds"/> at <paramref name="pauseStartSecond"/>: the watch keeps
    /// writing records while the athlete stands still, so the position, the device's own distance
    /// and the cadence stay where they were and the elapsed time grows while the moving time does
    /// not. Moving time is therefore <see cref="DurationSeconds"/> exactly, which is what makes
    /// the distances comparable with the pause-free run's.
    /// </summary>
    /// <remarks>
    /// The GPS never fails here: the only thing to measure is the pause. The immobility of the
    /// standing stretch is not read as a frozen GPS, because <see cref="GpsStallDetector"/> skips
    /// records that fall inside a pause — without that, a traffic-light wait would report
    /// <c>GPS_STALL_DETECTED</c> and drop the whole run after it into the estimated zone.
    /// </remarks>
    public static SyntheticRun CreateWithPause(int pauseStartSecond, int pauseSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pauseStartSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pauseSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pauseStartSecond, DurationSeconds);

        var course = BuildCourse();
        var length = course.Length;

        var noise = Noise();
        var startTime = StartTime;
        var elapsedSeconds = DurationSeconds + pauseSeconds;
        var pauseEndSecond = pauseStartSecond + pauseSeconds;

        var records = new List<ActivityRecord>(elapsedSeconds + 1);
        GeoPoint? standingAt = null;
        var standingDistance = 0.0;
        var standingAltitude = 0.0;

        for (var second = 0; second <= elapsedSeconds; second++)
        {
            // Inside the pause nothing moves. The record that opens it and the one that ends it
            // are moving records: that is where the two ends of the pause are measured.
            var paused = second > pauseStartSecond && second < pauseEndSecond;
            if (paused)
            {
                records.Add(new ActivityRecord
                {
                    Timestamp = startTime.AddSeconds(second),
                    Latitude = standingAt!.Value.Latitude,
                    Longitude = standingAt.Value.Longitude,
                    Altitude = standingAltitude,
                    Distance = standingDistance,
                    Speed = 0,
                    HeartRate = (byte)(150 + second % 10),
                    Power = 0,
                    // Standing still: the detector reads a cadence of zero as the real pause it
                    // is rather than as a frozen GPS.
                    Cadence = 0,
                    Temperature = 4
                });
                continue;
            }

            var moving = second <= pauseStartSecond ? second : second - pauseSeconds;
            var travelled = Math.Min(moving * SpeedMetersPerSecond, length);
            var measured = Measured(course, travelled, noise);
            var altitude = OriginalStartAltitude + Hill(travelled, length);

            if (second == pauseStartSecond)
            {
                standingAt = measured;
                standingDistance = travelled;
                standingAltitude = altitude;
            }

            records.Add(new ActivityRecord
            {
                Timestamp = startTime.AddSeconds(second),
                Latitude = measured.Latitude,
                Longitude = measured.Longitude,
                Altitude = altitude,
                Distance = travelled,
                Speed = SpeedMetersPerSecond,
                HeartRate = (byte)(150 + second % 10),
                Power = 200,
                Cadence = 85,
                Temperature = 4
            });
        }

        return new SyntheticRun
        {
            TrackPoints = course.TrackPoints,
            TrueRouteLength = length,
            Original = new OriginalActivity
            {
                Records = records,
                Sport = SportKind.Running,
                Pauses = [new PauseInterval(
                    startTime.AddSeconds(pauseStartSecond),
                    startTime.AddSeconds(pauseEndSecond))]
            }
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
