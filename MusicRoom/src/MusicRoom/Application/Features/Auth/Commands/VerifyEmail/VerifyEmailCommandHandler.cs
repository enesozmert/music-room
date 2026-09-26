using System.Security.Cryptography;
using System.Text;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

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
                VerificationTokenHash.Of(request.Token),
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// The one definition of how a verification token becomes the digest stored in
/// <c>email_verification_tokens.token_hash</c>.
/// </summary>
/// <remarks>
/// Issuing and spending must agree on this byte for byte, so both sides call this method instead of
/// repeating the algorithm. A copy in each feature folder is a bug waiting for one of them to be
/// changed.
/// </remarks>
internal static class VerificationTokenHash
{
    /// <summary>Hex-encoded SHA-256 of the token's UTF-8 bytes.</summary>
    /// <param name="token">The plaintext token, as issued and as submitted.</param>
    /// <returns>The 64-character digest the row is keyed by.</returns>
    /// <remarks>
    /// SHA-256 rather than a password hash on purpose: the token carries 256 bits of entropy from a
    /// cryptographic generator, so there is nothing to brute-force, and the lookup has to be a
    /// single indexed read — authentication-authorization.
    /// </remarks>
    internal static string Of(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
