using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Domain.Entities;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// Writes the audit record for a hub action, and puts the caller where the pipeline can read it — ADR-0019.
/// </summary>
/// <remarks>
/// The REST path gets its record from a MediatR behaviour, which hub invocations do not pass through. This
/// filter writes the **same** record for `JoinPlaylist` and `LeavePlaylist`, so the audit table has one
/// schema and one query surface, and subject V.6's "every action" is true on both transports.
/// <para>
/// Connect and disconnect are deliberately not audited: mobile networks reconnect constantly, and the noise
/// would bury the deliberate actions. They stay in the application log, where operational events belong.
/// </para>
/// <para>
/// A singleton, resolving per-invocation services from <see cref="HubInvocationContext.ServiceProvider"/>:
/// that is the invocation's own scope, which is where the scoped caller holder and the audit sink live.
/// </para>
/// </remarks>
internal sealed class PlaylistHubFilter : IHubFilter
{
    /// <summary>What the audit record calls the thing acted on.</summary>
    internal const string TargetType = "playlist";

    /// <summary>Dispatches the method, having set the caller, and records what became of it.</summary>
    /// <param name="invocationContext">The invocation SignalR is about to perform.</param>
    /// <param name="next">The rest of the invocation pipeline.</param>
    /// <returns>Whatever the hub method answered.</returns>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);

        CancellationToken cancellationToken = invocationContext.Context.ConnectionAborted;

        HubCaller caller = Caller(invocationContext);
        string? action = AuditAction(invocationContext.HubMethodName);

        try
        {
            object? result = await next(invocationContext).ConfigureAwait(false);

            await RecordAsync(invocationContext, caller, action, AuditOutcome.Completed, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        catch (HubException)
        {
            // A refused join is an action the user took and the server declined, which is exactly what the
            // REST path records as `refused`. Then it propagates: the client must still be told.
            await RecordAsync(invocationContext, caller, action, AuditOutcome.Refused, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>Fills the per-invocation holder from the connection and the validated principal.</summary>
    private static HubCaller Caller(HubInvocationContext invocationContext)
    {
        HubCaller caller = invocationContext.ServiceProvider.GetRequiredService<HubCaller>();

        caller.UserId = Claim(invocationContext.Context.User, ClaimTypes.NameIdentifier, "sub");
        caller.SessionId = Claim(invocationContext.Context.User, ClaimTypes.Sid, "sid");

        if (invocationContext.Context.Items.TryGetValue(HubClientContext.ItemKey, out object? stored)
            && stored is HubClientContext context)
        {
            caller.Context = new ClientContext(
                context.Platform,
                context.Device,
                context.AppVersion,
                context.CorrelationId,
                caller.UserId);
        }

        return caller;
    }

    /// <summary>The audit action for a hub method, or null for a method that is not audited.</summary>
    /// <remarks>
    /// The suffix is trimmed first, because SignalR reports the C# method name while a client calls the wire
    /// name the <c>HubMethodName</c> attribute publishes. Matching on one spelling only would silently audit
    /// nothing, which is the failure mode ADR-0019 exists to prevent.
    /// </remarks>
    private static string? AuditAction(string hubMethodName)
    {
        const string asyncSuffix = "Async";

        string name = hubMethodName.EndsWith(asyncSuffix, StringComparison.Ordinal)
            ? hubMethodName[..^asyncSuffix.Length]
            : hubMethodName;

        return name switch
        {
            PlaylistHub.JoinMethod => "playlist.join",
            PlaylistHub.LeaveMethod => "playlist.leave",
            _ => null,
        };
    }

    private static Guid? Claim(ClaimsPrincipal? principal, string mappedType, string rawType)
    {
        string? value = principal?.FindFirst(mappedType)?.Value ?? principal?.FindFirst(rawType)?.Value;

        return Guid.TryParse(value, out Guid parsed) ? parsed : null;
    }

    /// <summary>Writes the record, when the method is one ADR-0019 names.</summary>
    /// <remarks>
    /// The sink opens no transaction of its own, and this filter opens none either: one insert is atomic by
    /// itself, and there is no second write for it to be atomic *with* — a join changes no other row.
    /// </remarks>
    private static async Task RecordAsync(
        HubInvocationContext invocationContext,
        HubCaller caller,
        string? action,
        AuditOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (action is null || caller.Context is not ClientContext context)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;

        IAuditSink sink = invocationContext.ServiceProvider.GetRequiredService<IAuditSink>();

        await sink.WriteAsync(
            new AuditRecord
            {
                Id = Guid.NewGuid(),
                UserId = caller.UserId,
                Action = action,
                TargetType = TargetType,
                TargetId = invocationContext.HubMethodArguments is [Guid playlistId, ..] ? playlistId : null,
                Platform = context.Platform,
                Device = context.Device,
                AppVersion = context.AppVersion,
                OccurredAtUtc = nowUtc,
                CorrelationId = context.CorrelationId,
                Outcome = outcome.ToString().ToLowerInvariant(),
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            },
            cancellationToken)
            .ConfigureAwait(false);
    }
}
