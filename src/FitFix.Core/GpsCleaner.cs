using FitFix.Core.Diagnostics;

namespace FitFix.Core;

/// <summary>
/// An activity whose impossible positions are gone, together with how many were thrown away.
/// </summary>
public readonly record struct GpsCleaning(OriginalActivity Activity, int DiscardedPositionCount);

/// <summary>
/// Drops physically impossible GPS positions from the Original before anything else reads it
/// (AD-25). A watch that teleports for one second leaves a point thousands of kilometres away:
/// the anchor search already rejects it, but it would still be the Original's first position for
/// the compatibility check (FR-3) and would still reach the preview map and the zones (FR-8, FR-9).
/// </summary>
public static class GpsCleaner
{
    /// <summary>
    /// Returns the activity without its impossible positions. A position is discarded when the
    /// distance from the last valid position is more than
    /// <see cref="AlignmentOptions.MaxSpeedMetersPerSecond"/> could cover in
    /// <c>max(dt, ImpossibleSpeedJudgedOverSeconds)</c>, plus
    /// <see cref="AlignmentOptions.SpeedToleranceMeters"/> for GPS noise. The criterion is
    /// impossibility in time, never distance, so a hundred-kilometre ride across several cities
    /// survives intact (AD-25).
    /// </summary>
    /// <remarks>
    /// Four rules keep the judgement honest, each of them answering a measured failure recorded in
    /// the story's spec. The judging window: a step is weighed over several seconds at least, so
    /// one second of jitter at 1 Hz is never impossible while a sustained impossible speed still
    /// is. The vote: a position with nothing trustworthy behind it is put to the positions that
    /// follow (see <see cref="Vouches"/>). The stale allowance: a reference with unresolved
    /// garbage behind it vouches for at most
    /// <see cref="AlignmentOptions.StaleReferenceVouchesForSeconds"/>, so neither a run of
    /// discards nor a quiet stretch can widen the allowance until a distant fix fits inside it.
    /// And re-seating: when discards run on past <see cref="AlignmentOptions.LeadingAgreementPositions"/>
    /// in a row the reference has been left behind by the activity, so the vote seats a new one and
    /// a burst cannot condemn everything after it.
    /// Discarding a position is not discarding a record: the repaired file must carry every
    /// original timestamp with its sensors (FR-6), so only the coordinates are blanked.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A vote with no voters decides nothing and a judging window of zero reverts to the
    /// per-second allowance that was measured to delete real data, so neither is accepted.
    /// </exception>
    public static GpsCleaning Clean(OriginalActivity original, AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.LeadingAgreementPositions, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ImpossibleSpeedJudgedOverSeconds);

        var records = original.Records;
        List<ActivityRecord>? cleaned = null;
        var discarded = 0;

