using System.Globalization;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;

// FitFix CLI (PRD FR-12): fitfix repair ORIGINAL TRACK -o OUT
return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintUsage();
        return args.Length == 0 ? 1 : 0;
    }

    if (args[0] != "repair")
    {
        Console.Error.WriteLine($"Невідома команда: {args[0]}");
        PrintUsage();
        return 1;
    }

    var positional = new List<string>();
    string? output = null;
    // The conservative length until the user says otherwise (AD-17): the flag is how they say it,
    // and its absence can only produce the shorter of the two lengths.
    var trackLengthMode = TrackLengthMode.Plausible;
    // And the service's own answer about the track's coverage until the user confirms it (AD-27):
    // without the flag a fragment is still used, and the warning plus exit code 3 is the question
    // a command line has no other way to ask.
    var trackCoverage = TrackCoverageMode.Detect;

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] is "-o" or "--output")
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Після -o потрібен шлях до файлу.");
                return 1;
            }
            output = args[++i];
        }
        else if (args[i] is "--trust-track")
        {
            trackLengthMode = TrackLengthMode.Full;
        }
        else if (args[i] is "--trust-fragment")
        {
            trackCoverage = TrackCoverageMode.Fragment;
        }
        else
        {
            positional.Add(args[i]);
        }
    }

    if (positional.Count != 2)
    {
        Console.Error.WriteLine("Потрібно рівно два файли: ОРИГІНАЛ і ТРЕК.");
        PrintUsage();
        return 1;
    }

    var originalPath = positional[0];
    var trackPath = positional[1];
    try
    {
        // Inside the try: naming the output after the original rejects a bad path here, and the
        // catch below is the one place a bad path is reported.
        output ??= Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(originalPath)) ?? ".",
            Path.GetFileNameWithoutExtension(originalPath) + "_fixed.fit");

        // The composition root is the only place read → align → repair is assembled (AD-10),
        // so the CLI and the future API cannot disagree on the same two files.
        var result = RepairPipeline.RepairFiles(originalPath, trackPath, trackLengthMode, trackCoverage);

        // Story 1.10 takes the offset from the original activity; until then local time is UTC.
        const int utcOffsetSeconds = 0;
        FitActivityWriter.WriteFile(output, result, utcOffsetSeconds);
        PrintReport(result, output);

        // AD-22 withholds the result until the user confirms a blocking warning. A command-line
        // tool has nobody to ask and the file is already on disk, so the exit code is the gate it
        // can offer: a script cannot otherwise tell a trimmed, unconfirmed repair from a clean one.
        return result.Report.Warnings.Any(DiagnosticCodes.IsBlocking) ? 3 : 0;
    }
    // Every core refusal carries a code (AD-18). Human text for the codes is Story 1.11;
    // until then the code itself is the honest output.
    catch (FitFixException exception)
    {
        Console.Error.WriteLine($"Помилка [{exception.Code}]: {exception.Message}");
        return 2;
    }
    catch (FileNotFoundException exception)
    {
        Console.Error.WriteLine($"Файл не знайдено: {exception.FileName}");
        return 2;
    }
    // A directory that does not exist (`-o /nope/out.fit`) or a file the user may not read:
    // a problem with the path, not with the activity, so one message serves both.
    catch (Exception exception) when (exception is DirectoryNotFoundException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Немає доступу до файлу або теки: {exception.Message}");
        return 2;
    }
    // A path the file APIs reject outright — an empty argument, for one. An
    // ArgumentOutOfRangeException is excluded even though it derives from this: the core raises it
    // for a threshold or a chosen length that cannot be, which is a defect in this program rather
    // than a problem with the athlete's file path, and reporting it as one would hide a bug behind
    // advice the user cannot act on. It is left to crash with its stack trace.
    catch (ArgumentException exception) when (exception is not ArgumentOutOfRangeException)
    {
        Console.Error.WriteLine($"Хибний шлях до файлу: {exception.Message}");
        return 2;
    }
}

