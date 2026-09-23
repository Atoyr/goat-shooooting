using System.Collections.ObjectModel;
using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public readonly record struct InteractionHandle(int Value) : IDefinitionHandle;
public readonly record struct TagHandle(int Value);

[Flags]
public enum InteractionActions
{
    None = 0,
    DestroySource = 1 << 0,
    DestroyTarget = 1 << 1,
    ConvertTarget = 1 << 2,
    EmitProjectileInteraction = 1 << 3,
    EmitProjectileCancelled = 1 << 4,
    EmitLaserContact = 1 << 5,
    ReflectTarget = 1 << 6
}

public enum InteractionTeam : byte { Any, Player, Enemy }

public sealed class CompiledTagRegistry
{
    private readonly IReadOnlyDictionary<string, TagHandle> _handles;
    private readonly IReadOnlyList<string> _ids;

    internal CompiledTagRegistry(IEnumerable<string> tags)
    {
        var ids = tags.Concat(new[] { "projectile", "laser", "shot", "bullet", "hyper" })
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (ids.Length > 64) throw new DefinitionValidationException("Interaction tag registry exceeds 64 tags.");
        _ids = Array.AsReadOnly(ids);
        _handles = new ReadOnlyDictionary<string, TagHandle>(ids.Select((id, index) => (id, index))
            .ToDictionary(static value => value.id, static value => new TagHandle(value.index), StringComparer.Ordinal));
    }

    public IReadOnlyList<string> Ids => _ids;
    public TagHandle Resolve(string id) => _handles.TryGetValue(id, out var handle) ? handle :
        throw new DefinitionValidationException($"Unknown compiled interaction tag '{id}'.");
    public ulong Mask(IEnumerable<string> tags)
    {
        ulong mask = 0;
        foreach (var tag in tags) mask |= 1UL << Resolve(tag).Value;
        return mask;
    }
}

public sealed record CompiledInteractionFilter(
    InteractionTeam Team,
    ulong RequiredTags,
    ulong ExcludedTags,
    int MinimumPower,
    int MaximumResistance);

public sealed record CompiledInteractionProfile(
    InteractionHandle Handle,
    string Id,
    int Priority,
    string ShapeTest,
    CompiledInteractionFilter Source,
    CompiledInteractionFilter Target,
    InteractionActions Actions,
    ProjectileHandle? ConvertProjectileHandle,
    bool CompatibilityAdapter = false);

internal static class InteractionCompiler
{
    public static IReadOnlyList<CompiledInteractionProfile> Compile(
        DefinitionCatalog definitions,
        CompiledTagRegistry tags,
        IReadOnlyDictionary<string, ProjectileHandle> projectileHandles)
    {
        var profiles = definitions.Interactions.Values.Select((definition, index) => Compile(
            definition, new InteractionHandle(index), tags, projectileHandles)).ToList();
        if (definitions.Weapons.Values.Any(static weapon => weapon.Laser?.ProjectileInteraction == "cancel-soft"))
        {
            profiles.Add(new CompiledInteractionProfile(
                new InteractionHandle(profiles.Count),
                "v2-adapter.cancel-soft",
                100,
                "capsule",
                new CompiledInteractionFilter(InteractionTeam.Any, tags.Mask(new[] { "laser" }), 0, 0, int.MaxValue),
                new CompiledInteractionFilter(InteractionTeam.Any, tags.Mask(new[] { "projectile", "bullet" }), 0, 0,
                    (int)ProjectileCancelResistance.Soft),
                InteractionActions.DestroyTarget | InteractionActions.EmitProjectileInteraction |
                    InteractionActions.EmitProjectileCancelled,
                null,
                CompatibilityAdapter: true));
        }
        return Array.AsReadOnly(profiles.OrderByDescending(static profile => profile.Priority)
            .ThenBy(static profile => profile.Id, StringComparer.Ordinal).ToArray());
    }

    private static CompiledInteractionProfile Compile(
        InteractionProfileDefinition definition,
        InteractionHandle handle,
        CompiledTagRegistry tags,
        IReadOnlyDictionary<string, ProjectileHandle> projectileHandles) => new(
            handle,
            definition.Id,
            definition.Priority,
            definition.ShapeTest,
            Filter(definition.Source, tags),
            Filter(definition.Target, tags),
            definition.Actions.Aggregate(InteractionActions.None, static (actions, action) => actions | ParseAction(action)),
            string.IsNullOrWhiteSpace(definition.ConvertProjectileId)
                ? null
                : projectileHandles[definition.ConvertProjectileId],
            false);

