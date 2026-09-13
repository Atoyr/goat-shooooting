using System.Diagnostics;
using System.Numerics;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public enum PresentationEffectKind
{
    Muzzle,
    Hit,
    Destroy,
    BulletCancel,
    ItemCollect,
    Bomb,
    Special,
    BossTransition,
    Trail
}

public enum PresentationBlendMode
{
    Alpha,
    Additive
}

public sealed record PresentationSettings
{
    public float ParticleDensity { get; init; } = 1;
    public float FlashIntensity { get; init; } = 1;
    public float ShakeIntensity { get; init; } = 1;
    public bool TrailsEnabled { get; init; } = true;
    public int MaximumEffects { get; init; } = 512;

    public void Validate()
    {
        ValidateUnit(ParticleDensity, nameof(ParticleDensity));
        ValidateUnit(FlashIntensity, nameof(FlashIntensity));
        ValidateUnit(ShakeIntensity, nameof(ShakeIntensity));
        if (MaximumEffects is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(MaximumEffects));
    }

    private static void ValidateUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(name);
    }
}

public struct PresentationEffect
{
    public PresentationEffectKind Kind { get; internal set; }
    public PresentationBlendMode Blend { get; internal set; }
    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    public float Age { get; internal set; }
    public float Lifetime { get; internal set; }
    public float StartRadius { get; internal set; }
    public float EndRadius { get; internal set; }
    public uint Tint { get; internal set; }
    public float Progress => Lifetime <= 0 ? 1 : Math.Clamp(Age / Lifetime, 0, 1);
    public float Radius => StartRadius + ((EndRadius - StartRadius) * Progress);
}

/// <summary>Framework-owned, fixed-capacity presentation state driven only by semantic events and snapshots.</summary>
public sealed class PresentationEffectSystem
{
    private readonly PresentationEffect[] _effects;
    private readonly PresentationSettings _settings;
    private int _activeCount;

    public PresentationEffectSystem(PresentationSettings? settings = null)
    {
        _settings = settings ?? new PresentationSettings();
        _settings.Validate();
        _effects = new PresentationEffect[_settings.MaximumEffects];
    }

    public int ActiveCount => _activeCount;
    public int Capacity => _effects.Length;
    public long DroppedEffects { get; private set; }
    public float ScreenFlash { get; private set; }
    public float CameraShake { get; private set; }
    public float HitStopRemaining { get; private set; }

    public PresentationEffect GetEffect(int index)
    {
        if ((uint)index >= (uint)_activeCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _effects[index];
    }

    public void Reset()
    {
        _activeCount = 0;
        DroppedEffects = 0;
        ScreenFlash = 0;
        CameraShake = 0;
        HitStopRemaining = 0;
    }

    public void ObserveTick(FrameSnapshot snapshot, IReadOnlyList<IGameplayEvent> events)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(events);
        Advance(SimulationTiming.TickDurationSeconds);
        EmitTrails(snapshot);
        for (var index = 0; index < events.Count; index++) EmitEvent(events[index], snapshot);
    }

    private void Advance(float elapsed)
    {
        ScreenFlash = Math.Max(0, ScreenFlash - (elapsed * 4));
        CameraShake = Math.Max(0, CameraShake - (elapsed * 3));
        HitStopRemaining = Math.Max(0, HitStopRemaining - elapsed);
        var index = 0;
        while (index < _activeCount)
        {
            ref var effect = ref _effects[index];
            effect.Age += elapsed;
            effect.Position += effect.Velocity * elapsed;
            if (effect.Age < effect.Lifetime)
            {
                index++;
                continue;
            }

            _activeCount--;
            _effects[index] = _effects[_activeCount];
        }
    }

