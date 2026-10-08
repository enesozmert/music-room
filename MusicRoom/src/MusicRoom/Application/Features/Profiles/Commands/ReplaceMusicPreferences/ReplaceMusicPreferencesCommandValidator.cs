using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Profiles.Commands.ReplaceMusicPreferences;

/// <summary>
/// Input rules for <see cref="ReplaceMusicPreferencesCommand"/> — application-security, OWASP API4.
/// </summary>
/// <remarks>
/// Both bounds exist because an unbounded array is unrestricted resource consumption: this endpoint is behind
/// authentication and no rate limit of its own, so the request body is the only thing bounding the write.
/// </remarks>
public sealed class ReplaceMusicPreferencesCommandValidator
    : AbstractValidator<ReplaceMusicPreferencesCommand>
{
    /// <summary>How many entries of one kind a caller may name.</summary>
    public const int MaximumEntries = 50;

    /// <summary>The column width for one entry.</summary>
    public const int EntryMaximumLength = 64;

    /// <summary>Declares the rules.</summary>
    public ReplaceMusicPreferencesCommandValidator()
    {
        RuleFor(command => command.Genres).NotNull().WithErrorCode(ValidationCodes.GenresRequired);
        RuleFor(command => command.Artists).NotNull().WithErrorCode(ValidationCodes.ArtistsRequired);

        // `Count` is a CLR member and `MusicPreferencesRequest` publishes no such field, so a resolved key would
        // be `genres.count` — a key a client cannot look up in the body it sent. It names the array instead
        // (ADR-0043 as amended, which struck `genres.count` from its own examples).
        RuleFor(command => command.Genres.Count)
            .LessThanOrEqualTo(MaximumEntries).WithErrorCode(ValidationCodes.GenresTooMany)
            .OverridePropertyName("genres")
            .When(command => command.Genres is not null);

        RuleFor(command => command.Artists.Count)
            .LessThanOrEqualTo(MaximumEntries).WithErrorCode(ValidationCodes.ArtistsTooMany)
            .OverridePropertyName("artists")
            .When(command => command.Artists is not null);

        RuleForEach(command => command.Genres)
            .NotEmpty().WithErrorCode(ValidationCodes.GenreRequired)
            .MaximumLength(EntryMaximumLength).WithErrorCode(ValidationCodes.GenreTooLong)
            .When(command => command.Genres is not null);

        RuleForEach(command => command.Artists)
            .NotEmpty().WithErrorCode(ValidationCodes.ArtistRequired)
            .MaximumLength(EntryMaximumLength).WithErrorCode(ValidationCodes.ArtistTooLong)
            .When(command => command.Artists is not null);
    }
}
