using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum SimulationSandboxTargetKind
{
    Program,
    Pattern,
    Actor,
    BossPhase
}

public sealed partial class ShootingSimulation
{
    /// <summary>Bootstraps one authored target through the same factories and systems used by production runs.</summary>
    public void BootstrapSandboxTarget(
        SimulationSandboxTargetKind kind,
        string definitionId,
        string? checkpointId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        switch (kind)
        {
            case SimulationSandboxTargetKind.Program:
                SpawnProgramTarget(definitionId);
                break;
            case SimulationSandboxTargetKind.Pattern:
                SpawnPatternTarget(definitionId);
                break;
            case SimulationSandboxTargetKind.Actor:
                _ = _enemyFactory.CreateActor(
                    World,
                    CompiledDefinitions,
                    CompiledDefinitions.ResolveActor(definitionId),
                    SandboxPosition());
                Telemetry.EnemiesSpawned++;
                break;
            case SimulationSandboxTargetKind.BossPhase:
                SpawnBossTarget(definitionId, checkpointId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private void SpawnProgramTarget(string programId)
    {
        var program = CompiledDefinitions.Get(CompiledDefinitions.ResolveProgram(programId));
        if (program.ProjectileProgram is null)
            throw new DefinitionValidationException($"Program '{programId}' is not a projectile program.");
        var projectile = CompiledDefinitions.Projectiles.FirstOrDefault(candidate =>
            candidate.ProgramSlot is not null &&
            CompiledDefinitions.ResolveProgramBinding(candidate.ProgramSlot, CurrentVariantHandle).Program.Handle == program.Handle)
            ?? throw new DefinitionValidationException(
                $"Program '{programId}' is not linked from a projectile semantic slot in the selected variant.");
        _ = new BulletFactory().Create(
            Projectiles,
            projectile,
            new Vector2(Definitions.Game.Width / 2f, Math.Max(32, Definitions.Game.Height * 0.2f)),
            Vector2.UnitY,
            CollisionLayer.Enemy,
            ownerEntityId: Player.Id);
        Projectiles.CommitSpawns(Events);
    }

    private void SpawnPatternTarget(string patternId)
    {
        var pattern = CompiledDefinitions.Get(CompiledDefinitions.ResolvePattern(patternId));
        var owner = CompiledDefinitions.Enemies.FirstOrDefault(candidate =>
            candidate.AttackPatternHandles.Contains(pattern.Handle)) ?? CompiledDefinitions.Enemies.FirstOrDefault()
            ?? throw new DefinitionValidationException($"Pattern '{patternId}' requires at least one enemy definition.");
        var entity = _enemyFactory.Create(
            World,
            CompiledDefinitions,
            owner.Handle,
            SandboxPosition());
        entity.Remove<AttackTimelineComponent>();
        entity.Add(new AttackTimelineComponent([patternId], [pattern]));
        Telemetry.EnemiesSpawned++;
    }

    private void SpawnBossTarget(string bossId, string? checkpointId)
    {
        var bossHandle = CompiledDefinitions.ResolveBoss(bossId);
        var boss = CompiledDefinitions.GetCompiled(bossHandle);
        var entity = _enemyFactory.CreateActor(
            World,
            CompiledDefinitions,
            boss.ActorHandle,
            new Vector2(Definitions.Game.Width / 2f, Math.Max(32, Definitions.Game.Height * 0.15f)),
            isBoss: true,
            bossHandle: bossHandle);
        Telemetry.EnemiesSpawned++;
        if (!string.IsNullOrWhiteSpace(checkpointId))
            _bossPhaseSystem.BeginAtCheckpoint(
                World, entity, CompiledDefinitions, bossHandle, checkpointId,
                Projectiles, Telemetry, Events);
    }

    private Vector2 SandboxPosition() =>
        new(Definitions.Game.Width / 2f, Math.Max(48, Definitions.Game.Height * 0.25f));
}
