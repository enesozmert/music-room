using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>
/// Hashes the submitted token and asks the write path to spend it — SDD §10.1 step 7.
/// </summary>
internal sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, VerifyEmailOutcome>
{
    private readonly IUserRepository _users;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path and the audit context.</summary>
    /// <param name="users">The account write path.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public VerifyEmailCommandHandler(IUserRepository users, IAuditContext audit)
    {
        _users = users;
        _audit = audit;
    }

    /// <summary>Spends the token, or reports why it could not be spent.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into a status code.</returns>
    /// <remarks>
    /// Neither the token nor its digest is ever logged here. A digest of a live credential is still
    /// the lookup key that spends it.
    /// </remarks>
    public async Task<VerifyEmailOutcome> Handle(
        VerifyEmailCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTime nowUtc = DateTime.UtcNow;

        VerifyEmailOutcome outcome = request.Token is { Length: > 0 } token
            ? await _users
                .TryConsumeEmailVerificationTokenAsync(OpaqueTokenHash.Of(token), nowUtc, cancellationToken)
                .ConfigureAwait(false)
            : await ConsumeCodeAsync(request, nowUtc, cancellationToken).ConfigureAwait(false);

        // No account is named: this path is keyed on a token, and a row naming the account a refused token
        // once belonged to would say more than the caller proved.
        _audit.SetOutcome(outcome switch
        {
            VerifyEmailOutcome.Verified => AuditOutcome.Completed,
            VerifyEmailOutcome.Expired => AuditOutcome.Expired,
            _ => AuditOutcome.Refused,
        });

        return outcome;
    }

    /// <summary>
    /// Spends a typed code, after checking it could be one at all.
    /// </summary>
    /// <remarks>
    /// The shape check comes first so that a caller sending a sentence costs a comparison rather than a
    /// query — and, more importantly, so the attempt budget is spent by guesses at a code rather than by
    /// malformed input. An address with no code, or a code of the wrong shape, is the same `Invalid`
    /// everything else is.
    /// </remarks>
    private async Task<VerifyEmailOutcome> ConsumeCodeAsync(
        VerifyEmailCommand request,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (request.Email is not { Length: > 0 } email || request.Code is null)
        {
            return VerifyEmailOutcome.Invalid;
        }

        string code = VerificationCode.Normalise(request.Code);

        if (!VerificationCode.IsWellFormed(code))
        {
            return VerifyEmailOutcome.Invalid;
        }

        return await _users
            .TryConsumeEmailVerificationCodeAsync(
                email.Trim().ToLowerInvariant(),
                OpaqueTokenHash.Of(code),
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
