using FluentValidation;

namespace MusicRoom.Application.Features.Playlists.Commands.MoveTrack;

/// <summary>
/// Input rules for <see cref="MoveTrackCommand"/>.
/// </summary>
/// <remarks>
/// One destination, exactly. Both neighbours is the contract's own `422`; neither is refused here for the same
/// reason — "move this track" with nowhere to put it is a request the server would have to guess at, and
/// guessing at a reorder is how lists silently diverge.
/// </remarks>
public sealed class MoveTrackCommandValidator : AbstractValidator<MoveTrackCommand>
{
    /// <summary>Declares the rules.</summary>
    public MoveTrackCommandValidator()
    {
        RuleFor(command => command.PlaylistId).NotEmpty();
        RuleFor(command => command.PlaylistTrackId).NotEmpty();

        RuleFor(command => command)
            .Must(command => command.AfterTrackId is null ^ command.BeforeTrackId is null)
            .WithMessage("Send exactly one of afterTrackId or beforeTrackId.");

        RuleFor(command => command.AfterTrackId)
            .NotEqual(command => command.PlaylistTrackId)
            .WithMessage("A track cannot be moved after itself.");

        RuleFor(command => command.BeforeTrackId)
            .NotEqual(command => command.PlaylistTrackId)
            .WithMessage("A track cannot be moved before itself.");
    }
}
