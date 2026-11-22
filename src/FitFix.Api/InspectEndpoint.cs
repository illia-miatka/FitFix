using FitFix.Api.Contracts;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tracks;

namespace FitFix.Api;

/// <summary>
/// <c>POST /api/inspect</c> (Story 2.2): one file, one role, and what the core read from it.
/// A dropzone asks this the moment a file is chosen, so a wrong file is visible before a repair
/// is waited for.
/// <para>
/// Nothing is stored and nothing is derived here. The shell checks the size of the upload — the
/// middleware does that for every request — and the number of parts it carries; the core decides
/// what the bytes are (AD-13). Every number in the answer is read off the same core models the
/// repair reads, which is what keeps the dropzone and the result screen from stating two
/// different distances for one file (AD-15), and every refusal is one the repair would raise too.
/// </para>
/// </summary>
public static class InspectEndpoint
{
    /// <summary>The route, as the client and the request log both see it.</summary>
    public const string Route = "/api/inspect";

    /// <summary>The form field naming the role the file is offered in.</summary>
    public const string RoleField = "role";

    /// <summary>
    /// The category of this endpoint's own log lines. Separate from the request line's, so a
    /// deployment can keep one and drop the other.
    /// </summary>
    private const string LogCategory = "FitFix.Api.Inspect";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(Route, InspectAsync);
    }

    private static async Task<IResult> InspectAsync(HttpRequest request, ILoggerFactory loggers)
    {
        // A request that is not multipart carries no file part at all. This refuses the shape of
        // the request, never the type of a file: the content type of the file itself decides
        // nothing anywhere below (AD-13).
        if (!request.HasFormContentType)
            throw new MalformedArgumentException(
                DiagnosticCodes.RequestMalformed,
                "An inspect request carries one file and one role as multipart form data.");

        // The client's own token, so an upload abandoned halfway cancels the read instead of
        // being parsed for a response nobody is waiting for. The middleware's "the client left"
        // branch is keyed on exactly this token.
        var cancellation = request.HttpContext.RequestAborted;
        var form = await request.ReadFormAsync(cancellation);

        // The role is read before the file is touched, and read exactly: a FIT file is valid as
        // an activity and as a track, so a guess would answer one dropzone with the other's
        // fields (AD-15). Two role parts arrive joined by a comma and name no role at all, which
        // is the right answer to a request that states two.
        var describe = DescriberFor(form[RoleField].ToString());

        // Each of the three is a different mistake to the person who made it, so each carries its
        // own code: the registry is the whole vocabulary the interface has (AD-18).
        if (form.Files.Count == 0)
            throw new MalformedArgumentException(
                DiagnosticCodes.FileMissing, "An inspect request carries the file to describe.");

        if (form.Files.Count > 1)
            throw new MalformedArgumentException(
                DiagnosticCodes.TooManyFiles, "An inspect request describes one file, not several.");

        // Nothing below is worth doing for a client that has gone: decoding a 20 MB file for an
        // answer that cannot be delivered is the one expensive mistake available here.
        cancellation.ThrowIfCancellationRequested();

        await using var content = await SeekableAsync(form.Files[0], cancellation);
        var answer = describe(content, loggers.CreateLogger(LogCategory));

        return Results.Ok(answer);
    }

    /// <summary>
    /// What describes a file offered in <paramref name="role"/> — the one place the role is
    /// decided, and exhaustive: a third role added to this switch gets a reader of its own or no
    /// answer at all, where a ternary would quietly describe it as a track.
    /// </summary>
    private static Func<Stream, ILogger, object> DescriberFor(string role) => role switch
    {
        FileRole.Original => static (content, logger) => Describe(FitActivityReader.Read(content), logger),
        FileRole.Track => DescribeTrack,
        _ => throw new MalformedArgumentException(
            DiagnosticCodes.InspectRoleInvalid,
            $"An inspect request states a role of '{FileRole.Original}' or '{FileRole.Track}'.")
    };

    /// <summary>
    /// The original as the repair will see it. Cleaning runs here because an inspect request
    /// never repairs: without it the answer would promise a map out of positions the repair is
    /// about to throw away (AD-25). It runs with the thresholds of the activity's own sport,
    /// exactly as <see cref="RepairPipeline"/> does, so the same file is judged by the same speed
    /// cap in both places — a ride cleaned at the running cap would lose the route it rode.
    /// </summary>
    private static OriginalInspectionResponse Describe(OriginalActivity activity, ILogger logger)
    {
        // The refusal the repair raises over this file (see RepairService.Repair), raised here
        // too: a dropzone that called the file understood and a button that then fails is the gap
        // this endpoint exists to close.
        if (activity.ElapsedSeconds <= 0)
            throw new MalformedArgumentException(
                DiagnosticCodes.ActivityWithoutElapsedTime,
                $"The activity's records span no time at all ('{nameof(activity)}').");

        var (cleaned, discardedPositions) = GpsCleaner.Clean(activity, AlignmentOptions.ForSport(activity.Sport));

        // The codes the repair would report about the series, in its own order (AD-18): cleaning
        // runs first, and a series cleaned down to nothing says both.
        List<string> codes = [];
        if (discardedPositions > 0)
            codes.Add(DiagnosticCodes.PositionsDiscarded);
        if (!cleaned.HasPosition)
            codes.Add(DiagnosticCodes.NoOriginalGps);

        var answer = new OriginalInspectionResponse(
            Role: FileRole.Original,
            Sport: NameOf(activity.Sport),
            StartedAt: activity.StartTime,
            ElapsedSeconds: activity.ElapsedSeconds,
            // The device's own distance field, which cleaning never touches: this is the number
            // the athlete already saw, and the repair's report quotes the very same one — absent
            // there too when the watch recorded none.
            RecordedDistanceMeters: activity.RecordedDistance,
            RecordCount: activity.Records.Count,
            // The model owns the question, and the aligner asks it of the same cleaned series
            // before it reports NO_ORIGINAL_GPS.
            HasPosition: cleaned.HasPosition,
            DiscardedPositionCount: discardedPositions,
            Codes: codes);

        // What AD-9 allows about an upload: counts, the sport and codes, never a coordinate, the
        // file's contents or its name. Without it an operator reading a refusal cannot tell which
        // shape of file produced it.
        logger.LogInformation(
            "Inspected an original: {Sport}, {RecordCount} records, {ElapsedSeconds:F0} s, "
            + "{DiscardedPositionCount} positions discarded, codes {Codes}.",
            answer.Sport,
            answer.RecordCount,
            answer.ElapsedSeconds,
            answer.DiscardedPositionCount,
            codes.Count > 0 ? string.Join(' ', codes) : "-");

        return answer;
    }

    /// <summary>
    /// The track as the repair will build it, through the core's own composition of reading and
    /// de-duplicating — the overload that names the detected format, so a step added to that
    /// composition reaches this endpoint as well as the repair (AD-15).
    /// <para>
    /// The default thresholds on purpose. <see cref="AlignmentOptions.TrackMinSpacingMeters"/> is
    /// the same for every sport and <see cref="AlignmentOptions.ForSport"/> moves only the speed
    /// cap, so a track inspected before any activity is known is de-duplicated exactly as the
    /// repair will de-duplicate it. Should the spacing ever become sport-dependent, inspect has to
    /// be given the sport.
    /// </para>
    /// </summary>
    private static TrackInspectionResponse DescribeTrack(Stream content, ILogger logger)
    {
        var track = TrackReader.ReadReferenceTrack(content, new AlignmentOptions(), out var format);

        var answer = new TrackInspectionResponse(
            Role: FileRole.Track,
            LengthMeters: track.Length,
            PointCount: track.Points.Count,
            Format: NameOf(format),
            // Anywhere along the track, not at its first point: a track whose start lost its
            // elevation still has a profile, and the repaired file would carry it.
            HasAltitude: track.HasAltitude);

        logger.LogInformation(
            "Inspected a track: {Format}, {PointCount} points, altitude {HasAltitude}.",
            answer.Format,
            answer.PointCount,
            answer.HasAltitude);

        return answer;
    }

    /// <summary>
    /// The part as a stream the readers can rewind. The FIT decoder reads the header, the
    /// integrity check and the messages from the start each time, so a part the server did not
    /// buffer would fail on the first seek — as an unexplained 500, since no code covers it.
    /// A part that is already seekable is handed over untouched; the upload bound is what makes
    /// the copy safe.
    /// </summary>
    private static async Task<Stream> SeekableAsync(IFormFile file, CancellationToken cancellation)
    {
        var content = file.OpenReadStream();
        if (content.CanSeek)
            return content;

        await using (content)
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellation);
            buffer.Position = 0;
            return buffer;
        }
    }

    /// <summary>
    /// The sport as a string (AD-14). A sport the core does not model is <c>other</c> by
    /// definition — that is what <see cref="SportKind.Other"/> means — so the fall-through names
    /// it rather than inventing a fourth value.
    /// </summary>
    private static string NameOf(SportKind sport) => sport switch
    {
        SportKind.Running => "running",
        SportKind.Cycling => "cycling",
        _ => "other"
    };

    /// <summary>
    /// The detected format as a string (AD-14). A format the core recognises and this contract
    /// cannot name is a contract left behind, not a bad file: it leaves as the shell's own
    /// failure rather than telling the athlete their track is unreadable.
    /// </summary>
    private static string NameOf(TrackFormat format) => format switch
    {
        TrackFormat.Fit => "fit",
        TrackFormat.Gpx => "gpx",
        TrackFormat.Tcx => "tcx",
        _ => throw new InvalidOperationException(
            $"The track format {format} is recognised by the core and has no name in the contract.")
    };
}
