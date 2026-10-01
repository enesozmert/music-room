using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Pipelines.Realtime;
using Xunit;

namespace MusicRoom.Application.Tests.Pipelines;

/// <summary>
/// The broadcast behaviour — ADR-0035, `signalr-realtime`'s post-commit rule.
/// </summary>
/// <remarks>
/// Fakes are hand-written, as in <see cref="AuditBehaviourTests"/>: two one-method abstractions do not need a
/// mocking package. What is asserted is the ordering guarantee — nothing is sent before the inner pipeline
/// returns, and nothing at all if it throws, because the transaction it owns has then rolled back.
/// </remarks>
public sealed class BroadcastBehaviourTests
{
    /// <summary>What the handler queued is sent once the inner pipeline has returned.</summary>
    [Fact]
    public async Task Handle_WhenTheInnerPipelineReturns_SendsWhatWasQueuedAsync()
    {
        // Arrange
        PlaylistBroadcastQueue queue = new();
        RecordingBroadcaster broadcaster = new();
        BroadcastBehaviour<string, string> behaviour = new(queue, broadcaster);

        Guid playlistId = Guid.NewGuid();

        // Act
        string response = await behaviour.Handle(
            "command",
            _ =>
            {
                queue.Enqueue(new TrackMoved(playlistId, Version: 42, Guid.NewGuid()));

                // Nothing may have been sent yet: at this point the transaction is still open.
                Assert.Empty(broadcaster.Sent);

                return Task.FromResult("handled");
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("handled", response);
        PlaylistBroadcast sent = Assert.Single(broadcaster.Sent);
        Assert.Equal(playlistId, sent.PlaylistId);
        Assert.Equal(42u, sent.Version);
    }

    /// <summary>
    /// A throw from the inner pipeline sends nothing: the transaction rolled back, and there is no message
    /// that means "ignore what I just told you".
    /// </summary>
    [Fact]
    public async Task Handle_WhenTheInnerPipelineThrows_SendsNothingAsync()
    {
        // Arrange
        PlaylistBroadcastQueue queue = new();
        RecordingBroadcaster broadcaster = new();
        BroadcastBehaviour<string, string> behaviour = new(queue, broadcaster);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => behaviour.Handle(
                "command",
                _ =>
                {
                    queue.Enqueue(new TrackAdded(Guid.NewGuid(), Version: 1, Guid.NewGuid(), "dQw4w9WgXcQ"));

                    throw new InvalidOperationException("the write failed");
                },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(broadcaster.Sent);
    }

    /// <summary>A request that queued nothing sends nothing, and costs nothing.</summary>
    [Fact]
    public async Task Handle_WithAnEmptyQueue_SendsNothingAsync()
    {
        // Arrange
        PlaylistBroadcastQueue queue = new();
        RecordingBroadcaster broadcaster = new();
        BroadcastBehaviour<string, string> behaviour = new(queue, broadcaster);

        // Act
        await behaviour.Handle(
            "command",
            _ => Task.FromResult("handled"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(broadcaster.Sent);
    }

    /// <summary>Draining empties the queue, so a second pass cannot send the same fact twice.</summary>
    [Fact]
    public void Drain_Twice_YieldsTheFactsOnce()
    {
        // Arrange
        PlaylistBroadcastQueue queue = new();
        queue.Enqueue(new TrackRemoved(Guid.NewGuid(), Version: 7, Guid.NewGuid()));

        // Act
        IReadOnlyList<PlaylistBroadcast> first = queue.Drain();
        IReadOnlyList<PlaylistBroadcast> second = queue.Drain();

        // Assert
        Assert.Single(first);
        Assert.Empty(second);
    }

    /// <summary>Records what it was asked to send, and never throws — the port's contract.</summary>
    private sealed class RecordingBroadcaster : IPlaylistBroadcaster
    {
        internal List<PlaylistBroadcast> Sent { get; } = [];

        public Task SendAsync(PlaylistBroadcast broadcast, CancellationToken cancellationToken)
        {
            Sent.Add(broadcast);

            return Task.CompletedTask;
        }
    }
}
