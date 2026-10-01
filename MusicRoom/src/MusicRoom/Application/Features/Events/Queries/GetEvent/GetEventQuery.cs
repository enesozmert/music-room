using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Queries.GetEvent;

/// <summary>
/// Reads one event — `GET /api/v1/events/{eventId}`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <remarks>
/// Answers null when the caller may not see it, which the endpoint turns into `404`. Never `403`: a distinct
/// answer would confirm that a private event exists.
/// </remarks>
public sealed record GetEventQuery(Guid EventId) : IRequest<EventDetail?>;
