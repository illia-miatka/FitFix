using FitFix.Core.Diagnostics;

namespace FitFix.Core;

/// <summary>
/// Будує Вирівнювання: з GPS-точок Оригіналу набирає монотонний ланцюжок Якорів
/// на Еталонному треку (PRD FR-4).
/// </summary>
public static class Aligner
{
    /// <summary>
    /// Builds the alignment from <paramref name="original"/>, the cleaned series (AD-25), while
    /// the stall detector reads <paramref name="asRecorded"/> — the series as the watch wrote it.
    /// The detector walks positioned records only, so a blanked burst would bridge its window and
    /// manufacture a stall that never happened, sending every later anchor into the estimated
    /// zone. Taking both series is what makes that mistake impossible from the outside.
    /// </summary>
    public static Alignment Align(
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
        var elapsed = original.ElapsedSeconds;

        var positioned = original.Records.Where(r => r.Position is not null).ToArray();
        if (positioned.Length == 0)
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

        var measuredUntil = anchors[^1].TimeSeconds;

        // Кінцевий Якір — кінець активності у кінці треку (FR-5).
        if (elapsed > measuredUntil)
            anchors.Add(new Anchor(elapsed, track.Length, 0));
        else
            anchors[^1] = new Anchor(elapsed, track.Length, anchors[^1].DeviationMeters);

        if (anchors.Count < options.MinimumAnchorCount)
            warnings.Add(DiagnosticCodes.FewAnchors);

        return new Alignment
        {
            Anchors = anchors,
            MeasuredUntilSeconds = measuredUntil,
            GpsStallAtSeconds = stallAt,
            Warnings = warnings
        };
    }
}
