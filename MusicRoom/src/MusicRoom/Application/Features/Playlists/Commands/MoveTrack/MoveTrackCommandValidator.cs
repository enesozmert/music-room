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

        // A whole-command rule has no member path, so without this the key would be the empty string and the
        // message would reach a client it cannot route to a field — ADR-0043, the defect O28 named. It keys on
        // the first of the two fields it is about, which is the one a client would highlight.
        RuleFor(command => command)
            .Must(command => command.AfterTrackId is null ^ command.BeforeTrackId is null)
            .OverridePropertyName("afterTrackId")
            .WithMessage("Send exactly one of afterTrackId or beforeTrackId.");

        RuleFor(command => command.AfterTrackId)
            .NotEqual(command => command.PlaylistTrackId)
            .WithMessage("A track cannot be moved after itself.");

        RuleFor(command => command.BeforeTrackId)
            .NotEqual(command => command.PlaylistTrackId)
            .WithMessage("A track cannot be moved before itself.");
    }
}
