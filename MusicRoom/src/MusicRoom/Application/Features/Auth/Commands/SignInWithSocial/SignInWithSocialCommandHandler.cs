using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Identity;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth.Commands.SignInWithSocial;

/// <summary>
/// Signs in with a provider identity, creating the account the first time — subject V.1, SPEC-041.
/// </summary>
/// <remarks>
/// The identity is the pair `(provider, sub)` and nothing else. An address is a label: a provider can change
/// the address on an identity, two providers can assert the same address, and an address nobody verified is
/// an account-takeover path — ADR-0008 names all three.
/// <para>
/// **An unverified address is refused rather than ignored.** Creating a second account for it would leave two
/// rows a person cannot tell apart, and matching it to the existing one is the takeover. Refusing says the
/// truth: this provider has not proven the address, so this API will not act on it.
/// </para>
/// </remarks>
internal sealed class SignInWithSocialCommandHandler
    : IRequestHandler<SignInWithSocialCommand, SignInWithSocialResult>
{
    private readonly ISocialIdentityProvider _identities;
    private readonly ISocialLinkRepository _links;
    private readonly SessionIssuer _sessions;
    private readonly IAuditContext _audit;
    private readonly TimeProvider _clock;

    /// <summary>Takes the relying-party core, the link store, the session issuer, the audit context and the clock.</summary>
    /// <param name="identities">The core that exchanges a code and validates the token — ADR-0008.</param>
    /// <param name="links">Where a provider identity is bound to an account.</param>
    /// <param name="sessions">Opens the refresh-token family and mints the pair, as password sign-in does.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    /// <param name="clock">The instant the rows are stamped with.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public SignInWithSocialCommandHandler(
        ISocialIdentityProvider identities,
        ISocialLinkRepository links,
        SessionIssuer sessions,
        IAuditContext audit,
        TimeProvider clock)
    {
        _identities = identities;
        _links = links;
        _sessions = sessions;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>Exchanges, validates, resolves and issues.</summary>
    /// <param name="request">Which provider, and what the client came back with.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The pair, or why not.</returns>
    public async Task<SignInWithSocialResult> Handle(
        SignInWithSocialCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SocialAuthentication authentication = await _identities
            .AuthenticateAsync(
                new SocialAuthenticationRequest(
                    request.Provider,
                    request.Code,
                    request.CodeVerifier,
                    request.RedirectUri),
                cancellationToken)
            .ConfigureAwait(false);

        if (authentication is not { Outcome: SocialAuthenticationOutcome.Authenticated, Identity: { } identity })
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return SignInWithSocialResult.Refused(
                authentication.Outcome == SocialAuthenticationOutcome.UnknownProvider
                    ? SignInWithSocialOutcome.UnknownProvider
                    : SignInWithSocialOutcome.Refused);
        }

        Guid? existing = await _links
            .FindUserIdAsync(identity.Provider, identity.ProviderUserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is { } userId)
        {
            return SignInWithSocialResult.Issued(
                await IssueAsync(userId, cancellationToken).ConfigureAwait(false));
        }

        if (!identity.IsEmailVerified || string.IsNullOrWhiteSpace(identity.Email))
        {
            // The provider authenticated somebody and asserted no address it has proven. This API has nothing
            // to create an account with that it could later recognise, and nothing it may safely match.
            _audit.SetOutcome(AuditOutcome.Refused);

            return SignInWithSocialResult.Refused(SignInWithSocialOutcome.EmailUnverified);
        }

        return await RegisterAsync(identity, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates the account, its profile and its link in one transaction.</summary>
    /// <remarks>
    /// `IsEmailVerified` is true because the provider proved the address — that is what `email_verified`
    /// means, and sending this person a verification mail would be asking them to prove something already
    /// proven. A race is answered by looking the link up again rather than by guessing which index refused:
    /// by then the winner's row is the answer.
    /// </remarks>
    private async Task<SignInWithSocialResult> RegisterAsync(
        SocialIdentity identity,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        Guid userId = Guid.NewGuid();

        User user = NewUser(identity, userId, nowUtc);
        Profile profile = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        SocialLink link = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = identity.Provider,
            ProviderUserId = identity.ProviderUserId,
            Email = user.Email,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        bool registered = await _links
            .TryRegisterWithLinkAsync(user, profile, link, cancellationToken)
            .ConfigureAwait(false);

        if (registered)
        {
            return SignInWithSocialResult.Issued(
                await IssueAsync(userId, cancellationToken).ConfigureAwait(false));
        }

        Guid? winner = await _links
            .FindUserIdAsync(identity.Provider, identity.ProviderUserId, cancellationToken)
            .ConfigureAwait(false);

        if (winner is { } wonUserId)
        {
            return SignInWithSocialResult.Issued(
                await IssueAsync(wonUserId, cancellationToken).ConfigureAwait(false));
        }

        // The address is registered to an account this identity is not linked to. Linking it here would be
        // matching on an address, which is the takeover path; the person signs in and links deliberately.
        _audit.SetOutcome(AuditOutcome.Refused);

        return SignInWithSocialResult.Refused(SignInWithSocialOutcome.Refused);
    }

    /// <summary>Opens a session for an account this provider just authenticated.</summary>
    /// <remarks>
    /// `FirstPartySession.NoGrantedScope`, because this is not the authorization code flow: nobody asked for
    /// a scope, so `GET /oauth2/userinfo` answers this session the full claim set — SPEC-045 decision 2.
    /// Wrapped in one method rather than repeated at three call sites, which is also what keeps
    /// `RegisterAsync` inside the 40-line method limit the root .editorconfig enforces (S138).
    /// </remarks>
    private Task<AuthTokenPair> IssueAsync(Guid userId, CancellationToken cancellationToken) =>
        _sessions.IssueAsync(userId, FirstPartySession.NoGrantedScope, cancellationToken);

    /// <summary>The account a verified provider identity becomes.</summary>
    /// <remarks>
    /// The address is lower-cased on the way in, so `ux_users_email` stays case-insensitive without a
    /// functional index — the same rule registration follows. A provider that asserts no name leaves the
    /// local part standing in for one: an empty display name would be a profile nobody can address.
    /// </remarks>
    private static User NewUser(SocialIdentity identity, Guid userId, DateTime nowUtc)
    {
        string email = identity.Email!.Trim().ToLowerInvariant();

        return new User
        {
            Id = userId,
            Email = email,
            PasswordHash = null,
            DisplayName = string.IsNullOrWhiteSpace(identity.DisplayName)
                ? email.Split('@')[0]
                : identity.DisplayName.Trim(),
            IsEmailVerified = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }
}
