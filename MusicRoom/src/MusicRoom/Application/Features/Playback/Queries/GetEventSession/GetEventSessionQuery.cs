using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playback.Queries.GetEventSession;

/// <summary>
/// What an event is playing, for anybody who may see the event — SPEC-058.
/// </summary>
/// <param name="EventId">The event.</param>
/// <remarks>
/// The read a guest had no way to make. `GetSessionQuery` needs a session id they do not have,
/// `GetDeviceSessionQuery` is the device owner's and the controller's, and the only other source was the
/// next `NowPlayingChanged` — so somebody arriving mid-track waited for the next one.
/// </remarks>
public sealed record GetEventSessionQuery(Guid EventId) : IRequest<PlaybackSessionDetail?>;
