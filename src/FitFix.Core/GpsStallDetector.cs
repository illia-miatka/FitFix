namespace FitFix.Core;

/// <summary>
/// Знаходить момент, коли GPS Оригіналу «завис»: позиція перестала змінюватись,
/// хоча спортсмен рухався. Це замінює жорсткий cutoff прототипу (`t &gt; 1720`).
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
            .Where(r => r.Position is not null)
            .Select(r => (Time: original.SecondsFromStart(r), Position: r.Position!.Value, Record: r))
            .ToArray();

        if (positioned.Length == 0)
            return null;

        for (var i = 0; i < positioned.Length; i++)
        {
            var anchor = positioned[i].Position;
            var j = i;
            var movementEvidence = false;
            var cadencePresent = false;

            while (j + 1 < positioned.Length
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
