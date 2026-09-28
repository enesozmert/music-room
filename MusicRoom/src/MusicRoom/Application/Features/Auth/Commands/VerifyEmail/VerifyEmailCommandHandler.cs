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

        VerifyEmailOutcome outcome = await _users
            .TryConsumeEmailVerificationTokenAsync(
                OpaqueTokenHash.Of(request.Token),
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

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
}