        // The position every next one is judged against, how many positions have been thrown away
        // since it was accepted, and therefore whether it still answers for the series at all.
        var reference = -1;
        var discardsSinceReference = 0;

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Position is null)
                continue;

            var stale = discardsSinceReference > 0;

            bool keep;
            if (reference >= 0 && Reaches(records[reference], record, stale, options))
            {
                keep = true;
            }
            else if (reference < 0 || OutOfItsDepth(records[reference], record, discardsSinceReference, options))
            {
                // Either no reference has been seated yet, or the standing one has just refused a
                // position it is in no position to judge. The positions that follow decide.
                keep = Vouches(records, index, options)
                    ?? (reference < 0 && discardsSinceReference <= options.LeadingAgreementPositions);
            }
            else
            {
                keep = false;
            }

            if (keep)
            {
                reference = index;
                discardsSinceReference = 0;
                continue;
            }

            cleaned ??= [.. records];
            cleaned[index] = cleaned[index] with { Latitude = null, Longitude = null };
            discarded++;
            discardsSinceReference++;
        }

        // An activity with nothing impossible in it is handed back untouched, down to the very
        // instance, so no number pinned on a clean file can move because cleaning ran.
        return cleaned is null
            ? new GpsCleaning(original, 0)
            : new GpsCleaning(original with { Records = cleaned }, discarded);
    }

    /// <summary>
    /// Whether a reference that has just refused a position is in no position to judge it, so the
    /// vote may seat a new reference instead. Two ways for that to happen, both of them needing
    /// unresolved garbage behind the reference: more of it in a row than the vote even looks at,
    /// or a span that has outrun what a stale reference may vouch for — five minutes of silence
    /// after a single spike, say, where the clamped allowance would condemn a resumption the
    /// athlete reached at walking pace.
    /// </summary>
    private static bool OutOfItsDepth(
        ActivityRecord reference,
        ActivityRecord record,
        int discardsSinceReference,
        AlignmentOptions options) =>
        discardsSinceReference > options.LeadingAgreementPositions
        || (discardsSinceReference > 0
            && (record.Timestamp - reference.Timestamp).TotalSeconds > options.StaleReferenceVouchesForSeconds);

    /// <summary>
    /// Whether the positions that follow <paramref name="candidate"/> vouch for it as the
    /// reference the rest of the activity is judged against: <c>true</c> when one does,
    /// <c>false</c> when every one of them disowns it, and <c>null</c> when there was nobody to
    /// ask. The next <see cref="AlignmentOptions.LeadingAgreementPositions"/> positions that are
    /// somewhere else are asked, each over one single span from the candidate to itself — exactly
    /// as the forward rule would judge that pair.
    /// </summary>
    /// <remarks>
    /// Each part of this earns its place against a measurement in the spec. One span rather than a
    /// sum of per-step allowances, because summing hands out several times the real allowance and
    /// laundered thirty identical fixes a kilometre away into the reference. Anything within
    /// <see cref="AlignmentOptions.StallRadiusMeters"/> of the candidate is the same place and
    /// casts no vote, so a stale fix that repeats — identically or jittering — cannot vouch for
    /// itself at any burst length. An interval speed cannot judge contributes no allowance and no
    /// vote, because one duplicate timestamp inside the window would otherwise make every follower
    /// vote in favour. And one voter in favour suffices rather than a majority: a fast ride judged
    /// with the running profile is disowned by its own body three seconds out, where the honest
    /// span says 15 m/s, while its one-second neighbour vouches for it — demanding a majority there
    /// would delete the ride the matrix requires untouched.
    /// Nobody to ask is not a verdict, so it is answered at the call site: a first position with
    /// an empty activity behind it keeps its benefit of the doubt, because an activity that never
    /// leaves its starting place is not garbage, while a lone survivor at the end of a long run of
    /// discards does not — it would become the Original's first position and raise the false
    /// <c>TRACK_FAR_FROM_ORIGINAL</c> this story exists to remove. Such a series is cleaned to
    /// nothing, and <see cref="DiagnosticCodes.NoOriginalGps"/> then says so beside
    /// <see cref="DiagnosticCodes.PositionsDiscarded"/>.
    /// </remarks>
    private static bool? Vouches(
        IReadOnlyList<ActivityRecord> records,
        int candidate,
        AlignmentOptions options)
    {
        var from = records[candidate];
        var voters = 0;

        for (var index = candidate + 1;
             index < records.Count && voters < options.LeadingAgreementPositions;
             index++)
        {
            var voter = records[index];
            if (voter.Position is null)
                continue;
            if (Geo.Distance(from.Position!.Value, voter.Position.Value) <= options.StallRadiusMeters)
                continue;
            if ((voter.Timestamp - from.Timestamp).TotalSeconds <= 0)
                continue;

            voters++;
            if (Reaches(from, voter, stale: false, options))
                return true;
        }

        return voters > 0 ? false : null;
    }

    private static bool Reaches(ActivityRecord from, ActivityRecord to, bool stale, AlignmentOptions options) =>
        Geo.Distance(from.Position!.Value, to.Position!.Value)
            <= AllowanceMeters(from, to, stale, options);

    /// <summary>How far the athlete could have travelled between two records, m.</summary>
    private static double AllowanceMeters(
        ActivityRecord from,
        ActivityRecord to,
        bool stale,
        AlignmentOptions options)
    {
        var seconds = (to.Timestamp - from.Timestamp).TotalSeconds;

        // Speed cannot judge an interval that does not move forward: two records of the same
        // second, or a timestamp that goes backwards, are never called impossible.
        if (seconds <= 0)
            return double.PositiveInfinity;

        var vouchFor = stale ? Math.Min(seconds, options.StaleReferenceVouchesForSeconds) : seconds;
        return options.MaxSpeedMetersPerSecond * Math.Max(vouchFor, options.ImpossibleSpeedJudgedOverSeconds)
            + options.SpeedToleranceMeters;
    }
}
