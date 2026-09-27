using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One row of the mandatory audit log — subject V.6, SDD §13.4.
/// </summary>
/// <remarks>
/// Distinct from the application log: this is evidence, it is queryable, and it is kept rather than
/// rotated. Nothing here may carry a credential — §13.5 applies to this table as much as to a log sink,
/// which is why <see cref="TargetId"/> is an identifier and never an address or a token.
/// </remarks>
public sealed class AuditRecord : Entity
{
    /// <summary>
    /// The account that acted, or null when the action was anonymous. Every mutating endpoint that exists
    /// today is anonymous, so this is null on every row until the authenticated slices land — SPEC-008
    /// escalation 3, option A.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>The action, as a stable string such as <c>auth.login</c>. Never a type name: a class
    /// rename must not rewrite history.</summary>
    public required string Action { get; set; }

    /// <summary>What kind of thing the action was about, such as <c>account</c> or <c>session</c>.</summary>
    public required string TargetType { get; set; }

    /// <summary>The identifier of the thing acted on, or null when the command cannot name it.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>The client platform, from <c>X-Client-Platform</c>. Subject V.6 names this field.</summary>
    public required string Platform { get; set; }

    /// <summary>The client device, from <c>X-Client-Device</c>. Subject V.6 names this field.</summary>
    public required string Device { get; set; }

    /// <summary>The client app version, from <c>X-Client-App-Version</c>. Subject V.6 names this field.</summary>
    public required string AppVersion { get; set; }

    /// <summary>When the action happened. Always UTC.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>The trace id of the request, so a record joins the application log — ADR-0022.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>
    /// What became of the action. Constant today: the record is written inside the command's transaction,
    /// so a command that threw rolls its record back and a command refused by validation never reaches
    /// this point. Refusal outcomes are handler-level events and arrive with the T-27 follow-up.
    /// </summary>
    public required string Outcome { get; set; }
}
