using FluentValidation.Results;
using MusicRoom.Application.Features.Profiles.Commands.UpdateProfile;
using MusicRoom.Domain.Common;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Profiles;

/// <summary>
/// Validation layer — testing standard. Every bound is an upper bound: a profile field has no minimum, because
/// clearing a field is a legitimate thing to do.
/// </summary>
public sealed class UpdateProfileCommandValidatorTests
{
    private readonly UpdateProfileCommandValidator _validator = new();

    /// <summary>An empty command is valid: a PATCH that changes nothing changes nothing.</summary>
    [Fact]
    public void Validate_AnEmptyCommand_Passes()
    {
        // Arrange
        UpdateProfileCommand command = new(null, null, null, null);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A field with a value and a tier passes.</summary>
    [Fact]
    public void Validate_AFieldWithAValueAndATier_Passes()
    {
        // Arrange
        UpdateProfileCommand command = new(
            new ProfileField<string>("Ada Lovelace", ProfileFieldVisibility.Friends),
            null,
            null,
            null);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>Clearing a field is sending a null value with a tier, which is valid.</summary>
    [Fact]
    public void Validate_ANullValueWithATier_Passes()
    {
        // Arrange
        UpdateProfileCommand command = new(
            new ProfileField<string>(null, ProfileFieldVisibility.Private),
            null,
            null,
            null);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A real name past the column is refused rather than truncated.</summary>
    [Fact]
    public void Validate_ARealNameLongerThanTheColumn_Fails()
    {
        // Arrange
        UpdateProfileCommand command = new(
            new ProfileField<string>(
                new string('n', UpdateProfileCommandValidator.RealNameMaximumLength + 1),
                ProfileFieldVisibility.Friends),
            null,
            null,
            null);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A bio past the column is refused.</summary>
    [Fact]
    public void Validate_ABioLongerThanTheColumn_Fails()
    {
        // Arrange
        UpdateProfileCommand command = new(
            null,
            new ProfileField<string>(
                new string('b', UpdateProfileCommandValidator.BioMaximumLength + 1),
                ProfileFieldVisibility.Public),
            null,
            null);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A birth date in the future is not a fact about a person.</summary>
    [Fact]
    public void Validate_ABirthDateInTheFuture_Fails()
    {
        // Arrange
        UpdateProfileCommand command = new(
            null,
            null,
            null,
            new ProfileDateField(
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                ProfileFieldVisibility.Private));

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A date before 1900 is a bad parse rather than a very old user.</summary>
    [Fact]
    public void Validate_ABirthDateBefore1900_Fails()
    {
        // Arrange
        UpdateProfileCommand command = new(
            null,
            null,
            null,
            new ProfileDateField(new DateOnly(1899, 12, 31), ProfileFieldVisibility.Private));

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
