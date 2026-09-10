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
}