    private void EmitTrails(FrameSnapshot snapshot)
    {
        if (!_settings.TrailsEnabled || _settings.ParticleDensity <= 0) return;
        var stride = Math.Max(1, (int)MathF.Ceiling(6 / _settings.ParticleDensity));
        var emitted = 0;
        for (var index = 0; index < snapshot.Items.Count && emitted < 32; index++)
        {
            var item = snapshot.Items[index];
            if (item.Kind is not (RenderKind.PlayerBullet or RenderKind.EnemyBullet or RenderKind.Laser) ||
                unchecked((ulong)(item.EntityId + snapshot.Frame)) % (ulong)stride != 0)
                continue;
            Spawn(PresentationEffectKind.Trail, item.Position, Vector2.Zero, 0.12f,
                Math.Max(1, item.Radius), Math.Max(0.5f, item.Radius * 0.25f),
                item.Kind == RenderKind.EnemyBullet ? 0xff7a2e70u : 0x68e8ff60u,
                PresentationBlendMode.Alpha);
            emitted++;
        }
    }

    private void EmitEvent(IGameplayEvent gameplayEvent, FrameSnapshot snapshot)
    {
        var (kind, entityId, count, radius, lifetime, tint, blend) = gameplayEvent switch
        {
            ProjectileSpawnedEvent value =>
                (PresentationEffectKind.Muzzle, value.OwnerEntityId, 2, 3f, 0.12f, 0xffe26fffu, PresentationBlendMode.Additive),
            ProjectileHitEvent value =>
                (PresentationEffectKind.Hit, value.TargetEntityId, 4, 4f, 0.18f, 0xffffb0ffu, PresentationBlendMode.Additive),
            EnemyDamagedEvent value =>
                (PresentationEffectKind.Hit, value.EnemyEntityId, 2, 3f, 0.14f, 0xffffffd0u, PresentationBlendMode.Additive),
            EnemyDestroyedEvent value =>
                (PresentationEffectKind.Destroy, value.EnemyEntityId, 12, 8f, 0.45f, 0xff9b42ffu, PresentationBlendMode.Additive),
            ProjectileCancelledEvent value =>
                (PresentationEffectKind.BulletCancel, value.ProjectileEntityId, 3, 4f, 0.22f, 0x80eaffd0u, PresentationBlendMode.Additive),
            ItemCollectedEvent value =>
                (PresentationEffectKind.ItemCollect, value.PlayerEntityId, 8, 5f, 0.35f, 0x72ff8fffu, PresentationBlendMode.Additive),
            BombUsedEvent value =>
                (PresentationEffectKind.Bomb, value.PlayerEntityId, 24, 12f, 0.65f, 0x66ddffffu, PresentationBlendMode.Additive),
            SpecialActivatedEvent value =>
                (PresentationEffectKind.Special, value.PlayerEntityId, 24, 10f, 0.7f, 0xffdf62ffu, PresentationBlendMode.Additive),
            BossPhaseStartedEvent value =>
                (PresentationEffectKind.BossTransition, value.BossEntityId, 20, 14f, 0.8f, 0xff5b7fffu, PresentationBlendMode.Additive),
            BossPhaseEndedEvent value =>
                (PresentationEffectKind.BossTransition, value.BossEntityId, 20, 14f, 0.8f, 0xffd36fffu, PresentationBlendMode.Additive),
            _ => default
        };
        if (count == 0) return;
        var position = FindPosition(snapshot.Items, entityId);
        EmitBurst(kind, position, count, radius, lifetime, tint, blend, gameplayEvent.Frame, gameplayEvent.Sequence);

        switch (kind)
        {
            case PresentationEffectKind.Hit:
                HitStopRemaining = Math.Max(HitStopRemaining, 0.035f);
                CameraShake = Math.Max(CameraShake, 0.12f * _settings.ShakeIntensity);
                break;
            case PresentationEffectKind.Destroy:
                HitStopRemaining = Math.Max(HitStopRemaining, 0.055f);
                CameraShake = Math.Max(CameraShake, 0.28f * _settings.ShakeIntensity);
                break;
            case PresentationEffectKind.Bomb:
            case PresentationEffectKind.Special:
            case PresentationEffectKind.BossTransition:
                ScreenFlash = Math.Max(ScreenFlash, 0.7f * _settings.FlashIntensity);
                CameraShake = Math.Max(CameraShake, 0.7f * _settings.ShakeIntensity);
                break;
        }
    }

