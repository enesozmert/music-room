using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Features.Auth;

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
    private readonly IPasswordContextReader _passwordContext;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the hasher, the context read and the audit context.</summary>
    /// <param name="users">The account write path.</param>
    /// <param name="passwordHasher">The password hasher — ADR-0007.</param>
    /// <param name="passwordContext">The account's own words, which this path has no other way to reach —
    /// SPEC-035.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ResetPasswordCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IPasswordContextReader passwordContext,
        IAuditContext audit)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _passwordContext = passwordContext;
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

        // Before the write, and before BCrypt: a password refused here leaves the credential spendable,
        // so the caller can follow the same link, or type the same code, again with a different
        // passphrase — SPEC-035, ADR-0042. A credential that matches no account screens against the
        // project's own words alone, and the write path below still decides what an unknown, spent or
        // expired one answers.
        PasswordContextWords? context = await FindContextAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (PasswordContext.ContainsAContextWord(request.NewPassword, context?.Email, context?.DisplayName))
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return ResetPasswordOutcome.RefusedByContext;
        }

        // Hashed before the write path is asked anything, so the transaction holds no locks while
        // BCrypt runs for a quarter of a second.
        string newPasswordHash = _passwordHasher.Hash(request.NewPassword);

        PasswordResetResult result = await SpendAsync(request, newPasswordHash, cancellationToken)
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

    /// <summary>
    /// Reads the account's own words, by whichever credential the caller presented — SPEC-055.
    /// </summary>
    /// <remarks>
    /// The token identifies its row and the row names the account; the code is scoped by an address and
    /// the address names it directly. Both end at the same two words, which is all the screen wants.
    /// </remarks>
    private Task<PasswordContextWords?> FindContextAsync(
        ResetPasswordCommand request,
        CancellationToken cancellationToken) =>
        request.Token is { Length: > 0 } token
            ? _passwordContext.FindByPasswordResetTokenAsync(
                OpaqueTokenHash.Of(token),
                cancellationToken)
            : _passwordContext.FindByEmailAsync(Normalise(request.Email), cancellationToken);

    /// <summary>
    /// Spends whichever credential was presented, preferring the token when both are.
    /// </summary>
    /// <remarks>
    /// The shape of the code is checked here rather than in the validator, for the reason email
    /// verification gives: a caller sending a sentence costs a comparison rather than a query, and the
    /// attempt budget is then spent by guesses at a code rather than by malformed input.
    /// </remarks>
    private Task<PasswordResetResult> SpendAsync(
        ResetPasswordCommand request,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        if (request.Token is { Length: > 0 } token)
        {
            return _users.TryResetPasswordAsync(
                OpaqueTokenHash.Of(token),
                newPasswordHash,
                nowUtc,
                cancellationToken);
        }

        string code = VerificationCode.Normalise(request.Code ?? string.Empty);

        if (!VerificationCode.IsWellFormed(code) || request.Email is not { Length: > 0 })
        {
            return Task.FromResult(new PasswordResetResult(ResetPasswordOutcome.Invalid, UserId: null));
        }

        return _users.TryResetPasswordByCodeAsync(
            Normalise(request.Email),
            OpaqueTokenHash.Of(code),
            newPasswordHash,
            nowUtc,
            cancellationToken);
    }

    /// <summary>Lower-cases an address the way registration stored it, or returns empty for none.</summary>
    private static string Normalise(string? email) =>
        email is null ? string.Empty : email.Trim().ToLowerInvariant();
}
