using Microsoft.Xna.Framework.Audio;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>Small procedural sound set so gameplay feedback has no external asset dependency.</summary>
internal sealed class GameAudio : IDisposable
{
    private readonly SoundEffect _hit = CreateTone(520, 45, 0.12f);
    private readonly SoundEffect _explosion = CreateTone(120, 140, 0.18f);
    private readonly SoundEffect _playerHit = CreateTone(210, 180, 0.2f);

    public void Play(SimulationFeedback feedback)
    {
        if (feedback.Hits > 0)
        {
            _hit.Play();
        }

        if (feedback.EnemiesDestroyed > 0)
        {
            _explosion.Play();
        }

        if (feedback.PlayerHits > 0)
        {
            _playerHit.Play();
        }
    }

    public void Dispose()
    {
        _hit.Dispose();
        _explosion.Dispose();
        _playerHit.Dispose();
    }

    private static SoundEffect CreateTone(float frequency, int durationMilliseconds, float volume)
    {
        const int sampleRate = 22050;
        var sampleCount = sampleRate * durationMilliseconds / 1000;
        var buffer = new byte[sampleCount * sizeof(short)];
        for (var index = 0; index < sampleCount; index++)
        {
            var fade = 1f - ((float)index / sampleCount);
            var wave = MathF.Sin(2 * MathF.PI * frequency * index / sampleRate);
            var sample = (short)(short.MaxValue * volume * fade * wave);
            BitConverter.TryWriteBytes(buffer.AsSpan(index * sizeof(short), sizeof(short)), sample);
        }

        return new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
    }
}
