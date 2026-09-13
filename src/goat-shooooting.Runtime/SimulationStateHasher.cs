using GoatShooooting.Core;

namespace GoatShooooting.Runtime;

internal static class SimulationStateHasher
{
    public static ulong Compute(ShootingSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var hash = new StableHash();
        hash.Add(simulation.Configuration.GameId);
        hash.Add(simulation.Configuration.RuleSetId);
        hash.Add(simulation.Configuration.DifficultyId);
        hash.Add(simulation.Configuration.ShipId);
        hash.Add(simulation.Configuration.StartStageId);
        hash.Add(simulation.Configuration.CheckpointId);
        hash.Add(simulation.Configuration.IsPractice);
        hash.Add(simulation.Configuration.InitialPower ?? -1);
        hash.Add(simulation.Configuration.InitialLives ?? -1);
        hash.Add(simulation.Configuration.InitialBombs ?? -1);
        hash.Add(simulation.Configuration.InitialRank ?? -1);
        hash.Add(simulation.Configuration.InitialGauge ?? -1);
        hash.Add(simulation.Configuration.InitialInvincibilitySeconds ?? -1);
        hash.Add(simulation.Configuration.SlowPractice);
        hash.Add(simulation.Configuration.ShowHitboxes);
        hash.Add(simulation.Configuration.Seed);
        hash.Add(simulation.RandomState);
        hash.Add(simulation.RunState.Frame);
        hash.Add(simulation.RunState.Score);
        hash.Add(simulation.RunState.Power);
        hash.Add(simulation.RunState.Gauge);
        hash.Add(simulation.RunState.SpecialGaugeValue);
        hash.Add(simulation.RunState.Rank);
        hash.Add((int)simulation.RunState.SpecialPhase);
        hash.Add(simulation.RunState.SpecialLevel);
        hash.Add(simulation.RunState.SpecialTimeRemaining);
        hash.Add(simulation.RunState.SpecialCooldownRemaining);
        hash.Add(simulation.RunState.SpecialScoreMultiplier);
        hash.Add(simulation.RunState.CreditsRemaining);
        hash.Add(simulation.RunState.ContinuesUsed);
        hash.Add(simulation.RunState.Continued);
        hash.Add(simulation.RunState.Chain);
        hash.Add(simulation.RunState.MaximumChain);
        hash.Add(simulation.RunState.HitCombo);
        hash.Add(simulation.RunState.ConsecutiveItems);
        hash.Add(simulation.RunState.Multiplier);
        hash.Add(simulation.RunState.LastKillFrame);
        hash.Add(simulation.RunState.LastHitFrame);
        hash.Add(simulation.RunState.LastItemFrame);
        hash.Add(simulation.RunState.ScoreBreakdown.Count);
        foreach (var entry in simulation.RunState.ScoreBreakdown.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            hash.Add(entry.Key);
            hash.Add(entry.Value);
        }
        hash.Add(simulation.RunState.ClaimedExtendThresholds.Count);
        foreach (var threshold in simulation.RunState.ClaimedExtendThresholds.Order()) hash.Add(threshold);
        hash.Add((int)simulation.Status);
        hash.Add((int)simulation.Phase);
        hash.Add(simulation.StageNumber);
        hash.Add(simulation.CurrentStage.Id);
        hash.Add(simulation.CurrentShip.Id);
        hash.Add(simulation.CurrentRuleSet.Id);
        hash.Add(simulation.CurrentDifficulty?.Id);
        hash.Add(simulation.Elapsed);
        hash.Add(simulation.PhaseElapsed);
        AddTelemetry(hash, simulation.Telemetry);

        foreach (var entity in simulation.World.Entities.OrderBy(static entity => entity.Id))
        {
            hash.Add(entity.Id);
            AddComponent(hash, entity.TryGet<TransformComponent>(out var transform), () =>
            {
                hash.Add(transform.Position.X);
                hash.Add(transform.Position.Y);
            });
            AddComponent(hash, entity.TryGet<VelocityComponent>(out var velocity), () =>
            {
                hash.Add(velocity.Value.X);
                hash.Add(velocity.Value.Y);
            });
            AddComponent(hash, entity.TryGet<HealthComponent>(out var health), () =>
            {
                hash.Add(health.Maximum);
                hash.Add(health.Current);
            });
            AddComponent(hash, entity.TryGet<LivesComponent>(out var lives), () =>
            {
                hash.Add(lives.Initial);
                hash.Add(lives.Remaining);
            });
            AddComponent(hash, entity.TryGet<BombComponent>(out var bombs), () =>
            {
                hash.Add(bombs.Initial);
                hash.Add(bombs.Remaining);
                hash.Add(bombs.Damage);
            });
            AddComponent(hash, entity.TryGet<DamageComponent>(out var damage), () =>
                hash.Add(damage.Value));
            AddComponent(hash, entity.TryGet<ColliderComponent>(out var collider), () =>
            {
                hash.Add(collider.Radius);
                hash.Add((int)collider.Layer);
            });
            AddComponent(hash, entity.TryGet<GrazeRadiusComponent>(out var graze), () =>
                hash.Add(graze.Radius));
            AddComponent(hash, entity.TryGet<PlayerComponent>(out var player), () =>
            {
                hash.Add(player.DefinitionId);
                hash.Add(player.Speed);
            });
            AddComponent(hash, entity.TryGet<ShipComponent>(out var ship), () =>
            {
                hash.Add(ship.DefinitionId);
                hash.Add(ship.NormalSpeed);
                hash.Add(ship.FocusSpeed);
                hash.Add(ship.HitRadius);
                hash.Add(ship.GrazeRadius);
                hash.Add(ship.Power);
                hash.Add(ship.MaximumPower);
                hash.Add(ship.MaximumLives);
                hash.Add(ship.MaximumBombs);
                AddStrings(hash, ship.NormalWeaponIds);
                AddStrings(hash, ship.FocusWeaponIds);
                hash.Add(ship.BombWeaponId);
                hash.Add(ship.SpecialWeaponId);
                hash.Add(ship.VisualId);
                hash.Add(ship.IsFocused);
            });
            AddComponent(hash, entity.TryGet<PlayerLifeCycleComponent>(out var lifeCycle), () =>
            {
                hash.Add((int)lifeCycle.State);
                hash.Add(lifeCycle.Timer);
                hash.Add(lifeCycle.RespawnPosition.X);
                hash.Add(lifeCycle.RespawnPosition.Y);
                hash.Add(lifeCycle.DeathAnimationSeconds);
                hash.Add(lifeCycle.RespawnDelaySeconds);
                hash.Add(lifeCycle.RespawnInvincibilitySeconds);
                hash.Add(lifeCycle.PowerLossOnDeath);
                hash.Add(lifeCycle.BombsAfterRespawn);
                hash.Add(lifeCycle.HitSourceEntityId ?? 0);
            });
            AddComponent(hash, entity.TryGet<ItemComponent>(out var item), () =>
            {
                hash.Add(item.DefinitionId);
                hash.Add(item.Kind);
                hash.Add(item.Value);
                hash.Add(item.VisualId);
            });
            AddComponent(hash, entity.TryGet<ItemMotionComponent>(out var itemMotion), () =>
            {
                hash.Add((int)itemMotion.State);
                hash.Add(itemMotion.Velocity.X);
                hash.Add(itemMotion.Velocity.Y);
            });
            AddComponent(hash, entity.TryGet<OptionUnitComponent>(out var option), () =>
            {
                hash.Add(option.OwnerEntityId);
                hash.Add(option.DefinitionId);
                hash.Add(option.Offset.X);
                hash.Add(option.Offset.Y);
                hash.Add(option.FollowSpeed);
                hash.Add(option.Radius);
                AddStrings(hash, option.NormalWeaponIds);
                AddStrings(hash, option.FocusWeaponIds);
                hash.Add(option.VisualId);
            });
            AddComponent(hash, entity.TryGet<WeaponRuntimeComponent>(out var weaponRuntime), () =>
            {
                foreach (var pair in weaponRuntime.States.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    hash.Add(pair.Key);
                    hash.Add(pair.Value.CooldownRemaining);
                    hash.Add(pair.Value.BurstShotsRemaining);
                    hash.Add(pair.Value.BurstCooldownRemaining);
                    hash.Add(pair.Value.PatternAngleDegrees);
                    hash.Add(pair.Value.PatternDirection);
                    hash.Add(pair.Value.ShotsSinceDirectionChange);
                    hash.Add(pair.Value.WasHeld);
                    hash.Add(pair.Value.ActiveLaserEntityId);
                    hash.Add(pair.Value.LockedTargetEntityIds.Count);
                    foreach (var targetId in pair.Value.LockedTargetEntityIds) hash.Add(targetId);
                }
            });
            AddComponent(hash, entity.TryGet<LaserComponent>(out var laser), () =>
            {
                hash.Add(laser.OwnerEntityId);
                hash.Add((int)laser.OwnerLayer);
                hash.Add(laser.Direction.X);
                hash.Add(laser.Direction.Y);
                hash.Add(laser.Length);
                hash.Add(laser.Width);
                hash.Add(laser.Damage);
                hash.Add(laser.DamageInterval);
                hash.Add(laser.VisualId);
                hash.Add(laser.ProjectileInteraction);
                hash.Add(laser.DamageCooldownRemaining);
            });
            AddComponent(hash, entity.TryGet<EnemyComponent>(out var enemy), () =>
                hash.Add(enemy.DefinitionId));
            AddComponent(hash, entity.TryGet<ScoreValueComponent>(out var scoreValue), () =>
                hash.Add(scoreValue.Value));
            AddComponent(hash, entity.TryGet<BulletComponent>(out var bullet), () =>
            {
                hash.Add(bullet.DefinitionId);
                hash.Add((int)bullet.TargetLayer);
            });
            AddComponent(hash, entity.TryGet<WeaponHolderComponent>(out var weapon), () =>
            {
                hash.Add(weapon.WeaponId);
                hash.Add(weapon.CooldownRemaining);
                hash.Add(weapon.PatternAngleDegrees);
                hash.Add(weapon.PatternDirection);
                hash.Add(weapon.ShotsSinceDirectionChange);
            });
            AddComponent(hash, entity.TryGet<MotionTimelineComponent>(out var motionTimeline), () =>
            {
                hash.Add(motionTimeline.PatternId);
                hash.Add(motionTimeline.CommandIndex);
                hash.Add(motionTimeline.Elapsed);
                hash.Add(motionTimeline.Start.X);
                hash.Add(motionTimeline.Start.Y);
                hash.Add(motionTimeline.PlayerSnapshot.X);
                hash.Add(motionTimeline.PlayerSnapshot.Y);
                hash.Add(motionTimeline.CommandStarted);
            });
            AddComponent(hash, entity.TryGet<AttackTimelineComponent>(out var attackTimeline), () =>
            {
                hash.Add(attackTimeline.Initialized);
                hash.Add(attackTimeline.PatternIds.Count);
                foreach (var patternId in attackTimeline.PatternIds) hash.Add(patternId);
                hash.Add(attackTimeline.Tracks.Count);
                foreach (var track in attackTimeline.Tracks)
                {
                    hash.Add(track.PatternId);
                    hash.Add(track.CommandIndex);
                    hash.Add(track.WaitRemaining);
                }
            });
            AddComponent(hash, entity.TryGet<LifetimeComponent>(out var lifetime), () =>
                hash.Add(lifetime.Remaining));
            AddComponent(hash, entity.TryGet<HomingMovementComponent>(out var homing), () =>
                hash.Add(homing.TurnRadiansPerSecond));
            AddComponent(hash, entity.TryGet<InvincibilityComponent>(out var invincibility), () =>
            {
                hash.Add(invincibility.Duration);
                hash.Add(invincibility.Remaining);
            });
            AddComponent(hash, entity.TryGet<SineMovementComponent>(out var sine), () =>
            {
                hash.Add(sine.OriginX);
                hash.Add(sine.Amplitude);
                hash.Add(sine.Frequency);
                hash.Add(sine.Elapsed);
            });
            AddComponent(hash, entity.TryGet<ZigzagMovementComponent>(out var zigzag), () =>
            {
                hash.Add(zigzag.OriginX);
                hash.Add(zigzag.Amplitude);
                hash.Add(zigzag.Frequency);
                hash.Add(zigzag.Elapsed);
            });
            AddComponent(hash, entity.TryGet<HitFlashComponent>(out var hitFlash), () =>
                hash.Add(hitFlash.Remaining));
            AddComponent(hash, entity.TryGet<ExplosionComponent>(out var explosion), () =>
            {
                hash.Add(explosion.MaxRadius);
                hash.Add(explosion.Duration);
                hash.Add(explosion.Remaining);
            });
            AddComponent(hash, entity.TryGet<BossComponent>(out var boss), () =>
            {
                hash.Add(boss.DefinitionId);
                hash.Add(boss.DisplayName);
                hash.Add(boss.PhaseIndex);
                hash.Add(boss.PhaseId);
                hash.Add(boss.PhaseDisplayName);
                hash.Add(boss.PhaseElapsed);
                hash.Add(boss.PhaseTimeLimit);
                hash.Add(boss.WarningSeconds);
                hash.Add(boss.CheckpointId);
                hash.Add(boss.PlayerDeathsAtPhaseStart);
                hash.Add(boss.BombsUsedAtPhaseStart);
                hash.Add(boss.IsInitialized);
                hash.Add(boss.IsComplete);
            });
            hash.Add(entity.Has<PendingDestroyComponent>());
        }

        hash.Add(simulation.Projectiles.ActiveCount);
        for (var index = 0; index < simulation.Projectiles.ActiveCount; index++)
        {
            var projectile = simulation.Projectiles.GetSnapshot(index);
            hash.Add(projectile.Id);
            hash.Add(projectile.OwnerEntityId);
            hash.Add((int)projectile.Team);
            hash.Add(projectile.DefinitionId);
            hash.Add(projectile.PreviousPosition.X);
            hash.Add(projectile.PreviousPosition.Y);
            hash.Add(projectile.Position.X);
            hash.Add(projectile.Position.Y);
            hash.Add(projectile.Velocity.X);
            hash.Add(projectile.Velocity.Y);
            hash.Add(projectile.HitRadius);
            hash.Add(projectile.Damage);
            hash.Add(projectile.Age);
            hash.Add(projectile.Lifetime);
            hash.Add(projectile.VisualId);
            hash.Add((int)projectile.Behavior);
            hash.Add(projectile.CanDamage);
            hash.Add(projectile.CanBeCancelled);
            hash.Add((int)projectile.CancelResistance);
            hash.Add(projectile.PierceCount);
            hash.Add((int)projectile.DamageType);
            hash.Add((int)projectile.ClearBehavior);
            hash.Add(projectile.AccelerationPerSecond);
            hash.Add(projectile.GrazedPlayerEntityId);
            hash.Add(projectile.PendingRemoval);
        }

        hash.Add(simulation.CompletedBossIds.Count);
        foreach (var bossId in simulation.CompletedBossIds.OrderBy(static value => value, StringComparer.Ordinal))
        {
            hash.Add(bossId);
        }

        return hash.Value;
    }

