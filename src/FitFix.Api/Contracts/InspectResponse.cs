namespace FitFix.Api.Contracts;

/// <summary>
/// The two roles a file can be offered in, as the request states them and as the answer repeats
/// them (AD-15). The role is a token of the protocol, not text for a person, so it is matched
/// exactly: a FIT file reads as an activity and as a track, and a server that guessed between
/// them would answer one dropzone with the other's fields.
/// </summary>
public static class FileRole
{
    /// <summary>The file whose time and sensors the repair keeps.</summary>
    public const string Original = "original";

    /// <summary>The file whose geometry the repair borrows.</summary>
    public const string Track = "track";
}

/// <summary>
/// What the core read from a file offered as the original. Hand-written, like every other body
/// (AD-14), and holding base quantities only: no pace, no fraction of the activity, no
/// difference from anything — the interface computes what it wants to show from these (AD-6).
/// Every number comes from the same models the repair reads, so a dropzone and a result screen
/// cannot disagree about the same file (AD-15).
/// </summary>
/// <param name="Role">Always <see cref="FileRole.Original"/>; the member that discriminates the answer.</param>
/// <param name="Sport">
/// The sport the file's <c>session</c> message named, as a string (AD-14): <c>running</c>,
/// <c>cycling</c> or <c>other</c>. It decides the repair's speed thresholds, so a dropzone that
/// shows it shows why a file was judged the way it was.
/// </param>
/// <param name="StartedAt">The timestamp of the first record, in UTC, as the file carried it.</param>
/// <param name="ElapsedSeconds">Seconds from the first record to the last.</param>
/// <param name="RecordedDistanceMeters">
/// The distance the device itself recorded — what the athlete saw before the repair — or null
/// when no record carried a distance at all. Null rather than zero: a file that measured nothing
/// has not measured zero metres.
/// </param>
/// <param name="RecordCount">How many record messages the file holds.</param>
/// <param name="HasPosition">
/// Whether any position survived cleaning (AD-25). False says the repair has nothing to anchor
/// the track with, which is a caveat worth seeing at the dropzone rather than after a repair.
/// </param>
/// <param name="DiscardedPositionCount">
/// How many positions cleaning threw away as physically impossible. Zero for a file with nothing
/// impossible in it.
/// </param>
/// <param name="Codes">
/// What is worth saying about the file, as codes from the core's registry and in the order the
/// repair reports them: <c>POSITIONS_DISCARDED</c> when cleaning threw anything away, then
/// <c>NO_ORIGINAL_GPS</c> when nothing is left to anchor with. The dropzone looks these up in the
/// same dictionary the result screen uses (AD-18, AD-22) instead of inventing prose of its own
/// from the two members above. Empty for a file with nothing to report.
/// </param>
public sealed record OriginalInspectionResponse(
    string Role,
    string Sport,
    DateTime StartedAt,
    double ElapsedSeconds,
    double? RecordedDistanceMeters,
    int RecordCount,
    bool HasPosition,
    int DiscardedPositionCount,
    IReadOnlyList<string> Codes);

/// <summary>
/// What the core read from a file offered as the reference track. Built through
/// <see cref="FitFix.Core.ReferenceTrack"/> with the de-duplication spacing the repair uses, so
/// <see cref="LengthMeters"/> is the very number the repair will spread the activity over
/// (AD-15).
/// </summary>
/// <param name="Role">Always <see cref="FileRole.Track"/>; the member that discriminates the answer.</param>
/// <param name="LengthMeters">The track's length after de-duplication.</param>
/// <param name="PointCount">How many points survived de-duplication, not how many the file held.</param>
/// <param name="Format">
/// The format the core recognised from the file's own bytes, as a string (AD-14): <c>fit</c>,
/// <c>gpx</c> or <c>tcx</c>. Neither the content type nor the file name decided it (AD-13).
/// </param>
/// <param name="HasAltitude">
/// Whether the track carries altitude anywhere along it, which is what
/// <see cref="FitFix.Core.ReferenceTrack.HasAltitude"/> answers — not whether its first point
/// happens to have one. False leaves the repaired file with the original's own altitude; a flat
/// zero would be a profile the track never had.
/// </param>
public sealed record TrackInspectionResponse(
    string Role,
    double LengthMeters,
    int PointCount,
    string Format,
    bool HasAltitude);
