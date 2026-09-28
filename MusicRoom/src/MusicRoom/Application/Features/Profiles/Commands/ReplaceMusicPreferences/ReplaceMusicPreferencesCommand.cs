using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Profiles.Commands.ReplaceMusicPreferences;

/// <summary>
/// Replaces the caller's whole preference set — `PUT /api/v1/me/music-preferences`, subject V.1.
/// </summary>
/// <param name="Genres">The genres the user names. An empty list is valid and means "none".</param>
/// <param name="Artists">The artists the user names.</param>
/// <remarks>
/// A <c>PUT</c>, so the set is replaced wholesale rather than merged — a caller who sends one genre has one
/// genre afterwards, whatever they had before.
/// </remarks>
public sealed record ReplaceMusicPreferencesCommand(
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Artists) : IRequest, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "profile.replace-music-preferences";

    /// <inheritdoc />
    public string AuditTargetType => "profile";
}
