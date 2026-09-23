using GoatShooooting.Definitions;
using GoatShooooting.Platform;
using GoatShooooting.Runtime;
using Microsoft.Xna.Framework.Audio;

namespace GoatShooooting.Framework;

public interface IAudioCueVoice : IDisposable
{
    bool IsPlaying { get; }
    float Volume { get; set; }
    void Stop();
}

public interface IAudioCueBackend : IDisposable
{
    bool TryPlay(ResolvedAudioAsset asset, float volume, float pitch, bool loop, out IAudioCueVoice? voice);
}

public sealed class AudioCueEngine : IDisposable
{
    private const int MaximumTotalVoices = 32;
    private sealed class ActiveCue(string id, AudioDefinition definition, IAudioCueVoice voice, bool music)
    {
        public string Id { get; } = id;
        public AudioDefinition Definition { get; } = definition;
        public IAudioCueVoice Voice { get; } = voice;
        public bool Music { get; } = music;
        public float Fade { get; set; } = music && definition.CrossfadeSeconds > 0 ? 0 : 1;
        public bool FadingOut { get; set; }
    }

    private readonly IReadOnlyDictionary<string, ResolvedAudioAsset> _assets;
    private readonly IAudioCueBackend _backend;
    private readonly List<ActiveCue> _active = new();
    private readonly Dictionary<string, long> _lastPlayedFrame = new(StringComparer.Ordinal);
    private AudioSettings _settings;
    private float _duckRemaining;
    private string? _musicCueId;

    public AudioCueEngine(
        IReadOnlyDictionary<string, ResolvedAudioAsset> assets,
        IAudioCueBackend backend,
        AudioSettings settings)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public int MissingCueCount { get; private set; }
    public int DroppedCueCount { get; private set; }
    public int ActiveVoiceCount => _active.Count;
    public string? MusicCueId => _musicCueId;

