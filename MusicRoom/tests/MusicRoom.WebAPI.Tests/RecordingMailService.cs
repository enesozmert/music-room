using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Captures what the application tried to send. It is the only way a test can learn a verification
/// token: nothing else ever returns the plaintext, which is the property under test elsewhere.
/// </summary>
internal sealed class RecordingMailService : IMailService
{
    private readonly List<string> _tokens = [];

    private readonly List<string> _resetTokens = [];

    /// <summary>The plaintext verification tokens handed to the sender, in order.</summary>
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

    /// <summary>The plaintext password reset tokens handed to the sender, in order.</summary>
    internal IReadOnlyList<string> ResetTokens
    {
        get
        {
            lock (_resetTokens)
            {
                return [.. _resetTokens];
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

    /// <inheritdoc />
    public Task SendPasswordResetAsync(
        string emailAddress,
        string token,
        CancellationToken cancellationToken)
    {
        lock (_resetTokens)
        {
            _resetTokens.Add(token);
        }

        return Task.CompletedTask;
    }
}
