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
        hash.Add(simulation.Configuration.Seed);
        hash.Add(simulation.RandomState);
        hash.Add(simulation.RunState.Frame);
        hash.Add(simulation.RunState.Score);
        hash.Add((int)simulation.Status);
        hash.Add((int)simulation.Phase);
        hash.Add(simulation.StageNumber);
        hash.Add(simulation.CurrentStage.Id);
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
            AddComponent(hash, entity.TryGet<PlayerComponent>(out var player), () =>
            {
                hash.Add(player.DefinitionId);
                hash.Add(player.Speed);
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
            hash.Add(entity.Has<BossComponent>());
            hash.Add(entity.Has<PendingDestroyComponent>());
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
        hash.Add(telemetry.DamageEventsApplied);
        hash.Add(telemetry.PlayerDamageEventsApplied);
        hash.Add(telemetry.BombsUsed);
        hash.Add(telemetry.EnemyBulletsCleared);
        hash.Add(telemetry.EnemiesKilled);
        hash.Add(telemetry.BossesKilled);
        hash.Add(telemetry.Score);
    }

    private static void AddComponent(StableHash hash, bool isPresent, Action addValues)
    {
        hash.Add(isPresent);
        if (isPresent)
        {
            addValues();
        }
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
