using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public sealed class PlayerLifeCycleSystem
{
    private const float BombRescueTransitionSeconds = 1f / SimulationTiming.TicksPerSecond;

    public void Advance(World world, float deltaTime, SimulationTelemetry telemetry, GameEventBuffer events)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var player in world.Query<PlayerComponent, PlayerLifeCycleComponent>())
        {
            var lifeCycle = player.Get<PlayerLifeCycleComponent>();
            if (lifeCycle.State is PlayerLifeCycleState.Active or PlayerLifeCycleState.GameOverPending) continue;
            lifeCycle.Timer = Math.Max(0, lifeCycle.Timer - deltaTime);
            if (lifeCycle.Timer > 0) continue;

            switch (lifeCycle.State)
            {
                case PlayerLifeCycleState.BombRescue:
                    var bombInvincibility = player.Get<InvincibilityComponent>();
                    if (bombInvincibility.Remaining > 0)
                    {
                        lifeCycle.State = PlayerLifeCycleState.Invincible;
                        lifeCycle.Timer = bombInvincibility.Remaining;
                    }
                    else
                    {
                        lifeCycle.State = PlayerLifeCycleState.Active;
                    }
                    break;
                case PlayerLifeCycleState.Dying:
                    if (player.Get<LivesComponent>().Remaining <= 0)
                    {
                        lifeCycle.State = PlayerLifeCycleState.GameOverPending;
                    }
                    else
                    {
                        lifeCycle.State = PlayerLifeCycleState.Respawning;
                        lifeCycle.Timer = lifeCycle.RespawnDelaySeconds;
                    }

                    break;
                case PlayerLifeCycleState.Respawning:
                    player.Get<TransformComponent>().Snap(lifeCycle.RespawnPosition);
                    player.Get<VelocityComponent>().Value = Vector2.Zero;
                    var bombs = player.Get<BombComponent>();
                    var ship = player.Get<ShipComponent>();
                    bombs.Remaining = Math.Min(ship.MaximumBombs, lifeCycle.BombsAfterRespawn);
                    EnterInvincible(player, lifeCycle);
                    telemetry.PlayerRespawns++;
                    events.Publish((frame, sequence) => new PlayerRespawnedEvent(frame, sequence, player.Id));
                    break;
                case PlayerLifeCycleState.Invincible:
                    lifeCycle.State = PlayerLifeCycleState.Active;
                    break;
            }
        }
    }

    public IReadOnlyList<DamageEvent> ResolveHits(
        World world,
        IReadOnlyList<DamageEvent> damageEvents,
        ProjectileStore projectiles,
        RuleSetDefinition rules,
        bool autoBombEnabled,
        float bombEffectRadius,
        BombSystem bombSystem,
        RunState runState,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        ArgumentNullException.ThrowIfNull(damageEvents);
        var autoBombDamage = new List<DamageEvent>();
        foreach (var damageEvent in damageEvents)
        {
            var player = damageEvent.Target;
            if (!player.Has<PlayerComponent>() || !player.TryGet<PlayerLifeCycleComponent>(out var lifeCycle)) continue;
            if (!lifeCycle.CanBeHit ||
                player.TryGet<InvincibilityComponent>(out var invincibility) && invincibility.Remaining > 0) continue;

            lifeCycle.State = PlayerLifeCycleState.HitPending;
            lifeCycle.HitSourceEntityId = damageEvent.SourceEntityId;
            var bombs = player.Get<BombComponent>();
            if (autoBombEnabled && bombs.Remaining >= rules.AutoBombCost)
            {
                autoBombDamage.AddRange(bombSystem.UseAutoBomb(
                    world,
                    projectiles,
                    player,
                    bombEffectRadius,
                    rules.AutoBombCost,
                    rules.BombInvincibilitySeconds,
                    telemetry,
                    events));
                lifeCycle.State = PlayerLifeCycleState.BombRescue;
                lifeCycle.Timer = BombRescueTransitionSeconds;
                continue;
            }

            telemetry.DamageEventsApplied++;
            telemetry.PlayerDamageEventsApplied++;
            telemetry.PlayerDeaths++;
            var lives = player.Get<LivesComponent>();
            lives.Remaining--;
            events.Publish((frame, sequence) => new PlayerHitEvent(
                frame,
                sequence,
                player.Id,
                damageEvent.SourceEntityId));

            var ship = player.Get<ShipComponent>();
            var previousPower = ship.Power;
            ship.Power = Math.Max(0, ship.Power - lifeCycle.PowerLossOnDeath);
            runState.Power = ship.Power;
            if (ship.Power != previousPower)
            {
                events.Publish((frame, sequence) => new PowerChangedEvent(
                    frame,
                    sequence,
                    player.Id,
                    previousPower,
                    ship.Power,
                    "death"));
            }

            events.Publish((frame, sequence) => new PlayerDiedEvent(
                frame,
                sequence,
                player.Id,
                lives.Remaining));
            world.CreateEntity()
                .Add(new TransformComponent(player.Get<TransformComponent>().Position))
                .Add(new ExplosionComponent(Math.Max(12, ship.HitRadius * 4), Math.Max(0.01f, lifeCycle.DeathAnimationSeconds)));
            lifeCycle.State = PlayerLifeCycleState.Dying;
            lifeCycle.Timer = lifeCycle.DeathAnimationSeconds;
            player.Get<VelocityComponent>().Value = Vector2.Zero;
            ResetPlayerWeapons(world, player);
            if (rules.DeathClearsProjectiles)
            {
                ClearProjectilesOnDeath(projectiles, telemetry, events);
            }

            break;
        }

        return autoBombDamage;
    }

    private static void ResetPlayerWeapons(World world, Entity player)
    {
        foreach (var laser in world.Query<LaserComponent>().ToArray())
        {
            var ownerId = laser.Get<LaserComponent>().OwnerEntityId;
            var owner = OptionFollowSystem.FindEntity(world, ownerId);
            if (ownerId == player.Id ||
                owner?.TryGet<OptionUnitComponent>(out var option) == true && option.OwnerEntityId == player.Id)
            {
                world.DestroyEntity(laser);
            }
        }

        foreach (var owner in world.Entities.Where(entity =>
            entity.Id == player.Id ||
            entity.TryGet<OptionUnitComponent>(out var option) && option.OwnerEntityId == player.Id))
        {
            if (!owner.TryGet<WeaponRuntimeComponent>(out var runtime)) continue;
            foreach (var state in runtime.States.Values)
            {
                state.CooldownRemaining = 0;
                state.BurstShotsRemaining = 0;
                state.BurstCooldownRemaining = 0;
                state.WasHeld = false;
                state.HeldSeconds = 0;
                state.ActiveLaserEntityId = 0;
                state.LockedTargetEntityIds.Clear();
            }
        }
    }

    private static void ClearProjectilesOnDeath(
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (projectiles.TeamAt(index) != ProjectileTeam.Enemy || projectiles.IsPendingRemovalAt(index)) continue;
            projectiles.QueueRemoveAt(index);
            telemetry.EnemyBulletsCleared++;
            var projectileId = projectiles.IdAt(index);
            events.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, projectileId, AwardsScore: false));
        }
    }

    private static void EnterInvincible(Entity player, PlayerLifeCycleComponent lifeCycle)
    {
        lifeCycle.State = PlayerLifeCycleState.Invincible;
        var invincibility = player.Get<InvincibilityComponent>();
        invincibility.Remaining = Math.Max(invincibility.Remaining, lifeCycle.RespawnInvincibilitySeconds);
        lifeCycle.Timer = invincibility.Remaining;
        lifeCycle.HitSourceEntityId = null;
    }
}

