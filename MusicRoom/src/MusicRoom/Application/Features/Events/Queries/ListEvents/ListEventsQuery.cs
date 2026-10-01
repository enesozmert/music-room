using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Queries.ListEvents;

/// <summary>
/// Lists events in one scope — `GET /api/v1/events`.
/// </summary>
/// <param name="Scope">Mine, invited, or public.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows, capped by the endpoint.</param>
/// <remarks>
/// One scope at a time, and a private event the caller was not invited to appears in none of them.
/// </remarks>
public sealed record ListEventsQuery(EventScope Scope, int Page, int PageSize)
    : IRequest<IReadOnlyList<EventSummary>>;
