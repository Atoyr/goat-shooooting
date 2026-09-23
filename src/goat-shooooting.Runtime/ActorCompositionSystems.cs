using System.Numerics;
using GoatShooooting.Core;

namespace GoatShooooting.Runtime;

public sealed class ActorTransformSystem
{
    public void Update(World world)
    {
        foreach (var entity in world.Entities)
        {
            if (!entity.TryGet<ActorPartComponent>(out var part) ||
                !entity.TryGet<TransformComponent>(out var transform)) continue;
            var root = OptionFollowSystem.FindEntity(world, part.RootEntityId);
            if (root is null || root.Has<PendingDestroyComponent>())
            {
                if (!entity.Has<PendingDestroyComponent>()) entity.Add(new PendingDestroyComponent());
                continue;
            }
            if (part.Detached) continue;
            var parent = OptionFollowSystem.FindEntity(world, part.ParentEntityId);
            if (parent is null || parent.Has<PendingDestroyComponent>())
            {
                if (!entity.Has<PendingDestroyComponent>()) entity.Add(new PendingDestroyComponent());
                continue;
            }
            var parentTransform = parent.Get<TransformComponent>();
            var parentRotation = parent.TryGet<RotationComponent>(out var rotation) ? rotation.Degrees : 0;
            transform.Position = parentTransform.Position + Rotate(part.LocalOffset, parentRotation);
            if (entity.TryGet<RotationComponent>(out var ownRotation))
                ownRotation.Degrees = parentRotation + part.LocalRotationDegrees;
        }


        foreach (var entity in world.Entities)
        {
            if (!entity.TryGet<ParentTransformComponent>(out var attachment) ||
                !entity.TryGet<TransformComponent>(out var transform)) continue;
            var root = OptionFollowSystem.FindEntity(world, attachment.RootEntityId);
            var parent = OptionFollowSystem.FindEntity(world, attachment.ParentEntityId);
            if (root is null || parent is null || root.Has<PendingDestroyComponent>() || parent.Has<PendingDestroyComponent>())
            {
                if (!entity.Has<PendingDestroyComponent>()) entity.Add(new PendingDestroyComponent());
                continue;
            }
            var parentTransform = parent.Get<TransformComponent>();
            var parentRotation = parent.TryGet<RotationComponent>(out var rotation) ? rotation.Degrees : 0;
            transform.Position = parentTransform.Position + Rotate(attachment.LocalOffset, parentRotation);
            entity.Get<RotationComponent>().Degrees = parentRotation + attachment.LocalRotationDegrees;
        }
    }

    private static Vector2 Rotate(Vector2 value, float degrees)
    {
        var radians = degrees * MathF.PI / 180;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2(
            (value.X * cosine) - (value.Y * sine),
            (value.X * sine) + (value.Y * cosine));
    }
}

public static class ActorPartSignalSystem
{
    public static void Apply(
        World world,
        int rootEntityId,
        CompiledBossPhaseDefinition phase,
        GameEventBuffer? events = null)
    {
        var parts = world.Query<ActorPartComponent>()
            .Where(entity => entity.Get<ActorPartComponent>().RootEntityId == rootEntityId)
            .ToDictionary(entity => entity.Get<ActorPartComponent>().PartHandle);
        foreach (var signal in phase.PartSignals)
            foreach (var handle in signal.PartHandles)
            {
                if (!parts.TryGetValue(handle, out var entity)) continue;
                var part = entity.Get<ActorPartComponent>();
                switch (signal.Operation)
                {
                    case "enable":
                        part.Enabled = true;
                        break;
                    case "disable":
                        part.Enabled = false;
                        break;
                    case "detach":
                        part.Detached = true;
                        part.ParentEntityId = rootEntityId;
                        if (!string.IsNullOrWhiteSpace(part.DetachSignal))
                            events?.Publish((frame, sequence) => new RuleSignalEvent(
                                frame, sequence, part.DetachSignal, part.PartId, false));
                        break;
                }
            }
    }
}

public sealed class ActorAnimationStateSystem
{
    public void Update(World world)
    {
        foreach (var entity in world.Query<ActorPresentationComponent>())
        {
            var presentation = entity.Get<ActorPresentationComponent>();
            if (entity.Has<PendingDestroyComponent>()) presentation.SemanticState = "destroy";
            else if (entity.Has<HitFlashComponent>()) presentation.SemanticState = "damaged";
            else if (entity.TryGet<VelocityComponent>(out var velocity) && velocity.Value.X < 0) presentation.SemanticState = "move-left";
            else if (entity.TryGet<VelocityComponent>(out velocity) && velocity.Value.X > 0) presentation.SemanticState = "move-right";
            else presentation.SemanticState = "idle";
        }
    }
}

