using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// Sends the one welcome message an account ever gets — SPEC-052 decision 9.
/// </summary>
/// <remarks>
/// **Called from <see cref="SessionIssuer" />, so every way in is covered**: a password sign-in, a social
/// one, and the authorization code grant all open a session through it. Putting this in the password
/// handler would have meant somebody who only ever signs in with Google never being welcomed.
/// <para>
/// **The stamp decides, not a read.** `TryStampWelcomeAsync` is a conditional UPDATE, so two sign-ins that
/// race produce one message and the database picks the winner. A read-then-send would produce two.
/// </para>
/// <para>
/// **Nothing here can fail a sign-in.** The dispatcher swallows a provider failure, and the stamp is
/// written before the message is attempted: a mail provider having a bad minute must not cost somebody
/// their session, and the alternative — stamping after a successful send — would retry the welcome on
/// every sign-in until the provider came back.
/// </para>
/// </remarks>
public sealed class WelcomeMailSender
{
    private readonly IUserRepository _users;
    private readonly IIdentityClaimsReader _claims;
    private readonly CredentialMailDispatcher _mail;

    /// <summary>Takes the stamp, the claims to address the reader by, and the dispatcher.</summary>
    /// <param name="users">Where the stamp is written.</param>
    /// <param name="claims">Where the address and the display name come from — SPEC-045's reader, which
    /// projects three columns and never the password hash.</param>
    /// <param name="mail">The dispatcher, which swallows a provider failure.</param>
    public WelcomeMailSender(
        IUserRepository users,
        IIdentityClaimsReader claims,
        CredentialMailDispatcher mail)
    {
        _users = users;
        _claims = claims;
        _mail = mail;
    }

    /// <summary>Sends the welcome message, if this is the first session this account has opened.</summary>
    /// <param name="userId">The account that just signed in.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the attempt is over, sent or not.</returns>
    public async Task SendIfFirstAsync(Guid userId, CancellationToken cancellationToken)
    {
        bool first = await _users
            .TryStampWelcomeAsync(userId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!first)
        {
            return;
        }

        IdentityClaims? claims = await _claims.FindAsync(userId, cancellationToken).ConfigureAwait(false);

        if (claims is null)
        {
            // The account was deleted between the stamp and this read. Nothing to send, and nothing to
            // repair: the stamp belongs to a row that no longer exists.
            return;
        }

        await _mail
            .SendWelcomeAsync(claims.Email, claims.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }
}
