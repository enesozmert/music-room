using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Pipelines.Auditing;

/// <summary>
/// Runs every <see cref="IAuditableCommand"/> inside one transaction and writes its audit record there —
/// subject V.6, SDD §10.7 and §13.4.
/// </summary>
/// <remarks>
/// Fail closed, and atomic: the record is written before the commit, so an effect without a record cannot
/// exist and a record without an effect cannot either. If the write throws, the transaction is rolled back
/// and the exception reaches the caller as a `500` — the audit log is evidence, and an unrecorded action is
/// the gap subject V.6 asks about (SPEC-008 escalations 1 and 2, owner decision of 2026-09-27).
/// <para>
/// Registered after <c>ValidationBehaviour</c>, so a command refused by validation writes nothing: there
/// was no action to record.
/// </para>
/// <para>
/// A command that is not <see cref="IAuditableCommand"/> passes straight through, with no transaction of
/// this behaviour's making. That is how a read, or a command whose own repository method already arbitrates
/// its writes, stays untouched.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The command type.</typeparam>
/// <typeparam name="TResponse">What the command answers.</typeparam>
public sealed class AuditBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>What the outcome column says while refusals are still handler-level events.</summary>
    public const string CompletedOutcome = "completed";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditSink _audit;
    private readonly IClientContextAccessor _clientContext;

    /// <summary>Takes the transaction boundary, the sink and the client context.</summary>
    /// <param name="unitOfWork">The transaction boundary — SPEC-008 escalation 2.</param>
    /// <param name="audit">Where the record is written.</param>
    /// <param name="clientContext">The three client values subject V.6 names.</param>
    public AuditBehaviour(
        IUnitOfWork unitOfWork,
        IAuditSink audit,
        IClientContextAccessor clientContext)
    {
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clientContext = clientContext;
    }

    /// <summary>Wraps the handler, then records what it did.</summary>
    /// <param name="request">The command.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Whatever the handler answered.</returns>
    /// <exception cref="InvalidOperationException">The command is auditable and no client context exists.
    /// The middleware refuses a mutating request without the headers, so reaching here without them means
    /// the command was sent from somewhere that bypassed it — which must fail loudly rather than write a
    /// record with invented values.</exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is not IAuditableCommand auditable)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        ClientContext context = _clientContext.Current
            ?? throw new InvalidOperationException(
                "An auditable command reached the pipeline with no client context. The mandatory client "
                + "headers are validated by middleware — SDD §10.7 — and no audit record may be written "
                + "with substituted values.");

        bool owned = await _unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            TResponse response = await next(cancellationToken).ConfigureAwait(false);

            await _audit
                .WriteAsync(Record(auditable, context), cancellationToken)
                .ConfigureAwait(false);

            if (owned)
            {
                await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
        catch
        {
            if (owned)
            {
                await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    private static AuditRecord Record(IAuditableCommand command, ClientContext context)
    {
        DateTime nowUtc = DateTime.UtcNow;

        return new AuditRecord
        {
            Id = Guid.NewGuid(),

            // Null until an endpoint authenticates its caller — SPEC-008 escalation 3, option A. The
            // record still names the action, and the client context identifies the caller's device.
            UserId = null,
            Action = command.AuditAction,
            TargetType = command.AuditTargetType,

            // Null while no command can name its target: for these seven, the account id is resolved
            // inside the handler and never reaches the command.
            TargetId = null,
            Platform = context.Platform,
            Device = context.Device,
            AppVersion = context.AppVersion,
            OccurredAtUtc = nowUtc,
            CorrelationId = context.CorrelationId,
            Outcome = CompletedOutcome,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }
}
