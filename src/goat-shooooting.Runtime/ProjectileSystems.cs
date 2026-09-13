using System.Numerics;
using GoatShooooting.Core;

namespace GoatShooooting.Runtime;

public sealed class ProjectileMovementSystem
{
    public void Update(
        ProjectileStore projectiles,
        World world,
        float deltaTime,
        float width,
        float height,
        SimulationTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(telemetry);

        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (projectiles.IsPendingRemovalAt(index))
            {
                continue;
            }

            ref var position = ref projectiles.PositionAt(index);
            ref var previousPosition = ref projectiles.PreviousPositionAt(index);
            ref var velocity = ref projectiles.VelocityAt(index);
            previousPosition = position;
            if (projectiles.BehaviorAt(index) == ProjectileBehavior.Homing)
            {
                UpdateHomingVelocity(projectiles, index, world, position, ref velocity, deltaTime);
            }

            position += velocity * deltaTime;
            projectiles.AgeAt(index) += deltaTime;
            if (velocity != Vector2.Zero)
            {
                telemetry.BulletMovementFrames++;
            }

            var radius = projectiles.HitRadiusAt(index);
            if (position.X + radius < 0 || position.X - radius > width ||
                position.Y + radius < 0 || position.Y - radius > height)
            {
                projectiles.QueueRemoveAt(index);
            }
        }
    }

    private static void UpdateHomingVelocity(
        ProjectileStore projectiles,
        int projectileIndex,
        World world,
        Vector2 projectilePosition,
        ref Vector2 velocity,
        float deltaTime)
    {
        Entity? nearest = null;
        var nearestDistanceSquared = float.MaxValue;
        var targetLayer = projectiles.TeamAt(projectileIndex) == ProjectileTeam.Player
            ? CollisionLayer.Enemy
            : CollisionLayer.Player;
        foreach (var entity in world.Entities)
        {
            if (entity.Has<PendingDestroyComponent>() ||
                !entity.TryGet<TransformComponent>(out var transform) ||
                !entity.TryGet<ColliderComponent>(out var collider) ||
                collider.Layer != targetLayer)
            {
                continue;
            }

            var distanceSquared = Vector2.DistanceSquared(projectilePosition, transform.Position);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearest = entity;
                nearestDistanceSquared = distanceSquared;
            }
        }

        if (nearest is null || velocity == Vector2.Zero)
        {
            return;
        }

        var desired = nearest.Get<TransformComponent>().Position - projectilePosition;
        if (desired == Vector2.Zero)
        {
            return;
        }

        var speed = velocity.Length();
        var currentDirection = velocity / speed;
        var desiredDirection = Vector2.Normalize(desired);
        var signedAngle = MathF.Atan2(
            (currentDirection.X * desiredDirection.Y) - (currentDirection.Y * desiredDirection.X),
            Vector2.Dot(currentDirection, desiredDirection));
        var maximumTurn = projectiles.HomingTurnRateAt(projectileIndex) * deltaTime;
        velocity = Rotate(currentDirection, Math.Clamp(signedAngle, -maximumTurn, maximumTurn)) * speed;
    }

    private static Vector2 Rotate(Vector2 vector, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        return new Vector2(
            (vector.X * cosine) - (vector.Y * sine),
            (vector.X * sine) + (vector.Y * cosine));
    }
}

public sealed class ProjectileLifetimeSystem
{
    public void Update(ProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (!projectiles.IsPendingRemovalAt(index) &&
                projectiles.AgeAt(index) >= projectiles.LifetimeAt(index))
            {
                projectiles.QueueRemoveAt(index);
            }
        }
    }
}

public sealed class ActorSpatialGrid
{
    public const float DefaultCellSize = 64;
    private readonly Dictionary<(int X, int Y), List<Entity>> _cells = new();

    public ActorSpatialGrid(float cellSize = DefaultCellSize)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        CellSize = cellSize;
    }

    public float CellSize { get; }
    public float MaximumInteractionRadius { get; private set; }

    public void Rebuild(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var cell in _cells.Values)
        {
            cell.Clear();
        }

        MaximumInteractionRadius = 0;
        foreach (var entity in world.Entities)
        {
            if (entity.Has<PendingDestroyComponent>() ||
                !entity.TryGet<TransformComponent>(out var transform) ||
                !entity.TryGet<ColliderComponent>(out var collider) ||
                collider.Layer is not (CollisionLayer.Player or CollisionLayer.Enemy))
            {
                continue;
            }

            var interactionRadius = entity.TryGet<GrazeRadiusComponent>(out var graze)
                ? Math.Max(collider.Radius, graze.Radius)
                : collider.Radius;
            MaximumInteractionRadius = Math.Max(MaximumInteractionRadius, interactionRadius);
            var key = GetCell(transform.Position);
            if (!_cells.TryGetValue(key, out var entities))
            {
                entities = new List<Entity>();
                _cells.Add(key, entities);
            }

            entities.Add(entity);
        }
    }

    internal IReadOnlyList<Entity>? GetCell(int x, int y) =>
        _cells.TryGetValue((x, y), out var entities) ? entities : null;

    internal int GetCellCoordinate(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

    private (int X, int Y) GetCell(Vector2 position) =>
        (GetCellCoordinate(position.X), GetCellCoordinate(position.Y));
}