    private void EmitBurst(
        PresentationEffectKind kind,
        Vector2 position,
        int baseCount,
        float radius,
        float lifetime,
        uint tint,
        PresentationBlendMode blend,
        long frame,
        int sequence)
    {
        var count = (int)MathF.Round(baseCount * _settings.ParticleDensity);
        for (var index = 0; index < count; index++)
        {
            var unit = HashUnit(frame, sequence, index);
            var angle = unit * MathF.Tau;
            var speed = radius * (2 + (HashUnit(frame, sequence + 17, index) * 5));
            var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
            Spawn(kind, position, velocity, lifetime, radius * 0.35f, radius, tint, blend);
        }
    }

    private void Spawn(
        PresentationEffectKind kind,
        Vector2 position,
        Vector2 velocity,
        float lifetime,
        float startRadius,
        float endRadius,
        uint tint,
        PresentationBlendMode blend)
    {
        if (_activeCount >= _effects.Length)
        {
            DroppedEffects++;
            return;
        }
        _effects[_activeCount++] = new PresentationEffect
        {
            Kind = kind,
            Blend = blend,
            Position = position,
            Velocity = velocity,
            Lifetime = lifetime,
            StartRadius = startRadius,
            EndRadius = endRadius,
            Tint = tint
        };
    }

    private static Vector2 FindPosition(IReadOnlyList<RenderItem> items, int entityId)
    {
        Vector2? player = null;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].EntityId == entityId) return items[index].Position;
            if (items[index].Kind == RenderKind.Player) player = items[index].Position;
        }
        return player ?? Vector2.Zero;
    }

    private static float HashUnit(long frame, int sequence, int index)
    {
        var value = unchecked((uint)frame * 747796405u + (uint)sequence * 2891336453u + (uint)index * 277803737u);
        value = (value >> ((int)(value >> 28) + 4)) ^ value;
        value *= 277803737u;
        value = (value >> 22) ^ value;
        return value / (float)uint.MaxValue;
    }
}

public sealed record PresentationBenchmarkResult(
    int BulletCount,
    int TickCount,
    int PeakEffects,
    long DroppedEffects,
    double UpdateMilliseconds,
    long AllocatedBytes);

public static class PresentationBenchmark
{
    public static PresentationBenchmarkResult Run(int bulletCount = 10_000, int tickCount = 600)
    {
        if (bulletCount < 1) throw new ArgumentOutOfRangeException(nameof(bulletCount));
        if (tickCount < 0) throw new ArgumentOutOfRangeException(nameof(tickCount));
        var items = new RenderItem[bulletCount];
        for (var index = 0; index < items.Length; index++)
            items[index] = new RenderItem(index + 1, RenderKind.EnemyBullet, new Vector2(index % 100, index / 100), 3, 1);
        var snapshot = new FrameSnapshot(0, items, 0, 0, 1, 0, 100, 0, 100, 0, 1, "benchmark", 3, 3, null);
        var system = new PresentationEffectSystem();
        system.ObserveTick(snapshot, Array.Empty<IGameplayEvent>());
        var peak = system.ActiveCount;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var tick = 0; tick < tickCount; tick++)
        {
            system.ObserveTick(snapshot, Array.Empty<IGameplayEvent>());
            peak = Math.Max(peak, system.ActiveCount);
        }
        var elapsed = Stopwatch.GetTimestamp() - started;
        return new PresentationBenchmarkResult(
            bulletCount,
            tickCount,
            peak,
            system.DroppedEffects,
            Math.Round(elapsed * 1000d / Stopwatch.Frequency, 3),
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }
}
