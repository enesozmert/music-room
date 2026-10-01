using FluentValidation.Results;
using MusicRoom.Application.Features.Playlists.Commands.CreatePlaylist;
using MusicRoom.Domain.Common;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Playlists;

/// <summary>
/// Validation layer — testing standard. The licence rule is the one with teeth: a playlist may not carry the
/// event licence, because nothing checks a geofence on a playlist.
/// </summary>
public sealed class CreatePlaylistCommandValidatorTests
{
    private readonly CreatePlaylistCommandValidator _validator = new();

    /// <summary>An ordinary playlist passes.</summary>
    [Fact]
    public void Validate_ANamedPrivatePlaylist_Passes()
    {
        // Arrange
        CreatePlaylistCommand command = new("Road trip", Visibility.Private, LicenceMode.InvitedOnly);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A playlist with no name is not a playlist.</summary>
    [Fact]
    public void Validate_WithNoName_Fails()
    {
        // Arrange
        CreatePlaylistCommand command = new("  ", Visibility.Private, LicenceMode.Everyone);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A name past the column is refused rather than truncated.</summary>
    [Fact]
    public void Validate_WithAnOverlongName_Fails()
    {
        // Arrange
        CreatePlaylistCommand command = new(
            new string('n', CreatePlaylistCommandValidator.NameMaximumLength + 1),
            Visibility.Private,
            LicenceMode.Everyone);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// The event licence is refused on a playlist, and the message says where it belongs — SPEC-014 decision 4.
    /// </summary>
    [Fact]
    public void Validate_WithTheLocationAndTimeLicence_Fails()
    {
        // Arrange
        CreatePlaylistCommand command = new(
            "Geofenced",
            Visibility.Private,
            LicenceMode.LocationAndTimeRestricted);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.ErrorMessage.Contains("event", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A visibility outside the enum is refused, not coerced.</summary>
    [Fact]
    public void Validate_WithAnUnknownVisibility_Fails()
    {
        // Arrange
        CreatePlaylistCommand command = new("Odd", (Visibility)99, LicenceMode.Everyone);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
