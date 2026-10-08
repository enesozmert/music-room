using System.Text.RegularExpressions;
using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Playlists.Commands.AddTrack;

/// <summary>
/// Input rules for <see cref="AddTrackCommand"/> — application-security.
/// </summary>
/// <remarks>
/// The video id is checked against YouTube's own shape rather than stored as arbitrary text: an id is 11
/// characters of a known alphabet, and anything else is a client bug or an injection attempt dressed as one.
/// It is **not** checked against YouTube — that needs the key slice S4 is blocked on.
/// </remarks>
public sealed partial class AddTrackCommandValidator : AbstractValidator<AddTrackCommand>
{
    /// <summary>How long a YouTube video id is.</summary>
    public const int VideoIdLength = 11;

    /// <summary>Declares the rules.</summary>
    public AddTrackCommandValidator()
    {
        RuleFor(command => command.PlaylistId).NotEmpty().WithErrorCode(ValidationCodes.PlaylistRequired);

        RuleFor(command => command.YoutubeVideoId)
            .NotEmpty().WithErrorCode(ValidationCodes.VideoRequired)
            .Length(VideoIdLength).WithErrorCode(ValidationCodes.VideoInvalid)
            .Must(id => VideoId().IsMatch(id)).WithErrorCode(ValidationCodes.VideoInvalid)
            .WithMessage("A YouTube video id is 11 characters of A-Z, a-z, 0-9, hyphen and underscore.");

        // "At the head" and "after this track" are different destinations; asking for both is a client that has
        // not decided.
        RuleFor(command => command)
            .Must(command => !(command.AtHead && command.AfterTrackId is not null))
            // No member expression, so without this the key is empty — ADR-0043, and SPEC-037 fixed the
            // same shape in MoveTrackCommandValidator while this one was missed.
            .OverridePropertyName("atHead")
            .WithErrorCode(ValidationCodes.PositionConflicting)
            .WithMessage("Send either atHead or afterTrackId, not both.");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoId();
}
