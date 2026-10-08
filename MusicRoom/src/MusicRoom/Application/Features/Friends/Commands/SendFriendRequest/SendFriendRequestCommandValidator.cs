using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Friends.Commands.SendFriendRequest;

/// <summary>
/// Input rules for <see cref="SendFriendRequestCommand"/>.
/// </summary>
/// <remarks>
/// Only the shape is checked here. "Is this me", "are we already friends" and "is one pending" are facts about
/// the graph, and the graph is where they are decided — in one statement, not in a validator that would have to
/// read first and hope nothing changed.
/// </remarks>
public sealed class SendFriendRequestCommandValidator : AbstractValidator<SendFriendRequestCommand>
{
    /// <summary>Declares the rules.</summary>
    public SendFriendRequestCommandValidator() =>
        RuleFor(command => command.TargetUserId).NotEmpty().WithErrorCode(ValidationCodes.TargetUserRequired);
}
