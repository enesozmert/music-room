using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Events.Commands.RemoveSuggestion;

/// <summary>
/// Removes a suggestion — `DELETE /api/v1/events/{eventId}/tracks/{suggestionId}`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="SuggestionId">The suggestion.</param>
/// <remarks>
/// The suggester's or the owner's, and nobody else's. Its votes go with it: a vote is a vote *for that
/// suggestion*, and keeping orphaned rows would leave a count that counts nothing — SPEC-018 decision 7.
/// </remarks>
public sealed record RemoveSuggestionCommand(Guid EventId, Guid SuggestionId)
    : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.remove-suggestion";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