static void PrintUsage()
{
    Console.WriteLine("""
        FitFix — виправляє активності з битим GPS.

        Використання:
          fitfix repair ОРИГІНАЛ.fit ТРЕК.(fit|gpx|tcx) [-o РЕЗУЛЬТАТ.fit]
                        [--trust-track] [--trust-fragment]

        ОРИГІНАЛ — FIT із годинника: джерело часу й сенсорів.
        ТРЕК     — правильна геометрія маршруту (час у ньому ігнорується).

        --trust-track    — узяти повну довжину треку, навіть якщо вона більша за
                           правдоподібний рух за час Оригіналу. Без цього прапорця
                           задовгий трек обрізається до правдоподібної довжини.

        --trust-fragment — підтвердити, що трек покриває лише частину маршруту, і
                           перемалювати саму цю ділянку без попередження. Без
                           прапорця ділянку все одно буде перемальовано, але
                           результат вийде з попередженням і кодом 3.

        Коди виходу: 0 — готово; 1 — хибна команда; 2 — файл не підходить;
                     3 — файл записано, але є попередження, яке треба підтвердити.
        """);
}

static void PrintReport(RepairResult result, string output)
{
    var report = result.Report;

    Console.WriteLine($"Вид спорту:        {result.Sport}");
    // Every figure below is formatted by a helper that fixes the culture: a report that mixed
    // `30 %` with `32,50 км` would be stating two separators for one set of numbers.
    //
    // A watch that recorded no distance states none; printing a flat zero before the arrow would
    // read as a measurement of standing still.
    Console.WriteLine(report.OriginalDistanceMeters is { } recorded
        ? $"Дистанція:         {Km(recorded)} км → {Km(report.RepairedDistanceMeters)} км"
        : $"Дистанція:         {Km(report.RepairedDistanceMeters)} км (пристрій дистанції не записав)");
    // Both lengths AD-17 demands, so the athlete sees what the repair ended on and what it chose
    // that from. Said as one number when they are the same: restating an identity would read as a
    // decision somebody made.
    Console.WriteLine(TrackWasTrimmed(report)
        ? $"Довжина треку:     {Km(report.TrackLengthMeters)} км (використано {Km(report.UsedTrackLengthMeters)} км)"
        : $"Довжина треку:     {Km(report.TrackLengthMeters)} км");
    Console.WriteLine($"Загальний час:     {FormatDuration(report.ElapsedSeconds)}");

    // The written file states moving time, not elapsed time (FR-6), so the report says both —
    // otherwise the pace on screen silently contradicts the file.
    if (report.PausedSeconds > 0)
        Console.WriteLine($"Час у руху:        {FormatDuration(report.MovingSeconds)} (пауз: {FormatDuration(report.PausedSeconds)})");

    // Both paces are the core's own numbers (AD-16): the shell formats them and computes nothing.
    // Every line here reads the report, which is where the summary's values arrive — one number,
    // one path to the screen.
    Console.WriteLine($"Темп:              {FormatPace(report.OriginalPaceSecondsPerKm)} → {FormatPace(report.RepairedPaceSecondsPerKm)}");

    // A track without altitude states no climb, and saying nothing is better than printing a
    // flat zero the athlete would read as a measurement.
    if (report.ElevationGainMeters is { } gain)
        Console.WriteLine($"Набір висоти:      {Meters(gain)} м");
    // How much was measured and in how many stretches, never "up to X": the GPS drops out and
    // comes back, so a single boundary would disown the kilometre whose signal returned (FR-8).
    // The share is the core's own number; counting the measured zones is reading a list, not
    // arithmetic over the activity (AD-16).
    var measuredZones = report.Zones.Count(zone => zone.Kind == ZoneKind.Measured);
    Console.WriteLine(
        $"Надійна зона:      {Share(report.MeasuredFraction)} активності "
            + $"(ділянок: {measuredZones}), якорів: {report.AnchorCount}");

    if (report.GpsStallAtSeconds is { } stall)
        Console.WriteLine($"GPS завис на:      {FormatDuration(stall)}");

    // Which stretch came from a fragment of the route, in the report's own numbers (AD-16, AD-27).
    // Said whenever one was used, confirmed or not: the athlete has to be able to see which minutes
    // of their run were drawn rather than measured.
    if (report.RedrawnSpan is { } redrawn)
        Console.WriteLine($"Перемальовано:     {Span(redrawn)}");

    // Deleted positions are never passed over in silence (AD-25); the coordinates themselves
    // stay out of the output, as they do out of every log (AD-9).
    if (report.DiscardedPositionCount > 0)
        Console.WriteLine($"Відкинуто точок:   {report.DiscardedPositionCount}");

    // The laps the Original marked, carried through the repair (AD-24). Said only when there is
    // more than one: a single lap is what an unlapped activity gets, and announcing it would read
    // as news.
    if (report.LapCount > 1)
        Console.WriteLine($"Кіл:               {report.LapCount}");

    if (report.Warnings.Count > 0)
        Console.WriteLine($"Попередження:      {string.Join(", ", report.Warnings)}");

    Console.WriteLine($"Записано:          {output}");

    // A blocking warning must not be left to its code in the line above: these are the ones the
    // user has to answer before the file means anything, and AD-22 withholds the result until they
    // do. Said last, so they are the final thing on the screen, and said for every blocking code
    // rather than for the one this story added — a buried TRACK_FAR_FROM_ORIGINAL is the same
    // mistake.
    foreach (var code in report.Warnings.Where(DiagnosticCodes.IsBlocking))
    {
        Console.WriteLine();
        Console.WriteLine($"УВАГА [{code}]: {BlockingWarningText(code, report)}");
    }
}

