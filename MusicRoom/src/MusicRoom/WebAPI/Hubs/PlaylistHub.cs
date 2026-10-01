using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MusicRoom.Application.Features.Playlists.Queries.AuthoriseJoin;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// The live playlist — SDD §9.2, ENDPOINTS §9, ADR-0004.
/// </summary>
/// <remarks>
/// Transport only. The two methods validate an argument, dispatch one CQRS message and return; no state
/// changes here, because every mutation goes through the REST routes and their one authorisation and
/// validation pipeline (ENDPOINTS §10).
/// <para>
/// Group membership is authorised **server-side on every join**, and a reconnect is a new connection with a
/// new join, so the gate is re-asked by construction rather than by remembering to.
/// </para>
/// </remarks>
[Authorize]
public sealed class PlaylistHub : Hub
{
    /// <summary>
    /// Where the hub is mapped. Under <see cref="Middleware.ClientContextMiddleware.HubPaths"/>, which is
    /// what exempts the negotiate `POST` from the header check the hub performs itself.
    /// </summary>
    public const string Path = "/hubs/playlists";

    /// <summary>The `TrackAdded` event name.</summary>
    public const string TrackAddedEvent = "TrackAdded";

    /// <summary>The `TrackMoved` event name.</summary>
    public const string TrackMovedEvent = "TrackMoved";

    /// <summary>The `TrackRemoved` event name.</summary>
    public const string TrackRemovedEvent = "TrackRemoved";

    /// <summary>The `CollaboratorChanged` event name.</summary>
    public const string CollaboratorChangedEvent = "CollaboratorChanged";

    /// <summary>The wire name of the join method — ENDPOINTS §9.</summary>
    public const string JoinMethod = "JoinPlaylist";

    /// <summary>The wire name of the leave method — ENDPOINTS §9.</summary>
    public const string LeaveMethod = "LeavePlaylist";

    private readonly ISender _sender;

    /// <summary>Takes the dispatcher the two methods send through.</summary>
    /// <param name="sender">MediatR, so the hub holds no data access of its own.</param>
    public PlaylistHub(ISender sender) => _sender = sender;

    /// <summary>The group one playlist's listeners share.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <returns>The group name, <c>playlist:{playlistId}</c> — SDD §9.2.</returns>
    public static string Group(Guid playlistId) =>
        string.Create(CultureInfo.InvariantCulture, $"playlist:{playlistId}");

    /// <summary>Captures the client context, or refuses the connection — ADR-0019.</summary>
    /// <returns>A task that completes when the connection is accepted.</returns>
    /// <exception cref="HubException">
    /// The handshake carried no platform, device and app version. Refused rather than defaulted: this is the
    /// `400` a REST mutation gets for the same omission, so neither transport can produce an action nobody
    /// can attribute.
    /// </exception>
    public override async Task OnConnectedAsync()
    {
        HubClientContext context = HubClientContext.FromHandshake(Context.GetHttpContext())
            ?? throw new HubException(
                "A hub handshake must carry platform, device and appVersion — ENDPOINTS §9.");

        Context.Items[HubClientContext.ItemKey] = context;

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>Joins the group for one playlist, if the caller may see it.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <returns>A task that completes when the connection is in the group.</returns>
    /// <exception cref="HubException">
    /// The caller may not see this playlist, or there is no such playlist. One message for both, as the REST
    /// `404` is one answer for both: a distinct refusal would confirm the playlist exists.
    /// </exception>
    /// <remarks>
    /// The wire name is <c>JoinPlaylist</c>, which is what ENDPOINTS §9 publishes and what a client calls.
    /// The C# name carries the suffix the repository's naming rule requires of an async method; the attribute
    /// keeps the contract from depending on a local convention.
    /// </remarks>
    [HubMethodName(JoinMethod)]
    public async Task JoinPlaylistAsync(Guid playlistId)
    {
        bool mayJoin = await _sender
            .Send(new AuthorisePlaylistJoinQuery(playlistId), Context.ConnectionAborted)
            .ConfigureAwait(false);

        if (!mayJoin)
        {
            throw new HubException("No such playlist.");
        }

        await Groups
            .AddToGroupAsync(Context.ConnectionId, Group(playlistId), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>Leaves the group for one playlist.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <returns>A task that completes when the connection has left.</returns>
    /// <remarks>
    /// Unauthorised on purpose: leaving a group tells nobody anything, and refusing to let a client stop
    /// listening would be a worse answer than letting it.
    /// </remarks>
    [HubMethodName(LeaveMethod)]
    public Task LeavePlaylistAsync(Guid playlistId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(playlistId), Context.ConnectionAborted);
}
