using FluentValidation.Results;
using MusicRoom.Application.Features.Auth.Commands.RotateSession;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. The bound is what stops an anonymous caller making the
/// service hash and query a megabyte per request.
/// </summary>
public sealed class RotateSessionCommandValidatorTests
{
    private readonly RotateSessionCommandValidator _validator = new();

    /// <summary>A token of the shape the generator produces passes.</summary>
    [Fact]
    public void Validate_ATokenOfTheIssuedLength_Passes()
    {
        // Arrange
        RotateSessionCommand command = new(
            new string('t', RotateSessionCommandValidator.RefreshTokenLength));

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
        RotateSessionCommand command = new(string.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "refreshToken");
    }

    /// <summary>A token longer than the generator can produce is refused rather than hashed.</summary>
    [Fact]
    public void Validate_ATokenLongerThanTheGeneratorProduces_Fails()
    {
        // Arrange — one character past the 43 of a base64url-encoded 32-byte token.
        RotateSessionCommand command = new(
            new string('t', RotateSessionCommandValidator.RefreshTokenLength + 1));

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
