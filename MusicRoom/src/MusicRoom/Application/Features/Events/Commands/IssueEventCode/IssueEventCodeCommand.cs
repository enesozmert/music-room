using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Events.Commands.IssueEventCode;

/// <summary>
/// Issues a short-lived venue code — `POST /api/v1/events/{eventId}/event-codes`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="TimeToLiveSeconds">How long the code should be accepted for.</param>
/// <remarks>
/// Owner only, and rate limited per account: a code is credential-shaped, and a route that mints credentials
/// without a limit is a route that mints them in bulk.
/// </remarks>
public sealed record IssueEventCodeCommand(Guid EventId, int TimeToLiveSeconds)
    : IRequest<string?>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.issue-code";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
