namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// Who is calling, according to the token the bearer middleware already validated — SDD §13.2 gate 1.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>WebAPI</c>, where the principal lives, exactly as
/// <c>IClientContextAccessor</c> is. A handler behind <c>RequireAuthorization</c> may rely on both properties
/// being present; a handler that finds them absent must fail loudly rather than act on a guess, because the
/// alternative is acting on somebody else's data.
/// <para>
/// It is deliberately not in <c>Core.Security</c>: <see cref="SessionId"/> is this application's session
/// model, not a domain-agnostic concept.
/// </para>
/// </remarks>
public interface ICurrentUserAccessor
{
    /// <summary>The account from <c>sub</c>, or null on an anonymous request.</summary>
    Guid? UserId { get; }

    /// <summary>The refresh-token family from <c>sid</c>, or null on an anonymous request.</summary>
    Guid? SessionId { get; }
}