/// <summary>
/// What a blocking warning means to the athlete and what they can do about it. Human text per code
/// is Story 1.11; until then the codes a repair can actually raise say what they have to say here,
/// and any other blocking code still announces itself by its code rather than hiding in the list.
/// </summary>
static string BlockingWarningText(string code, RepairReport report) => code switch
{
    DiagnosticCodes.TrackLongerThanActivity =>
        $"трек ({Km(report.TrackLengthMeters)} км) довший за правдоподібний рух за час Оригіналу."
            + Environment.NewLine
            + (TrackWasTrimmed(report)
                ? $"Узято {Km(report.UsedTrackLengthMeters)} км. Якщо трек правильний, а GPS був "
                    + "поганий із самого початку, повторіть команду з --trust-track."
                : $"За вашим --trust-track узято повну довжину — {Km(report.UsedTrackLengthMeters)} км."),
    DiagnosticCodes.TrackFarFromOriginal =>
        report.StartToTrackMeters is { } far
            ? $"трек проходить за {Km(far)} км від місця, де почалося тренування, і жодна точка "
                + "не збіглася з ним — можливо, це маршрут іншої активності."
            : "жодна точка Оригіналу не збіглася з треком — можливо, це маршрут іншої активності.",
    // The stretch, and the way to say "yes, that one" — the exit code alone cannot tell a fragment
    // the athlete meant from a file they grabbed by mistake (AD-27).
    DiagnosticCodes.TrackIsAFragment when report.RedrawnSpan is { } span =>
        $"трек покриває лише частину маршруту, тож перемальовано ділянку {Span(span)}."
            + Environment.NewLine
            + "Решта тренування лишилася такою, якою її записав годинник. Якщо це саме та "
            + "ділянка, повторіть команду з --trust-fragment.",
    _ => "перевірте результат перед тим, як вантажити його у Strava."
};

/// <summary>
/// The redrawn stretch as one phrase: when it ran and how many kilometres of it came from the
/// fragment. One formatting of it, read by the report line and by the warning beside it, so the
/// two cannot name different seconds for the same repair (AD-16).
/// </summary>
static string Span(RedrawnSpan span) =>
    $"{FormatDuration(span.FromSeconds)}–{FormatDuration(span.ToSeconds)} "
        + $"({Km(span.DistanceMeters)} км із треку)";

/// <summary>
/// Whether the repair ended on less than the whole track (AD-17). Half a metre of slack, because
/// the two lengths are the same `double` when nothing was trimmed and a formatted comparison
/// would call a rounding difference a decision.
/// </summary>
static bool TrackWasTrimmed(RepairReport report) =>
    report.TrackLengthMeters - report.UsedTrackLengthMeters > 0.5;

/// <summary>
/// The report's figures, every one of them in the invariant culture: kilometres to two places,
/// metres whole, and a share as a percentage. The athlete's own locale would put a comma in one
/// figure and a point in the next, because the percentage carries its own format while the rest
/// are plain interpolations — one report, one decimal separator.
/// </summary>
static string Km(double meters) => (meters / 1000).ToString("F2", CultureInfo.InvariantCulture);

/// <inheritdoc cref="Km"/>
static string Meters(double meters) => meters.ToString("F0", CultureInfo.InvariantCulture);

/// <inheritdoc cref="Km"/>
static string Share(double fraction) => fraction.ToString("P0", CultureInfo.InvariantCulture);

static string FormatDuration(double seconds) =>
    TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

static string FormatPace(double? secondsPerKm) =>
    secondsPerKm is null or <= 0 or double.PositiveInfinity
        ? "—"
        : $"{(int)(secondsPerKm.Value / 60)}:{(int)(secondsPerKm.Value % 60):D2}/км";
