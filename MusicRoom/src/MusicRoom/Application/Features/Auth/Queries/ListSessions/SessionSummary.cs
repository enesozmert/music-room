namespace MusicRoom.Application.Features.Auth.Queries.ListSessions;

/// <summary>
/// One live session, as its owner sees it — `GET /api/v1/auth/sessions`.
/// </summary>
/// <param name="SessionId">The refresh-token family id, which is what `DELETE /auth/sessions/{sessionId}`
/// takes. An opaque identifier: holding it grants nothing.</param>
/// <param name="Platform">The client platform that started the session.</param>
/// <param name="Device">The device that started it — the field that makes this list usable by a human.</param>
/// <param name="AppVersion">The app version that started it.</param>
/// <param name="StartedAtUtc">When the session began.</param>
/// <param name="ExpiresAtUtc">When it stops being renewable — the family's absolute 30-day expiry.</param>
/// <remarks>
/// It carries **no token and no digest**. The point of the endpoint is to let a user recognise a session, not
/// to hand them the credential that drives it (§13.5).
/// </remarks>
public sealed record SessionSummary(
    Guid SessionId,
    string Platform,
    string Device,
    string AppVersion,
    DateTime StartedAtUtc,
    DateTime ExpiresAtUtc);
