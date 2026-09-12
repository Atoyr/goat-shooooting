using Microsoft.Xna.Framework.Audio;
using GoatShooooting.Platform;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>Small procedural sound set so gameplay feedback has no external asset dependency.</summary>
internal sealed class GameAudio : IDisposable
{
    private readonly SoundEffect _hit = CreateTone(520, 45);
    private readonly SoundEffect _explosion = CreateTone(120, 140);
    private readonly SoundEffect _playerHit = CreateTone(210, 180);
    private readonly SoundEffect _bomb = CreateTone(75, 300);
    private AudioSettings _settings;

    public GameAudio(AudioSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Apply(AudioSettings settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public void Play(SimulationFeedback feedback)
    {
        if (feedback.Hits > 0)
        {
            Play(_hit, 0.12f);
        }

        if (feedback.EnemiesDestroyed > 0)
        {
            Play(_explosion, 0.18f);
        }

        if (feedback.PlayerHits > 0)
        {
            Play(_playerHit, 0.2f);
        }

        if (feedback.BombsUsed > 0)
        {
            Play(_bomb, 0.22f);
        }
    }

    public void Dispose()
    {
        _hit.Dispose();
        _explosion.Dispose();
        _playerHit.Dispose();
        _bomb.Dispose();
    }

    private void Play(SoundEffect sound, float soundBaseVolume)
    {
        var volume = AudioMixer.CalculateEffectiveVolume(_settings, soundBaseVolume);
        if (volume > 0)
        {
            sound.Play(volume, pitch: 0, pan: 0);
        }
    }

    private static SoundEffect CreateTone(float frequency, int durationMilliseconds)
    {
        const int sampleRate = 22050;
        var sampleCount = sampleRate * durationMilliseconds / 1000;
        var buffer = new byte[sampleCount * sizeof(short)];
        for (var index = 0; index < sampleCount; index++)
        {
            var fade = 1f - ((float)index / sampleCount);
            var wave = MathF.Sin(2 * MathF.PI * frequency * index / sampleRate);
            var sample = (short)(short.MaxValue * 0.5f * fade * wave);
            BitConverter.TryWriteBytes(buffer.AsSpan(index * sizeof(short), sizeof(short)), sample);
        }

        return new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
    }
}
