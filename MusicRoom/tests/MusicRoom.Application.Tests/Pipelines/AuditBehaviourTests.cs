using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Pipelines.Auditing;
using MusicRoom.Domain.Entities;
using Xunit;

namespace MusicRoom.Application.Tests.Pipelines;

/// <summary>
/// The audit behaviour — subject V.6, SDD §10.7 and §13.4, SPEC-008.
/// </summary>
/// <remarks>
/// Fakes are hand-written: this project pins no mocking package, and three one-method abstractions do not
/// need one. Behaviour is asserted through what the fakes recorded, not through how they were called.
/// </remarks>
public sealed class AuditBehaviourTests
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string Platform = "ios";
    internal const string Device = "iPhone 15";
    internal const string AppVersion = "1.2.3";
    internal const string CorrelationId = "0af7651916cd43dd8448eb211c80319c";

    /// <summary>One handled command, one record — the requirement subject V.6 states.</summary>
    [Fact]
    public async Task Handle_AnAuditableCommand_WritesExactlyOneRecordAsync()
    {
        // Arrange
        (AuditBehaviour<AuditableCommand, string> behaviour, RecordingSink sink, RecordingUnitOfWork work) =
            Build(WithContext());

        // Act
        string response = await behaviour.Handle(
            new AuditableCommand(),
            _ => Task.FromResult("handled"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("handled", response);
        AuditRecord record = Assert.Single(sink.Records);
        Assert.Equal("test.action", record.Action);
        Assert.Equal("test-target", record.TargetType);
        Assert.Equal(1, work.Commits);
        Assert.Equal(0, work.Rollbacks);
    }

    /// <summary>The three fields subject V.6 names are copied from the client context, not invented.</summary>
    [Fact]
    public async Task Handle_CopiesThePlatformDeviceAppVersionAndCorrelationIdAsync()
    {
        // Arrange
        (AuditBehaviour<AuditableCommand, string> behaviour, RecordingSink sink, _) = Build(WithContext());

        // Act
        await behaviour.Handle(
            new AuditableCommand(),
            _ => Task.FromResult("handled"),
            TestContext.Current.CancellationToken);

        // Assert
        AuditRecord record = Assert.Single(sink.Records);
        Assert.Equal(Platform, record.Platform);
        Assert.Equal(Device, record.Device);
        Assert.Equal(AppVersion, record.AppVersion);
        Assert.Equal(CorrelationId, record.CorrelationId);
        Assert.Null(record.UserId);
    }

    /// <summary>A command that opts out passes straight through and opens no transaction.</summary>
    [Fact]
    public async Task Handle_ACommandThatIsNotAuditable_WritesNothingAsync()
    {
        // Arrange
        RecordingSink sink = new();
        RecordingUnitOfWork work = new();
        AuditBehaviour<PlainCommand, string> behaviour = new(work, sink, WithContext());

        // Act
        string response = await behaviour.Handle(
            new PlainCommand(),
            _ => Task.FromResult("handled"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("handled", response);
        Assert.Empty(sink.Records);
        Assert.Equal(0, work.Begins);
    }

    /// <summary>
    /// Fail closed: a sink that throws rolls the transaction back and the exception reaches the caller, so
    /// the effect cannot stand without its record — SPEC-008 escalations 1 and 2.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTheSinkThrows_RollsBackAndRethrowsAsync()
    {
        // Arrange
        ThrowingSink sink = new();
        RecordingUnitOfWork work = new();
        AuditBehaviour<AuditableCommand, string> behaviour = new(work, sink, WithContext());

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => behaviour.Handle(
            new AuditableCommand(),
            _ => Task.FromResult("handled"),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(1, work.Rollbacks);
        Assert.Equal(0, work.Commits);
    }

    /// <summary>A failing handler rolls back too, so no record describes an effect that did not happen.</summary>
    [Fact]
    public async Task Handle_WhenTheHandlerThrows_RollsBackAndWritesNothingAsync()
    {
        // Arrange
        (AuditBehaviour<AuditableCommand, string> behaviour, RecordingSink sink, RecordingUnitOfWork work) =
            Build(WithContext());

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() => behaviour.Handle(
            new AuditableCommand(),
            _ => throw new TimeoutException(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(sink.Records);
        Assert.Equal(1, work.Rollbacks);
    }

    /// <summary>
    /// With no client context the behaviour refuses rather than substituting values. The middleware makes
    /// this unreachable over HTTP; a command sent from anywhere else must fail loudly.
    /// </summary>
    [Fact]
    public async Task Handle_WithNoClientContext_ThrowsRatherThanInventingValuesAsync()
    {
        // Arrange
        (AuditBehaviour<AuditableCommand, string> behaviour, RecordingSink sink, _) =
            Build(new StubClientContextAccessor(null));

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behaviour.Handle(
                new AuditableCommand(),
                _ => Task.FromResult("handled"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("client context", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sink.Records);
    }

    /// <summary>The transaction is not committed twice when an outer caller already owns it.</summary>
    [Fact]
    public async Task Handle_WhenATransactionIsAlreadyOpen_DoesNotCommitItAsync()
    {
        // Arrange
        RecordingSink sink = new();
        RecordingUnitOfWork work = new() { AlreadyOpen = true };
        AuditBehaviour<AuditableCommand, string> behaviour = new(work, sink, WithContext());

        // Act
        await behaviour.Handle(
            new AuditableCommand(),
            _ => Task.FromResult("handled"),
            TestContext.Current.CancellationToken);

        // Assert — the record is written, and committing is the owner's business.
        Assert.Single(sink.Records);
        Assert.Equal(0, work.Commits);
    }

    private static (AuditBehaviour<AuditableCommand, string> Behaviour, RecordingSink Sink, RecordingUnitOfWork Work)
        Build(IClientContextAccessor clientContext)
    {
        RecordingSink sink = new();
        RecordingUnitOfWork work = new();

        return (new AuditBehaviour<AuditableCommand, string>(work, sink, clientContext), sink, work);
    }

    private static StubClientContextAccessor WithContext() =>
        new(new ClientContext(Platform, Device, AppVersion, CorrelationId));

    /// <summary>A command that asks to be audited.</summary>
    internal sealed record AuditableCommand : IRequest<string>, IAuditableCommand
    {
        public string AuditAction => "test.action";

        public string AuditTargetType => "test-target";
    }

    /// <summary>A command that does not.</summary>
    internal sealed record PlainCommand : IRequest<string>;

    /// <summary>Collects the records the behaviour wrote.</summary>
    internal sealed class RecordingSink : IAuditSink
    {
        internal List<AuditRecord> Records { get; } = [];

        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);

            return Task.CompletedTask;
        }
    }

    /// <summary>A sink that cannot write, which is the case escalation 1 decided.</summary>
    internal sealed class ThrowingSink : IAuditSink
    {
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the audit table is unavailable");
    }

    /// <summary>Counts what the behaviour did with the transaction boundary.</summary>
    internal sealed class RecordingUnitOfWork : IUnitOfWork
    {
        /// <summary>True to simulate an outer caller that already opened one.</summary>
        internal bool AlreadyOpen { get; init; }

        internal int Begins { get; private set; }

        internal int Commits { get; private set; }

        internal int Rollbacks { get; private set; }

        public Task<bool> BeginAsync(CancellationToken cancellationToken)
        {
            Begins++;

            return Task.FromResult(!AlreadyOpen);
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Commits++;

            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            Rollbacks++;

            return Task.CompletedTask;
        }
    }

    /// <summary>Answers a fixed client context, or none.</summary>
    internal sealed class StubClientContextAccessor : IClientContextAccessor
    {
        internal StubClientContextAccessor(ClientContext? current) => Current = current;

        public ClientContext? Current { get; }
    }
}
