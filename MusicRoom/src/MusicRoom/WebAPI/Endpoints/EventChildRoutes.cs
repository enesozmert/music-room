namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The routes that hang off one event: its licence and its roster.
/// </summary>
/// <remarks>
/// One call instead of two in <see cref="EventEndpoints"/>. Each route set already lives in its own class for
/// the file-length gate; naming both of them from the parent put that class over the coupling gate (S1200), so
/// the parent names this one instead — the same reason <c>PlaylistChildRoutes</c> exists.
/// </remarks>
internal static class EventChildRoutes
{
    /// <summary>Maps every child route onto the event group.</summary>
    /// <param name="group">The <c>/api/v1/events</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        EventLicenceEndpoints.Map(group);
        EventRosterEndpoints.Map(group);
    }
}
