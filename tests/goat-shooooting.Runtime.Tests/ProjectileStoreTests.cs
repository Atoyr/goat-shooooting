using System.Numerics;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class ProjectileStoreTests
{
    [Fact]
    public void SpawnAndRemovalAreCommittedAtExplicitBoundaries()
    {
        var store = new ProjectileStore(initialCapacity: 1);
        var events = new GameEventBuffer();
        events.BeginTick(4);

        var firstId = store.QueueSpawn(CreateCommand(new Vector2(10, 20)));
        var secondId = store.QueueSpawn(CreateCommand(new Vector2(30, 40)));

        Assert.Equal(0, store.ActiveCount);
        Assert.Equal(2, store.PendingSpawnCount);

        store.CommitSpawns(events);
        Assert.Equal(2, store.ActiveCount);
        Assert.True(store.Capacity >= 2);
        Assert.Equal(new[] { firstId, secondId },
            Enumerable.Range(0, store.ActiveCount).Select(index => store.GetSnapshot(index).Id));
        Assert.All(events.Events, static gameplayEvent => Assert.IsType<ProjectileSpawnedEvent>(gameplayEvent));

        store.QueueRemoveAt(0);
        Assert.Equal(2, store.ActiveCount);
        Assert.True(store.GetSnapshot(0).PendingRemoval);

        store.CommitRemovals();
        Assert.Equal(1, store.ActiveCount);
        Assert.Equal(secondId, store.GetSnapshot(0).Id);
    }

    [Fact]
    public void CapacityIsReusedAndProjectileIdsRemainMonotonic()
    {
        var store = new ProjectileStore(initialCapacity: 2);
        var first = store.QueueSpawn(CreateCommand(Vector2.Zero));
        store.QueueSpawn(CreateCommand(Vector2.One));
        store.CommitSpawns();
        var capacity = store.Capacity;
        store.QueueRemoveAll();
        store.CommitRemovals();

        var next = store.QueueSpawn(CreateCommand(new Vector2(2, 2)));
        store.CommitSpawns();

        Assert.Equal(capacity, store.Capacity);
        Assert.True(next > first);
        Assert.Equal(1, store.ActiveCount);
    }

    [Fact]
    public void InvalidSpawnDataIsRejectedWithoutChangingTheStore()
    {
        var store = new ProjectileStore();
        var invalid = CreateCommand(Vector2.Zero) with { HitRadius = float.NaN };

        Assert.Throws<ArgumentOutOfRangeException>(() => store.QueueSpawn(invalid));
        Assert.Equal(0, store.ActiveCount);
        Assert.Equal(0, store.PendingSpawnCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.QueueRemoveAt(0));
    }

    private static ProjectileSpawnCommand CreateCommand(Vector2 position) => new(
        OwnerEntityId: 1,
        Team: ProjectileTeam.Player,
        DefinitionId: "shot",
        Position: position,
        Velocity: -Vector2.UnitY,
        HitRadius: 2,
        Damage: 5,
        Lifetime: 2,
        VisualId: "shot");
}
