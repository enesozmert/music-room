using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Queries.GetQueue;

/// <summary>
/// The vote-ordered queue — `GET /api/v1/events/{eventId}/queue`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows, capped by the endpoint.</param>
/// <remarks>
/// The `member` gate: anybody who may see the event may see its queue. Answers null when they may not, which
/// the endpoint turns into `404`.
/// </remarks>
public sealed record GetQueueQuery(Guid EventId, int Page, int PageSize)
    : IRequest<IReadOnlyList<QueueEntry>?>;
