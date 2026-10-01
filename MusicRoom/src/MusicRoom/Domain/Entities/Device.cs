using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One device registered to an account, and who may command playback on it — subject V.2.2.
/// </summary>
/// <remarks>
/// The control grant **is** this row: <see cref="ControllerUserId"/> plus PostgreSQL's <c>xmin</c> is the
/// single-row state machine with a concurrency token that `docs/api/ENDPOINTS.md` §6 calls for. A separate
/// grants table would be two rows that can disagree about who controls one device.
/// <para>
/// Null <see cref="ControllerUserId"/> means **the owner controls it**. A revoke writes null rather than the
/// owner's id, because two spellings of one state is how a query ends up checking only one of them.
/// </para>
/// <para>
/// A device is unique per owner, platform and model. <see cref="AppVersion"/> is deliberately not part of that
/// key: a phone that updates its app is the same phone — SPEC-020 decision 3.
/// </para>
/// </remarks>
public sealed class Device : Entity
{
    /// <summary>The account that registered it, and the only one that may delegate control of it.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The platform, from <c>X-Client-Platform</c>.</summary>
    public required string Platform { get; set; }

    /// <summary>The model, from <c>X-Client-Device</c>.</summary>
    public required string Model { get; set; }

    /// <summary>The application version, from <c>X-Client-App-Version</c>, as last registered.</summary>
    public required string AppVersion { get; set; }

    /// <summary>
    /// Who holds delegated control, or null while the owner holds it. This is what a playback session reads to
    /// decide who may command it — ADR-0017.
    /// </summary>
    public Guid? ControllerUserId { get; set; }

    /// <summary>PostgreSQL's <c>xmin</c>, the concurrency token. Never set by this application.</summary>
    public uint Version { get; set; }
}
