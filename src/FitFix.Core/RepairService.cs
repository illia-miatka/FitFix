using FitFix.Core.Diagnostics;

namespace FitFix.Core;

/// <summary>
/// Ядро FitFix: бере час і сенсори з Оригіналу, геометрію з Еталонного треку
/// і будує Відновлену активність (PRD FR-5, FR-6).
/// </summary>
public static class RepairService
{
    /// <summary>
    /// Repairs the activity with the thresholds that suit its own sport, ending on the
    /// conservative length (AD-17). The overload exists so that a caller with nothing to say about
    /// the thresholds says so in the signature, instead of omitting an optional argument and
    /// silently getting different numbers (AD-10).
    /// </summary>
    public static RepairResult Repair(OriginalActivity original, ReferenceTrack track)
    {
        ArgumentNullException.ThrowIfNull(original);

        return Repair(original, track, AlignmentOptions.ForSport(original.Sport), TrackLengthMode.Plausible);
    }

    /// <summary>
    /// Repairs with the thresholds of the activity's own sport and the stated track-length mode —
    /// the overload a shell calls once the user has confirmed that an over-long track is right
    /// (AD-17). An explicit overload rather than an argument the previous one may omit: an omitted
    /// argument must not be able to change the distance (AD-10).
    /// </summary>
    public static RepairResult Repair(
        OriginalActivity original,
        ReferenceTrack track,
        TrackLengthMode trackLengthMode)
    {
        ArgumentNullException.ThrowIfNull(original);

        return Repair(original, track, AlignmentOptions.ForSport(original.Sport), trackLengthMode);
    }

    /// <summary>
    /// Repairs the activity with the given thresholds, the only tuning source (AD-10), ending on
    /// the conservative length (AD-17).
    /// </summary>
    public static RepairResult Repair(OriginalActivity original, ReferenceTrack track, AlignmentOptions options) =>
        Repair(original, track, options, TrackLengthMode.Plausible);

    /// <summary>
    /// Repairs the activity with the given thresholds and the given track-length mode — the one
    /// implementation every overload above arrives at, so no two entry points can disagree about
    /// the same files (AD-10).
    /// </summary>
    public static RepairResult Repair(
        OriginalActivity original,
        ReferenceTrack track,
        AlignmentOptions options,
        TrackLengthMode trackLengthMode)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(options);

        // The three thresholds the chosen length is computed from, checked where they are read.
        // AD-10 puts every threshold in the caller's hands, and these are the ones whose nonsense
        // values reach an invariant rather than merely a different number: a cap of nothing bounds
        // the activity at its anchors alone, which for an original without GPS is a chosen length
        // of zero and every record left on the start line. A caller's defect, so no declared code.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSpeedMetersPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegative(options.TrackLengthToleranceMeters);
        ArgumentOutOfRangeException.ThrowIfNegative(options.TrackLengthToleranceFraction);
        // A gap of nothing makes every stretch between two anchors estimated, so the report
        // would claim a measured share of zero about an activity the GPS covered.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.EstimatedZoneGapSeconds);

        // No elapsed time means no time axis to spread the track over: the interpolation would
        // be left with a single anchor. User data, so it refuses with a code (AD-18).
        if (original.ElapsedSeconds <= 0)
            throw new MalformedArgumentException(
                DiagnosticCodes.ActivityWithoutElapsedTime,
                $"The activity's records span no time at all ('{nameof(original)}').");

        // A pause list that leaves the activity no moving second cannot be true, and it is struck
        // out here — before a single moving second is computed — so the interpolation, the
        // smoothing, the summary and the report all read one answer (AD-7). Without it the
        // distance would be spread over a measure that never advances, and the whole track would
        // fall due at the finish.
        var believable = original.WithoutImpossiblePauses();

        // Cleaning comes next, so the compatibility check (FR-3), the anchor search and every
        // series a shell will show read the same cleaned original (AD-25). It is done here and
        // nowhere else, which is how every entry point inherits it.
        var (cleaned, discardedPositions) = GpsCleaner.Clean(believable, options);

