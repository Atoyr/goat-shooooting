namespace GoatShooooting.Core;

/// <summary>Owns all live entities in a simulation.</summary>
public sealed class World
{
    private readonly List<Entity> _entities = new();
    private int _nextEntityId = 1;

    public IReadOnlyList<Entity> Entities => _entities;

    public Entity CreateEntity()
    {
        var entity = new Entity(_nextEntityId++);
        _entities.Add(entity);
        return entity;
    }

    public bool DestroyEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _entities.Remove(entity);
    }

    public IEnumerable<Entity> Query<T>() where T : class => _entities.Where(entity => entity.Has<T>());

    public IEnumerable<Entity> Query<T1, T2>()
        where T1 : class
        where T2 : class => _entities.Where(entity => entity.Has<T1>() && entity.Has<T2>());

    public IEnumerable<Entity> Query<T1, T2, T3>()
        where T1 : class
        where T2 : class
        where T3 : class => _entities.Where(entity => entity.Has<T1>() && entity.Has<T2>() && entity.Has<T3>());

    /// <summary>Creates an isolated copy while allowing the owning runtime to copy its component types.</summary>
    public World Clone(Func<object, object> cloneComponent)
    {
        ArgumentNullException.ThrowIfNull(cloneComponent);
        var clone = new World { _nextEntityId = _nextEntityId };
        foreach (var entity in _entities)
        {
            var clonedEntity = new Entity(entity.Id);
            foreach (var component in entity.Components) clonedEntity.AddComponent(cloneComponent(component));
            clone._entities.Add(clonedEntity);
        }
        return clone;
    }
}