    public void Apply(AudioSettings settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public void Process(IReadOnlyList<IGameplayEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        for (var index = 0; index < events.Count; index++)
        {
            var gameplayEvent = events[index];
            if (gameplayEvent is StageAudioCueEvent stageAudio)
            {
                switch (stageAudio.Kind)
                {
                    case "set-bgm":
                        SetMusic(stageAudio.CueId, stageAudio.Frame);
                        break;
                    case "stinger" when stageAudio.CueId is not null:
                        Play(stageAudio.CueId, stageAudio.Frame, stageAudio.Sequence);
                        break;
                    case "duck":
                        _duckRemaining = Math.Max(
                            _duckRemaining,
                            stageAudio.DurationFrames / (float)SimulationTiming.TicksPerSecond);
                        break;
                }
                continue;
            }
            var cueId = gameplayEvent switch
            {
                AudioCueEvent value => value.CueId,
                ProjectileSpawnedEvent => "se-shot",
                ProjectileHitEvent or EnemyDamagedEvent or PlayerHitEvent => "se-hit",
                EnemyDestroyedEvent => "se-destroy",
                ItemCollectedEvent => "se-item",
                PlayerGrazedEvent => "se-graze",
                BombUsedEvent => "se-bomb",
                SpecialActivatedEvent value when _assets.ContainsKey(value.AudioCue) => value.AudioCue,
                SpecialActivatedEvent => "se-special",
                BossPhaseStartedEvent => "se-warning",
                _ => null
            };
            if (cueId is not null) Play(cueId, gameplayEvent.Frame, gameplayEvent.Sequence);
            if (gameplayEvent is SpecialActivatedEvent or BossPhaseStartedEvent)
                _duckRemaining = Math.Max(_duckRemaining, 0.8f);
        }
    }

    public bool Play(string cueId, long frame, int sequence = 0)
    {
        if (!_assets.TryGetValue(cueId, out var asset))
        {
            MissingCueCount++;
            return false;
        }
        var definition = asset.Definition;
        var cooldownFrames = (long)Math.Ceiling(definition.CooldownSeconds * SimulationTiming.TicksPerSecond);
        if (_lastPlayedFrame.TryGetValue(cueId, out var lastFrame) && frame - lastFrame <= cooldownFrames)
        {
            DroppedCueCount++;
            return false;
        }
        RemoveStopped();
        var sameCueCount = 0;
        for (var index = 0; index < _active.Count; index++)
            if (_active[index].Id == cueId) sameCueCount++;
        if (sameCueCount >= definition.MaximumInstances)
        {
            DroppedCueCount++;
            return false;
        }
        if (_active.Count >= MaximumTotalVoices)
        {
            var replaceIndex = -1;
            var lowestPriority = int.MaxValue;
            for (var index = 0; index < _active.Count; index++)
            {
                if (_active[index].Music || _active[index].Definition.Priority >= lowestPriority) continue;
                replaceIndex = index;
                lowestPriority = _active[index].Definition.Priority;
            }
            if (replaceIndex < 0 || lowestPriority >= definition.Priority)
            {
                DroppedCueCount++;
                return false;
            }
            _active[replaceIndex].Voice.Stop();
            _active[replaceIndex].Voice.Dispose();
            _active.RemoveAt(replaceIndex);
        }
        var pitch = definition.PitchVariation <= 0
            ? 0
            : ((((frame * 31) + sequence) & 1023) / 1023f * 2 - 1) * definition.PitchVariation;
        var initialFade = definition.Category == "music" && definition.CrossfadeSeconds > 0 ? 0 : 1;
        var volume = AudioMixer.CalculateEffectiveVolume(_settings, definition.Category, definition.BaseVolume) *
            initialFade;
        if (!_backend.TryPlay(asset, volume, pitch, definition.Loop, out var voice) || voice is null)
        {
            DroppedCueCount++;
            return false;
        }
        _lastPlayedFrame[cueId] = frame;
        _active.Add(new ActiveCue(cueId, definition, voice, music: definition.Category == "music"));
        return true;
    }

    public bool SetMusic(string? cueId, long frame)
    {
        if (string.Equals(_musicCueId, cueId, StringComparison.Ordinal)) return true;
        if (cueId is not null && !Play(cueId, frame)) return false;
        for (var index = 0; index < _active.Count; index++)
            if (_active[index].Music && _active[index].Id != cueId) _active[index].FadingOut = true;
        _musicCueId = cueId;
        return true;
    }

    public void Preview(string category, long frame)
    {
        foreach (var asset in _assets.Values)
        {
            if (asset.Definition.Category != category) continue;
            Play(asset.Definition.Id, frame);
            return;
        }
    }

    public void Update(float elapsedSeconds)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        _duckRemaining = Math.Max(0, _duckRemaining - elapsedSeconds);
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var cue = _active[index];
            if (!cue.Voice.IsPlaying)
            {
                cue.Voice.Dispose();
                _active.RemoveAt(index);
                continue;
            }
            if (cue.Music)
            {
                var step = cue.Definition.CrossfadeSeconds <= 0 ? 1 : elapsedSeconds / cue.Definition.CrossfadeSeconds;
                cue.Fade = Math.Clamp(cue.Fade + (cue.FadingOut ? -step : step), 0, 1);
                if (cue.FadingOut && cue.Fade <= 0)
                {
                    cue.Voice.Stop();
                    cue.Voice.Dispose();
                    _active.RemoveAt(index);
                    continue;
                }
            }
            var duck = cue.Music && _duckRemaining > 0 ? 1 - cue.Definition.Ducking : 1;
            cue.Voice.Volume = AudioMixer.CalculateEffectiveVolume(
                _settings, cue.Definition.Category, cue.Definition.BaseVolume) * cue.Fade * duck;
        }
    }

    private void RemoveStopped()
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            if (_active[index].Voice.IsPlaying) continue;
            _active[index].Voice.Dispose();
            _active.RemoveAt(index);
        }
    }

    public void Dispose()
    {
        foreach (var cue in _active)
        {
            cue.Voice.Stop();
            cue.Voice.Dispose();
        }
        _active.Clear();
        _backend.Dispose();
    }
}

internal sealed class GameAudio : IDisposable
{
    private AudioSettings _settings;
    private AudioCueEngine? _engine;

