namespace FitFix.Api.Contracts;

/// <summary>
/// The body of every failed response: an RFC 9457 problem document carrying the code the
/// interface looks up (AD-22). Hand-written, like every other body the API returns (AD-14),
/// and deliberately without a <c>detail</c> member — nothing of an exception reaches the
/// client, and the human sentence belongs to the UI dictionary keyed by
/// <see cref="Code"/> (AD-4, AD-18).
/// </summary>
/// <param name="Type">A stable URN naming the problem; it identifies the kind, not the instance.</param>
/// <param name="Title">The HTTP reason phrase of <paramref name="Status"/>, for a developer reading a log or a trace.</param>
/// <param name="Status">The HTTP status code, repeated inside the body as RFC 9457 asks.</param>
/// <param name="Code">A code declared in <see cref="FitFix.Core.Diagnostics.DiagnosticCodes"/>.</param>
public sealed record ProblemResponse(string Type, string Title, int Status, string Code);
