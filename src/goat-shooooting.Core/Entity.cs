namespace GoatShooooting.Core;

/// <summary>A lightweight identity and component container. Game behaviour lives in systems.</summary>
public sealed class Entity
{
    private readonly Dictionary<Type, object> _components = new();

    internal Entity(int id) => Id = id;

    public int Id { get; }

    public Entity Add<T>(T component) where T : class
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!_components.TryAdd(typeof(T), component))
        {
            throw new InvalidOperationException($"Entity {Id} already has component {typeof(T).Name}.");
        }

        return this;
    }

    public T Get<T>() where T : class =>
        TryGet<T>(out var component)
            ? component
            : throw new InvalidOperationException($"Entity {Id} does not have component {typeof(T).Name}.");

    public bool TryGet<T>(out T component) where T : class
    {
        if (_components.TryGetValue(typeof(T), out var value))
        {
            component = (T)value;
            return true;
        }

        component = null!;
        return false;
    }

    public bool Has<T>() where T : class => _components.ContainsKey(typeof(T));

    public bool Remove<T>() where T : class => _components.Remove(typeof(T));

    internal IEnumerable<object> Components => _components.Values;

    internal void AddComponent(object component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _components.Add(component.GetType(), component);
    }
}
