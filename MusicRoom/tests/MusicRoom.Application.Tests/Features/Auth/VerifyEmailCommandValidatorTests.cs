using FluentValidation.Results;
using MusicRoom.Application.Features.Auth.Commands.VerifyEmail;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. Input is rejected here, before anything is hashed or
/// queried, so a caller cannot make the service work for a token it never issued.
/// </summary>
public sealed class VerifyEmailCommandValidatorTests
{
    private readonly VerifyEmailCommandValidator _validator = new();

    /// <summary>An empty token never reaches the handler.</summary>
    [Fact]
    public void Validate_AnEmptyToken_Fails()
    {
        // Arrange
        VerifyEmailCommand command = new(string.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(VerifyEmailCommand.Token));
    }

    /// <summary>
    /// A token longer than the generator can produce is refused rather than hashed. The bound is
    /// what stops an anonymous caller forcing a large allocation per request.
    /// </summary>
    [Fact]
    public void Validate_ATokenLongerThanTheGeneratorProduces_Fails()
    {
        // Arrange — one character past the 43 of a base64url-encoded 32-byte token.
        VerifyEmailCommand command = new(new string('a', VerifyEmailCommandValidator.TokenLength + 1));

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(VerifyEmailCommand.Token));
    }

    /// <summary>
    /// A token of exactly the issued shape passes, so the tests above fail for their own reason.
    /// </summary>
    [Fact]
    public void Validate_ATokenOfTheIssuedLength_Passes()
    {
        // Arrange
        VerifyEmailCommand command = new(new string('a', VerifyEmailCommandValidator.TokenLength));

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }
}
