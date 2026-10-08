using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Events.Commands.SetEventLicence;

/// <summary>
/// Input rules for <see cref="SetEventLicenceCommand"/> — ADR-0014, SPEC-017 decision 3.
/// </summary>
/// <remarks>
/// The same licence rules creation uses. An event must not be able to *become* a
/// `LocationAndTimeRestricted` event with nothing configured to check, which is the state this rule exists to
/// make unreachable from either direction.
/// </remarks>
public sealed class SetEventLicenceCommandValidator : AbstractValidator<SetEventLicenceCommand>
{
    /// <summary>Declares the rules.</summary>
    public SetEventLicenceCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty().WithErrorCode(ValidationCodes.EventRequired);

        EventLicenceRules.AddLicenceRules(this, command => command.Licence);
    }
}
