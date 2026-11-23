using FitFix.Core;
using FitFix.Core.Diagnostics;

namespace FitFix.Api.Contracts;

/// <summary>
/// The strings the contract names the core's enums by (AD-14). One owner per enum: the dropzone
/// and the result screen read the same answer out of the same dictionary, so a sport the inspect
/// endpoint calls <c>running</c> and the repair endpoint called <c>run</c> would be two names for
/// one file (AD-15).
/// </summary>
public static class SportName
{
    /// <summary>
    /// A sport the core does not model is <c>other</c> by definition — that is what
    /// <see cref="SportKind.Other"/> means — so the fall-through names it rather than inventing a
    /// fourth value.
    /// </summary>
    public static string Of(SportKind sport) => sport switch
    {
        SportKind.Running => "running",
        SportKind.Cycling => "cycling",
        _ => "other"
    };
}

/// <summary>
/// What the repair knew about the position over one stretch (FR-4), as a string (AD-14).
/// </summary>
public static class ZoneKindName
{
    /// <summary>The Надійна зона.</summary>
    public const string Measured = "measured";

    /// <summary>The Розрахована зона.</summary>
    public const string Estimated = "estimated";

    /// <summary>
    /// A kind the core models and this contract cannot name is a contract left behind, not a bad
    /// file: it leaves as the shell's own failure rather than telling the athlete their activity
    /// is unreadable.
    /// </summary>
    public static string Of(ZoneKind kind) => kind switch
    {
        ZoneKind.Measured => Measured,
        ZoneKind.Estimated => Estimated,
        _ => throw new InvalidOperationException(
            $"The zone kind {kind} is modelled by the core and has no name in the contract.")
    };
}

/// <summary>
/// Whether a warning withholds the download until the user confirms (AD-22), as a string. The
/// severity travels beside every code so the interface decides the gate from the answer it was
/// given rather than from a table of its own that the registry could outgrow (AD-18).
/// </summary>
public static class SeverityName
{
    /// <summary>The repaired file stays behind an explicit confirmation.</summary>
    public const string Blocking = "blocking";

    /// <summary>Shown to the user and nothing else.</summary>
    public const string Info = "info";

    /// <inheritdoc cref="ZoneKindName.Of"/>
    public static string Of(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Blocking => Blocking,
        DiagnosticSeverity.Info => Info,
        _ => throw new InvalidOperationException(
            $"The severity {severity} is declared by the core and has no name in the contract.")
    };
}
