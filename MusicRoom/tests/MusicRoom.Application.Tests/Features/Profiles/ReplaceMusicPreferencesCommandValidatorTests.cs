using FluentValidation.Results;
using MusicRoom.Application.Features.Profiles.Commands.ReplaceMusicPreferences;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Profiles;

/// <summary>
/// Validation layer — testing standard, OWASP API4. The bounds are the only thing limiting a write behind an
/// authenticated endpoint with no rate limit of its own.
/// </summary>
public sealed class ReplaceMusicPreferencesCommandValidatorTests
{
    private readonly ReplaceMusicPreferencesCommandValidator _validator = new();

    /// <summary>Two short lists pass.</summary>
    [Fact]
    public void Validate_AShortSet_Passes()
    {
        // Arrange
        ReplaceMusicPreferencesCommand command = new(["drum and bass"], ["Goldie"]);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>An empty set is valid: naming nothing is an answer.</summary>
    [Fact]
    public void Validate_AnEmptySet_Passes()
    {
        // Arrange
        ReplaceMusicPreferencesCommand command = new([], []);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>More entries than the bound is refused — unrestricted resource consumption.</summary>
    [Fact]
    public void Validate_MoreGenresThanTheBound_Fails()
    {
        // Arrange
        string[] genres = Enumerable
            .Range(0, ReplaceMusicPreferencesCommandValidator.MaximumEntries + 1)
            .Select(index => $"genre-{index}")
            .ToArray();

        ReplaceMusicPreferencesCommand command = new(genres, []);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>An entry past the column is refused rather than truncated.</summary>
    [Fact]
    public void Validate_AnEntryLongerThanTheColumn_Fails()
    {
        // Arrange
        ReplaceMusicPreferencesCommand command = new(
            [new string('g', ReplaceMusicPreferencesCommandValidator.EntryMaximumLength + 1)],
            []);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A blank entry is not a preference.</summary>
    [Fact]
    public void Validate_ABlankEntry_Fails()
    {
        // Arrange
        ReplaceMusicPreferencesCommand command = new(["  "], []);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
