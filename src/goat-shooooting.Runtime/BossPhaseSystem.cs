using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public sealed class BossPhaseSystem(
    ItemDropSystem? itemDropSystem = null,
    RunModifierState? modifiers = null)
{
    private readonly ItemDropSystem _itemDropSystem = itemDropSystem ?? new ItemDropSystem();
    private readonly RunModifierState? _modifiers = modifiers;

    public void BeginAndAdvance(
        World world,
        DefinitionCatalog definitions,
        float deltaTime,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events) =>
        BeginAndAdvanceCore(world, definitions, null, deltaTime, deltaTime, projectiles, telemetry, events);

    public void BeginAndAdvance(
        World world,
        CompiledCatalog definitions,
        float deltaTime,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events) =>
        BeginAndAdvanceCore(world, definitions.Source, definitions, deltaTime, deltaTime, projectiles, telemetry, events);

    public void BeginAndAdvance(
        World world,
        CompiledCatalog definitions,
        float runDeltaTime,
        float worldDeltaTime,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events) =>
        BeginAndAdvanceCore(
            world, definitions.Source, definitions, runDeltaTime, worldDeltaTime,
            projectiles, telemetry, events);

    private void BeginAndAdvanceCore(
        World world,
        DefinitionCatalog definitions,
        CompiledCatalog? compiledDefinitions,
        float runDeltaTime,
        float worldDeltaTime,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        foreach (var entity in world.Query<BossComponent, HealthComponent>().ToArray())
        {
            var state = entity.Get<BossComponent>();
            if (!state.IsManaged || state.IsComplete || entity.Has<PendingDestroyComponent>()) continue;
            if (!state.IsInitialized)
            {
                var compiledBoss = compiledDefinitions is null ? null : ResolveCompiledBoss(compiledDefinitions, state);
                BeginPhase(
                    world,
                    entity,
                    compiledBoss?.Definition ?? ResolveBoss(definitions, null, state),
                    phaseIndex: 0,
                    projectiles,
                    telemetry,
                    events,
                    compiledDefinitions,
                    compiledBoss);
            }
            else
            {
                var boss = compiledDefinitions is null ? ResolveBoss(definitions, null, state) :
                    ResolveCompiledBoss(compiledDefinitions, state).Definition;
                var phaseDelta = boss.Phases[state.PhaseIndex].Clock == "world-time"
                    ? worldDeltaTime : runDeltaTime;
                state.PhaseElapsed = Math.Min(state.PhaseTimeLimit, state.PhaseElapsed + phaseDelta);
            }
        }
    }

    public void Resolve(
        World world,
        DefinitionCatalog definitions,
        ProjectileStore projectiles,
        IRandomSource random,
        SimulationTelemetry telemetry,
        GameEventBuffer events) =>
        ResolveCore(world, definitions, null, projectiles, random, telemetry, events);

    public void Resolve(
        World world,
        CompiledCatalog definitions,
        ProjectileStore projectiles,
        IRandomSource random,
        SimulationTelemetry telemetry,
        GameEventBuffer events) =>
        ResolveCore(world, definitions.Source, definitions, projectiles, random, telemetry, events);

    private void ResolveCore(
        World world,
        DefinitionCatalog definitions,
        CompiledCatalog? compiledDefinitions,
        ProjectileStore projectiles,
        IRandomSource random,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        foreach (var entity in world.Query<BossComponent, HealthComponent>().ToArray())
        {
            var state = entity.Get<BossComponent>();
            if (!state.IsManaged || !state.IsInitialized || state.IsComplete || entity.Has<PendingDestroyComponent>()) continue;
            var healthDepleted = entity.Get<HealthComponent>().Current <= 0;
            var timedOut = !healthDepleted && state.PhaseElapsed >= state.PhaseTimeLimit;
            if (!healthDepleted && !timedOut) continue;
            EndPhase(
                world, entity, definitions, compiledDefinitions, projectiles, random, telemetry, events, timedOut);
        }
    }

    public void BeginAtCheckpoint(
        Entity entity,
        BossDefinition boss,
        string checkpointId,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(boss);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        var phaseIndex = boss.Phases.ToList().FindIndex(phase => phase.CheckpointId == checkpointId);
        if (phaseIndex < 0)
            throw new DefinitionValidationException($"Boss '{boss.Id}' has no checkpoint '{checkpointId}'.");
        BeginPhase(null, entity, boss, phaseIndex, projectiles, telemetry, events, null, null);
    }

    public void BeginAtCheckpoint(
        World world,
        Entity entity,
        CompiledCatalog definitions,
        BossHandle bossHandle,
        string checkpointId,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var compiledBoss = definitions.GetCompiled(bossHandle);
        var boss = compiledBoss.Definition;
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        var phaseIndex = boss.Phases.ToList().FindIndex(phase => phase.CheckpointId == checkpointId);
        if (phaseIndex < 0)
            throw new DefinitionValidationException($"Boss '{boss.Id}' has no checkpoint '{checkpointId}'.");
        BeginPhase(world, entity, boss, phaseIndex, projectiles, telemetry, events, definitions, compiledBoss);
    }

    private void EndPhase(
        World world,
        Entity entity,
        DefinitionCatalog definitions,
        CompiledCatalog? compiledDefinitions,
        ProjectileStore projectiles,
        IRandomSource random,
        SimulationTelemetry telemetry,
        GameEventBuffer events,
        bool timedOut)
    {
        var state = entity.Get<BossComponent>();
        var compiledBoss = compiledDefinitions is null ? null : ResolveCompiledBoss(compiledDefinitions, state);
        var boss = compiledBoss?.Definition ?? ResolveBoss(definitions, null, state);
        var phase = boss.Phases[state.PhaseIndex];
        var compiledPhase = compiledBoss?.Phases[state.PhaseIndex];
        CancelProjectiles(projectiles, phase.EndProjectileCancel, telemetry, events);
        var remainingSeconds = Math.Max(0, phase.TimeLimit - state.PhaseElapsed);
        events.Publish((frame, sequence) => new BossPhaseEndedEvent(
            frame, sequence, boss.Id, phase.Id, timedOut, entity.Id));
        events.Publish((frame, sequence) => new BossPhaseBonusEvent(
            frame,
            sequence,
            entity.Id,
            boss.Id,
            phase.Id,
            phase.BaseBonus,
            MultiplySaturating(phase.TimeBonusPerSecond, remainingSeconds),
            telemetry.PlayerDeaths == state.PlayerDeathsAtPhaseStart ? phase.NoMissBonus : 0,
            telemetry.BombsUsed == state.BombsUsedAtPhaseStart ? phase.NoBombBonus : 0));
        if (compiledDefinitions is null)
        {
            _itemDropSystem.SpawnDropTable(
                world, definitions, phase.DropTable, entity.Get<TransformComponent>().Position,
                random, telemetry, events);
        }
        else
        {
            _itemDropSystem.SpawnDropTable(
                world, compiledDefinitions, compiledPhase!.DropTable, entity.Get<TransformComponent>().Position,
                random, telemetry, events);
        }
        ResetPatterns(entity);

        var nextPhaseIndex = state.PhaseIndex + 1;
        if (nextPhaseIndex < boss.Phases.Count)
        {
            BeginPhase(
                world,
                entity, boss, nextPhaseIndex, projectiles, telemetry, events, compiledDefinitions, compiledBoss);
            return;
        }

        state.IsComplete = true;
        entity.Add(new PendingDestroyComponent());
        telemetry.EnemiesKilled++;
        telemetry.BossesKilled++;
        events.Publish((frame, sequence) => new EnemyDestroyedEvent(
            frame,
            sequence,
            entity.Id,
            entity.Get<EnemyComponent>().DefinitionId,
            entity.Get<ScoreValueComponent>().Value,
            DistanceToPlayer(world, entity)));
        events.Publish((frame, sequence) => new BossCompletedEvent(frame, sequence, entity.Id, boss.Id));
    }

    private void BeginPhase(
        World? world,
        Entity entity,
        BossDefinition boss,
        int phaseIndex,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events,
        CompiledCatalog? compiledDefinitions,
        CompiledBossDefinition? compiledBoss)
    {
        var phase = boss.Phases[phaseIndex];
        var compiledPhase = compiledBoss?.Phases[phaseIndex];
        var state = entity.Get<BossComponent>();
        state.PhaseIndex = phaseIndex;
        state.PhaseId = phase.Id;
        state.PhaseDisplayName = phase.DisplayName;
        state.PhaseElapsed = 0;
        state.PhaseTimeLimit = phase.TimeLimit;
        state.WarningSeconds = boss.WarningSeconds;
        state.CheckpointId = phase.CheckpointId;
        state.PlayerDeathsAtPhaseStart = telemetry.PlayerDeaths;
        state.BombsUsedAtPhaseStart = telemetry.BombsUsed;
        state.IsInitialized = true;
        entity.Remove<HealthComponent>();
        entity.Add(new HealthComponent(Math.Max(1, (int)MathF.Round(
            phase.Hp * (_modifiers?.EnemyHealthMultiplier ?? 1)))));
        entity.Remove<HitFlashComponent>();
        entity.Get<VelocityComponent>().Value = System.Numerics.Vector2.Zero;
        ResetPatterns(entity);
        if (!string.IsNullOrWhiteSpace(phase.MotionPatternId))
        {
            entity.Add(new MotionTimelineComponent(
                phase.MotionPatternId,
                compiledDefinitions is not null && compiledPhase?.MotionPatternHandle is { } motionHandle
                    ? compiledDefinitions.Get(motionHandle).Commands
                    : null));
        }

        if (phase.AttackPatternIds.Count > 0)
        {
            entity.Add(new AttackTimelineComponent(
                phase.AttackPatternIds,
                compiledDefinitions is null
                    ? null
                    : compiledPhase!.AttackPatternHandles
                        .Select(compiledDefinitions.Get)
                        .ToArray()));
        }

        entity.Remove<InvincibilityComponent>();
        if (phase.InvulnerabilitySeconds > 0)
        {
            var invincibility = new InvincibilityComponent(phase.InvulnerabilitySeconds)
            {
                Remaining = phase.InvulnerabilitySeconds
            };
            entity.Add(invincibility);
        }

        CancelProjectiles(projectiles, phase.StartProjectileCancel, telemetry, events);
        if (world is not null && compiledPhase is not null)
            ActorPartSignalSystem.Apply(world, entity.Id, compiledPhase, events);
        events.Publish((frame, sequence) => new BossPhaseStartedEvent(
            frame, sequence, entity.Id, boss.Id, phase.Id, phaseIndex, phase.CheckpointId));
    }

    private static BossDefinition ResolveBoss(
        DefinitionCatalog definitions,
        CompiledCatalog? compiledDefinitions,
        BossComponent state) =>
        compiledDefinitions is not null && state.DefinitionHandle >= 0
            ? compiledDefinitions.Get(new BossHandle(state.DefinitionHandle))
            : definitions.GetBoss(state.DefinitionId!);

    private static CompiledBossDefinition ResolveCompiledBoss(
        CompiledCatalog definitions,
        BossComponent state)
    {
        if (state.DefinitionHandle < 0)
            throw new InvalidOperationException($"Boss '{state.DefinitionId}' was not compiled.");
        return definitions.GetCompiled(new BossHandle(state.DefinitionHandle));
    }

    private static void ResetPatterns(Entity entity)
    {
        entity.Remove<MotionTimelineComponent>();
        entity.Remove<AttackTimelineComponent>();
        entity.Remove<WeaponRuntimeComponent>();
    }

    private static void CancelProjectiles(
        ProjectileStore projectiles,
        string policy,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        if (policy == "none") return;
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (projectiles.TeamAt(index) != ProjectileTeam.Enemy || projectiles.IsPendingRemovalAt(index)) continue;
            if (policy == "soft" &&
                (!projectiles.CanBeCancelledAt(index) ||
                 projectiles.CancelResistanceAt(index) != ProjectileCancelResistance.Soft)) continue;
            projectiles.QueueRemoveAt(index);
            telemetry.EnemyBulletsCleared++;
            var projectileId = projectiles.IdAt(index);
            events.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, projectileId, AwardsScore: false));
        }
    }

    private static float? DistanceToPlayer(World world, Entity boss)
    {
        var player = world.Query<PlayerComponent, TransformComponent>()
            .Where(static entity => !entity.Has<PendingDestroyComponent>())
            .OrderBy(static entity => entity.Id)
            .FirstOrDefault();
        return player is null
            ? null
            : System.Numerics.Vector2.Distance(
                boss.Get<TransformComponent>().Position,
                player.Get<TransformComponent>().Position);
    }

    private static long MultiplySaturating(long value, float multiplier)
    {
        var result = (decimal)value * (decimal)MathF.Floor(multiplier);
        return result >= long.MaxValue ? long.MaxValue : (long)result;
    }
}
