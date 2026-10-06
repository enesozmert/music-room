using FluentValidation.Results;
using MusicRoom.Application.Features.Auth.Commands.Login;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. Input is bounded here, before anything is hashed or queried,
/// so an anonymous caller cannot make the service work for a credential it never issued.
/// </summary>
public sealed class LoginCommandValidatorTests
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string Password = "correct horse battery";

    private readonly LoginCommandValidator _validator = new();

    /// <summary>The ordinary case passes; nothing about the stored account is consulted here.</summary>
    [Fact]
    public void Validate_AnAddressAndAPassword_Passes()
    {
        // Arrange
        LoginCommand command = new("player@example.com", Password);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>An empty address never reaches the handler.</summary>
    [Fact]
    public void Validate_AnEmptyEmail_Fails()
    {
        // Arrange
        LoginCommand command = new(string.Empty, Password);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == "email");
    }

    /// <summary>An empty password never reaches the hasher.</summary>
    [Fact]
    public void Validate_AnEmptyPassword_Fails()
    {
        // Arrange
        LoginCommand command = new("player@example.com", string.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == "password");
    }

    /// <summary>
    /// A password longer than registration accepts cannot be anybody's stored password, so it is
    /// refused rather than hashed. The bound is what stops a caller forcing a large allocation per
    /// request.
    /// </summary>
    [Fact]
    public void Validate_APasswordLongerThanRegistrationAccepts_Fails()
    {
        // Arrange
        LoginCommand command = new(
            "player@example.com",
            new string('p', LoginCommandValidator.PasswordMaximumLength + 1));

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>An address past the column's length is refused before it is queried.</summary>
    [Fact]
    public void Validate_AnEmailLongerThanTheColumn_Fails()
    {
        // Arrange
        LoginCommand command = new(
            new string('e', LoginCommandValidator.EmailMaximumLength + 1),
            Password);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// A short password is accepted by this validator on purpose: rejecting it would tell a caller
    /// which candidates are too short to be anybody's password, and every refusal on this endpoint
    /// leaves as the same 401 anyway.
    /// </summary>
    [Fact]
    public void Validate_APasswordShorterThanThePolicyMinimum_Passes()
    {
        // Arrange
        LoginCommand command = new("player@example.com", "short");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }
}
