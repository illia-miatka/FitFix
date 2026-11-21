namespace FitFix.Api.Contracts;

/// <summary>
/// What <c>GET /health</c> answers: enough for a container probe to tell a running service
/// from a port that merely accepts connections. Hand-written like every other body (AD-14).
/// </summary>
/// <param name="Status">Always <c>ok</c>; a service that cannot answer does not answer at all.</param>
public sealed record HealthResponse(string Status);