public sealed class ItemFactory
{
    public Entity Create(
        World world,
        ItemDefinition definition,
        Vector2 position,
        Vector2 scatterVelocity,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var entity = world.CreateEntity()
            .Add(new TransformComponent(position))
            .Add(new ItemComponent(definition.Id, definition.Kind, definition.Value, definition.VisualId))
            .Add(new ItemMotionComponent(scatterVelocity));
        telemetry.ItemsSpawned++;
        events.Publish((frame, sequence) => new ItemSpawnedEvent(frame, sequence, entity.Id, definition.Id));
        return entity;
    }
}

public sealed class ItemDropSystem(ItemFactory? itemFactory = null)
{
    private readonly ItemFactory _itemFactory = itemFactory ?? new ItemFactory();

    public void SpawnDrops(
        World world,
        DefinitionCatalog definitions,
        IEnumerable<IGameplayEvent> gameplayEvents,
        IRandomSource random,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        foreach (var destroyed in gameplayEvents.OfType<EnemyDestroyedEvent>().ToArray())
        {
            var enemy = OptionFollowSystem.FindEntity(world, destroyed.EnemyEntityId);
            if (enemy is null || !enemy.TryGet<TransformComponent>(out var transform)) continue;
            SpawnDropTable(
                world,
                definitions,
                definitions.GetEnemy(destroyed.EnemyDefinitionId).DropTable,
                transform.Position,
                random,
                telemetry,
                events);
        }
    }

