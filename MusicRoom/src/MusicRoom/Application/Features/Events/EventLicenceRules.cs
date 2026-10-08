using FluentValidation;
using MusicRoom.Application.Validation;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Events;

/// <summary>
/// The rules a licence must satisfy, shared by creation and by `PUT /events/{eventId}/licence`.
/// </summary>
/// <remarks>
/// One place, because the two routes must refuse the same things: a mode that is accepted at creation and
/// rejected on update — or the reverse — is a way to reach a state neither route meant to allow.
/// SPEC-017 decision 3 is the rule with teeth.
/// <para>
/// **Keys, since SPEC-037.** A rule declared over the whole licence or the whole metadata has no member path, so
/// FluentValidation has no name for it and the key is whatever the rule overrides it with — `presenceProof`,
/// `geofence`, `licence`, `endsAtUtc`. `OverridePropertyName` rather than `WithName`, which sets only the word a
/// message interpolates and leaves the key alone (ADR-0043 as amended). The member-access rules beside them need
/// nothing: `ValidationKeyNaming` already resolves `Geofence.Latitude` to `geofence.latitude` and `Name` to
/// `name`, which is why five `WithName` calls left this file without any key moving.
/// </para>
/// </remarks>
public static class EventLicenceRules
{
    /// <summary>The widest radius an event may claim, in metres.</summary>
    /// <remarks>
    /// Fifty kilometres. A "given place" with a radius larger than a city is not a place, and an unbounded
    /// number here would make the geofence check decorative while looking configured.
    /// </remarks>
    public const int MaximumRadiusMetres = 50_000;

    /// <summary>The smallest radius that means anything, in metres.</summary>
    public const int MinimumRadiusMetres = 10;

    /// <summary>Declares the licence rules on a validator for anything carrying one.</summary>
    /// <typeparam name="TCommand">The command being validated.</typeparam>
    /// <param name="validator">The validator to add the rules to.</param>
    /// <param name="licence">How to read the licence off the command.</param>
    public static void AddLicenceRules<TCommand>(
        AbstractValidator<TCommand> validator,
        Func<TCommand, EventLicence> licence)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(licence);

        validator.RuleFor(command => licence(command).Visibility).IsInEnum().WithErrorCode(ValidationCodes.VisibilityUnknown);
        validator.RuleFor(command => licence(command).Licence).IsInEnum().WithErrorCode(ValidationCodes.LicenceUnknown);

        // Decision 3. A licence that says "only people present at a given place during a given time" with no
        // proof configured would check nothing while reading, to an evaluator, as though it checked something.
        validator.RuleFor(command => licence(command))
            .Must(value => value.Licence != LicenceMode.LocationAndTimeRestricted || value.Proof is not null)
            .OverridePropertyName("presenceProof")
            .WithErrorCode(ValidationCodes.PresenceProofRequired)
            .WithMessage(
                "A LocationAndTimeRestricted event must say which presence proof it requires: Geofence, "
                + "EventCode or Both.");

        // A geofence proof with no centre and radius is the same failure one level down.
        validator.RuleFor(command => licence(command))
            .Must(value => value.Proof is not (PresenceProof.Geofence or PresenceProof.Both)
                || value.Geofence is not null)
            .OverridePropertyName("geofence")
            .WithErrorCode(ValidationCodes.GeofenceRequired)
            .WithMessage("A geofence proof requires a centre and a radius.");

        // And a policy on a mode that checks nothing is a policy nobody will ever read.
        validator.RuleFor(command => licence(command))
            .Must(value => value.Licence == LicenceMode.LocationAndTimeRestricted
                || (value.Proof is null && value.Geofence is null))
            .OverridePropertyName("licence")
            .WithErrorCode(ValidationCodes.LicenceNotAllowed)
            .WithMessage(
                "A presence policy belongs to the LocationAndTimeRestricted licence and to no other.");

        validator.When(command => licence(command).Geofence is not null, () =>
        {
            validator.RuleFor(command => licence(command).Geofence!.Latitude)
                .InclusiveBetween(-90m, 90m).WithErrorCode(ValidationCodes.GeofenceLatitudeOutOfRange);

            validator.RuleFor(command => licence(command).Geofence!.Longitude)
                .InclusiveBetween(-180m, 180m).WithErrorCode(ValidationCodes.GeofenceLongitudeOutOfRange);

            validator.RuleFor(command => licence(command).Geofence!.RadiusMetres)
                .InclusiveBetween(MinimumRadiusMetres, MaximumRadiusMetres)
                .WithErrorCode(ValidationCodes.GeofenceRadiusOutOfRange);
        });
    }

    /// <summary>Declares the window rules: a start, an end, and an end that is after it.</summary>
    /// <typeparam name="TCommand">The command being validated.</typeparam>
    /// <param name="validator">The validator to add the rules to.</param>
    /// <param name="metadata">How to read the metadata off the command.</param>
    public static void AddWindowRules<TCommand>(
        AbstractValidator<TCommand> validator,
        Func<TCommand, EventMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(metadata);

        validator.RuleFor(command => metadata(command).Name)
            .NotEmpty().WithErrorCode(ValidationCodes.NameRequired)
            .MaximumLength(MaximumNameLength).WithErrorCode(ValidationCodes.NameTooLong);

        validator.RuleFor(command => metadata(command).StartsAtUtc).NotEmpty().WithErrorCode(ValidationCodes.StartsAtRequired);

        // Decision 4. An event that ends before it starts has no window at all, and the licence mode that
        // depends on one would then be unenforceable rather than merely unconfigured.
        validator.RuleFor(command => metadata(command))
            .Must(value => value.EndsAtUtc > value.StartsAtUtc)
            .OverridePropertyName("endsAtUtc")
            .WithErrorCode(ValidationCodes.EndsAtBeforeStart)
            .WithMessage("endsAtUtc must be after startsAtUtc.");
    }

    /// <summary>How long an event's name may be, matching the column.</summary>
    public const int MaximumNameLength = 120;
}
