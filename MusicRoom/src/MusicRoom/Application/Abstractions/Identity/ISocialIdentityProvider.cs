namespace MusicRoom.Application.Abstractions.Identity;

/// <summary>
/// Turns an authorization code into a provider identity this API trusts — ADR-0008, SPEC-041.
/// </summary>
/// <remarks>
/// **One implementation for every provider.** A provider is a configuration row — its discovery URL, its
/// client id and secret, and how its ID token arrives — and the validation that follows names no provider at
/// all. Adding Facebook is a row plus an acquisition, not a second integration.
/// <para>
/// The seam is here rather than in the handler because what happens behind it is two network calls and a
/// signature check, and a handler that owned those would be untestable without a provider.
/// </para>
/// </remarks>
public interface ISocialIdentityProvider
{
    /// <summary>
    /// Exchanges an authorization code and validates the ID token it yields.
    /// </summary>
    /// <param name="request">Which provider, and what the client came back from it with.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The identity, or why it was refused. Never an exception for a refusal.</returns>
    /// <remarks>
    /// A refusal is a returned value, not a throw: the audit behaviour is fail closed and shares the handler's
    /// transaction, so an exception from inside a handler takes the row recording the refusal with it —
    /// SPEC-009's lesson, paid for once already.
    /// </remarks>
    Task<SocialAuthentication> AuthenticateAsync(
        SocialAuthenticationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>What the client came back from the provider with — ADR-0008's B shape, SPEC-041.</summary>
/// <param name="Provider">The configuration row's name, lower case.</param>
/// <param name="Code">The authorization code. Exchanged here, by this API, with the client secret.</param>
/// <param name="CodeVerifier">The PKCE verifier matching the challenge the client sent to the provider.</param>
/// <param name="RedirectUri">The redirect URI the code was issued against. The provider checks it matches.</param>
public sealed record SocialAuthenticationRequest(
    string Provider,
    string Code,
    string CodeVerifier,
    string RedirectUri);

/// <summary>Why an authentication ended as it did.</summary>
public enum SocialAuthenticationOutcome
{
    /// <summary>The identity is established and may be trusted.</summary>
    Authenticated = 0,

    /// <summary>No configuration row names this provider.</summary>
    UnknownProvider = 1,

    /// <summary>The provider refused the code, the verifier or the redirect URI.</summary>
    ExchangeRefused = 2,

    /// <summary>The ID token failed one of the checks. Which one is in the log, never in the response.</summary>
    TokenInvalid = 3,
}

/// <summary>The result of an authentication — ADR-0008's six checks, passed or not.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Identity">The identity, present only when <paramref name="Outcome"/> is authenticated.</param>
public sealed record SocialAuthentication(SocialAuthenticationOutcome Outcome, SocialIdentity? Identity)
{
    /// <summary>An identity this API may act on.</summary>
    /// <param name="identity">Who the provider says this is.</param>
    /// <returns>The authenticated result.</returns>
    public static SocialAuthentication Of(SocialIdentity identity) =>
        new(SocialAuthenticationOutcome.Authenticated, identity);

    /// <summary>A refusal, carrying no identity.</summary>
    /// <param name="outcome">Which refusal.</param>
    /// <returns>The refused result.</returns>
    public static SocialAuthentication Refused(SocialAuthenticationOutcome outcome) => new(outcome, null);
}

/// <summary>Who a provider says somebody is — the claims of a validated ID token, and nothing else.</summary>
/// <param name="Provider">The configuration row's name.</param>
/// <param name="ProviderUserId">The token's `sub`. The only identifier this API keys on.</param>
/// <param name="Email">The asserted address, or null when the provider asserted none.</param>
/// <param name="IsEmailVerified">
/// The token's `email_verified`. An unverified address never matches an existing account — ADR-0008 names
/// that as the account-takeover path.
/// </param>
/// <param name="DisplayName">The asserted name, used only when this API creates an account.</param>
public sealed record SocialIdentity(
    string Provider,
    string ProviderUserId,
    string? Email,
    bool IsEmailVerified,
    string? DisplayName);
