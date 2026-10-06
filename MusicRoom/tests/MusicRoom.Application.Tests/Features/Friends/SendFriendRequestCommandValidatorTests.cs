using FluentValidation.Results;
using MusicRoom.Application.Features.Friends.Commands.SendFriendRequest;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Friends;

/// <summary>
/// Validation layer — testing standard. Only the shape is checked here; whether the pair may become friends is
/// a fact about the graph, decided in one statement by the database.
/// </summary>
public sealed class SendFriendRequestCommandValidatorTests
{
    private readonly SendFriendRequestCommandValidator _validator = new();

    /// <summary>A real id passes.</summary>
    [Fact]
    public void Validate_AnAccountId_Passes()
    {
        // Arrange
        SendFriendRequestCommand command = new(Guid.NewGuid());

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>An empty id is not an account, and never reaches the graph.</summary>
    [Fact]
    public void Validate_AnEmptyId_Fails()
    {
        // Arrange
        SendFriendRequestCommand command = new(Guid.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "targetUserId");
    }
}
