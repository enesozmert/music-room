using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Captures what the application tried to send. It is the only way a test can learn a verification
/// token: nothing else ever returns the plaintext, which is the property under test elsewhere.
/// </summary>
internal sealed class RecordingMailService : IMailService
{
    private readonly List<string> _tokens = [];

    /// <summary>The plaintext tokens handed to the sender, in order.</summary>
    internal IReadOnlyList<string> Tokens
    {
        get
        {
            lock (_tokens)
            {
                return [.. _tokens];
            }
        }
    }

    /// <inheritdoc />
    public Task SendEmailVerificationAsync(
        string emailAddress,
        string token,
        CancellationToken cancellationToken)
    {
        // Registrations run in parallel in one of these tests, so the list is guarded.
        lock (_tokens)
        {
            _tokens.Add(token);
        }

        return Task.CompletedTask;
    }
}
