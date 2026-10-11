using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Captures what the application tried to send. It is the only way a test can learn a verification
/// credential: nothing else ever returns the plaintext, which is the property under test elsewhere.
/// </summary>
/// <remarks>
/// Since SPEC-052 it captures the whole message rather than the token alone, because the language, the
/// code and the recipient's name are now things a test has to be able to assert.
/// </remarks>
internal sealed class RecordingMailService : IMailService
{
    private readonly List<VerificationMail> _verifications = [];
    private readonly List<PasswordResetMail> _resets = [];
    private readonly List<WelcomeMail> _welcomes = [];

    /// <summary>The verification messages handed to the sender, in order.</summary>
    internal IReadOnlyList<VerificationMail> Verifications => Snapshot(_verifications);

    /// <summary>The plaintext verification tokens, in order — what most suites actually want.</summary>
    internal IReadOnlyList<string> Tokens => [.. Snapshot(_verifications).Select(mail => mail.Token)];

    /// <summary>The six-character codes, in order.</summary>
    internal IReadOnlyList<string> Codes => [.. Snapshot(_verifications).Select(mail => mail.Code)];

    /// <summary>The plaintext password reset tokens, in order.</summary>
    internal IReadOnlyList<string> ResetTokens => [.. Snapshot(_resets).Select(mail => mail.Token)];

    /// <summary>The welcome messages, in order. One per account, or the stamp is not doing its job.</summary>
    internal IReadOnlyList<WelcomeMail> Welcomes => Snapshot(_welcomes);

    /// <inheritdoc />
    public Task SendEmailVerificationAsync(VerificationMail message, CancellationToken cancellationToken)
    {
        // Registrations run in parallel in one of these tests, so every list is guarded.
        Add(_verifications, message);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendPasswordResetAsync(PasswordResetMail message, CancellationToken cancellationToken)
    {
        Add(_resets, message);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendWelcomeAsync(WelcomeMail message, CancellationToken cancellationToken)
    {
        Add(_welcomes, message);

        return Task.CompletedTask;
    }

    private static void Add<T>(List<T> messages, T message)
    {
        lock (messages)
        {
            messages.Add(message);
        }
    }

    private static IReadOnlyList<T> Snapshot<T>(List<T> messages)
    {
        lock (messages)
        {
            return [.. messages];
        }
    }
}