    public GameAudio(AudioSettings settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public string? LastError { get; private set; }

    public void Load(IReadOnlyDictionary<string, ResolvedAudioAsset> assets)
    {
        _engine?.Dispose();
        var merged = CreateFallbackAssets();
        foreach (var pair in assets) merged[pair.Key] = pair.Value;
        try
        {
            _engine = new AudioCueEngine(merged, new MonoGameAudioCueBackend(), _settings);
            LastError = null;
        }
        catch (Exception exception)
        {
            _engine = new AudioCueEngine(merged, new SilentAudioCueBackend(), _settings);
            LastError = $"Audio device unavailable: {exception.Message}";
        }
    }

    public void Apply(AudioSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _engine?.Apply(settings);
    }

    public void Process(IReadOnlyList<IGameplayEvent> events) => _engine?.Process(events);
    public bool Play(string cueId, long frame) => _engine?.Play(cueId, frame) ?? false;
    public bool SetMusic(string? cueId, long frame) => _engine?.SetMusic(cueId, frame) ?? false;
    public void Preview(string category, long frame) => _engine?.Preview(category, frame);
    public void Update(float elapsedSeconds) => _engine?.Update(elapsedSeconds);
    public void Dispose() => _engine?.Dispose();

    private static Dictionary<string, ResolvedAudioAsset> CreateFallbackAssets()
    {
        (string Id, float Frequency, int Duration)[] cues =
        [
            ("se-shot", 820, 35),
            ("se-laser", 680, 100),
            ("se-hit", 520, 45),
            ("se-destroy", 120, 160),
            ("se-item", 980, 70),
            ("se-graze", 1260, 30),
            ("se-bomb", 75, 320),
            ("se-special", 330, 280),
            ("se-warning", 240, 420),
            ("se-menu", 700, 45)
        ];
        return cues.ToDictionary(
            static cue => cue.Id,
            static cue => new ResolvedAudioAsset(
                new AudioDefinition
                {
                    Id = cue.Id,
                    AssetId = $"tone://{cue.Frequency}/{cue.Duration}",
                    Category = cue.Id == "se-warning" ? "voice" : "effect",
                    CooldownSeconds = cue.Id is "se-hit" or "se-shot" ? 0.04f : 0,
                    MaximumInstances = 2
                },
                null,
                cue.Frequency,
                cue.Duration),
            StringComparer.Ordinal);
    }

    private sealed class MonoGameAudioCueBackend : IAudioCueBackend
    {
        private readonly Dictionary<string, SoundEffect> _sounds = new(StringComparer.Ordinal);

        public bool TryPlay(
            ResolvedAudioAsset asset,
            float volume,
            float pitch,
            bool loop,
            out IAudioCueVoice? voice)
        {
            try
            {
                if (!_sounds.TryGetValue(asset.Definition.Id, out var sound))
                {
                    sound = asset.ToneFrequency is { } frequency
                        ? CreateTone(frequency, asset.ToneDurationMilliseconds)
                        : LoadSound(asset.ResolvedPath!);
                    _sounds.Add(asset.Definition.Id, sound);
                }
                var instance = sound.CreateInstance();
                instance.Volume = volume;
                instance.Pitch = Math.Clamp(pitch, -1, 1);
                instance.IsLooped = loop;
                instance.Play();
                voice = new MonoGameAudioVoice(instance);
                return true;
            }
            catch
            {
                voice = null;
                return false;
            }
        }

        public void Dispose()
        {
            foreach (var sound in _sounds.Values) sound.Dispose();
            _sounds.Clear();
        }

        private static SoundEffect LoadSound(string path)
        {
            using var stream = File.OpenRead(path);
            return SoundEffect.FromStream(stream);
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
                var sample = (short)(short.MaxValue * 0.35f * fade * wave);
                BitConverter.TryWriteBytes(buffer.AsSpan(index * sizeof(short), sizeof(short)), sample);
            }
            return new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
        }
    }

    private sealed class MonoGameAudioVoice(SoundEffectInstance instance) : IAudioCueVoice
    {
        public bool IsPlaying => instance.State == SoundState.Playing;
        public float Volume { get => instance.Volume; set => instance.Volume = Math.Clamp(value, 0, 1); }
        public void Stop() => instance.Stop();
        public void Dispose() => instance.Dispose();
    }

    private sealed class SilentAudioCueBackend : IAudioCueBackend
    {
        public bool TryPlay(ResolvedAudioAsset asset, float volume, float pitch, bool loop, out IAudioCueVoice? voice)
        {
            voice = null;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
