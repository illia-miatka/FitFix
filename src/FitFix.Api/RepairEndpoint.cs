using FitFix.Api.Contracts;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;

namespace FitFix.Api;

/// <summary>
/// <c>POST /api/repair</c> (Story 2.3): the two files in one request, and everything about the
/// repair in one answer — the summaries before and after, the trust zones, the per-record series,
/// the warnings with their severities and the repaired FIT as base64. Nothing is stored, so a
/// client that wants a different track-length mode sends the same two files again (AD-3, AD-17).
/// <para>
/// This endpoint composes nothing and computes nothing. <see cref="RepairPipeline"/> is the single
/// composition of read, align and repair that the CLI already calls, so the two entry points
/// cannot disagree about the same two files (AD-10), and every number below is transcribed from
/// the summary the core settled once (AD-16) — there is no sum, no average and no division over an
/// activity anywhere in this file (AD-1).
/// </para>
/// </summary>
public static class RepairEndpoint
{
    /// <summary>The route, as the client and the request log both see it.</summary>
    public const string Route = "/api/repair";

    /// <summary>
    /// The form field naming which of the two track lengths to end on (AD-17). Absent means the
    /// conservative one.
    /// </summary>
    public const string TrackLengthModeField = "trackLengthMode";

    /// <summary>
    /// The category of this endpoint's own log lines. Separate from the request line's, so a
    /// deployment can keep one and drop the other.
    /// </summary>
    private const string LogCategory = "FitFix.Api.Repair";

    /// <summary>One file per role, and no other arrangement of parts is a repair request.</summary>
    private const int ExpectedFileCount = 2;

