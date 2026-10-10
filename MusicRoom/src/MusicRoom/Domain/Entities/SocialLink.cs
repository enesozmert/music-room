using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One provider identity bound to one account — subject V.1, ADR-0008, SPEC-041.
/// </summary>
/// <remarks>
/// The pair <c>(provider, provider_user_id)</c> is unique, so a Google account signs in to exactly one
/// MusicRoom account and never to two. That uniqueness is the whole of the identity model: an address is
/// **not** the key, because a provider can change the address on an identity and two providers can assert the
/// same address.
/// <para>
/// The address is stored anyway, as it was asserted at the time of linking, because a person looking at their
/// linked accounts needs to recognise which one it is. It is a label, never a lookup key — ADR-0008's
/// account-takeover note is about exactly that distinction.
/// </para>
/// </remarks>
public sealed class SocialLink : Entity
{
    /// <summary>The account this identity signs in.</summary>
    public Guid UserId { get; set; }

    /// <summary>Which provider asserted it — the configuration row's name, lower case.</summary>
    public required string Provider { get; set; }

    /// <summary>The provider's own identifier for the person. The `sub` of a validated ID token.</summary>
    public required string ProviderUserId { get; set; }

    /// <summary>The address the provider asserted when this link was made. A label, not a key.</summary>
    public string? Email { get; set; }
}
