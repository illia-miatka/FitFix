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
/// <para>
/// Those two are the <em>whole-track</em> mechanism: the activity starts where the track starts and
/// ends where it ends, and the chain between them says which stretches the GPS measured. A track
/// covering only a stretch of the route cannot be aligned that way at all — how far its chain
/// reaches says nothing about where the GPS broke — so <see cref="AttachingFragment"/> answers that
/// case by the track's own two endpoints (AD-27). The repair asks the fragment question first and
/// falls through to <see cref="Measure"/> when the answer is no.
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
    /// <exception cref="MalformedArgumentException">
    /// Nothing on the track attaches to the activity at all
    /// (<see cref="DiagnosticCodes.TrackAttachesNowhere"/>), so there is no route here to lay over
    /// the activity's time (AD-27).
    /// </exception>
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

        var stallAt = GpsStallDetector.Detect(asRecorded, options);
        if (stallAt is not null)
            warnings.Add(DiagnosticCodes.GpsStallDetected);

        // Перший Якір — старт треку: активність починається там, де починається трек. True of a
        // whole track by definition, and this is the whole-track mechanism — a track covering one
        // stretch of the route never reaches here (AD-27).
        var anchors = new List<Anchor> { new(0, 0, 0) };
        var segment = 0;

        // How near the search ever came to attaching, m — what decides between the refusal
        // TRACK_ATTACHES_NOWHERE and the warning TRACK_FAR_FROM_ORIGINAL when nothing attaches at
        // all (FR-3, AD-27). It is not the number the warning reports: a loop crossing the route
        // can come within a metre and still attach nothing, so the report states the first
        // position's distance instead (see `farBy`). MaxValue while the search has looked at nothing: an
        // activity whose watch never found a fix offers no attachment to judge, and must not be
        // refused as though it had offered a bad one.
        var closestApproach = double.MaxValue;

        foreach (var record in positioned)
        {
            var time = original.SecondsFromStart(record);
            if (time <= 0 || (stallAt is { } stall && time >= stall))
                continue;

            var projection = track.Project(
                record.Position!.Value,
                segment - options.SearchWindowBackward,
                segment + options.SearchWindowForward);

            closestApproach = Math.Min(closestApproach, projection.Deviation);

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

        // An activity whose every position the stall condemned, or whose only position is its first
        // second, leaves the search with nothing to judge — and a track from another region would
        // then be laid over the athlete's time with no caveat at all, which is the one outcome FR-3
        // exists to prevent. Its own first position against the whole track is what can still be
        // measured there, so that is what answers the question.
        if (closestApproach == double.MaxValue && positioned.Length > 0)
            closestApproach = track.MinDeviationToWholeTrack(positioned[0].Position!.Value);

        // Said beside the warning and nowhere else, so the number and the caveat cannot part (FR-3).
        double? farBy = null;

        // Nothing the Original's own GPS established: the seeded start line is the whole chain.
        if (anchors.Count == 1 && closestApproach < double.MaxValue)
        {
            // AD-27: a route that attaches to the activity nowhere at all is about somewhere else,
            // and laying it over this athlete's time would be nonsense dressed as a result.
            if (closestApproach > options.TrackFarFromOriginalMeters)
                throw new MalformedArgumentException(
                    DiagnosticCodes.TrackAttachesNowhere,
                    $"No position of the activity came within {closestApproach:F0} m of the "
                        + $"reference track, where {options.AnchorToleranceMeters:F0} m is what "
                        + $"anchors it: the track is about another route ('{nameof(track)}').");

            // Near enough to be this route and still nothing to anchor: the repair proceeds as it
            // always has — the whole track laid over the activity's time — and says so (FR-3).
            warnings.Add(DiagnosticCodes.TrackFarFromOriginal);
            farBy = track.MinDeviationToWholeTrack(positioned[0].Position!.Value);
        }

        // A whole track spans the activity: it opens on the start line above and closes on the
        // chosen length in EndingAt (AD-27). Only a fragment reports anything else.
        var reach = new TrackReach(
            OpensAtTrackStart: true,
            ClosesAtTrackEnd: true,
            FromSeconds: 0,
            ToSeconds: anchors[^1].TimeSeconds);

        return new Alignment
        {
            Anchors = anchors,
            Reach = reach,
            // No finish yet, so the zones tile only as far as the anchors reach; EndingAt derives
            // them again over the whole activity once it knows where it ends.
            Zones = Zones(anchors, anchors[^1].TimeSeconds, options),
            MeasuredUntilSeconds = anchors[^1].TimeSeconds,
            MeasuredDistanceMeters = anchors[^1].TrackDistance,
            StartToTrackMeters = farBy,
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
    /// <para>
    /// Only ever called on a whole track, whose finish is its end by definition. A fragment's two
    /// ends are established by <see cref="AttachingFragment"/> and need no pin (AD-27).
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

        var reach = measured.Reach with { ToSeconds = anchors[^1].TimeSeconds };

        return new Alignment
        {
            Anchors = anchors,
            Reach = reach,
            // Over the whole activity now, so the zones cover every second of it once — including
            // the stretch between the last measured anchor and the finish, which is estimated by
            // definition and is the one a single boundary used to stand for.
            Zones = Zones(anchors, elapsedSeconds, options),
            MeasuredUntilSeconds = measured.MeasuredUntilSeconds,
            MeasuredDistanceMeters = measured.MeasuredDistanceMeters,
            StartToTrackMeters = measured.StartToTrackMeters,
            GpsStallAtSeconds = measured.GpsStallAtSeconds,
            // The first and the last anchor are counted too (FR-4), so the thinness of the chain
            // can only be judged once the finish is on it.
            Warnings = anchors.Count < options.MinimumAnchorCount
                ? [.. measured.Warnings, DiagnosticCodes.FewAnchors]
                : measured.Warnings
        };
    }

    /// <summary>
    /// The complete alignment of a track that covers only a <em>stretch</em> of the route (AD-27),
    /// or <c>null</c> when the track is no fragment and the whole-track mechanism above is the one
    /// that applies. The repair asks this first.
    /// <para>
    /// A fragment attaches by its own two endpoints and never by how far a chain of anchors reaches:
    /// its first point is matched against the whole activity to find the last true position before
    /// the failure, its last point to find the first true position after it, the two are required to
    /// order in time, and where no true position survives on a side the activity's own start or
    /// finish becomes that end. The span between the two attach times is the Розрахована зона and
    /// takes the fragment's length as its increment; everything outside it is measured and keeps the
    /// watch's own coordinates and geodesic distance, untouched (AD-26).
    /// </para>
    /// <para>
    /// Which pair, when the route passes an endpoint more than once — a lap whose first and last
    /// hundred metres run along the same street, as Ілля's own route does — is decided by the failure
    /// the pair claims to cover: the span has to be where the activity could not account for itself,
    /// and the fragment's length has to be reachable inside it at the sport's cap. A pair the watch
    /// measured cleanly throughout is still allowed, but it is the last one considered.
    /// </para>
    /// </summary>
    /// <exception cref="MalformedArgumentException">
    /// Neither endpoint of the fragment finds a place on the activity, or no pair of places survives
    /// the arithmetic (<see cref="DiagnosticCodes.TrackAttachesNowhere"/>): there is no stretch of
    /// this route in this activity, and a guess at where to lay it would be nonsense dressed as a
    /// result.
    /// </exception>
    public static Alignment? AttachingFragment(
        OriginalActivity original,
        OriginalActivity asRecorded,
        ReferenceTrack track,
        AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(asRecorded);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(options);

        // Nothing to attach to. An original without a position of its own has only its time, and
        // laying the whole track over that time is what the whole-track mechanism already does.
        if (!original.HasPosition || original.ElapsedSeconds <= 0)
            return null;

        // A track is a fragment when it is materially shorter than the distance the activity
        // measured for itself (AD-27). The device's own figure first — it is the activity's own
        // claim about how far it went, and it stands even where the GPS lied — and the geodesic of
        // the surviving positions only when the watch recorded no distance at all. The margin is
        // AD-17's: the same disagreement between a drawn route and a watch that decides whether a
        // track is too long decides whether it is too short to be the whole of one.
        var own = original.LargestRecordedDistance is > 0 and { } recorded
            ? recorded
            : OwnGeodesicDistance(original);
        if (own <= 0)
            return null;

        var margin = Math.Max(
            options.TrackLengthToleranceMeters,
            options.TrackLengthToleranceFraction * own);
        if (track.Length >= own - margin)
            return null;

        var stallAt = GpsStallDetector.Detect(asRecorded, options);

        // The positions a fragment may attach to: the Original's own, minus the ones the stall
        // detector has already disbelieved (AD-25). A frozen watch must not offer the place its
        // freezing invented as the end of a fragment.
        var positions = original.Records
            .Where(record => record.Position is not null)
            .Select(record => (Seconds: original.SecondsFromStart(record), record.Position!.Value))
            .Where(position => stallAt is not { } stall || position.Seconds < stall)
            .ToArray();

        var heads = Matches(positions, track.Points[0], options.TrackEndAttachmentMeters);
        var tails = Matches(positions, track.Points[^1], options.TrackEndAttachmentMeters);

        var span = Choose(heads, tails, original, track, options)
            ?? throw new MalformedArgumentException(
                DiagnosticCodes.TrackAttachesNowhere,
                $"The {track.Length:F0} m track is shorter than the {own:F0} m this activity "
                    + "measured for itself, and no place on the activity matches both of its ends "
                    + $"within {options.TrackEndAttachmentMeters:F0} m in the order they would have "
                    + $"to be run ('{nameof(track)}').");

        // A stretch that covers the whole activity is not a stretch of it: whatever its length says,
        // this track is being offered as the route, so the whole-track mechanism answers it and the
        // watch's own geometry is not thrown away.
        var outside = span.FromSeconds + (original.ElapsedSeconds - span.ToSeconds);
        if (outside < options.EstimatedZoneGapSeconds)
            return null;

        var anchors = new List<Anchor>
        {
            new(span.FromSeconds, 0, span.FromDeviationMeters),
            new(span.ToSeconds, track.Length, span.ToDeviationMeters)
        };

        // The tiling AD-27 asks for, and the one thing that makes a fragment a fragment: estimated
        // between the two attach times, measured on either side of them, whatever the anchors'
        // spacing. The gap rule of the whole-track tiling has nothing to say here — two anchors a
        // minute apart bound the failure rather than describing a measured minute.
        var zones = new List<TrustZone>();
        Append(zones, ZoneKind.Measured, 0, span.FromSeconds);
        Append(zones, ZoneKind.Estimated, span.FromSeconds, span.ToSeconds);
        Append(zones, ZoneKind.Measured, span.ToSeconds, original.ElapsedSeconds);

        return new Alignment
        {
            Anchors = anchors,
            Reach = new TrackReach(
                OpensAtTrackStart: span.FromSeconds <= 0,
                ClosesAtTrackEnd: span.ToSeconds >= original.ElapsedSeconds,
                span.FromSeconds,
                span.ToSeconds),
            Zones = zones,
            MeasuredUntilSeconds = span.ToSeconds,
            MeasuredDistanceMeters = track.Length,
            GpsStallAtSeconds = stallAt,
            // No FEW_ANCHORS: two anchors are all a fragment has by design, and most of this
            // activity's distance is measured rather than estimated — the warning would be false.
            Warnings = stallAt is null ? [] : [DiagnosticCodes.GpsStallDetected]
        };
    }

    /// <summary>Where one endpoint of the fragment sits on the activity, and how far off it is.</summary>
    private readonly record struct EndpointMatch(double Seconds, double DeviationMeters);

    /// <summary>
    /// The span a candidate pair of attach points claims, with the deviation of each end — zero for
    /// an end that is the activity's own boundary rather than a position of its own.
    /// </summary>
    private readonly record struct FragmentSpan(
        double FromSeconds,
        double ToSeconds,
        double FromDeviationMeters,
        double ToDeviationMeters);

    /// <summary>
    /// Every place the activity comes within <paramref name="toleranceMeters"/> of
    /// <paramref name="endpoint"/>, one per visit: the closest position of each run that stays
    /// inside the tolerance. A lap that passes the same corner twice offers two places, and which
    /// of them is the fragment's end is settled by <see cref="Choose"/> rather than by taking the
    /// nearest and hoping.
    /// </summary>
    private static List<EndpointMatch> Matches(
        (double Seconds, GeoPoint Position)[] positions,
        GeoPoint endpoint,
        double toleranceMeters)
    {
        var matches = new List<EndpointMatch>();
        var visiting = false;
        var closest = default(EndpointMatch);

        foreach (var (seconds, position) in positions)
        {
            var away = Geo.Distance(position, endpoint);
            if (away <= toleranceMeters)
            {
                if (!visiting || away < closest.DeviationMeters)
                    closest = new EndpointMatch(seconds, away);
                visiting = true;
            }
            else if (visiting)
            {
                matches.Add(closest);
                visiting = false;
            }
        }

        if (visiting)
            matches.Add(closest);

        return matches;
    }

    /// <summary>
    /// Which pair of places the fragment attaches between (AD-27).
    /// <para>
    /// First the pairs where both of its ends are positions of the Original's own, taken in the
    /// order the activity is run: one pass over the fragment has a beginning and then an end, so a
    /// place its last point matches <em>before</em> the place its first point matches is the lap's
    /// own closing metres offering themselves, not the stretch the athlete needs redrawn.
    /// </para>
    /// <para>
    /// Only when its two ends cannot both be placed that way does AD-27's fallback apply, and then
    /// the activity's own start or finish becomes the end that found no place. The fallbacks are not
    /// offered beside a pair the arithmetic went on to reject: a fragment too long for the stretch
    /// between its own two ends must not be re-read as covering everything from one of them to the
    /// end of the activity, which is how a doubled fragment came to swallow 25 minutes of good GPS.
    /// </para>
    /// <para>
    /// Of the survivors, the pair whose span the activity could least account for itself wins — the
    /// seconds it wrote no position for, and the seconds its own distance did not grow — because that
    /// is the failure a fragment is offered to repair. Ties go to the closer match.
    /// </para>
    /// </summary>
    private static FragmentSpan? Choose(
        List<EndpointMatch> heads,
        List<EndpointMatch> tails,
        OriginalActivity original,
        ReferenceTrack track,
        AlignmentOptions options)
    {
        var candidates = new List<FragmentSpan>();

        foreach (var head in heads)
        {
            foreach (var tail in tails)
            {
                if (tail.Seconds > head.Seconds)
                    candidates.Add(new FragmentSpan(
                        head.Seconds, tail.Seconds, head.DeviationMeters, tail.DeviationMeters));
            }
        }

        if (candidates.Count == 0)
        {
            // Which side ran out of positions is not something an endpoint that matched nothing can
            // say, so both fallbacks are offered and the arithmetic below tells a head fragment from
            // a tail one: an activity's last five seconds cannot hold two kilometres of route.
            var elapsed = original.ElapsedSeconds;
            foreach (var head in heads)
                candidates.Add(new FragmentSpan(head.Seconds, elapsed, head.DeviationMeters, 0));
            foreach (var tail in tails)
                candidates.Add(new FragmentSpan(0, tail.Seconds, 0, tail.DeviationMeters));
        }

        return candidates
            .Where(span => Reachable(span, original, track, options))
            .OrderByDescending(span => Unaccounted(span, original))
            .ThenBy(span => span.FromDeviationMeters + span.ToDeviationMeters)
            .ThenBy(span => span.FromSeconds)
            .Cast<FragmentSpan?>()
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether the athlete could have covered the fragment inside the span the pair claims, bounded
    /// from both sides by the moving seconds it holds (AD-7, AD-10) — the same two gates the anchor
    /// chain judges its own steps by, asked of a stretch instead of a step.
    /// <para>
    /// The cap above: a fragment claiming a kilometre of route across a minute of running is not
    /// describing that minute, whatever its endpoints sit near. The minimum speed below, over a span
    /// long enough for it to mean anything: a pair that straddles two laps of the same route — the
    /// fragment's start on the first and its end on the second — claims the athlete crawled the
    /// stretch for three quarters of an hour, and the estimated zone it would open is most of the
    /// activity. Both ends of a fragment can only be the two ends of <em>one</em> pass over it.
    /// </para>
    /// </summary>
    private static bool Reachable(
        FragmentSpan span,
        OriginalActivity original,
        ReferenceTrack track,
        AlignmentOptions options)
    {
        var moving = original.MovingSecondsFromStart(span.ToSeconds)
            - original.MovingSecondsFromStart(span.FromSeconds);

        if (moving <= 0)
            return false;
        if (track.Length > options.MaxSpeedMetersPerSecond * moving + options.SpeedToleranceMeters)
            return false;

        return moving < options.LongGapSeconds
            || track.Length / moving >= options.LongGapMinSpeedMetersPerSecond;
    }

    /// <summary>
    /// How many seconds of the span the activity could not account for itself: those it wrote no
    /// position for, and those its own distance did not grow through. That is what a failure looks
    /// like from the outside, and a fragment is offered to repair a failure — so of two pairs of
    /// places that both fit, this is what tells the stretch that broke from the stretch beside it.
    /// Paused seconds are nobody's failure and are not counted (AD-7).
    /// </summary>
    private static double Unaccounted(FragmentSpan span, OriginalActivity original)
    {
        var unaccounted = 0.0;
        double? previousDistance = null;
        var previousSeconds = span.FromSeconds;

        foreach (var record in original.Records)
        {
            var seconds = original.SecondsFromStart(record);
            if (seconds <= span.FromSeconds || seconds > span.ToSeconds)
                continue;

            if (!original.IsPaused(record.Timestamp))
            {
                var stalled = record.Distance is { } distance
                    && previousDistance is { } earlier
                    && distance <= earlier;

                if (record.Position is null || stalled)
                    unaccounted += seconds - previousSeconds;
            }

            previousDistance = record.Distance ?? previousDistance;
            previousSeconds = seconds;
        }

        return unaccounted;
    }

    /// <summary>
    /// The distance the Original's own positions describe, m — the geodesic sum over the positions
    /// it kept. Read only when the device recorded no distance of its own, because real GPS error
    /// inflates it: on the 23.11.2026 files the positions sum to 7.0 km where the watch itself
    /// claimed 4.8, and judging a track's length against the inflated figure would call a whole
    /// route a fragment.
    /// </summary>
    private static double OwnGeodesicDistance(OriginalActivity original)
    {
        var sum = 0.0;
        GeoPoint? previous = null;

        foreach (var record in original.Records)
        {
            if (record.Position is not { } position)
                continue;
            if (previous is { } earlier)
                sum += Geo.Distance(earlier, position);
            previous = position;
        }

        return sum;
    }

    /// <summary>
    /// The Зони довіри the anchors imply over <c>[0, <paramref name="untilSeconds"/>]</c> (FR-4,
    /// FR-8). The stretch between two consecutive anchors is measured while they are no further
    /// apart than <see cref="AlignmentOptions.EstimatedZoneGapSeconds"/> and estimated when they
    /// are.
    /// Before the first anchor and after the last there is nothing but interpolation, so the head
    /// and the tail are estimated too — true of a whole track, which opens on the start line and
    /// closes on its own end. A fragment's zones are the other way round and
    /// <see cref="AttachingFragment"/> tiles them itself (AD-27).
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
