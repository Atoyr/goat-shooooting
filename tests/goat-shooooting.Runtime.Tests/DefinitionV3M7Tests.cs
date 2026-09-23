using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class DefinitionV3M7Tests
{
    [Fact]
    public void CheckpointRestoreRecoversCanonicalHashAndDeterministicContinuation()
    {
        var definitions = TestDefinitions.Create(spawnTime: 0);
        var configuration = new RunConfiguration("m7-checkpoint", seed: 7123, initialInvincibilitySeconds: 30);
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);
        var inputs = Enumerable.Range(0, 240)
            .Select(frame => new InputFrame(
                (sbyte)(frame % 80 < 40 ? 32 : -32),
                0,
                frame % 3 == 0 ? InputButtons.Fire : InputButtons.None))
            .ToArray();

        for (var frame = 0; frame < 90; frame++) simulation.Tick(inputs[frame]);
        var checkpoint = simulation.CreateCheckpoint();
        Assert.Equal(simulation.RunState.Frame, checkpoint.Frame);
        Assert.Equal(simulation.ComputeCanonicalStateHash(), checkpoint.StateHash);

        for (var frame = 90; frame < inputs.Length; frame++) simulation.Tick(inputs[frame]);
        var expectedHash = simulation.ComputeCanonicalStateHash();
        var expectedProjectiles = simulation.Projectiles.ActiveCount;

        simulation.RestoreCheckpoint(checkpoint);
        Assert.Equal(checkpoint.StateHash, simulation.ComputeCanonicalStateHash());
        for (var frame = 90; frame < inputs.Length; frame++) simulation.Tick(inputs[frame]);

        Assert.Equal(expectedHash, simulation.ComputeCanonicalStateHash());
        Assert.Equal(expectedProjectiles, simulation.Projectiles.ActiveCount);

        simulation.RestoreCheckpoint(checkpoint);
        Assert.Equal(checkpoint.StateHash, simulation.ComputeCanonicalStateHash());
    }

    [Fact]
    public void CheckpointRejectsDifferentCompiledContent()
    {
        var source = TestDefinitions.Create(spawnTime: 20);
        var original = new ShootingSimulation(
            new MemoryDefinitionRepository(source), new MutableInputState(), new RunConfiguration("m7", 1));
        var checkpoint = original.CreateCheckpoint();
        var changed = new DefinitionCatalog(
            source.Game with { Width = source.Game.Width + 1 }, source.Players.Values,
            source.Enemies.Values, source.Bullets.Values, source.Weapons.Values, source.Stages.Values);
        var target = new ShootingSimulation(
            new MemoryDefinitionRepository(changed), new MutableInputState(), new RunConfiguration("m7", 1));

        var exception = Assert.Throws<InvalidOperationException>(() => target.RestoreCheckpoint(checkpoint));

        Assert.Contains("compiled content hash", exception.Message, StringComparison.Ordinal);
    }
}
