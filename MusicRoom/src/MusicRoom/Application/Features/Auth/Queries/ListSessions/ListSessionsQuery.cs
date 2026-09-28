using MediatR;

namespace MusicRoom.Application.Features.Auth.Queries.ListSessions;

/// <summary>
/// Lists the caller's own live sessions — `GET /api/v1/auth/sessions`.
/// </summary>
/// <remarks>
/// The first query in this codebase, and deliberately **not** an <c>IAuditableCommand</c>: reading your own
/// session list changes nothing, and an audit row per read would bury the deliberate actions the log exists
/// for — the same reasoning ADR-0019 gives for not auditing hub connects.
/// <para>
/// It takes no parameter. The account comes from the validated token, never from the request: a query that
/// accepted a user id would be an authorisation bug with a nice signature.
/// </para>
/// </remarks>
public sealed record ListSessionsQuery : IRequest<IReadOnlyList<SessionSummary>>;