    /// <summary>
    /// The offset the written file states as its local time. Zero until Story 1.10 takes the real
    /// one from the Original; stated here rather than defaulted in the writer, so no entry point
    /// can inherit a silent value (AD-10).
    /// </summary>
    private const int UtcOffsetSeconds = 0;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(Route, RepairAsync);
    }

    private static async Task<IResult> RepairAsync(HttpRequest request, ILoggerFactory loggers)
    {
        // A request that is not multipart carries no file part at all. This refuses the shape of
        // the request, never the type of either file: the content type of a part decides nothing
        // anywhere below, because the core determines the format from the bytes (AD-13).
        if (!request.HasFormContentType)
            throw new MalformedArgumentException(
                DiagnosticCodes.RequestMalformed,
                "A repair request carries the original and the track as multipart form data.");

        // The client's own token, so an upload abandoned halfway cancels the read instead of
        // being repaired for a response nobody is waiting for.
        var cancellation = request.HttpContext.RequestAborted;
        var form = await request.ReadFormAsync(cancellation);

        // Read before either file is touched, and read exactly: a mode nobody understood must not
        // decide between the two lengths on the athlete's behalf (AD-17). Two mode parts arrive
        // joined by a comma and name no mode at all, which is the right answer to a request that
        // states two.
        var trackLengthMode = ModeFrom(form[TrackLengthModeField].ToString());

        // A third file is a mistake about the request rather than about either role, so it is
        // refused before the roles are looked for — otherwise a request carrying two originals and
        // a track would be reported as a track that is missing.
        if (form.Files.Count > ExpectedFileCount)
            throw new MalformedArgumentException(
                DiagnosticCodes.TooManyFiles,
                $"A repair request carries {ExpectedFileCount} files, one per role, not "
                    + $"{form.Files.Count}.");

        // Each part is named for the role it is offered in, and matched exactly: a FIT file is
        // valid as an activity and as a track, so a server that guessed which was which would
        // repair the track against the original (AD-15).
        var originalPart = PartFor(form.Files, FileRole.Original);
        var trackPart = PartFor(form.Files, FileRole.Track);

        // Nothing below is worth doing for a client that has gone: repairing a two-hour activity
        // for an answer that cannot be delivered is the one expensive mistake available here.
        cancellation.ThrowIfCancellationRequested();

        await using var original = await UploadPart.SeekableAsync(originalPart, cancellation);
        await using var track = await UploadPart.SeekableAsync(trackPart, cancellation);

        // The one composition of read, align and repair (AD-10). Every refusal it raises carries a
        // code and leaves through the failure handler, so this endpoint writes no status of its own.
        var result = RepairPipeline.Repair(original, track, trackLengthMode);
        var answer = Transcribe(result);

        Log(loggers.CreateLogger(LogCategory), result, trackLengthMode);

        return Results.Ok(answer);
    }

    /// <summary>
    /// The one part offered in <paramref name="role"/>. Each mistake is a different mistake to the
    /// person who made it, so each carries its own code: the registry is the whole vocabulary the
    /// interface has (AD-18), and a dropzone can only say "choose the track" if a code says that is
    /// what happened.
    /// </summary>
    private static IFormFile PartFor(IFormFileCollection files, string role)
    {
        var offered = files.GetFiles(role);

        if (offered.Count == 0)
            throw new MalformedArgumentException(
                DiagnosticCodes.FileMissing,
                $"A repair request carries a file part named '{role}'.");

        if (offered.Count > 1)
            throw new MalformedArgumentException(
                DiagnosticCodes.TooManyFiles,
                $"A repair request offers one file as the '{role}', not {offered.Count}.");

        return offered[0];
    }

    /// <summary>
    /// Which of the two track lengths the request asks for (AD-17). An empty field is a request
    /// that said nothing, and the conservative mode is the only one it may inherit — a preview that
    /// opened with an inflated pace would already have told the athlete something flattering and
    /// false. Anything else is refused rather than guessed: reading a misspelt mode as "trust the
    /// track" hands over a distance nobody confirmed, and reading it as the conservative one
    /// silently ignores a confirmation that was given.
    /// </summary>
    private static TrackLengthMode ModeFrom(string mode) => mode switch
    {
        "" => TrackLengthMode.Plausible,
        TrackLengthModes.Plausible => TrackLengthMode.Plausible,
        TrackLengthModes.Full => TrackLengthMode.Full,
        _ => throw new MalformedArgumentException(
            DiagnosticCodes.TrackLengthModeInvalid,
            $"A repair request states a track-length mode of '{TrackLengthModes.Plausible}' or "
                + $"'{TrackLengthModes.Full}', or none at all.")
    };

    /// <summary>
    /// The result as the contract states it. Transcription only: every number is read off the
    /// report or the summary the core settled (AD-16), and the derived values the core does carry —
    /// the paces and the measured share — are deliberately left behind (AD-14).
    /// </summary>
    private static RepairResponse Transcribe(RepairResult result)
    {
        var report = result.Report;
        var total = result.Summary.Total;
        var original = result.CleanedOriginal;

        return new RepairResponse(
            Before: new OriginalSummaryResponse(
                Sport: SportName.Of(result.Sport),
                StartedAt: original.StartTime,
                RecordCount: original.Records.Count,
                // The device's own distance field, which cleaning never touches: this is the
                // number the athlete already saw in Strava, and the inspect endpoint quotes the
                // very same one — absent there too when the watch recorded none (AD-15).
                RecordedDistanceMeters: report.OriginalDistanceMeters,
                DiscardedPositionCount: report.DiscardedPositionCount),
            After: new RepairedSummaryResponse(
                DistanceMeters: report.RepairedDistanceMeters,
                ElapsedSeconds: report.ElapsedSeconds,
                MovingSeconds: report.MovingSeconds,
                PausedSeconds: report.PausedSeconds,
                AverageSpeedMetersPerSecond: total.AverageSpeedMetersPerSecond,
                MaximumSpeedMetersPerSecond: total.MaximumSpeedMetersPerSecond,
                AverageHeartRate: total.AverageHeartRate,
                MaximumHeartRate: total.MaximumHeartRate,
                AveragePower: total.AveragePower,
                MaximumPower: total.MaximumPower,
                AverageCadence: total.AverageCadence,
                MaximumCadence: total.MaximumCadence,
                ElevationGainMeters: total.ElevationGainMeters,
                ElevationLossMeters: total.ElevationLossMeters,
                LapCount: report.LapCount),
            Track: new TrackLengthsResponse(
                LengthMeters: report.TrackLengthMeters,
                UsedLengthMeters: report.UsedTrackLengthMeters),
            Zones: [.. report.Zones.Select(zone => new TrustZoneResponse(
                Kind: ZoneKindName.Of(zone.Kind),
                FromSeconds: zone.FromSeconds,
                ToSeconds: zone.ToSeconds))],
            Series: SeriesOf(result),
            // The severity beside every code, so the interface decides the download gate from the
            // answer it was given rather than from a table of its own (AD-22).
            Warnings: [.. report.Warnings.Select(code => new RepairWarningResponse(
                Code: code,
                Severity: SeverityName.Of(DiagnosticCodes.SeverityOf(code))))],
            FitBase64: Convert.ToBase64String(FitActivityWriter.WriteToArray(result, UtcOffsetSeconds)));
    }

    /// <summary>
    /// Every series at the length of the record list, with null in the gaps and nothing thinned
    /// (AD-5). One pass over both series at once: the repaired records and the cleaned Original
    /// describe the same activity at the same timestamps — <see cref="ActivitySummary.Of"/> has
    /// already refused a pair that does not — so an index means the same record in both, which is
    /// the whole guarantee a client lines a chart up with a map on.
    /// </summary>
    private static RepairSeriesResponse SeriesOf(RepairResult result)
    {
        var original = result.CleanedOriginal;
        var sources = original.Records;
        var records = result.Records;
        var count = records.Count;

        var secondsFromStart = new double[count];

        var originalLatitude = new double?[count];
        var originalLongitude = new double?[count];
        var originalDistance = new double?[count];
        var originalSpeed = new double?[count];

        var latitude = new double[count];
        var longitude = new double[count];
        var altitude = new double?[count];
        var distance = new double[count];
        var speed = new double[count];
        var heartRate = new int?[count];
        var power = new int?[count];
        var cadence = new int?[count];
        var temperature = new int?[count];

        for (var index = 0; index < count; index++)
        {
            var source = sources[index];
            var record = records[index];

            // The core's own measure, not a subtraction performed here: the zones are stated in it
            // and so is the smoothing, and one owner of "which second is this" is what keeps a
            // hover on the zone it belongs to.
            secondsFromStart[index] = original.SecondsFromStart(source);

            originalLatitude[index] = source.Latitude;
            originalLongitude[index] = source.Longitude;
            originalDistance[index] = source.Distance;
            originalSpeed[index] = source.Speed;

            latitude[index] = record.Latitude;
            longitude[index] = record.Longitude;
            altitude[index] = record.Altitude;
            distance[index] = record.Distance;
            speed[index] = record.Speed;
            heartRate[index] = record.HeartRate;
            power[index] = record.Power;
            cadence[index] = record.Cadence;
            temperature[index] = record.Temperature;
        }

        return new RepairSeriesResponse(
            SecondsFromStart: secondsFromStart,
            Original: new OriginalSeriesResponse(
                Latitude: originalLatitude,
                Longitude: originalLongitude,
                DistanceMeters: originalDistance,
                SpeedMetersPerSecond: originalSpeed),
            Repaired: new RepairedSeriesResponse(
                Latitude: latitude,
                Longitude: longitude,
                AltitudeMeters: altitude,
                DistanceMeters: distance,
                SpeedMetersPerSecond: speed,
                HeartRate: heartRate,
                Power: power,
                Cadence: cadence,
                TemperatureCelsius: temperature));
    }

    /// <summary>
    /// What AD-9 allows about a repair: durations, counts, the sport, the mode the request asked
    /// for and the warning codes — never a coordinate, a distance of the athlete's, the contents of
    /// either file or the name either was uploaded under. Without it an operator reading a refusal
    /// cannot tell which shape of activity produced it.
    /// </summary>
    private static void Log(ILogger logger, RepairResult result, TrackLengthMode trackLengthMode)
    {
        var report = result.Report;

        logger.LogInformation(
            "Repaired an activity: {Sport}, {RecordCount} records, {ElapsedSeconds:F0} s, "
            + "{AnchorCount} anchors, {LapCount} laps, {TrackLengthMode} track length, "
            + "{DiscardedPositionCount} positions discarded, codes {Codes}.",
            SportName.Of(result.Sport),
            result.Records.Count,
            report.ElapsedSeconds,
            report.AnchorCount,
            report.LapCount,
            trackLengthMode,
            report.DiscardedPositionCount,
            report.Warnings.Count > 0 ? string.Join(' ', report.Warnings) : "-");
    }
}
