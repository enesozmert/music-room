namespace MusicRoom.Application.Features.Auth.Commands.EndSession;

/// <summary>
/// What an attempt to end a session did.
/// </summary>
public enum EndSessionOutcome
{
    /// <summary>
    /// There was no live session of that id belonging to the caller. Unknown, already ended and
    /// **somebody else's** are one outcome on purpose: the endpoint answers <c>404</c> to all three, because
    /// a distinct answer would confirm another account's session exists.
    /// </summary>
    NotFound = 0,

    /// <summary>The session was ended: its family is revoked and no token from it can rotate again.</summary>
    Ended = 1,
}
