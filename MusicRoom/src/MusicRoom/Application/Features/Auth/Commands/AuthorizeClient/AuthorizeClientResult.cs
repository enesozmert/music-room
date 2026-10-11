namespace MusicRoom.Application.Features.Auth.Commands.AuthorizeClient;

/// <summary>
/// What the authorization endpoint decided — SPEC-044.
/// </summary>
/// <param name="Outcome">Which decision.</param>
/// <param name="Code">The authorization code, in plaintext, on <see cref="AuthorizeOutcome.Granted"/> only.
/// This is the only moment it exists outside the client's own redirect: the database keeps its digest.</param>
public sealed record AuthorizeClientResult(AuthorizeOutcome Outcome, string? Code)
{
    /// <summary>A refusal, which carries no code.</summary>
    /// <param name="outcome">Which refusal.</param>
    /// <returns>The result.</returns>
    public static AuthorizeClientResult Refused(AuthorizeOutcome outcome) => new(outcome, null);
}

/// <summary>
/// The decisions, split so each one answers differently.
/// </summary>
/// <remarks>
/// **The first two never redirect.** An unknown client and an unregistered redirect URI are the two cases
/// where this API does not know that the URI in the request belongs to anybody, so sending an error to it
/// would be the open redirect the registry exists to prevent — RFC 6749 §4.1.2.1 says so in as many words.
/// Everything below them is reported to a URI that is known to be the client's own.
/// </remarks>
public enum AuthorizeOutcome
{
    /// <summary>A code was issued and is bound to the client, the URI, the challenge and the account.</summary>
    Granted = 0,

    /// <summary>No configuration row names that client. Answered `400`, never redirected.</summary>
    UnknownClient = 1,

    /// <summary>The client exists and has not registered that URI. Answered `400`, never redirected.</summary>
    UnregisteredRedirectUri = 2,

    /// <summary>Something other than <c>code</c> was asked for — <c>unsupported_response_type</c>.</summary>
    UnsupportedResponseType = 3,

    /// <summary>The challenge is absent, malformed, or its method is not <c>S256</c> —
    /// <c>invalid_request</c>.</summary>
    InvalidChallenge = 4,
}
