using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Queries.ListInvitations;

/// <summary>
/// Lists who the owner invited — `GET /api/v1/events/{eventId}/invitations`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <remarks>
/// Owner only. Who else was invited to a private event is not a participant's business, and an empty list for
/// a non-owner would still confirm the event exists — so the endpoint answers `404`.
/// </remarks>
public sealed record ListInvitationsQuery(Guid EventId) : IRequest<IReadOnlyList<InvitationSummary>?>;
