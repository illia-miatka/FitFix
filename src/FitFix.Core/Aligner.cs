using FitFix.Core.Diagnostics;

namespace FitFix.Core;

/// <summary>
/// Будує Вирівнювання: з GPS-точок Оригіналу набирає монотонний ланцюжок Якорів
/// на Еталонному треку (PRD FR-4).
/// <para>
/// In two steps, because the two depend on each other's inputs and not on each other's outputs:
/// <see cref="Measure"/> finds the anchors the Original's own GPS establishes, and
/// <see cref="EndingAt"/> pins the finish to the length the repair chose from them (AD-17). One
/// call could not do both — the chosen length is computed from the measured distance.
/// </para>
/// </summary>
public static class Aligner
{
    /// <summary>
    /// Builds the measured part of the alignment from <paramref name="original"/>, the cleaned
    /// series (AD-25), while the stall detector reads <paramref name="asRecorded"/> — the series as
    /// the watch wrote it. The detector walks positioned records only, so a blanked burst would
    /// bridge its window and manufacture a stall that never happened, sending every later anchor
    /// into the estimated zone. Taking both series is what makes that mistake impossible from the
    /// outside.
    /// <para>
    /// The result carries no finish: its last anchor is the last one the GPS measured. Hand it to
    /// <see cref="EndingAt"/> to close the activity on a length.
    /// </para>
    /// </summary>
    public static Alignment Measure(
        OriginalActivity original,
        OriginalActivity asRecorded,
        ReferenceTrack track,
        AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(asRecorded);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();

        var positioned = original.Records.Where(r => r.Position is not null).ToArray();

        // The model owns the question, so a shell describing the same series before any repair
        // cannot answer it differently (AD-15).
        if (!original.HasPosition)
            warnings.Add(DiagnosticCodes.NoOriginalGps);
        else if (track.MinDeviationToWholeTrack(positioned[0].Position!.Value) > options.TrackFarFromOriginalMeters)
            warnings.Add(DiagnosticCodes.TrackFarFromOriginal);

        var stallAt = GpsStallDetector.Detect(asRecorded, options);
        if (stallAt is not null)
            warnings.Add(DiagnosticCodes.GpsStallDetected);

        // Перший Якір — старт треку: активність починається там, де починається трек.
        var anchors = new List<Anchor> { new(0, 0, 0) };
        var segment = 0;

        foreach (var record in positioned)
        {
            var time = original.SecondsFromStart(record);
            if (time <= 0 || (stallAt is { } stall && time >= stall))
                continue;

            var projection = track.Project(
                record.Position!.Value,
                segment - options.SearchWindowBackward,
                segment + options.SearchWindowForward);

            if (projection.Deviation > options.AnchorToleranceMeters)
                continue;

            var previous = anchors[^1];
            var dt = time - previous.TimeSeconds;
            var ds = projection.TrackDistance - previous.TrackDistance;

            if (dt <= 0 || ds < 0)
                continue;
            if (ds > dt * options.MaxSpeedMetersPerSecond + options.SpeedToleranceMeters)
                continue;
            if (dt >= options.LongGapSeconds && ds / dt < options.LongGapMinSpeedMetersPerSecond)
                continue;

            anchors.Add(new Anchor(time, projection.TrackDistance, projection.Deviation));
            segment = projection.SegmentIndex;
        }

        return new Alignment
        {
            Anchors = anchors,
            // No finish yet, so the zones tile only as far as the anchors reach; EndingAt derives
            // them again over the whole activity once it knows where it ends.
            Zones = Zones(anchors, anchors[^1].TimeSeconds, options),
            MeasuredUntilSeconds = anchors[^1].TimeSeconds,
            MeasuredDistanceMeters = anchors[^1].TrackDistance,
            GpsStallAtSeconds = stallAt,
            Warnings = warnings
        };
    }

