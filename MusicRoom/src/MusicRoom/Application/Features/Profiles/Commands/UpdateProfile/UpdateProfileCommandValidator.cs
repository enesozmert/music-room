using FluentValidation;

namespace MusicRoom.Application.Features.Profiles.Commands.UpdateProfile;

/// <summary>
/// Input rules for <see cref="UpdateProfileCommand"/> — application-security.
/// </summary>
/// <remarks>
/// The tier is not validated here and cannot be missing: <see cref="ProfileField{T}"/> makes it a required
/// positional value, so a body that omits it fails to bind and answers `422` before a validator runs. That is
/// the contract's "a missing tier is `422`, never a silent default to public", enforced by the type.
/// <para>
/// Every bound is an upper bound. A profile field has no minimum: a user who clears their bio is a user with no
/// bio, not an invalid request.
/// </para>
/// </remarks>
public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    /// <summary>The column width for a real name.</summary>
    public const int RealNameMaximumLength = 128;

    /// <summary>The column width for a bio.</summary>
    public const int BioMaximumLength = 512;

    /// <summary>The column width for a location.</summary>
    public const int LocationMaximumLength = 128;

    /// <summary>Declares the rules.</summary>
    public UpdateProfileCommandValidator()
    {
        RuleFor(command => command.RealName!.Value)
            .MaximumLength(RealNameMaximumLength)
            .When(command => command.RealName?.Value is not null);

        RuleFor(command => command.Bio!.Value)
            .MaximumLength(BioMaximumLength)
            .When(command => command.Bio?.Value is not null);

        RuleFor(command => command.Location!.Value)
            .MaximumLength(LocationMaximumLength)
            .When(command => command.Location?.Value is not null);

        // A date of birth in the future is not a typo worth storing, and one before 1900 is a bad parse.
        RuleFor(command => command.BirthDate!.Value)
            .InclusiveBetween(new DateOnly(1900, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow))
            .When(command => command.BirthDate?.Value is not null);
    }
}
