using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.ResetPassword;

/// <summary>
/// Spends a reset token, replaces the password, and ends every session opened with the old one —
/// SDD §13.1.
/// </summary>
/// <remarks>
/// The three writes are one transaction in the write path: a reset that replaced the password and
/// failed to revoke the families would leave a thief holding a live session behind a password the
/// owner has already changed.
/// </remarks>
internal sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ResetPasswordOutcome>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the hasher and the audit context.</summary>
    /// <param name="users">The account write path.</param>
    /// <param name="passwordHasher">The password hasher — ADR-0007.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ResetPasswordCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IAuditContext audit)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    /// <summary>Replaces the password, or reports why the token could not be spent.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into a status code.</returns>
    /// <remarks>
    /// Neither the token, nor its digest, nor either password is ever logged here.
    /// </remarks>
    public async Task<ResetPasswordOutcome> Handle(
        ResetPasswordCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Hashed before the write path is asked anything, so the transaction holds no locks while
        // BCrypt runs for a quarter of a second.
        string newPasswordHash = _passwordHasher.Hash(request.NewPassword);

        PasswordResetResult result = await _users
            .TryResetPasswordAsync(
                OpaqueTokenHash.Of(request.Token),
                newPasswordHash,
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        _audit.SetOutcome(result.Outcome switch
        {
            ResetPasswordOutcome.Reset => AuditOutcome.Completed,
            ResetPasswordOutcome.Expired => AuditOutcome.Expired,
            _ => AuditOutcome.Refused,
        });

        if (result.UserId is Guid userId)
        {
            _audit.SetUserId(userId);
            _audit.SetTargetId(userId);
        }

        return result.Outcome;
    }
}
