using MediatR;

namespace MusicRoom.Application.Features.Events.Queries.AuthoriseJoin;

/// <summary>
/// May this caller listen to an event? — `EventHub.JoinEvent`, ENDPOINTS §9.
/// </summary>
/// <param name="EventId">The event the client asked to join.</param>
/// <remarks>
/// A query rather than a repository call from the hub: `signalr-realtime` says a hub validates its arguments,
/// dispatches a CQRS message and returns, so the gate is decided in the one place REST also asks. A client
/// asking to join `event:42` proves nothing.
/// </remarks>
public sealed record AuthoriseEventJoinQuery(Guid EventId) : IRequest<bool>;
