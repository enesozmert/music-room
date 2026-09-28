using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>
/// Hashes the submitted token and asks the write path to spend it — SDD §10.1 step 7.
/// </summary>
internal sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, VerifyEmailOutcome>
{
    private readonly IUserRepository _users;

    /// <summary>Takes the write path; the abstraction this layer declares.</summary>
    /// <param name="users">The account write path.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public VerifyEmailCommandHandler(IUserRepository users) => _users = users;

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

        return await _users
            .TryConsumeEmailVerificationTokenAsync(
                OpaqueTokenHash.Of(request.Token),
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
