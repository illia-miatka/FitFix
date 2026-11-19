namespace FitFix.Core.Diagnostics;

/// <summary>
/// Base class of every refusal the core raises over user data (AD-18). The
/// <see cref="Code"/> comes from <see cref="DiagnosticCodes"/>, so a shell can fill
/// <c>ProblemDetails.code</c> from any core failure without a type switch (AD-22).
/// <see cref="Exception.Message"/> is developer-facing English: human text belongs to
/// the UI dictionary and to the CLI report.
/// </summary>
public abstract class FitFixException : Exception
{
    protected FitFixException(string code, string message)
        : base(message) => Code = Declared(code);

    protected FitFixException(string code, string message, Exception innerException)
        : base(message, innerException) => Code = Declared(code);

    /// <summary>A code declared in <see cref="DiagnosticCodes"/>; never empty.</summary>
    public string Code { get; }

    /// <summary>The severity the registry attaches to <see cref="Code"/> (AD-22).</summary>
    public DiagnosticSeverity Severity => DiagnosticCodes.SeverityOf(Code);

    /// <summary>
    /// An undeclared code is a programming error: it would reach the shell and the UI
    /// dictionary with no severity and no text, so it is rejected where it is written
    /// rather than later, inside error handling.
    /// </summary>
    private static string Declared(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        if (!DiagnosticCodes.IsDeclared(code))
            throw new ArgumentException($"Code '{code}' is not declared in DiagnosticCodes.", nameof(code));
        return code;
    }
}

/// <summary>The file offered as an activity or as a FIT track is not usable FIT.</summary>
public sealed class InvalidFitFileException : FitFixException
{
    public InvalidFitFileException(string code, string message)
        : base(code, message)
    {
    }

    public InvalidFitFileException(string code, string message, Exception innerException)
        : base(code, message, innerException)
    {
    }
}

/// <summary>The file offered as the reference track is not a usable track.</summary>
public sealed class InvalidTrackFileException : FitFixException
{
    public InvalidTrackFileException(string code, string message)
        : base(code, message)
    {
    }

    public InvalidTrackFileException(string code, string message, Exception innerException)
        : base(code, message, innerException)
    {
    }
}

/// <summary>
/// A refusal over a malformed argument, where a generic <see cref="ArgumentException"/>
/// would hide the code AD-18 requires. Used where the data reaches a core method already
/// parsed, so no file format is at fault.
/// </summary>
public sealed class MalformedArgumentException(string code, string message) : FitFixException(code, message);
