using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Events.Commands.UpdateEvent;

/// <summary>
/// Input rules for <see cref="UpdateEventCommand"/>.
/// </summary>
/// <remarks>
/// The same window rules creation uses: an event cannot be edited into a shape it could not have been created
/// in.
/// </remarks>
public sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    /// <summary>Declares the rules.</summary>
    public UpdateEventCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty().WithErrorCode(ValidationCodes.EventRequired);

        EventLicenceRules.AddWindowRules(this, command => command.Metadata);
    }
}