    public void SpawnDropTable(
        World world,
        DefinitionCatalog definitions,
        IReadOnlyList<DropEntryDefinition> dropTable,
        Vector2 position,
        IRandomSource random,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        foreach (var drop in dropTable)
        {
            for (var index = 0; index < drop.Count; index++)
            {
                if (random.NextSingle() >= drop.Chance) continue;
                var angle = random.NextSingle() * MathF.Tau;
                var speed = drop.ScatterSpeed * (0.5f + (random.NextSingle() * 0.5f));
                _itemFactory.Create(
                    world,
                    definitions.GetItem(drop.ItemId),
                    position,
                    new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                    telemetry,
                    events);
            }
        }
    }

    public void SpawnProjectileCancelDrops(
        World world,
        DefinitionCatalog definitions,
        IEnumerable<IGameplayEvent> gameplayEvents,
        string? itemDefinitionId,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        if (string.IsNullOrWhiteSpace(itemDefinitionId)) return;
        var item = definitions.GetItem(itemDefinitionId);
        foreach (var cancelled in gameplayEvents.OfType<ProjectileCancelledEvent>().ToArray())
        {
            if (cancelled.Source != "special-gauge" || cancelled.X is not { } x || cancelled.Y is not { } y)
                continue;
            _itemFactory.Create(world, item, new Vector2(x, y), Vector2.Zero, telemetry, events);
        }
    }
}

public sealed class ItemSystem
{
    public void Update(
        World world,
        RuleSetDefinition rules,
        float deltaTime,
        float playfieldHeight,
        RunState runState,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var player = world.Query<PlayerComponent, ShipComponent, TransformComponent>()
            .FirstOrDefault(static entity =>
                !entity.Has<PendingDestroyComponent>() &&
                (!entity.TryGet<PlayerLifeCycleComponent>(out var lifeCycle) || lifeCycle.CanAct));
        if (player is null) return;
        var playerPosition = player.Get<TransformComponent>().Position;
        var ship = player.Get<ShipComponent>();
        var collectAll = playerPosition.Y <= rules.CollectionLineY;
        foreach (var itemEntity in world.Query<ItemComponent, ItemMotionComponent, TransformComponent>().ToArray())
        {
            if (itemEntity.Has<PendingDestroyComponent>()) continue;
            var transform = itemEntity.Get<TransformComponent>();
            var motion = itemEntity.Get<ItemMotionComponent>();
            var difference = playerPosition - transform.Position;
            var magnetized = collectAll ||
                ship.IsFocused && difference.LengthSquared() <= rules.FocusMagnetRadius * rules.FocusMagnetRadius;
            if (magnetized) motion.State = ItemMotionState.Magnetized;

            switch (motion.State)
            {
                case ItemMotionState.Scatter:
                    transform.Position += motion.Velocity * deltaTime;
                    motion.Velocity = new Vector2(
                        MoveTowards(motion.Velocity.X, 0, 180 * deltaTime),
                        MoveTowards(motion.Velocity.Y, rules.ItemFallSpeed, 240 * deltaTime));
                    if (Math.Abs(motion.Velocity.X) < 0.01f && motion.Velocity.Y >= rules.ItemFallSpeed)
                    {
                        motion.State = ItemMotionState.Falling;
                    }

                    break;
                case ItemMotionState.Falling:
                    transform.Position += new Vector2(0, rules.ItemFallSpeed * deltaTime);
                    break;
                case ItemMotionState.Magnetized:
                    if (difference != Vector2.Zero)
                    {
                        var movement = Vector2.Normalize(difference) * rules.ItemMagnetSpeed * deltaTime;
                        transform.Position += movement.LengthSquared() >= difference.LengthSquared() ? difference : movement;
                    }

                    break;
            }

            difference = playerPosition - transform.Position;
            if (difference.LengthSquared() <= rules.ItemCollectionRadius * rules.ItemCollectionRadius)
            {
                Collect(player, itemEntity, rules, runState, telemetry, events, collectAll);
            }
            else if (transform.Position.Y > playfieldHeight + 32)
            {
                itemEntity.Add(new PendingDestroyComponent());
            }
        }
    }

