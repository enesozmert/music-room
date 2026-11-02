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
        ResetPasswordCommand command = new(NewPassword, Token: Token());

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A request with neither credential never reaches the database — SPEC-055.</summary>
    [Fact]
    public void Validate_AnEmptyToken_Fails()
    {
        // Arrange
        ResetPasswordCommand command = new(NewPassword, Token: string.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == "token");
    }

    /// <summary>A token longer than the generator produces is refused rather than hashed.</summary>
    [Fact]
    public void Validate_ATokenLongerThanTheGeneratorProduces_Fails()
    {
        // Arrange — one character past the 43 of a base64url-encoded 32-byte token.
        ResetPasswordCommand command = new(
            NewPassword,
            Token: new string('t', ResetPasswordCommandValidator.TokenLength + 1));

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
        ResetPasswordCommand command = new("short", Token: Token());

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert — the endpoint answers 422 for this and 400 for a token failure, so the property name
        // is what separates them.
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "newPassword");
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

        ResetPasswordCommand command = new(multiByte, Token: Token());

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>The address and a code are a complete request, with no token — SPEC-055.</summary>
    [Fact]
    public void Validate_AnAddressAndACode_Passes()
    {
        // Arrange
        ResetPasswordCommand command = new(NewPassword, Email: "reader@example.com", Code: "RST234");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>
    /// A code without an address is refused: six characters are not unique across accounts, so there
    /// would be no row to look at — SPEC-055 decision 4.
    /// </summary>
    [Fact]
    public void Validate_ACodeWithNoAddress_Fails()
    {
        // Arrange
        ResetPasswordCommand command = new(NewPassword, Code: "RST234");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == "email");
    }

    /// <summary>
    /// A code far longer than six characters is refused here rather than hashed and queried.
    /// </summary>
    /// <remarks>
    /// The bound is 32 rather than 6: a person copying six characters out of a mail client brings
    /// spaces and dashes with them, and the handler normalises those away. What this refuses is a
    /// megabyte of "code".
    /// </remarks>
    [Fact]
    public void Validate_ACodeLongerThanTheEndpointReads_Fails()
    {
        // Arrange
        ResetPasswordCommand command = new(
            NewPassword,
            Email: "reader@example.com",
            Code: new string('C', ResetPasswordCommandValidator.CodeLength + 1));

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
        ResetPasswordCommand command = new(
            RegisterCommandValidatorTests.BreachedPassword,
            Token: Token());

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "newPassword"
                && failure.ErrorMessage == PasswordRules.BreachedMessage);
    }
}
