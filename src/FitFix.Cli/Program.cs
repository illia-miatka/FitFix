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
        var result = RepairPipeline.RepairFiles(originalPath, trackPath);

        // Story 1.10 takes the offset from the original activity; until then local time is UTC.
        const int utcOffsetSeconds = 0;
        FitActivityWriter.WriteFile(output, result, utcOffsetSeconds);
        PrintReport(result, output);
        return 0;
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
    // A path the file APIs reject outright — an empty argument, for one.
    catch (ArgumentException exception)
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

        ОРИГІНАЛ — FIT із годинника: джерело часу й сенсорів.
        ТРЕК     — правильна геометрія маршруту (час у ньому ігнорується).
        """);
}

static void PrintReport(RepairResult result, string output)
{
    var report = result.Report;
    var culture = CultureInfo.InvariantCulture;

    Console.WriteLine($"Вид спорту:        {result.Sport}");
    Console.WriteLine($"Дистанція:         {report.OriginalDistanceMeters / 1000:F2} км → {report.RepairedDistanceMeters / 1000:F2} км");
    Console.WriteLine($"Загальний час:     {FormatDuration(report.ElapsedSeconds)}");

    // The written file states moving time, not elapsed time (FR-6), so the report says both —
    // otherwise the pace on screen silently contradicts the file. The pace formula itself is
    // Story 1.7's.
    if (report.PausedSeconds > 0)
        Console.WriteLine($"Час у руху:        {FormatDuration(report.MovingSeconds)} (пауз: {FormatDuration(report.PausedSeconds)})");

    Console.WriteLine($"Темп:              {FormatPace(report.OriginalPaceSecondsPerKm)} → {FormatPace(report.RepairedPaceSecondsPerKm)}");
    Console.WriteLine($"Надійна зона:      до {FormatDuration(report.MeasuredUntilSeconds)} ({report.MeasuredFraction.ToString("P0", culture)} активності), якорів: {report.AnchorCount}");

    if (report.GpsStallAtSeconds is { } stall)
        Console.WriteLine($"GPS завис на:      {FormatDuration(stall)}");

    // Deleted positions are never passed over in silence (AD-25); the coordinates themselves
    // stay out of the output, as they do out of every log (AD-9).
    if (report.DiscardedPositionCount > 0)
        Console.WriteLine($"Відкинуто точок:   {report.DiscardedPositionCount}");

    if (report.Warnings.Count > 0)
        Console.WriteLine($"Попередження:      {string.Join(", ", report.Warnings)}");

    Console.WriteLine($"Записано:          {output}");
}

static string FormatDuration(double seconds) =>
    TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

static string FormatPace(double? secondsPerKm) =>
    secondsPerKm is null or <= 0 or double.PositiveInfinity
        ? "—"
        : $"{(int)(secondsPerKm.Value / 60)}:{(int)(secondsPerKm.Value % 60):D2}/км";