    private static CompiledInteractionFilter Filter(InteractionFilterDefinition definition, CompiledTagRegistry tags) => new(
        definition.Team switch
        {
            "any" => InteractionTeam.Any,
            "player" => InteractionTeam.Player,
            "enemy" => InteractionTeam.Enemy,
            _ => throw new DefinitionValidationException($"Unknown interaction team '{definition.Team}'.")
        },
        tags.Mask(definition.RequiredTags),
        tags.Mask(definition.ExcludedTags),
        definition.MinimumPower,
        definition.MaximumResistance);

    private static InteractionActions ParseAction(string action) => action switch
    {
        "destroy-source" => InteractionActions.DestroySource,
        "destroy-target" => InteractionActions.DestroyTarget,
        "convert-target" => InteractionActions.ConvertTarget,
        "reflect-target" => InteractionActions.ReflectTarget,
        "emit-projectile-interaction" => InteractionActions.EmitProjectileInteraction,
        "emit-projectile-cancelled" => InteractionActions.EmitProjectileCancelled,
        "emit-laser-contact" => InteractionActions.EmitLaserContact,
        _ => throw new DefinitionValidationException($"Unknown interaction action '{action}'.")
    };
}

public sealed class InteractionSystem
{
    private const float CellSize = 64;
    private readonly Dictionary<(int X, int Y), List<int>> _projectileCells = new();
    private readonly List<Entity> _lasers = new();
    private float _maximumProjectileRadius;
    private readonly Dictionary<(int X, int Y), List<int>> _laserCells = new();
    private readonly HashSet<long> _laserPairs = new();

    public void Update(
        World world,
        ProjectileStore projectiles,
        CompiledCatalog definitions,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        if (definitions.Interactions.Count == 0) return;
        BuildProjectileGrid(projectiles);
        ProcessProjectilePairs(projectiles, definitions.Interactions, telemetry, events);
        _lasers.Clear();
        foreach (var entity in world.Entities)
            if (!entity.Has<PendingDestroyComponent>() && entity.Has<LaserComponent>() && entity.Has<TransformComponent>())
                _lasers.Add(entity);
        ProcessLaserProjectiles(projectiles, definitions.Interactions, telemetry, events);
        ProcessLaserPairs(definitions.Interactions, events);
    }

