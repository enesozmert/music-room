using System.Text;
using FluentValidation.Results;
using MusicRoom.Application.Features.Auth;
using MusicRoom.Application.Features.Auth.Commands.ResetPassword;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. The password rules are registration's, because the same BCrypt
/// truncation applies to the same column.
/// </summary>
public sealed class ResetPasswordCommandValidatorTests
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string NewPassword = "a brand new passphrase";

    private readonly ResetPasswordCommandValidator _validator =
        new(new StubBreachedPasswordSet(RegisterCommandValidatorTests.BreachedPassword));

    /// <summary>A token of the issued shape and a policy-compliant password pass.</summary>
    [Fact]
    public void Validate_AnIssuedTokenAndAStrongPassword_Passes()
    {
        // Arrange
        ResetPasswordCommand command = new(Token(), NewPassword);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>An empty token never reaches the database.</summary>
    [Fact]
    public void Validate_AnEmptyToken_Fails()
    {
        // Arrange
        ResetPasswordCommand command = new(string.Empty, NewPassword);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(ResetPasswordCommand.Token));
    }

    /// <summary>A token longer than the generator produces is refused rather than hashed.</summary>
    [Fact]
    public void Validate_ATokenLongerThanTheGeneratorProduces_Fails()
    {
        // Arrange — one character past the 43 of a base64url-encoded 32-byte token.
        ResetPasswordCommand command = new(
            new string('t', ResetPasswordCommandValidator.TokenLength + 1),
            NewPassword);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A password below the policy minimum is refused, and the failure names the password.</summary>
    [Fact]
    public void Validate_APasswordBelowTheMinimum_FailsOnThePassword()
    {
        // Arrange
        ResetPasswordCommand command = new(Token(), "short");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert — the endpoint answers 422 for this and 400 for a token failure, so the property name
        // is what separates them.
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(ResetPasswordCommand.NewPassword));
    }

    /// <summary>
    /// A password inside the character limit but past BCrypt's 72-byte input window is refused, because
    /// the library would truncate it silently.
    /// </summary>
    [Fact]
    public void Validate_APasswordPastTheBCryptByteWindow_Fails()
    {
        // Arrange — 30 three-byte characters is 90 bytes in 30 characters.
        string multiByte = new('€', 30);
        Assert.True(Encoding.UTF8.GetByteCount(multiByte) > PasswordRules.PasswordMaximumUtf8Bytes);

        ResetPasswordCommand command = new(Token(), multiByte);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    private static string Token() => new('t', ResetPasswordCommandValidator.TokenLength);

    /// <summary>The breached screen reaches this path too, keyed on its own property — SPEC-035.</summary>
    [Fact]
    public void Validate_APasswordInTheBreachedSet_Fails()
    {
        // Arrange
        ResetPasswordCommand command = new(Token(), RegisterCommandValidatorTests.BreachedPassword);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(ResetPasswordCommand.NewPassword)
                && failure.ErrorMessage == PasswordRules.BreachedMessage);
    }
}
