using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class GameEventBufferTests
{
    [Fact]
    public void EventsReceiveCurrentFrameAndIncreasingSequence()
    {
        var buffer = new GameEventBuffer();
        buffer.BeginTick(12);

        var damaged = buffer.Publish((frame, sequence) =>
            new EnemyDamagedEvent(frame, sequence, 7, 10));
        var destroyed = buffer.Publish((frame, sequence) =>
            new EnemyDestroyedEvent(frame, sequence, 7, "scout"));

        Assert.Equal(12, damaged.Frame);
        Assert.Equal(0, damaged.Sequence);
        Assert.Equal(1, destroyed.Sequence);
        Assert.Equal(new IGameplayEvent[] { damaged, destroyed }, buffer.Events);
    }

    [Fact]
    public void BeginTickClearsPriorTickEventsAndRestartsSequence()
    {
        var buffer = new GameEventBuffer();
        buffer.BeginTick(2);
        buffer.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, 9));

        buffer.BeginTick(3);
        var playerHit = buffer.Publish((frame, sequence) =>
            new PlayerHitEvent(frame, sequence, 1, 10));

        Assert.Equal(3, buffer.Frame);
        Assert.Equal(0, playerHit.Sequence);
        Assert.Single(buffer.Events);
        Assert.Same(playerHit, buffer.Events[0]);
    }

    [Fact]
    public void PublishBeforeBeginTickFails()
    {
        var buffer = new GameEventBuffer();

        Assert.Throws<InvalidOperationException>(() => buffer.Publish((frame, sequence) =>
            new PlayerGrazedEvent(frame, sequence, 1, 2)));
    }

    [Fact]
    public void PublishRejectsIncorrectEventStamp()
    {
        var buffer = new GameEventBuffer();
        buffer.BeginTick(4);

        Assert.Throws<InvalidOperationException>(() => buffer.Publish((_, _) =>
            new ItemCollectedEvent(3, 8, 1, "power", 1)));
        Assert.Empty(buffer.Events);
    }

    [Fact]
    public void BeginTickRejectsNegativeFrame()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameEventBuffer().BeginTick(-1));
    }
}
