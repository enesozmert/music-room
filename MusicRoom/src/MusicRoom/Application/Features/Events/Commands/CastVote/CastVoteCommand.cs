using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Events.Presence;

namespace MusicRoom.Application.Features.Events.Commands.CastVote;

/// <summary>
/// Votes for a suggestion — `PUT /api/v1/events/{eventId}/tracks/{suggestionId}/vote`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="SuggestionId">The suggestion.</param>
/// <param name="Proof">What the client supplied to prove presence, when the licence requires it.</param>
/// <remarks>
/// The contended write subject V.2.1 grades. Idempotent: `201` the first time and `200` after, including when a
/// concurrent request wrote the row — the unique index is the arbiter, never a read-then-write check.
/// <para>
/// The voter is the validated token's `sub`. There is deliberately no user id in the command: a route that
/// accepted one would be a route somebody would eventually pass somebody else's.
/// </para>
/// </remarks>
public sealed record CastVoteCommand(Guid EventId, Guid SuggestionId, PresenceProofSubmission? Proof)
    : IRequest<VoteWriteResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.vote";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
