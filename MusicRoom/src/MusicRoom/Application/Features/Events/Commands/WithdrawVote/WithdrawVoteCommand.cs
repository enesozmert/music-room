using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Events.Commands.WithdrawVote;

/// <summary>
/// Takes back a vote — `DELETE /api/v1/events/{eventId}/tracks/{suggestionId}/vote`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="SuggestionId">The suggestion.</param>
/// <remarks>
/// The caller's own vote and no other. No presence proof is asked for: leaving a venue is not a reason to be
/// stuck having voted, and a withdrawal lowers a count rather than raising one.
/// </remarks>
public sealed record WithdrawVoteCommand(Guid EventId, Guid SuggestionId)
    : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.withdraw-vote";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