        // The anchors first, then the length they bound, then the finish pinned to it (AD-17). The
        // measured distance is an input to the choice, so the alignment cannot be built in one
        // call: a length chosen without the anchors either ignores what the GPS proved or has to
        // guess at it. The stall detector reads the original as it arrived, because blanking a
        // burst would otherwise look like a stall.
        var measured = Aligner.Measure(cleaned, believable, track, options);
        var length = ChooseTrackLength(believable, track, measured, options, trackLengthMode);
        var alignment = Aligner.EndingAt(measured, cleaned.ElapsedSeconds, length.Used, options);

        var records = cleaned.Records;
        var times = new double[records.Count];
        var movingTimes = new double[records.Count];
        var paused = new bool[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            times[i] = cleaned.SecondsFromStart(records[i]);
            movingTimes[i] = cleaned.MovingSecondsFromStart(times[i]);
            paused[i] = cleaned.IsPaused(records[i].Timestamp);
        }

        // Distance is spread over moving time, never over the clock: inside a pause the measure
        // stops advancing, so a record there keeps the distance of the record that opened the
        // pause and the wait earns no metres (FR-5, AD-7). With no pauses this is the identity.
        var anchorMovingTimes = alignment.Anchors
            .Select(anchor => cleaned.MovingSecondsFromStart(anchor.TimeSeconds))
            .ToArray();

        // Which records the Original's own GPS spoke for, in the zones' own terms (AD-25, AD-26):
        // those keep the watch's geometry, and only the rest is drawn from the track.
        var measuredPosition = MeasuredPositions(times, alignment.Zones);

        // Where each record falls along the reference track. The estimated zones read both their
        // geometry and their increment from this series; a measured zone reads it only where the
        // watch left no position of its own (AD-26). It is this series, and not the repaired
        // distance, that the chosen track length bounds — the only metres taken from the track are
        // the ones the estimated zones draw from it (AD-17).
        var trackDistances = InterpolateDistances(movingTimes, anchorMovingTimes, alignment.Anchors);
        Smooth(trackDistances, times, PauseBlocks(cleaned), measuredPosition, options);
        EnforceMonotonicity(trackDistances, length.Used);

        // The repaired distance is a sum rather than a position along the track (AD-26): a measured
        // second is worth the metres between the watch's own two positions, an estimated one the
        // metres the track holds between the anchors bounding its zone. Monotonicity is verified
        // after the smoothing that precedes it, never instead of it (AD-8).
        var distances = Accumulate(records, trackDistances, movingTimes, measuredPosition);
        EnforceMonotonicity(distances);

        // Speed is a distance over the time the athlete was moving for, never over the clock, and
        // a record a pause covers states no speed at all (FR-5, AD-7).
        var speeds = ComputeSpeeds(distances, movingTimes, paused, options);
        var altitudeOffset = ComputeAltitudeOffset(cleaned, track);

        var repaired = new List<RepairedRecord>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var source = records[i];

            // The point of the story: where the watch was telling the truth, its own coordinates
            // and its own altitude stand — a route drawn in an editor cuts corners and does not
            // know which side of the path the athlete ran on (AD-26). A measured record the
            // cleaning left without a position has nothing of its own to keep, so it falls back to
            // the track like an estimated one.
            var own = measuredPosition[i] ? source.Position : null;
            var drawn = own is null ? track.At(trackDistances[i]) : default;

