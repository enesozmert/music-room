namespace MusicRoom.Application.Abstractions.Auditing;

/// <summary>
/// Implemented by a command that must leave an audit record — subject V.6, SDD §10.7.
/// </summary>
/// <remarks>
/// An opt-in marker rather than reflection over command type names. Two reasons: a renamed class must not
/// silently rewrite history, and a command that should not be audited must be able to say so by omission
/// rather than by an exclusion list nobody maintains.
/// </remarks>
public interface IAuditableCommand
{
    /// <summary>The action, as a stable string such as <c>auth.login</c>.</summary>
    string AuditAction { get; }

    /// <summary>What kind of thing the action is about, such as <c>account</c> or <c>session</c>.</summary>
    string AuditTargetType { get; }
}