internal static class ActorCollision
{
    public static bool IntersectsSweptProjectile(
        Entity actor,
        Vector2 start,
        Vector2 end,
        float projectileRadius,
        out float hitTime)
    {
        var transform = actor.Get<TransformComponent>();
        if (!actor.TryGet<HurtboxSetComponent>(out var set))
        {
            return ProjectileCollisionSystem.IntersectsSweptCircle(
                start, end, transform.Position, projectileRadius + actor.Get<ColliderComponent>().Radius, out hitTime);
        }

        hitTime = float.MaxValue;
        var rotation = actor.TryGet<RotationComponent>(out var orientation) ? orientation.Degrees : 0;
        var hit = false;
        foreach (var shape in set.Shapes)
        {
            var center = transform.Position + Rotate(shape.Offset, rotation);
            var shapeRotation = shape.Shape == HurtboxShape.Aabb ? 0 : rotation + shape.RotationDegrees;
            var localStart = Rotate(start - center, -shapeRotation);
            var localEnd = Rotate(end - center, -shapeRotation);
            bool intersects;
            float candidateTime;
            switch (shape.Shape)
            {
                case HurtboxShape.Circle:
                    intersects = ProjectileCollisionSystem.IntersectsSweptCircle(
                        localStart, localEnd, Vector2.Zero, projectileRadius + shape.Radius, out candidateTime);
                    break;
                case HurtboxShape.Capsule:
                    intersects = IntersectsCapsule(
                        localStart, localEnd, projectileRadius, shape.Width, shape.Length, out candidateTime);
                    break;
                default:
                    intersects = IntersectsExpandedBox(
                        localStart, localEnd,
                        (shape.Width * 0.5f) + projectileRadius,
                        (shape.Height * 0.5f) + projectileRadius,
                        out candidateTime);
                    break;
            }
            if (intersects && candidateTime < hitTime)
            {
                hit = true;
                hitTime = candidateTime;
            }
        }
        return hit;
    }

    private static bool IntersectsCapsule(
        Vector2 start,
        Vector2 end,
        float projectileRadius,
        float width,
        float length,
        out float hitTime)
    {
        var shapeRadius = width * 0.5f;
        var radius = projectileRadius + shapeRadius;
        var halfSegment = Math.Max(0, (length * 0.5f) - shapeRadius);
        var distanceSquared = SegmentDistanceSquared(
            start,
            end,
            new Vector2(0, -halfSegment),
            new Vector2(0, halfSegment),
            out hitTime);
        return distanceSquared <= radius * radius;
    }

    private static float SegmentDistanceSquared(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd,
        out float firstTime)
    {
        var first = firstEnd - firstStart;
        var second = secondEnd - secondStart;
        var offset = firstStart - secondStart;
        var firstLength = Vector2.Dot(first, first);
        var secondLength = Vector2.Dot(second, second);
        var secondOffset = Vector2.Dot(second, offset);
        if (firstLength <= float.Epsilon && secondLength <= float.Epsilon)
        {
            firstTime = 0;
            return offset.LengthSquared();
        }
        if (firstLength <= float.Epsilon)
        {
            firstTime = 0;
            var secondTime = Math.Clamp(secondOffset / secondLength, 0, 1);
            return Vector2.DistanceSquared(firstStart, secondStart + (second * secondTime));
        }

        var firstOffset = Vector2.Dot(first, offset);
        if (secondLength <= float.Epsilon)
        {
            firstTime = Math.Clamp(-firstOffset / firstLength, 0, 1);
            return Vector2.DistanceSquared(firstStart + (first * firstTime), secondStart);
        }

        var dot = Vector2.Dot(first, second);
        var denominator = (firstLength * secondLength) - (dot * dot);
        firstTime = denominator > float.Epsilon
            ? Math.Clamp(((dot * secondOffset) - (firstOffset * secondLength)) / denominator, 0, 1)
            : 0;
        var secondTimeResolved = (dot * firstTime + secondOffset) / secondLength;
        if (secondTimeResolved < 0)
        {
            secondTimeResolved = 0;
            firstTime = Math.Clamp(-firstOffset / firstLength, 0, 1);
        }
        else if (secondTimeResolved > 1)
        {
            secondTimeResolved = 1;
            firstTime = Math.Clamp((dot - firstOffset) / firstLength, 0, 1);
        }
        return Vector2.DistanceSquared(
            firstStart + (first * firstTime),
            secondStart + (second * secondTimeResolved));
    }

    private static bool IntersectsExpandedBox(
        Vector2 start,
        Vector2 end,
        float halfWidth,
        float halfHeight,
        out float hitTime)
    {
        var direction = end - start;
        var entry = 0f;
        var exit = 1f;
        if (!Clip(start.X, direction.X, -halfWidth, halfWidth, ref entry, ref exit) ||
            !Clip(start.Y, direction.Y, -halfHeight, halfHeight, ref entry, ref exit))
        {
            hitTime = float.MaxValue;
            return false;
        }
        hitTime = entry;
        return true;
    }

    private static bool Clip(float start, float direction, float minimum, float maximum, ref float entry, ref float exit)
    {
        if (Math.Abs(direction) <= float.Epsilon) return start >= minimum && start <= maximum;
        var first = (minimum - start) / direction;
        var second = (maximum - start) / direction;
        if (first > second) (first, second) = (second, first);
        entry = Math.Max(entry, first);
        exit = Math.Min(exit, second);
        return entry <= exit;
    }

    private static Vector2 Rotate(Vector2 value, float degrees)
    {
        var radians = degrees * MathF.PI / 180;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2(
            (value.X * cosine) - (value.Y * sine),
            (value.X * sine) + (value.Y * cosine));
    }
}
