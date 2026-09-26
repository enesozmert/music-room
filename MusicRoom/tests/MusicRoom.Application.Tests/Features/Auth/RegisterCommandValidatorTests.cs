using System.Text;
using FluentValidation.Results;
using MusicRoom.Application.Features.Auth.Commands.Register;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. Proves that the ADR-0007 password bounds are enforced
/// before a handler ever sees the command.
/// </summary>
public sealed class RegisterCommandValidatorTests
{
    internal const string ValidEmail = "player@example.com";
    internal const string ValidDisplayName = "Player One";

    private readonly RegisterCommandValidator _validator = new();

    /// <summary>A password under the ADR-0007 lower bound is refused.</summary>
    [Fact]
    public void Validate_PasswordShorterThan12Characters_Fails()
    {
        // Arrange — 11 characters, one below the bound.
        RegisterCommand command = new(ValidEmail, "shortpass11", ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Password));
    }

    /// <summary>A password over the ADR-0007 upper bound is refused.</summary>
    [Fact]
    public void Validate_PasswordLongerThan64Characters_Fails()
    {
        // Arrange — 65 characters.
        RegisterCommand command = new(ValidEmail, new string('a', 65), ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Password));
    }

    /// <summary>
    /// The BCrypt truncation footgun: a password that passes the character bounds and still exceeds
    /// the 72-byte input window is refused rather than silently cut.
    /// </summary>
    [Fact]
    public void Validate_PasswordOver72BytesOfUtf8_Fails()
    {
        // Arrange — 20 four-byte characters: 40 UTF-16 chars, under the 64-character limit, and
        // 80 UTF-8 bytes, over BCrypt's 72.
        string multiBytePassword = string.Concat(Enumerable.Repeat("\U0001F3B5", 20));
        Assert.InRange(multiBytePassword.Length, 12, 64);
        Assert.True(Encoding.UTF8.GetByteCount(multiBytePassword) > 72);

        RegisterCommand command = new(ValidEmail, multiBytePassword, ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Password));
    }

    /// <summary>A malformed address is refused — subject V.1 input validation.</summary>
    [Fact]
    public void Validate_MalformedEmail_Fails()
    {
        // Arrange
        RegisterCommand command = new("not-an-address", "correct horse battery", ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Email));
    }

    /// <summary>A payload inside every bound passes, so the tests above fail for their own reason.</summary>
    [Fact]
    public void Validate_WellFormedCommand_Passes()
    {
        // Arrange
        RegisterCommand command = new(ValidEmail, "correct horse battery", ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }
}
