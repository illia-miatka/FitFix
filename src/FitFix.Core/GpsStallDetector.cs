namespace FitFix.Core;

/// <summary>
/// Знаходить момент, коли GPS Оригіналу «завис»: позиція перестала змінюватись,
/// хоча спортсмен рухався. Це замінює жорсткий cutoff прототипу (`t &gt; 1720`).
/// Records inside a pause are not read at all: a watch that keeps recording while the athlete
/// waits at a light is immobile for a reason the file states outright (AD-7), and reading those
/// seconds as a frozen GPS would send everything after the pause into the estimated zone.
/// </summary>
public static class GpsStallDetector
{
    /// <summary>
    /// Повертає час від початку активності, з якого позиціям Оригіналу більше не можна вірити,
    /// або null, якщо зависання не виявлено.
    /// </summary>
    public static double? Detect(OriginalActivity original, AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(options);

        var positioned = original.Records
            .Where(r => r.Position is not null && !original.IsPaused(r.Timestamp))
            .Select(r => (
                Time: original.SecondsFromStart(r),
                Position: r.Position!.Value,
                Segment: original.PauseSegment(r.Timestamp),
                Record: r))
            .ToArray();

        if (positioned.Length == 0)
            return null;

        for (var i = 0; i < positioned.Length; i++)
        {
            var anchor = positioned[i].Position;
            var j = i;
            var movementEvidence = false;
            var cadencePresent = false;

            // The window stops at a pause: the records on either side of one are minutes apart
            // while the athlete stood between them, so a window spanning the pause would call
            // that a frozen GPS however the two sides look (AD-7).
            while (j + 1 < positioned.Length
                   && positioned[j + 1].Segment == positioned[i].Segment
                   && Geo.Distance(anchor, positioned[j + 1].Position) <= options.StallRadiusMeters)
            {
                j++;
                if (positioned[j].Record.Cadence is { } cadence)
                {
                    cadencePresent = true;
                    if (cadence > 0)
                        movementEvidence = true;
                }
            }

            var span = positioned[j].Time - positioned[i].Time;
            if (span < options.StallMinimumSeconds)
                continue;

            // Якщо каденс є і він нульовий — це справжня пауза, а не збій GPS.
            if (cadencePresent && !movementEvidence)
                continue;

            return positioned[i].Time;
        }

        return null;
    }
}
