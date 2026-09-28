using FluentValidation;

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
        RuleFor(command => command.Genres).NotNull();
        RuleFor(command => command.Artists).NotNull();

        RuleFor(command => command.Genres.Count)
            .LessThanOrEqualTo(MaximumEntries)
            .When(command => command.Genres is not null);

        RuleFor(command => command.Artists.Count)
            .LessThanOrEqualTo(MaximumEntries)
            .When(command => command.Artists is not null);

        RuleForEach(command => command.Genres)
            .NotEmpty()
            .MaximumLength(EntryMaximumLength)
            .When(command => command.Genres is not null);

        RuleForEach(command => command.Artists)
            .NotEmpty()
            .MaximumLength(EntryMaximumLength)
            .When(command => command.Artists is not null);
    }
}