    private void BuildProjectileGrid(ProjectileStore projectiles)
    {
        foreach (var cell in _projectileCells.Values) cell.Clear();
        _maximumProjectileRadius = 0;
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (projectiles.IsPendingRemovalAt(index)) continue;
            var position = projectiles.PositionAt(index);
            var key = ((int)MathF.Floor(position.X / CellSize), (int)MathF.Floor(position.Y / CellSize));
            if (!_projectileCells.TryGetValue(key, out var values))
            {
                values = new List<int>();
                _projectileCells.Add(key, values);
            }
            values.Add(index);
            _maximumProjectileRadius = Math.Max(_maximumProjectileRadius, projectiles.HitRadiusAt(index));
        }
    }

    private void ProcessProjectilePairs(
        ProjectileStore store,
        IReadOnlyList<CompiledInteractionProfile> profiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        for (var source = 0; source < store.ActiveCount; source++)
        {
            if (store.IsPendingRemovalAt(source)) continue;
            var position = store.PositionAt(source);
            var cellX = (int)MathF.Floor(position.X / CellSize);
            var cellY = (int)MathF.Floor(position.Y / CellSize);
            var cellRange = Math.Max(1, (int)MathF.Ceiling(
                (store.HitRadiusAt(source) + _maximumProjectileRadius) / CellSize));
            for (var y = cellY - cellRange; y <= cellY + cellRange; y++)
                for (var x = cellX - cellRange; x <= cellX + cellRange; x++)
                {
                    if (!_projectileCells.TryGetValue((x, y), out var candidates)) continue;
                    foreach (var target in candidates)
                    {
                        if (target <= source || store.IsPendingRemovalAt(target) || store.TeamAt(source) == store.TeamAt(target)) continue;
                        if (!ProjectileCollisionSystem.IntersectsSweptCircle(
                            store.PreviousPositionAt(source), store.PositionAt(source), store.PositionAt(target),
                            store.HitRadiusAt(source) + store.HitRadiusAt(target), out _)) continue;
                        if (TryApplyProjectilePair(store, source, target, profiles, telemetry, events)) break;
                        _ = TryApplyProjectilePair(store, target, source, profiles, telemetry, events);
                    }
                }
        }
    }

    private void ProcessLaserProjectiles(
        ProjectileStore store,
        IReadOnlyList<CompiledInteractionProfile> profiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        foreach (var entity in _lasers)
        {
            var laser = entity.Get<LaserComponent>();
            var start = entity.Get<TransformComponent>().Position;
            var end = start + (Vector2.Normalize(laser.Direction) * laser.Length);
            var extent = laser.Width + _maximumProjectileRadius;
            var minimumX = (int)MathF.Floor((Math.Min(start.X, end.X) - extent) / CellSize);
            var maximumX = (int)MathF.Floor((Math.Max(start.X, end.X) + extent) / CellSize);
            var minimumY = (int)MathF.Floor((Math.Min(start.Y, end.Y) - extent) / CellSize);
            var maximumY = (int)MathF.Floor((Math.Max(start.Y, end.Y) + extent) / CellSize);
            for (var y = minimumY; y <= maximumY; y++)
                for (var x = minimumX; x <= maximumX; x++)
                {
                    if (!_projectileCells.TryGetValue((x, y), out var candidates)) continue;
                    foreach (var index in candidates)
                    {
                        if (store.IsPendingRemovalAt(index) || !ProjectileCollisionSystem.IntersectsSweptCircle(
                            start, end, store.PositionAt(index), laser.Width + store.HitRadiusAt(index), out _)) continue;
                        var applied = false;
                        foreach (var profile in profiles)
                        {
                            if (profile.CompatibilityAdapter && !store.CanBeCancelledAt(index)) continue;
                            if (!Matches(profile.Source, laser.OwnerLayer, laser.TagMask, laser.InteractionPower, laser.InteractionResistance) ||
                                !Matches(profile.Target, store.TeamAt(index), store.TagMaskAt(index), store.InteractionPowerAt(index),
                                    store.InteractionResistanceAt(index)) || !Opposing(laser.OwnerLayer, store.TeamAt(index))) continue;
                            ApplyLaserProjectile(profile, entity, store, index, telemetry, events);
                            applied = true;
                            break;
                        }
                        if (!applied) _ = TryApplyProjectileLaser(entity, store, index, profiles, events);
                    }
                }
        }
    }

    private void ProcessLaserPairs(IReadOnlyList<CompiledInteractionProfile> profiles, GameEventBuffer events)
    {
        foreach (var cell in _laserCells.Values) cell.Clear();
        _laserPairs.Clear();
        for (var index = 0; index < _lasers.Count; index++)
        {
            var entity = _lasers[index];
            var laser = entity.Get<LaserComponent>();
            var start = entity.Get<TransformComponent>().Position;
            var end = start + (Vector2.Normalize(laser.Direction) * laser.Length);
            var minimumX = (int)MathF.Floor((Math.Min(start.X, end.X) - laser.Width) / CellSize);
            var maximumX = (int)MathF.Floor((Math.Max(start.X, end.X) + laser.Width) / CellSize);
            var minimumY = (int)MathF.Floor((Math.Min(start.Y, end.Y) - laser.Width) / CellSize);
            var maximumY = (int)MathF.Floor((Math.Max(start.Y, end.Y) + laser.Width) / CellSize);
            for (var y = minimumY; y <= maximumY; y++)
                for (var x = minimumX; x <= maximumX; x++)
                {
                    if (!_laserCells.TryGetValue((x, y), out var values))
                    {
                        values = new List<int>();
                        _laserCells.Add((x, y), values);
                    }
                    values.Add(index);
                }
        }

        foreach (var candidates in _laserCells.Values)
            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                for (var otherIndex = candidateIndex + 1; otherIndex < candidates.Count; otherIndex++)
                {
                    var leftIndex = Math.Min(candidates[candidateIndex], candidates[otherIndex]);
                    var rightIndex = Math.Max(candidates[candidateIndex], candidates[otherIndex]);
                    var pairKey = ((long)leftIndex << 32) | (uint)rightIndex;
                    if (!_laserPairs.Add(pairKey)) continue;
                    var leftEntity = _lasers[leftIndex];
                    var rightEntity = _lasers[rightIndex];
                    var left = leftEntity.Get<LaserComponent>();
                    var right = rightEntity.Get<LaserComponent>();
                    if (left.OwnerLayer == right.OwnerLayer || !LasersIntersect(leftEntity, rightEntity)) continue;
                    if (!TryApplyLaserPair(leftEntity, rightEntity, profiles, events))
                        _ = TryApplyLaserPair(rightEntity, leftEntity, profiles, events);
                }
    }

    private static bool TryApplyLaserPair(
        Entity sourceEntity,
        Entity targetEntity,
        IReadOnlyList<CompiledInteractionProfile> profiles,
        GameEventBuffer events)
    {
        var source = sourceEntity.Get<LaserComponent>();
        var target = targetEntity.Get<LaserComponent>();
        foreach (var profile in profiles)
        {
            if (!Matches(profile.Source, source.OwnerLayer, source.TagMask, source.InteractionPower, source.InteractionResistance) ||
                !Matches(profile.Target, target.OwnerLayer, target.TagMask, target.InteractionPower, target.InteractionResistance)) continue;
            if (profile.Actions.HasFlag(InteractionActions.DestroySource)) sourceEntity.Add(new PendingDestroyComponent());
            if (profile.Actions.HasFlag(InteractionActions.DestroyTarget)) targetEntity.Add(new PendingDestroyComponent());
            if (profile.Actions.HasFlag(InteractionActions.ReflectTarget)) ReflectLaser(target, source.OwnerLayer);
            if (profile.Actions.HasFlag(InteractionActions.EmitLaserContact))
                events.Publish((frame, sequence) => new LaserContactEvent(
                    frame, sequence, sourceEntity.Id, targetEntity.Id, profile.Id));
            return true;
        }
        return false;
    }

    private static bool TryApplyProjectilePair(
        ProjectileStore store,
        int source,
        int target,
        IReadOnlyList<CompiledInteractionProfile> profiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        foreach (var profile in profiles)
        {
            if (!Matches(profile.Source, store.TeamAt(source), store.TagMaskAt(source), store.InteractionPowerAt(source),
                    store.InteractionResistanceAt(source)) ||
                !Matches(profile.Target, store.TeamAt(target), store.TagMaskAt(target), store.InteractionPowerAt(target),
                    store.InteractionResistanceAt(target))) continue;
            var sourceId = store.IdAt(source);
            var targetId = store.IdAt(target);
            if (profile.Actions.HasFlag(InteractionActions.DestroySource)) store.QueueRemoveAt(source);
            ApplyProjectileTarget(profile, store, target, telemetry, events);
            if (profile.Actions.HasFlag(InteractionActions.EmitProjectileInteraction))
                events.Publish((frame, sequence) => new ProjectileInteractionEvent(
                    frame, sequence, "projectile", sourceId, "projectile", targetId, profile.Id));
            return true;
        }
        return false;
    }

    private static void ApplyLaserProjectile(
        CompiledInteractionProfile profile,
        Entity laser,
        ProjectileStore store,
        int target,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var targetId = store.IdAt(target);
        ApplyProjectileTarget(profile, store, target, telemetry, events);
        if (profile.Actions.HasFlag(InteractionActions.DestroySource)) laser.Add(new PendingDestroyComponent());
        if (profile.Actions.HasFlag(InteractionActions.EmitProjectileInteraction))
            events.Publish((frame, sequence) => new ProjectileInteractionEvent(
                frame, sequence, "laser", laser.Id, "projectile", targetId, profile.Id));
        if (profile.Actions.HasFlag(InteractionActions.EmitLaserContact))
            events.Publish((frame, sequence) => new LaserContactEvent(frame, sequence, laser.Id, targetId, profile.Id));
    }

    private static bool TryApplyProjectileLaser(
        Entity laserEntity,
        ProjectileStore store,
        int sourceIndex,
        IReadOnlyList<CompiledInteractionProfile> profiles,
        GameEventBuffer events)
    {
        var laser = laserEntity.Get<LaserComponent>();
        foreach (var profile in profiles)
        {
            if (!Matches(profile.Source, store.TeamAt(sourceIndex), store.TagMaskAt(sourceIndex),
                    store.InteractionPowerAt(sourceIndex), store.InteractionResistanceAt(sourceIndex)) ||
                !Matches(profile.Target, laser.OwnerLayer, laser.TagMask, laser.InteractionPower,
                    laser.InteractionResistance) || !Opposing(laser.OwnerLayer, store.TeamAt(sourceIndex))) continue;
            var sourceId = store.IdAt(sourceIndex);
            if (profile.Actions.HasFlag(InteractionActions.DestroySource)) store.QueueRemoveAt(sourceIndex);
            if (profile.Actions.HasFlag(InteractionActions.DestroyTarget)) laserEntity.Add(new PendingDestroyComponent());
            if (profile.Actions.HasFlag(InteractionActions.ReflectTarget))
                ReflectLaser(laser, store.TeamAt(sourceIndex) == ProjectileTeam.Player
                    ? CollisionLayer.Player : CollisionLayer.Enemy);
            if (profile.Actions.HasFlag(InteractionActions.EmitProjectileInteraction))
                events.Publish((frame, sequence) => new ProjectileInteractionEvent(
                    frame, sequence, "projectile", sourceId, "laser", laserEntity.Id, profile.Id));
            if (profile.Actions.HasFlag(InteractionActions.EmitLaserContact))
                events.Publish((frame, sequence) => new LaserContactEvent(
                    frame, sequence, sourceId, laserEntity.Id, profile.Id));
            return true;
        }
        return false;
    }

    private static void ApplyProjectileTarget(
        CompiledInteractionProfile profile,
        ProjectileStore store,
        int target,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var targetId = store.IdAt(target);
        var targetPosition = store.PositionAt(target);
        if (profile.Actions.HasFlag(InteractionActions.DestroyTarget))
        {
            store.QueueRemoveAt(target);
            if (store.TeamAt(target) == ProjectileTeam.Enemy) telemetry.EnemyBulletsCleared++;
        }
        if (profile.Actions.HasFlag(InteractionActions.ConvertTarget))
            store.TransformAt(target, profile.ConvertProjectileHandle!.Value);
        if (profile.Actions.HasFlag(InteractionActions.EmitProjectileCancelled))
            events.Publish((frame, sequence) => new ProjectileCancelledEvent(
                frame, sequence, targetId, X: targetPosition.X, Y: targetPosition.Y));
    }

    private static bool Matches(
        CompiledInteractionFilter filter,
        ProjectileTeam team,
        ulong tags,
        int power,
        int resistance) => Matches(filter, team == ProjectileTeam.Player ? CollisionLayer.Player : CollisionLayer.Enemy,
            tags, power, resistance);

    private static bool Matches(
        CompiledInteractionFilter filter,
        CollisionLayer layer,
        ulong tags,
        int power,
        int resistance) =>
        (filter.Team == InteractionTeam.Any || filter.Team == InteractionTeam.Player && layer == CollisionLayer.Player ||
            filter.Team == InteractionTeam.Enemy && layer == CollisionLayer.Enemy) &&
        (tags & filter.RequiredTags) == filter.RequiredTags && (tags & filter.ExcludedTags) == 0 &&
        power >= filter.MinimumPower && resistance <= filter.MaximumResistance;

    private static bool Opposing(CollisionLayer layer, ProjectileTeam team) =>
        layer == CollisionLayer.Player && team == ProjectileTeam.Enemy ||
        layer == CollisionLayer.Enemy && team == ProjectileTeam.Player;

    private static void ReflectLaser(LaserComponent laser, CollisionLayer newOwnerLayer)
    {
        laser.OwnerLayer = newOwnerLayer;
        laser.Direction = -laser.Direction;
        laser.DamageCooldownRemaining = 0;
    }

    private static bool LasersIntersect(Entity leftEntity, Entity rightEntity)
    {
        var left = leftEntity.Get<LaserComponent>();
        var right = rightEntity.Get<LaserComponent>();
        var leftStart = leftEntity.Get<TransformComponent>().Position;
        var leftEnd = leftStart + (Vector2.Normalize(left.Direction) * left.Length);
        var rightStart = rightEntity.Get<TransformComponent>().Position;
        var rightEnd = rightStart + (Vector2.Normalize(right.Direction) * right.Length);
        return SegmentDistanceSquared(leftStart, leftEnd, rightStart, rightEnd) <=
            (left.Width + right.Width) * (left.Width + right.Width);
    }

    private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var first = b - a;
        var second = d - c;
        var denominator = Cross(first, second);
        if (MathF.Abs(denominator) > float.Epsilon)
        {
            var delta = c - a;
            var firstAmount = Cross(delta, second) / denominator;
            var secondAmount = Cross(delta, first) / denominator;
            if (firstAmount is >= 0 and <= 1 && secondAmount is >= 0 and <= 1) return 0;
        }
        return Math.Min(
            Math.Min(DistanceToSegmentSquared(a, c, d), DistanceToSegmentSquared(b, c, d)),
            Math.Min(DistanceToSegmentSquared(c, a, b), DistanceToSegmentSquared(d, a, b)));
    }

    private static float Cross(Vector2 left, Vector2 right) => (left.X * right.Y) - (left.Y * right.X);

    private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var length = segment.LengthSquared();
        var amount = length <= float.Epsilon ? 0 : Math.Clamp(Vector2.Dot(point - start, segment) / length, 0, 1);
        return Vector2.DistanceSquared(point, start + (segment * amount));
    }
}
