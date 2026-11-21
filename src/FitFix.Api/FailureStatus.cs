using FitFix.Core.Diagnostics;

namespace FitFix.Api;

/// <summary>
/// Which HTTP status a failure leaves as, and which code a status that the framework produced
/// on its own carries. HTTP is the shell's business, so this translation lives here and not in
/// the registry (AD-1); the registry stays the only place a code is declared (AD-18).
/// </summary>
public static class FailureStatus
{
    /// <summary>
    /// Codes that describe a broken invariant of our own rather than a fault in the files the
    /// user chose. Answering them 400 would tell the athlete their file is wrong and leave our
    /// bug unreported, so they leave as 500 while keeping their own code.
    /// </summary>
    private static readonly HashSet<string> BrokenInvariants = new(StringComparer.Ordinal)
    {
        DiagnosticCodes.SummarySeriesMismatch,
        DiagnosticCodes.SummaryWithoutLaps,
        DiagnosticCodes.DistanceSeriesImplausible
    };

    /// <summary>
    /// The status a core refusal leaves as, decided by its code rather than by the type of the
    /// exception carrying it: the same exception type is raised over a malformed file and over
    /// a series the repair itself got wrong.
    /// </summary>
    public static int ForCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (BrokenInvariants.Contains(code))
            return StatusCodes.Status500InternalServerError;

        return code switch
        {
            DiagnosticCodes.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
            DiagnosticCodes.InternalError => StatusCodes.Status500InternalServerError,
            DiagnosticCodes.RouteNotFound => StatusCodes.Status404NotFound,
            DiagnosticCodes.MethodNotAllowed => StatusCodes.Status405MethodNotAllowed,
            DiagnosticCodes.MediaTypeNotSupported => StatusCodes.Status415UnsupportedMediaType,
            // Everything else is a problem with the input the user offered (AD-22).
            _ => StatusCodes.Status400BadRequest
        };
    }

    /// <summary>
    /// The code for a failure status the framework settled by itself — routing that matched no
    /// endpoint or no method, a body in a media type an endpoint cannot read, a request Kestrel
    /// could not finish reading. Every one of them reaches the client as a problem document,
    /// so every one of them needs a code the interface can look up (AD-22).
    /// </summary>
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status404NotFound => DiagnosticCodes.RouteNotFound,
        StatusCodes.Status405MethodNotAllowed => DiagnosticCodes.MethodNotAllowed,
        StatusCodes.Status413PayloadTooLarge => DiagnosticCodes.FileTooLarge,
        StatusCodes.Status415UnsupportedMediaType => DiagnosticCodes.MediaTypeNotSupported,
        // Any other refusal of the request itself; anything at 500 or above is ours.
        < StatusCodes.Status500InternalServerError => DiagnosticCodes.RequestMalformed,
        _ => DiagnosticCodes.InternalError
    };
}
