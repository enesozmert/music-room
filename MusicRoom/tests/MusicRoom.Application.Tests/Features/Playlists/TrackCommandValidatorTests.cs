using FluentValidation.Results;
using MusicRoom.Application.Features.Playlists.Commands.AddTrack;
using MusicRoom.Application.Features.Playlists.Commands.MoveTrack;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Playlists;

/// <summary>
/// Validation layer for the track writes — testing standard, SPEC-015.
/// </summary>
/// <remarks>
/// Two rules carry weight. A video id is checked against YouTube's own shape, so arbitrary text never reaches
/// the column; and a move names **exactly one** neighbour, because a reorder the server has to guess at is how
/// two clients end up with different lists.
/// </remarks>
public sealed class TrackCommandValidatorTests
{
    /// <summary>A well-formed YouTube video id.</summary>
    internal const string SomeVideoId = "dQw4w9WgXcQ";

    private readonly AddTrackCommandValidator _add = new();
    private readonly MoveTrackCommandValidator _move = new();

    /// <summary>An ordinary append passes.</summary>
    [Fact]
    public void Validate_AnAppendedTrack_Passes()
    {
        // Arrange
        AddTrackCommand command = new(Guid.NewGuid(), SomeVideoId, null, AtHead: false, ExpectedVersion: 7);

        // Act
        ValidationResult result = _add.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A video id of the wrong length is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("dQw4w9WgXcQQ")]
    public void Validate_AVideoIdOfTheWrongLength_Fails(string youtubeVideoId)
    {
        // Arrange
        AddTrackCommand command = new(
            Guid.NewGuid(), youtubeVideoId, null, AtHead: false, ExpectedVersion: 1);

        // Act
        ValidationResult result = _add.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A video id outside YouTube's alphabet is refused, not stored and hoped for.</summary>
    [Fact]
    public void Validate_AVideoIdOutsideTheAlphabet_Fails()
    {
        // Arrange
        AddTrackCommand command = new(
            Guid.NewGuid(), "dQw4w9WgX!Q", null, AtHead: false, ExpectedVersion: 1);

        // Act
        ValidationResult result = _add.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>Two destinations at once is a client that has not decided.</summary>
    [Fact]
    public void Validate_AnAddBothAtTheHeadAndAfterATrack_Fails()
    {
        // Arrange
        AddTrackCommand command = new(
            Guid.NewGuid(), SomeVideoId, Guid.NewGuid(), AtHead: true, ExpectedVersion: 1);

        // Act
        ValidationResult result = _add.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A move naming one neighbour passes.</summary>
    [Fact]
    public void Validate_AMoveAfterOneTrack_Passes()
    {
        // Arrange
        MoveTrackCommand command = new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExpectedVersion: 3);

        // Act
        ValidationResult result = _move.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>Both neighbours is the contract's own `422`.</summary>
    [Fact]
    public void Validate_AMoveWithBothNeighbours_Fails()
    {
        // Arrange
        MoveTrackCommand command = new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ExpectedVersion: 3);

        // Act
        ValidationResult result = _move.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>Neither neighbour is refused: a move with no destination is not a move.</summary>
    [Fact]
    public void Validate_AMoveWithNoNeighbour_Fails()
    {
        // Arrange
        MoveTrackCommand command = new(Guid.NewGuid(), Guid.NewGuid(), null, null, ExpectedVersion: 3);

        // Act
        ValidationResult result = _move.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A track is not its own neighbour.</summary>
    [Fact]
    public void Validate_AMoveAfterItself_Fails()
    {
        // Arrange
        Guid trackId = Guid.NewGuid();
        MoveTrackCommand command = new(Guid.NewGuid(), trackId, trackId, null, ExpectedVersion: 3);

        // Act
        ValidationResult result = _move.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
