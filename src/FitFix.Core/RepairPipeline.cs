using FitFix.Core.Fit;
using FitFix.Core.Tracks;

namespace FitFix.Core;

/// <summary>
/// The one composition of a repair: read the original, read the reference track with the same
/// thresholds, align and repair. The CLI calls it and the API will call it, so two entry points
/// cannot assemble the steps differently and disagree on the distance (AD-10).
/// It composes and nothing else — no HTTP, no logging, no arithmetic of its own (AD-1).
/// </summary>
public static class RepairPipeline
{
    /// <summary>
    /// Repairs with the thresholds that suit the original's own sport. The sport is known only
    /// after the original is read, which is why the track is read second: both it and the repair
    /// then use the very same options instance.
    /// </summary>
    public static RepairResult Repair(Stream original, Stream track)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(track);

        var activity = FitActivityReader.Read(original);
        return Finish(activity, track, AlignmentOptions.ForSport(activity.Sport));
    }

    /// <summary>Repairs with the given thresholds, used for both the track and the alignment.</summary>
    public static RepairResult Repair(Stream original, Stream track, AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(options);

        return Finish(FitActivityReader.Read(original), track, options);
    }

    /// <inheritdoc cref="Repair(Stream, Stream)"/>
    public static RepairResult RepairFiles(string originalPath, string trackPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackPath);

        using var original = System.IO.File.OpenRead(originalPath);
        using var track = System.IO.File.OpenRead(trackPath);
        return Repair(original, track);
    }

    /// <inheritdoc cref="Repair(Stream, Stream, AlignmentOptions)"/>
    public static RepairResult RepairFiles(string originalPath, string trackPath, AlignmentOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackPath);
        ArgumentNullException.ThrowIfNull(options);

        using var original = System.IO.File.OpenRead(originalPath);
        using var track = System.IO.File.OpenRead(trackPath);
        return Repair(original, track, options);
    }

    private static RepairResult Finish(OriginalActivity activity, Stream track, AlignmentOptions options) =>
        RepairService.Repair(activity, TrackReader.ReadReferenceTrack(track, options), options);
}
