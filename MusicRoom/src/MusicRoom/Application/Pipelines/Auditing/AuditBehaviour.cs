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
/// What the record *says* comes from <see cref="IAuditContext"/>, which the handler writes — SPEC-009. A
/// handler that says nothing yields <see cref="AuditOutcome.Completed"/> and no account, which is still
/// true of a command that ran to completion.
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditSink _audit;
    private readonly IClientContextAccessor _clientContext;
    private readonly IAuditContext _auditContext;

    /// <summary>Takes the transaction boundary, the sink, the client context and the audit context.</summary>
    /// <param name="unitOfWork">The transaction boundary — SPEC-008 escalation 2.</param>
    /// <param name="audit">Where the record is written.</param>
    /// <param name="clientContext">The three client values subject V.6 names.</param>
    /// <param name="auditContext">What the handler said became of the action — SPEC-009.</param>
    public AuditBehaviour(
        IUnitOfWork unitOfWork,
        IAuditSink audit,
        IClientContextAccessor clientContext,
        IAuditContext auditContext)
    {
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clientContext = clientContext;
        _auditContext = auditContext;
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

            // Read after the handler ran: the outcome and the account are what it just learned.
            await _audit
                .WriteAsync(Record(auditable, context, _auditContext), cancellationToken)
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

    private static AuditRecord Record(
        IAuditableCommand command,
        ClientContext context,
        IAuditContext auditContext)
    {
        DateTime nowUtc = DateTime.UtcNow;

        return new AuditRecord
        {
            Id = Guid.NewGuid(),

            // Named by the handler where it knows the account, null where it does not — an anonymous
            // refusal must not claim an account, and naming one on a failed login would make the table an
            // account-existence oracle.
            UserId = auditContext.UserId,
            Action = command.AuditAction,
            TargetType = command.AuditTargetType,
            TargetId = auditContext.TargetId,
            Platform = context.Platform,
            Device = context.Device,
            AppVersion = context.AppVersion,
            OccurredAtUtc = nowUtc,
            CorrelationId = context.CorrelationId,
            Outcome = auditContext.Outcome.ToString().ToLowerInvariant(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }
}