            repaired.Add(new RepairedRecord
            {
                Timestamp = source.Timestamp,
                Latitude = own?.Latitude ?? drawn.Position.Latitude,
                Longitude = own?.Longitude ?? drawn.Position.Longitude,
                Altitude = own is not null
                    ? source.Altitude
                    : drawn.Altitude is { } altitude ? altitude + altitudeOffset : source.Altitude,
                Distance = distances[i],
                Speed = speeds[i],
                HeartRate = source.HeartRate,
                Power = KeepPower(records, i, times, source.Power, options),
                Cadence = source.Cadence,
                Temperature = source.Temperature
            });
        }

        // Every summary number is settled here, once, for the activity and for each of its laps
        // (AD-16). The report below and the FIT writer both read these values; neither computes
        // one of its own, which is what keeps the file and the preview from stating two different
        // average heart rates for the same run.
        var summary = ActivitySummary.Of(repaired, cleaned, alignment, options);
        var total = summary.Total;

        var report = new RepairReport
        {
            // The device's own distance field, which cleaning never touches: the "before" number
            // has to stay what the user saw in Strava, and stay absent when the watch recorded
            // no distance at all.
            OriginalDistanceMeters = original.RecordedDistance,
            RepairedDistanceMeters = total.DistanceMeters,
            // Both lengths, in both modes: the user cannot choose between two numbers they are
            // never shown (AD-17).
            TrackLengthMeters = track.Length,
            UsedTrackLengthMeters = length.Used,
            // The span of the series the file was written from, so the report cannot state one
            // elapsed time while the session message states another.
            ElapsedSeconds = total.ElapsedSeconds,
            // Both from the same activity, the one whose pauses the whole pipeline believed:
            // taking the moving seconds from the summary and the paused ones from a list it
            // disbelieved would have the two add up past the elapsed time.
            PausedSeconds = cleaned.PausedSeconds,
            MovingSeconds = total.MovingSeconds,
            // The zones themselves, not a boundary: which stretches the position was measured over
            // is what a preview has to draw and what the athlete has to be able to read (FR-8).
            Zones = alignment.Zones,
            MeasuredFraction = summary.MeasuredFraction,
            OriginalPaceSecondsPerKm = summary.OriginalPaceSecondsPerKilometer,
            RepairedPaceSecondsPerKm = total.PaceSecondsPerKilometer,
            AnchorCount = alignment.Anchors.Count,
            GpsStallAtSeconds = alignment.GpsStallAtSeconds,
            DiscardedPositionCount = discardedPositions,
            // The laps the writer will transcribe, counted here so the report and the file agree
            // on how many there are (AD-24).
            LapCount = summary.Laps.Count,
            ElevationGainMeters = total.ElevationGainMeters,
            ElevationLossMeters = total.ElevationLossMeters,
            Warnings = WarningsOf(alignment, discardedPositions, length.TrackIsLonger)
        };

        return new RepairResult
        {
            Records = repaired,
            Report = report,
            Summary = summary,
            Alignment = alignment,
            CleanedOriginal = cleaned,
            Sport = original.Sport
        };
    }


    /// <summary>
    /// The codes the report carries: the alignment's own, preceded by <c>POSITIONS_DISCARDED</c>
    /// when cleaning threw anything away and by <c>TRACK_LONGER_THAN_ACTIVITY</c> when the track
    /// outruns the Original's own time (AD-18). Both of those are settled before the alignment, so
    /// both come first, and a shell can say why the map lost a point instead of leaving
    /// <c>NO_ORIGINAL_GPS</c> as the only hint after a full discard.
    /// <para>
    /// The over-long-track code is reported in <em>both</em> modes: trusting the track changes the
    /// result but does not make the track plausible, and a report that dropped the code would leave
    /// a shell unable to say which of the two lengths the file in front of the user ended on
    /// (AD-17).
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> WarningsOf(
        Alignment alignment,
        int discardedPositions,
        bool trackIsLonger)
    {
        if (discardedPositions == 0 && !trackIsLonger)
            return alignment.Warnings;

        var warnings = new List<string>(alignment.Warnings.Count + 2);
        if (discardedPositions > 0)
            warnings.Add(DiagnosticCodes.PositionsDiscarded);
        if (trackIsLonger)
            warnings.Add(DiagnosticCodes.TrackLongerThanActivity);
        warnings.AddRange(alignment.Warnings);
        return warnings;
    }

    /// <summary>
    /// The two lengths of AD-17 and the one the repair will use. <see cref="ChosenTrackLength.Used"/>
    /// is the whole track unless it outruns the plausible distance by more than the tolerance —
    /// and even then only while nobody has confirmed the track with
    /// <see cref="TrackLengthMode.Full"/>. It is never below the distance the anchors established:
    /// a shorter one would have the series reach its finish inside the measured zone and then hold
    /// the athlete still for the rest of the activity, which is worse than the inflated pace this
    /// whole decision exists to avoid.
    /// </summary>
    private static ChosenTrackLength ChooseTrackLength(
        OriginalActivity original,
        ReferenceTrack track,
        Alignment measured,
        AlignmentOptions options,
        TrackLengthMode mode)
    {
        // The anchors project onto the track, so this cannot exceed its length; clamped anyway,
        // because it becomes a floor below and no floor may lift the finish past the track's end.
        var anchored = Math.Clamp(measured.MeasuredDistanceMeters, 0, track.Length);
        var plausible = PlausibleDistance(original, measured, anchored, options);

        // A floor plus a share of the plausible distance (AD-10): the disagreement between a
        // sensor and a drawn route grows with the route, and a flat number of metres would warn
        // about every long ride.
        var tolerance = Math.Max(
            options.TrackLengthToleranceMeters,
            options.TrackLengthToleranceFraction * plausible);

        // Inside the tolerance the whole length stands as it is, so a correctly drawn route is
        // never trimmed by a metre and no number a plausible pairing produces moves.
        var longer = track.Length - plausible > tolerance;

        // Exhaustive on purpose: a cast, or a member a later story adds, must stop here rather
        // than fall through the boolean that the conservative default was supposed to close.
        var used = mode switch
        {
            TrackLengthMode.Plausible when longer => Math.Max(plausible, anchored),
            TrackLengthMode.Plausible => track.Length,
            TrackLengthMode.Full => track.Length,
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode), mode, $"Unknown {nameof(TrackLengthMode)}.")
        };

        return new ChosenTrackLength(Used: used, TrackIsLonger: longer);
    }

    /// <summary>
    /// How far the Original's own time says the athlete could have gone, by the priority AD-17
    /// fixes. First a sensor that measures movement without a fix — a footpod, a wheel sensor or a
    /// power meter the file declares: its furthest reading is a better witness than any cap.
    /// Otherwise what the anchors already measured <em>plus</em> the sport's maximum speed over the
    /// <em>moving</em> seconds that are left after them (FR-5, AD-7).
    /// <para>
    /// The remainder and not the whole activity: a cap spread over every second treats the stretch
    /// the GPS measured as if it too could have been run at 7 m/s, which on the Chabany case
    /// allows 15 km for a run that measured 3.9 km before the fix died and could have covered six
    /// more. Bounding only what is unknown is what makes the number generous without making it
    /// useless — its job is to catch a track twice too long, not to second-guess a plausible one.
    /// </para>
    /// <para>
    /// A sensor is believed only as far as it agrees with the anchors. One claiming less than the
    /// GPS proved is contradicting positions that can be pointed at, so it is set aside and the cap
    /// rule decides — which also keeps the athlete moving: a bound at the measured distance exactly
    /// would hold them at the finish for the whole estimated zone. A declared sensor that recorded
    /// nothing, or zero, has measured nothing at all and is set aside for the same reason.
    /// </para>
    /// </summary>
    private static double PlausibleDistance(
        OriginalActivity original,
        Alignment measured,
        double anchored,
        AlignmentOptions options)
    {
        if (original.DeclaresSpeedSensor
            && original.LargestRecordedDistance is > 0 and { } sensor
            && sensor >= anchored)
        {
            return sensor;
        }

        var movingTotal = original.MovingSecondsFromStart(original.ElapsedSeconds);
        var movingMeasured = original.MovingSecondsFromStart(measured.MeasuredUntilSeconds);

        return anchored + options.MaxSpeedMetersPerSecond * Math.Max(0, movingTotal - movingMeasured);
    }

    /// <summary>
    /// What the repair decided about the track's length: the length the series will end on, and
    /// whether the whole track outran the Original's own time far enough to warn about (AD-17).
    /// Two values and not one, because the warning stands even in the mode that ends on the whole
    /// length — confirming the track changes the result without making it plausible.
    /// </summary>
    private readonly record struct ChosenTrackLength(double Used, bool TrackIsLonger);

    /// <summary>
    /// Linear interpolation of distance between anchors, over the measure
    /// <paramref name="times"/> and <paramref name="anchorTimes"/> are stated in — moving
    /// seconds, so that a pause holds the distance flat (FR-5). Both the bucket and the fraction
    /// inside it are taken from <paramref name="anchorTimes"/>: an anchor's own
    /// <see cref="Anchor.TimeSeconds"/> is elapsed time, and mixing the two measures would
    /// divide moving seconds by an elapsed span and leave every record after a pause short.
    /// <paramref name="anchors"/> is read for distance alone.
    /// </summary>
    private static double[] InterpolateDistances(
        double[] times,
        double[] anchorTimes,
        IReadOnlyList<Anchor> anchors)
    {
        var result = new double[times.Length];

        for (var i = 0; i < times.Length; i++)
        {
            var index = UpperBound(anchorTimes, times[i]) - 1;
            index = Math.Clamp(index, 0, anchors.Count - 2);

            var span = anchorTimes[index + 1] - anchorTimes[index];
            var u = span > 0 ? (times[i] - anchorTimes[index]) / span : 0;
            result[i] = anchors[index].TrackDistance
                + u * (anchors[index + 1].TrackDistance - anchors[index].TrackDistance);
        }

        return result;
    }

    /// <summary>
    /// Which stretch between pause boundaries each record falls in, so a smoothing window can be
    /// clipped at the nearest boundary instead of carrying movement into a pause (AD-21). A
    /// boundary is a pause's own start or end, clipped to the activity: the record that opened a
    /// pause goes with the pause — it already holds the pause's distance — and the record that
    /// ends one opens the stretch that follows. An activity with no pauses is one stretch, which
    /// is what leaves the numbers of a pause-free file exactly where they were.
    /// </summary>
    private static int[] PauseBlocks(OriginalActivity activity)
    {
        var records = activity.Records;
        var blocks = new int[records.Count];
        if (activity.Pauses.Count == 0)
            return blocks;

        for (var i = 0; i < records.Count; i++)
            blocks[i] = activity.PauseSegment(records[i].Timestamp);

        return blocks;
    }

    /// <summary>
    /// Симетричне ковзне середнє в Розрахованих зонах, де лінійна інтерполяція дає злами на
    /// Якорях. Every estimated stretch is widened and not only the one after a single boundary,
    /// which left the kinks inside a dropout that later recovered exactly as sharp as the
    /// interpolation made them (AD-21). The window is clipped at pause boundaries, so the records
    /// before a pause are smoothed against their own side of it and no record inside one gains
    /// distance from the movement outside. Monotonicity is verified after this, never instead of
    /// it (AD-8).
    /// <para>
    /// A record whose position the GPS measured is left exactly as it is: smoothing it would put
    /// an approximation back where this story has just taken one away, and the jitter in a real
    /// position is what the watch saw (AD-26). Inside an estimated zone the series is an
    /// interpolation already, and smoothing it is what keeps the kinks out.
    /// </para>
    /// </summary>
    private static void Smooth(
        double[] distances,
        double[] times,
        int[] blocks,
        bool[] measuredPosition,
        AlignmentOptions options)
    {
        var raw = (double[])distances.Clone();

        for (var i = 0; i < distances.Length; i++)
        {
            if (measuredPosition[i])
                continue;

            var halfWidth = options.EstimatedZoneSmoothingSeconds;

            var low = i;
            while (low > 0 && times[i] - times[low - 1] <= halfWidth && blocks[low - 1] == blocks[i])
                low--;

            var high = i;
            while (high < distances.Length - 1 && times[high + 1] - times[i] <= halfWidth && blocks[high + 1] == blocks[i])
                high++;

            // Симетричне вікно — щоб згладжування не зсувало дистанцію вперед або назад.
            var reach = Math.Min(i - low, high - i);
            if (reach == 0)
                continue;

            var sum = 0.0;
            for (var j = i - reach; j <= i + reach; j++)
                sum += raw[j];
            distances[i] = sum / (2 * reach + 1);
        }
    }

    /// <summary>
    /// Which records keep the Original's own geometry: the ones inside a Виміряна зона (AD-26). A
    /// record on a zone's <see cref="TrustZone.ToSeconds"/> belongs to the zone that ends there,
    /// exactly as the smoothing reads it, so a boundary second is measured rather than estimated
    /// and the two kinds never claim it twice. An alignment with no zone at all measured nothing,
    /// so every record there is drawn from the track.
    /// </summary>
    private static bool[] MeasuredPositions(double[] times, IReadOnlyList<TrustZone> zones)
    {
        var measured = new bool[times.Length];
        if (zones.Count == 0)
            return measured;

        // The records rise through the activity and so do the zones, so the walk is one pass over
        // both rather than a search per record.
        var zone = 0;
        for (var i = 0; i < times.Length; i++)
        {
            while (zone < zones.Count - 1 && times[i] > zones[zone].ToSeconds)
                zone++;

            measured[i] = zones[zone].Kind == ZoneKind.Measured;
        }

        return measured;
    }

    /// <summary>
    /// The repaired distance series, metre by metre (AD-26). Each second is worth what its own
    /// source says it is worth: inside a Виміряна зона the geodesic distance between the watch's
    /// two positions, inside a Розрахована зона the increment the track holds across that second.
    /// The total is therefore a sum and need not equal the track's length — the inflation real GPS
    /// jitter adds on a measured stretch is the accepted price of real data.
    /// <para>
    /// An increment and never an absolute position along the track: taking the track's own
    /// cumulative figure at the start of an estimated zone, while the measured side had accrued
    /// its own, would show up as a jump of tens or hundreds of metres in one second. Adding only
    /// the difference leaves both sources' totals intact and meets in the middle.
    /// </para>
    /// <para>
    /// A second no moving time passed in earns nothing, in either kind of zone: inside a pause the
    /// watch's own jitter would otherwise buy metres the athlete stood still for (FR-5, AD-7). The
    /// measure is <paramref name="movingTimes"/>, the one owner of that arithmetic, rather than a
    /// second reading of the pause intervals here.
    /// </para>
    /// </summary>
    private static double[] Accumulate(
        IReadOnlyList<ActivityRecord> records,
        double[] trackDistances,
        double[] movingTimes,
        bool[] measuredPosition)
    {
        var distances = new double[records.Count];

        for (var i = 1; i < records.Count; i++)
        {
            double step;

            if (movingTimes[i] - movingTimes[i - 1] <= 0)
            {
                step = 0;
            }
            else if (measuredPosition[i]
                && records[i].Position is { } here
                && records[i - 1].Position is { } previous)
            {
                // The watch measured where it went, so the metres between the two fixes are the
                // athlete's own. Cleaning has already taken the impossible ones out (AD-25), and
                // the record on the far side of a seam is an anchor by construction.
                step = Geo.Distance(previous, here);
            }
            else
            {
                step = trackDistances[i] - trackDistances[i - 1];
            }

            distances[i] = distances[i - 1] + Math.Max(0, step);
        }

        return distances;
    }

    /// <summary>
    /// Гарантує AD-8: функція «час → дистанція» монотонно неспадна. The finish is no longer pinned
    /// to the chosen track length — under AD-26 the total is a sum of two sources and the track's
    /// length is not the answer it has to come to — so this is the running maximum and nothing
    /// else. Verified after the smoothing, never instead of it.
    /// </summary>
    private static void EnforceMonotonicity(double[] distances)
    {
        var running = 0.0;
        for (var i = 0; i < distances.Length; i++)
        {
            running = Math.Max(running, distances[i]);
            distances[i] = running;
        }
    }

    /// <summary>
    /// The same for the series that says where along the track a record falls, with the chosen
    /// length as a ceiling (AD-17): the estimated zones draw their metres from here, so a trimmed
    /// track takes the anchors the GPS measured past it down with it rather than leaving the
    /// geometry running off the end of the route.
    /// </summary>
    private static void EnforceMonotonicity(double[] trackDistances, double chosenTrackLength)
    {
        var running = 0.0;
        for (var i = 0; i < trackDistances.Length; i++)
        {
            running = Math.Clamp(Math.Max(running, trackDistances[i]), 0, chosenTrackLength);
            trackDistances[i] = running;
        }
    }

    /// <summary>
    /// The repaired speed of each record, over a window of
    /// <see cref="AlignmentOptions.SpeedWindowHalfWidthRecords"/> records either side.
    /// <paramref name="movingTimes"/> is moving seconds rather than the clock, so a window that
    /// reaches across a pause divides its metres by the seconds the athlete was actually moving
    /// and the records beside a pause keep the athlete's own pace (FR-5, AD-7).
    /// <para>
    /// A record a pause covers states a speed of zero, whatever its window spans. The window of a
    /// record just inside a pause still reaches the moving records outside it, and its moving
    /// seconds collapse to the one or two the window has left there — a full window of metres over
    /// a fraction of the time, which reads as the athlete sprinting while standing at a light.
    /// Zero is the only honest number for a second nothing moved in.
    /// </para>
    /// </summary>
    private static double[] ComputeSpeeds(
        double[] distances,
        double[] movingTimes,
        bool[] paused,
        AlignmentOptions options)
    {
        var speeds = new double[distances.Length];
        for (var i = 0; i < distances.Length; i++)
        {
            if (paused[i])
                continue;

            var from = Math.Max(0, i - options.SpeedWindowHalfWidthRecords);
            var to = Math.Min(distances.Length - 1, i + options.SpeedWindowHalfWidthRecords);
            var dt = movingTimes[to] - movingTimes[from];
            speeds[i] = dt > 0 ? Math.Max(0, (distances[to] - distances[from]) / dt) : 0;
        }

        return speeds;
    }

    /// <summary>Висота береться з треку зі зсувом, що вирівнює старт з Оригіналом (FR-6).</summary>
    private static double ComputeAltitudeOffset(OriginalActivity original, ReferenceTrack track)
    {
        var originalStart = original.Records.FirstOrDefault(r => r.Altitude is not null)?.Altitude;
        if (originalStart is null || track.StartAltitude is null)
            return 0;
        return originalStart.Value - track.StartAltitude.Value;
    }

    /// <summary>
    /// Zero power the device derived from a frozen GPS distance is not carried over (FR-6).
    /// The criterion is local: the device's own distance did not grow by
    /// <see cref="AlignmentOptions.DeviceDistanceGrowthMeters"/> within
    /// <see cref="AlignmentOptions.DeviceDistanceStallWindowSeconds"/>.
    /// </summary>
    private static ushort? KeepPower(
        IReadOnlyList<ActivityRecord> records,
        int index,
        double[] times,
        ushort? power,
        AlignmentOptions options)
    {
        if (power is not 0)
            return power;

        var current = records[index].Distance;
        if (current is null)
            return power;

        for (var j = index - 1; j >= 0; j--)
        {
            if (times[index] - times[j] > options.DeviceDistanceStallWindowSeconds)
                break;
            if (records[j].Distance is { } earlier && current.Value - earlier > options.DeviceDistanceGrowthMeters)
                return power;
        }

        return null;
    }

    private static int UpperBound(double[] sorted, double value)
    {
        var index = Array.BinarySearch(sorted, value);
        if (index < 0)
            return ~index;
        while (index < sorted.Length && sorted[index] <= value)
            index++;
        return index;
    }
}
