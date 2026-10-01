namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The routes that hang off one playlist: its collaborators and its tracks.
/// </summary>
/// <remarks>
/// One call instead of two in <see cref="PlaylistEndpoints"/>. Each route set already lives in its own class
/// for the file-length gate; naming both of them from the parent put that class one type over the coupling gate
/// (S1200), so the parent names this one instead. The same reason <c>CallerServices</c> exists in
/// <c>Program.cs</c>.
/// </remarks>
internal static class PlaylistChildRoutes
{
    /// <summary>Maps every child route onto the playlist group.</summary>
    /// <param name="group">The <c>/api/v1/playlists</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        PlaylistCollaboratorEndpoints.Map(group);
        PlaylistTrackEndpoints.Map(group);
    }
}
