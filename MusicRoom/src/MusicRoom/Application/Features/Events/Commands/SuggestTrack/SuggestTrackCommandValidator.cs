using System.Text.RegularExpressions;
using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Events.Commands.SuggestTrack;

/// <summary>
/// Input rules for <see cref="SuggestTrackCommand"/> — application-security.
/// </summary>
/// <remarks>
/// The same video-id shape the playlist path enforces: 11 characters of YouTube's own alphabet. It is **not**
/// checked against YouTube — that needs the key slice S4 is blocked on — but arbitrary text never reaches the
/// column.
/// </remarks>
public sealed partial class SuggestTrackCommandValidator : AbstractValidator<SuggestTrackCommand>
{
    /// <summary>How long a YouTube video id is.</summary>
    public const int VideoIdLength = 11;

    /// <summary>Declares the rules.</summary>
    public SuggestTrackCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty().WithErrorCode(ValidationCodes.EventRequired);

        RuleFor(command => command.YoutubeVideoId)
            .NotEmpty().WithErrorCode(ValidationCodes.VideoRequired)
            .Length(VideoIdLength).WithErrorCode(ValidationCodes.VideoInvalid)
            .Must(id => VideoId().IsMatch(id)).WithErrorCode(ValidationCodes.VideoInvalid)
            .WithMessage("A YouTube video id is 11 characters of A-Z, a-z, 0-9, hyphen and underscore.");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoId();
}