public sealed class ProjectileCollisionSystem
{
    private readonly List<DamageEvent> _damageEvents = new();

    public IReadOnlyList<DamageEvent> Detect(
        ProjectileStore projectiles,
        ActorSpatialGrid grid,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(events);
        _damageEvents.Clear();

        for (var projectileIndex = 0; projectileIndex < projectiles.ActiveCount; projectileIndex++)
        {
            if (projectiles.IsPendingRemovalAt(projectileIndex))
            {
                continue;
            }

            DetectProjectile(projectiles, projectileIndex, grid, telemetry, events);
        }

        return _damageEvents;
    }

    private void DetectProjectile(
        ProjectileStore projectiles,
        int projectileIndex,
        ActorSpatialGrid grid,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var previous = projectiles.PreviousPositionAt(projectileIndex);
        var current = projectiles.PositionAt(projectileIndex);
        var projectileRadius = projectiles.HitRadiusAt(projectileIndex);
        var searchRadius = projectileRadius + grid.MaximumInteractionRadius;
        var minX = grid.GetCellCoordinate(Math.Min(previous.X, current.X) - searchRadius);
        var maxX = grid.GetCellCoordinate(Math.Max(previous.X, current.X) + searchRadius);
        var minY = grid.GetCellCoordinate(Math.Min(previous.Y, current.Y) - searchRadius);
        var maxY = grid.GetCellCoordinate(Math.Max(previous.Y, current.Y) + searchRadius);
        var targetLayer = projectiles.TeamAt(projectileIndex) == ProjectileTeam.Player
            ? CollisionLayer.Enemy
            : CollisionLayer.Player;
        if (!projectiles.CanDamageAt(projectileIndex))
        {
            return;
        }

        Entity? nearestHit = null;
        var nearestHitTime = float.MaxValue;
        Entity? grazeTarget = null;

        for (var cellY = minY; cellY <= maxY; cellY++)
        {
            for (var cellX = minX; cellX <= maxX; cellX++)
            {
                var actors = grid.GetCell(cellX, cellY);
                if (actors is null)
                {
                    continue;
                }

                for (var actorIndex = 0; actorIndex < actors.Count; actorIndex++)
                {
                    var actor = actors[actorIndex];
                    var collider = actor.Get<ColliderComponent>();
                    if (collider.Layer != targetLayer || actor.Has<PendingDestroyComponent>())
                    {
                        continue;
                    }

                    telemetry.CollisionCandidatesChecked++;
                    var actorPosition = actor.Get<TransformComponent>().Position;
                    if (IntersectsSweptCircle(
                            previous,
                            current,
                            actorPosition,
                            projectileRadius + collider.Radius,
                            out var hitTime) &&
                        hitTime < nearestHitTime)
                    {
                        nearestHit = actor;
                        nearestHitTime = hitTime;
                        continue;
                    }

                    if (targetLayer == CollisionLayer.Player &&
                        projectiles.GrazedPlayerEntityIdAt(projectileIndex) != actor.Id &&
                        actor.TryGet<GrazeRadiusComponent>(out var graze) &&
                        IntersectsSweptCircle(
                            previous,
                            current,
                            actorPosition,
                            projectileRadius + graze.Radius,
                            out _))
                    {
                        grazeTarget = actor;
                    }
                }
            }
        }

        if (nearestHit is not null)
        {
            var projectileId = projectiles.IdAt(projectileIndex);
            var damage = projectiles.DamageAt(projectileIndex);
            _damageEvents.Add(new DamageEvent(nearestHit, damage, projectileId));
            telemetry.CollisionsDetected++;
            events.Publish((frame, sequence) => new ProjectileHitEvent(
                frame,
                sequence,
                projectileId,
                nearestHit.Id,
                damage));
            ref var pierceCount = ref projectiles.PierceCountAt(projectileIndex);
            if (pierceCount > 0)
            {
                pierceCount--;
            }
            else
            {
                projectiles.QueueRemoveAt(projectileIndex);
            }

            return;
        }

        if (grazeTarget is not null)
        {
            projectiles.GrazedPlayerEntityIdAt(projectileIndex) = grazeTarget.Id;
            telemetry.PlayerGrazes++;
            events.Publish((frame, sequence) => new PlayerGrazedEvent(
                frame,
                sequence,
                grazeTarget.Id,
                projectiles.IdAt(projectileIndex)));
        }
    }

    internal static bool IntersectsSweptCircle(
        Vector2 start,
        Vector2 end,
        Vector2 center,
        float combinedRadius,
        out float closestTime)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        closestTime = lengthSquared <= float.Epsilon
            ? 0
            : Math.Clamp(Vector2.Dot(center - start, segment) / lengthSquared, 0, 1);
        var closest = start + (segment * closestTime);
        return Vector2.DistanceSquared(closest, center) <= combinedRadius * combinedRadius;
    }
}
