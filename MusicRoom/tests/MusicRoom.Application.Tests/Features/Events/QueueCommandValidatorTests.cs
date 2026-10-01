using FluentValidation.Results;
using MusicRoom.Application.Features.Events.Commands.IssueEventCode;
using MusicRoom.Application.Features.Events.Commands.SuggestTrack;
using MusicRoom.Application.Features.Events.Presence;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Events;

/// <summary>
/// Validation layer for the contended path — testing standard, SPEC-018.
/// </summary>
/// <remarks>
/// Two rules with weight: a video id is checked against YouTube's own shape, so arbitrary text never reaches the
/// column; and a code's life is bounded in both directions, because a code that outlives the evening stops being
/// evidence of being there and becomes a password somebody photographed.
/// </remarks>
public sealed class QueueCommandValidatorTests
{
    /// <summary>A well-formed YouTube video id.</summary>
    internal const string SomeVideoId = "dQw4w9WgXcQ";

    private readonly SuggestTrackCommandValidator _suggest = new();
    private readonly IssueEventCodeCommandValidator _code = new();

    /// <summary>An ordinary suggestion passes.</summary>
    [Fact]
    public void Validate_ASuggestion_Passes()
    {
        // Arrange
        SuggestTrackCommand command = new(Guid.NewGuid(), SomeVideoId);

        // Act
        ValidationResult result = _suggest.Validate(command);

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
        SuggestTrackCommand command = new(Guid.NewGuid(), youtubeVideoId);

        // Act
        ValidationResult result = _suggest.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A video id outside YouTube's alphabet is refused, not stored and hoped for.</summary>
    [Fact]
    public void Validate_AVideoIdOutsideTheAlphabet_Fails()
    {
        // Arrange
        SuggestTrackCommand command = new(Guid.NewGuid(), "dQw4w9WgX!Q");

        // Act
        ValidationResult result = _suggest.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A code with a sensible life passes.</summary>
    [Fact]
    public void Validate_ACodeWithAFifteenMinuteLife_Passes()
    {
        // Arrange
        IssueEventCodeCommand command = new(Guid.NewGuid(), 900);

        // Act
        ValidationResult result = _code.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A code that expires before it can be read out is refused.</summary>
    [Fact]
    public void Validate_ACodeShorterThanTheMinimum_Fails()
    {
        // Arrange
        IssueEventCodeCommand command = new(
            Guid.NewGuid(),
            IssueEventCodeCommandValidator.MinimumTimeToLiveSeconds - 1);

        // Act
        ValidationResult result = _code.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A code that outlives the evening is refused: that is a password, not a presence proof.</summary>
    [Fact]
    public void Validate_ACodeLongerThanTheMaximum_Fails()
    {
        // Arrange
        IssueEventCodeCommand command = new(
            Guid.NewGuid(),
            IssueEventCodeCommandValidator.MaximumTimeToLiveSeconds + 1);

        // Act
        ValidationResult result = _code.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A generated code is the declared length and uses only the unambiguous alphabet.</summary>
    [Fact]
    public void Create_AGeneratedCode_UsesTheUnambiguousAlphabet()
    {
        // Act
        string code = EventCodeGenerator.Create();

        // Assert
        Assert.Equal(EventCodeGenerator.Length, code.Length);
        Assert.All(code, character => Assert.Contains(character, EventCodeGenerator.Alphabet));
    }

    /// <summary>Two generated codes differ: the generator is cryptographic, not a counter.</summary>
    [Fact]
    public void Create_TwoCodes_Differ()
    {
        // Act
        string first = EventCodeGenerator.Create();
        string second = EventCodeGenerator.Create();

        // Assert
        Assert.NotEqual(first, second);
    }
}
