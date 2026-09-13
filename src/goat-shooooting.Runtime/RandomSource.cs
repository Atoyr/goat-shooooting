namespace GoatShooooting.Runtime;

/// <summary>Deterministic random stream owned by a simulation run.</summary>
public interface IRandomSource
{
    ulong State { get; }
    void Reset(long seed);
    uint NextUInt32();
    float NextSingle();
}

/// <summary>Small, reproducible SplitMix64 source suitable for gameplay decisions.</summary>
public sealed class SeededRandomSource : IRandomSource
{
    private ulong _state;

    public SeededRandomSource(long seed) => Reset(seed);

    public ulong State => _state;

    public void Reset(long seed) => _state = unchecked((ulong)seed);

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    public float NextSingle() => (NextUInt32() >> 8) * (1f / (1 << 24));

    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var value = _state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