    private static void AddTelemetry(StableHash hash, SimulationTelemetry telemetry)
    {
        hash.Add(telemetry.EnemiesSpawned);
        hash.Add(telemetry.EnemyMovementFrames);
        hash.Add(telemetry.BulletsSpawned);
        hash.Add(telemetry.EnemyBulletsSpawned);
        hash.Add(telemetry.BulletMovementFrames);
        hash.Add(telemetry.CollisionsDetected);
        hash.Add(telemetry.CollisionCandidatesChecked);
        hash.Add(telemetry.PlayerGrazes);
        hash.Add(telemetry.DamageEventsApplied);
        hash.Add(telemetry.PlayerDamageEventsApplied);
        hash.Add(telemetry.BombsUsed);
        hash.Add(telemetry.AutoBombsUsed);
        hash.Add(telemetry.EnemyBulletsCleared);
        hash.Add(telemetry.EnemiesKilled);
        hash.Add(telemetry.BossesKilled);
        hash.Add(telemetry.Score);
        hash.Add(telemetry.ItemsSpawned);
        hash.Add(telemetry.ItemsCollected);
        hash.Add(telemetry.PlayerDeaths);
        hash.Add(telemetry.PlayerRespawns);
        hash.Add(telemetry.ExtendsAwarded);
        hash.Add(telemetry.ContinuesUsed);
    }

    private static void AddComponent(StableHash hash, bool isPresent, Action addValues)
    {
        hash.Add(isPresent);
        if (isPresent)
        {
            addValues();
        }
    }

    private static void AddStrings(StableHash hash, IReadOnlyList<string> values)
    {
        hash.Add(values.Count);
        foreach (var value in values) hash.Add(value);
    }

    private sealed class StableHash
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _value;

        public ulong Value => _value == 0 ? OffsetBasis : _value;

        public void Add(bool value) => Add(value ? 1 : 0);
        public void Add(int value) => Add(unchecked((ulong)(uint)value));
        public void Add(long value) => Add(unchecked((ulong)value));
        public void Add(float value) => Add(BitConverter.SingleToUInt32Bits(value));
        public void Add(double value) => Add(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

        public void Add(string? value)
        {
            if (value is null)
            {
                Add(ulong.MaxValue);
                return;
            }

            Add(value.Length);
            foreach (var character in value)
            {
                Add(character);
            }
        }

        public void Add(ulong value)
        {
            if (_value == 0)
            {
                _value = OffsetBasis;
            }

            for (var shift = 0; shift < 64; shift += 8)
            {
                _value = unchecked((_value ^ (byte)(value >> shift)) * Prime);
            }
        }
    }
}
