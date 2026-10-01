using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Commands.SuggestTrack;

/// <summary>
/// Suggests a track for an event — `POST /api/v1/events/{eventId}/tracks`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="YoutubeVideoId">The video, stored verbatim.</param>
/// <remarks>
/// Behind the `licence` gate: who may suggest is who may vote. The same video twice is `409`, because two rows
/// would split its votes between two queue entries — SPEC-018 decision 3.
/// </remarks>
public sealed record SuggestTrackCommand(Guid EventId, string YoutubeVideoId)
    : IRequest<SuggestionWriteResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.suggest-track";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
