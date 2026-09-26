using System.Security.Cryptography;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth.Commands.Register;

/// <summary>
/// Hashes the password, writes the account and issues its verification token — SDD §10.1 step 3.
/// </summary>
internal sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Takes the write path and the hasher; both are abstractions this layer declares.</summary>
    public RegisterCommandHandler(IUserRepository users, IPasswordHasher passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    /// <summary>Registers the account, or does nothing visible when the address is already taken.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        User user = new()
        {
            Id = Guid.NewGuid(),
            // Lower-cased on the way in, so ux_users_email is case-insensitive without a functional
            // index: Alice@example.com and alice@example.com are one account, not two.
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = request.DisplayName.Trim(),
            IsEmailVerified = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        // The result is deliberately discarded. A duplicate address must be indistinguishable from a
        // fresh one to the caller — SDD §10.1 step 5 — and the mailbox, not the status code, tells
        // the real user what happened.
        _ = await _users
            .TryRegisterAsync(user, IssueVerificationToken(user.Id, nowUtc), cancellationToken)
            .ConfigureAwait(false);
    }

    private static EmailVerificationToken IssueVerificationToken(Guid userId, DateTime nowUtc)
    {
        // 256 bits of entropy, which is what the token needs to be unguessable.
        const int tokenByteLength = 32;

        // The verification window — authentication-authorization.
        const int tokenLifetimeHours = 24;

        // The token is generated, hashed and stored. Nothing carries it out of this method: the mail
        // that would deliver it is SPEC-003, and a token nobody has seen cannot be replayed.
        byte[] token = RandomNumberGenerator.GetBytes(tokenByteLength);

        return new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Convert.ToHexString(SHA256.HashData(token)),
            ExpiresAtUtc = nowUtc.AddHours(tokenLifetimeHours),
            ConsumedAtUtc = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }
}
