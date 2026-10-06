using FluentValidation;

namespace MusicRoom.Application.Features.Events.Commands.IssueEventCode;

/// <summary>
/// Input rules for <see cref="IssueEventCodeCommand"/> — ADR-0024's expiry discipline.
/// </summary>
/// <remarks>
/// A bounded TTL in both directions. Too short and the code expires while it is being read out; too long and it
/// stops being evidence of being *there now* and becomes a password somebody photographed.
/// </remarks>
public sealed class IssueEventCodeCommandValidator : AbstractValidator<IssueEventCodeCommand>
{
    /// <summary>The shortest life a code may be given, in seconds.</summary>
    public const int MinimumTimeToLiveSeconds = 60;

    /// <summary>The longest life a code may be given, in seconds — one hour.</summary>
    public const int MaximumTimeToLiveSeconds = 3_600;

    /// <summary>Declares the rules.</summary>
    public IssueEventCodeCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();

        // `EventCodeRequest` publishes `ttlSeconds` while the command carries `TimeToLiveSeconds`, so the key is
        // stated rather than resolved. `WithName` used to sit here and never moved the key — it names the word a
        // message interpolates, which is the correction ADR-0043's amendment carries.
        RuleFor(command => command.TimeToLiveSeconds)
            .InclusiveBetween(MinimumTimeToLiveSeconds, MaximumTimeToLiveSeconds)
            .OverridePropertyName("ttlSeconds");
    }
}
