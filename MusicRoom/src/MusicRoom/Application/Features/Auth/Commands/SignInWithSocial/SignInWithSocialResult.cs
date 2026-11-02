using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.SignInWithSocial;

/// <summary>
/// What a social sign-in produced — SPEC-041.
/// </summary>
/// <param name="Pair">The token pair on success, null otherwise.</param>
/// <param name="Outcome">Why, when there is no pair.</param>
public sealed record SignInWithSocialResult(AuthTokenPair? Pair, SignInWithSocialOutcome Outcome)
{
    /// <summary>A successful sign-in, whether the account existed a moment ago or not.</summary>
    /// <param name="pair">The issued pair.</param>
    /// <returns>The result the endpoint turns into `200`.</returns>
    public static SignInWithSocialResult Issued(AuthTokenPair pair) =>
        new(pair, SignInWithSocialOutcome.SignedIn);

    /// <summary>A refusal.</summary>
    /// <param name="outcome">Which one.</param>
    /// <returns>The result the endpoint turns into `401` or `422`.</returns>
    public static SignInWithSocialResult Refused(SignInWithSocialOutcome outcome) => new(null, outcome);
}

/// <summary>What became of the attempt.</summary>
public enum SignInWithSocialOutcome
{
    /// <summary>A session was opened.</summary>
    SignedIn = 0,

    /// <summary>No configuration row names that provider — a client error, so `422`.</summary>
    UnknownProvider = 1,

    /// <summary>
    /// The provider refused the code, or the token failed a check. One outcome for both, so the response
    /// cannot be used to tell which — the distinction is in the log.
    /// </summary>
    Refused = 2,

    /// <summary>
    /// An address this API already knows, asserted by a provider that has not verified it. Matching on it
    /// would be the account-takeover path ADR-0008 names, so the attempt is refused rather than linked.
    /// </summary>
    EmailUnverified = 3,

    /// <summary>
    /// The provider verified an address that already belongs to an account this identity is not linked
    /// to — SPEC-057. Still refused, for ADR-0008's reason, and now said out loud.
    /// </summary>
    /// <remarks>
    /// The only refusal on this route that describes the state of an account, and it is safe precisely
    /// because of what had to happen first: the provider asserted <c>email_verified</c> for this address,
    /// so the caller has proved they control it. Telling the owner of an address that it has an account
    /// tells them nothing a password-reset request would not.
    /// </remarks>
    AddressAlreadyRegistered = 4,
}
