using FluentValidation;
using MusicRoom.Application.Validation;

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
        RuleFor(command => command.DeviceId).NotEmpty().WithErrorCode(ValidationCodes.DeviceRequired);
        // The resolver produces `granteeUserId` from the property, so no override is needed here — the
        // `WithName` that used to sit on this rule never moved the key anyway (ADR-0043 as amended).
        RuleFor(command => command.GranteeUserId).NotEmpty().WithErrorCode(ValidationCodes.GranteeRequired);
    }
}
