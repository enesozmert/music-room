using FluentValidation.Results;
using MusicRoom.Application.Features.Auth.Commands.RequestPasswordReset;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. The rules describe the shape of an address and nothing about
/// which addresses exist.
/// </summary>
public sealed class RequestPasswordResetCommandValidatorTests
{
    private readonly RequestPasswordResetCommandValidator _validator = new();

    /// <summary>A well-formed address passes, whether or not any account holds it.</summary>
    [Fact]
    public void Validate_AWellFormedAddress_Passes()
    {
        // Arrange
        RequestPasswordResetCommand command = new("forgot@example.com");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>An empty address never reaches the handler.</summary>
    [Fact]
    public void Validate_AnEmptyAddress_Fails()
    {
        // Arrange
        RequestPasswordResetCommand command = new(string.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(RequestPasswordResetCommand.Email));
    }

    /// <summary>Something that is not an address is refused before anything is written.</summary>
    [Fact]
    public void Validate_AMalformedAddress_Fails()
    {
        // Arrange
        RequestPasswordResetCommand command = new("not-an-address");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>An address past the column's length is refused before it is queried.</summary>
    [Fact]
    public void Validate_AnAddressLongerThanTheColumn_Fails()
    {
        // Arrange
        string local = new('e', RequestPasswordResetCommandValidator.EmailMaximumLength);
        RequestPasswordResetCommand command = new($"{local}@example.com");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
