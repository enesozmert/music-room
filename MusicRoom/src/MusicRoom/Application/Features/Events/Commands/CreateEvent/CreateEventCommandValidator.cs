using FluentValidation;

namespace MusicRoom.Application.Features.Events.Commands.CreateEvent;

/// <summary>
/// Input rules for <see cref="CreateEventCommand"/> — application-security, ADR-0014.
/// </summary>
/// <remarks>
/// Both rule sets come from <see cref="EventLicenceRules"/>, so creating an event and changing its licence
/// refuse exactly the same things.
/// </remarks>
public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    /// <summary>Declares the rules.</summary>
    public CreateEventCommandValidator()
    {
        EventLicenceRules.AddWindowRules(this, command => command.Metadata);
        EventLicenceRules.AddLicenceRules(this, command => command.Licence);
    }
}