    /// <summary>
    /// The same alignment with its finish pinned: the activity ends at
    /// <paramref name="elapsedSeconds"/> on <paramref name="chosenTrackLength"/> — the whole track,
    /// or the part of it the Original's own time makes plausible (AD-17), FR-5 either way. The
    /// length arrives as a number rather than as a mode, because choosing between the two lengths
    /// is the repair's job and doing it again here would be a second owner of the decision.
    /// <para>
    /// When the GPS measured anchors to the very last record there is no estimated zone to spread a
    /// difference over, so the finish replaces that last anchor — and the replacement is refused
    /// when it moves the athlete further in that one interval than the sport's cap allows, exactly
    /// as the monotonicity pass refuses the shortfall in the other direction (AD-8). An activity
    /// whose anchors stop earlier keeps the appended finish and the estimated zone between: a long
    /// interpolation there is the design, not a teleport.
    /// </para>
    /// </summary>
    /// <exception cref="MalformedArgumentException">
    /// The finish would move the athlete an impossible distance in the activity's last interval
    /// (<see cref="DiagnosticCodes.DistanceSeriesImplausible"/>).
    /// </exception>
    public static Alignment EndingAt(
        Alignment measured,
        double elapsedSeconds,
        double chosenTrackLength,
        AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(options);

        // A length of nothing leaves every record on the start line, so it is a defect in the
        // caller rather than a claim about the user's files: no declared code, no FitFixException.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chosenTrackLength);

        var anchors = new List<Anchor>(measured.Anchors);

        // Кінцевий Якір — кінець активності у кінці обраної довжини треку (FR-5, AD-17).
        if (elapsedSeconds > measured.MeasuredUntilSeconds || anchors.Count < 2)
        {
            anchors.Add(new Anchor(elapsedSeconds, chosenTrackLength, 0));
        }
        else
        {
            var previous = anchors[^2];
            var interval = elapsedSeconds - previous.TimeSeconds;

            // What the finish may pay for: the distance the sport's cap covers in that interval,
            // plus the divergence between a track and an activity this configuration already
            // tolerates (AD-17). The second term is what a projection owes — the last anchor sits
            // where the athlete's last fix fell on the track, which is short of its end by however
            // much of it they did not run — and without it the pinned finish would contradict the
            // very tolerance that chose the whole length.
            var tolerated = Math.Max(
                options.TrackLengthToleranceMeters,
                options.TrackLengthToleranceFraction * chosenTrackLength);
            var step = options.MaxSpeedMetersPerSecond * interval
                + options.SpeedToleranceMeters
                + tolerated;

            // Derived from the activity and the track the user offered, so it carries a declared
            // code rather than a bare exception a shell could only report as an internal error
            // (AD-18).
            if (chosenTrackLength - previous.TrackDistance > step)
                throw new MalformedArgumentException(
                    DiagnosticCodes.DistanceSeriesImplausible,
                    $"The activity's GPS reached {measured.MeasuredDistanceMeters:F1} m at its last "
                        + $"record, so finishing on {chosenTrackLength:F1} m would cover "
                        + $"{chosenTrackLength - previous.TrackDistance:F1} m in the last "
                        + $"{interval:F0} s, where no more than {step:F1} m is possible: the track "
                        + "is longer than this activity, not the activity shorter than itself.");

            anchors[^1] = new Anchor(elapsedSeconds, chosenTrackLength, anchors[^1].DeviationMeters);
        }

        return new Alignment
        {
            Anchors = anchors,
            // Over the whole activity now, so the zones cover every second of it once — including
            // the stretch between the last measured anchor and the finish, which is estimated by
            // definition and is the one a single boundary used to stand for.
            Zones = Zones(anchors, elapsedSeconds, options),
            MeasuredUntilSeconds = measured.MeasuredUntilSeconds,
            MeasuredDistanceMeters = measured.MeasuredDistanceMeters,
            GpsStallAtSeconds = measured.GpsStallAtSeconds,
            // The first and the last anchor are counted too (FR-4), so the thinness of the chain
            // can only be judged once the finish is on it.
            Warnings = anchors.Count < options.MinimumAnchorCount
                ? [.. measured.Warnings, DiagnosticCodes.FewAnchors]
                : measured.Warnings
        };
    }

    /// <summary>
    /// The Зони довіри the anchors imply over <c>[0, <paramref name="untilSeconds"/>]</c> (FR-4,
    /// FR-8). The stretch between two consecutive anchors is measured while they are no further
    /// apart than <see cref="AlignmentOptions.EstimatedZoneGapSeconds"/> and estimated when they
    /// are; before the first anchor and after the last there is nothing but interpolation, so the
    /// head and the tail are estimated too.
    /// <para>
    /// The result tiles the span exactly once: adjacent stretches of one kind are merged into a
    /// single zone and an empty one is never emitted, which is what makes "no two neighbours share
    /// a kind" and "no zone is of no duration" true of every alignment rather than of most.
    /// </para>
    /// </summary>
    private static IReadOnlyList<TrustZone> Zones(
        IReadOnlyList<Anchor> anchors,
        double untilSeconds,
        AlignmentOptions options)
    {
        var zones = new List<TrustZone>();

        if (anchors.Count == 0)
        {
            // Nothing was measured at all, so the whole span is one estimate — the activity whose
            // watch never found a fix (AD-15).
            Append(zones, ZoneKind.Estimated, 0, untilSeconds);
            return zones;
        }

        Append(zones, ZoneKind.Estimated, 0, anchors[0].TimeSeconds);

        for (var index = 1; index < anchors.Count; index++)
        {
            var from = anchors[index - 1].TimeSeconds;
            var to = anchors[index].TimeSeconds;
            Append(
                zones,
                to - from <= options.EstimatedZoneGapSeconds ? ZoneKind.Measured : ZoneKind.Estimated,
                from,
                to);
        }

        Append(zones, ZoneKind.Estimated, anchors[^1].TimeSeconds, untilSeconds);
        return zones;
    }

    /// <summary>
    /// Adds one stretch to the tiling: nothing for a stretch of no duration, an extension of the
    /// last zone when it is of the same kind, and a new zone otherwise.
    /// </summary>
    private static void Append(List<TrustZone> zones, ZoneKind kind, double from, double to)
    {
        if (to <= from)
            return;

        if (zones.Count > 0 && zones[^1].Kind == kind)
            zones[^1] = zones[^1] with { ToSeconds = to };
        else
            zones.Add(new TrustZone(kind, from, to));
    }
}
