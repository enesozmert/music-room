using FluentValidation;

namespace MusicRoom.Application.Features.Devices.Commands.GrantControl;

/// <summary>
/// Input rules for <see cref="GrantControlCommand"/>.
/// </summary>
/// <remarks>
/// Shape only. Whether the grantee is a **friend** is a read of the graph and therefore the handler's job: a
/// validator cannot reach the database, and faking the check here would make the `422` a guess.
/// </remarks>
public sealed class GrantControlCommandValidator : AbstractValidator<GrantControlCommand>
{
    /// <summary>Declares the rules.</summary>
    public GrantControlCommandValidator()
    {
        RuleFor(command => command.DeviceId).NotEmpty();
        RuleFor(command => command.GranteeUserId).NotEmpty().WithName("granteeUserId");
    }
}
