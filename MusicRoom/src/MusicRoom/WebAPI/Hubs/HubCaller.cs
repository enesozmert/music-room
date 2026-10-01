using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// Who is calling over the hub, for the one invocation being handled.
/// </summary>
/// <remarks>
/// A hub invocation has no <c>HttpContext</c>: the handshake had one, the invocation does not. The two
/// accessors in <c>Middleware</c> read this when <c>HttpContext</c> is absent, so a handler is authorised the
/// same way whichever transport reached it — which is what `signalr-realtime` requires and what a second
/// accessor abstraction would have quietly broken.
/// <para>
/// Registered scoped. SignalR creates a service scope per invocation, so one instance holds one call's
/// caller and nothing leaks between connections.
/// </para>
/// </remarks>
internal sealed class HubCaller
{
    /// <summary>The three client values from the handshake, plus the trace id and the account.</summary>
    internal ClientContext? Context { get; set; }

    /// <summary>The account, from the validated token the handshake carried.</summary>
    internal Guid? UserId { get; set; }

    /// <summary>The session, from the token's <c>sid</c> claim.</summary>
    internal Guid? SessionId { get; set; }
}