    private static void Collect(
        Entity player,
        Entity itemEntity,
        RuleSetDefinition rules,
        RunState runState,
        SimulationTelemetry telemetry,
        GameEventBuffer events,
        bool collectedAboveLine)
    {
        var item = itemEntity.Get<ItemComponent>();
        var ship = player.Get<ShipComponent>();
        var scoreValue = 0;
        switch (item.Kind)
        {
            case "power":
                if (ship.Power >= ship.MaximumPower)
                {
                    scoreValue = rules.MaximumPowerItemScoreValue;
                }
                else
                {
                    var previous = ship.Power;
                    ship.Power = Math.Min(ship.MaximumPower, ship.Power + item.Value);
                    runState.Power = ship.Power;
                    events.Publish((frame, sequence) => new PowerChangedEvent(
                        frame, sequence, player.Id, previous, ship.Power, "item"));
                }

                break;
            case "score":
                scoreValue = item.Value;
                break;
            case "bomb":
                var bombs = player.Get<BombComponent>();
                bombs.Remaining = Math.Min(ship.MaximumBombs, bombs.Remaining + item.Value);
                break;
            case "life":
                var lives = player.Get<LivesComponent>();
                if (lives.Remaining < ship.MaximumLives)
                {
                    lives.Remaining = Math.Min(ship.MaximumLives, lives.Remaining + item.Value);
                    telemetry.ExtendsAwarded++;
                    events.Publish((frame, sequence) => new ExtendAwardedEvent(
                        frame, sequence, player.Id, "item"));
                }

                break;
            case "gauge":
                runState.Gauge = Math.Min(rules.MaximumGauge, runState.Gauge + item.Value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported item kind '{item.Kind}'.");
        }

        telemetry.ItemsCollected++;
        events.Publish((frame, sequence) => new ItemCollectedEvent(
            frame, sequence, player.Id, item.DefinitionId, item.Value, item.Kind, scoreValue, collectedAboveLine));
        itemEntity.Add(new PendingDestroyComponent());
    }

    private static float MoveTowards(float current, float target, float maximumDelta)
    {
        var difference = target - current;
        return Math.Abs(difference) <= maximumDelta
            ? target
            : current + (Math.Sign(difference) * maximumDelta);
    }
}

public sealed class ExtendSystem
{
    public void Update(
        Entity player,
        RuleSetDefinition rules,
        RunState runState,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var lives = player.Get<LivesComponent>();
        var maximumLives = player.Get<ShipComponent>().MaximumLives;
        foreach (var threshold in rules.ExtendScoreThresholds)
        {
            if (runState.Score < threshold || !runState.ClaimExtend(threshold)) continue;
            if (lives.Remaining < maximumLives) lives.Remaining++;
            telemetry.ExtendsAwarded++;
            events.Publish((frame, sequence) => new ExtendAwardedEvent(
                frame, sequence, player.Id, "score", threshold));
        }
    }
}
