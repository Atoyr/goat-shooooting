using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Platform;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class AudioCueEngineTests
{
    [Fact]
    public void GameplayEventsAggregateSameCueAndApplyCategoryVolumeAndPitch()
    {
        using var backend = new RecordingBackend();
        using var engine = new AudioCueEngine(CreateAssets(
            Cue("se-hit", baseVolume: 0.5f, pitchVariation: 0.2f, cooldown: 0.05f)), backend,
            new AudioSettings { MasterVolume = 0.5f, EffectsVolume = 0.4f });

        engine.Process(new IGameplayEvent[]
        {
            new EnemyDamagedEvent(10, 0, 1, 1),
            new ProjectileHitEvent(10, 1, 2, 1, 1),
            new EnemyDamagedEvent(10, 2, 1, 1)
        });

        var play = Assert.Single(backend.Plays);
        Assert.Equal("se-hit", play.Id);
        Assert.InRange(play.Volume, 0.099f, 0.101f);
        Assert.InRange(play.Pitch, -0.2f, 0.2f);
        Assert.Equal(2, engine.DroppedCueCount);
    }

    [Fact]
    public void MusicCrossfadesAndDucksForBossTransition()
    {
        using var backend = new RecordingBackend();
        using var engine = new AudioCueEngine(CreateAssets(
            Cue("bgm-stage", "music", loop: true, crossfade: 1, ducking: 0.5f),
            Cue("bgm-boss", "music", loop: true, crossfade: 1, ducking: 0.5f),
            Cue("se-warning", "voice")), backend, new AudioSettings());

        Assert.True(engine.SetMusic("bgm-stage", 0));
        var stageVoice = backend.Voices[0];
        Assert.True(backend.Plays[0].Loop);
        Assert.Equal(0f, stageVoice.Volume);
        engine.Update(0.5f);
        Assert.InRange(stageVoice.Volume, 0.399f, 0.401f);
        Assert.True(engine.SetMusic("bgm-boss", 30));
        engine.Process(new IGameplayEvent[] { new BossPhaseStartedEvent(31, 0, 1, "boss", "phase", 0, null) });
        engine.Update(0.25f);

        Assert.Equal("bgm-boss", engine.MusicCueId);
        Assert.InRange(backend.Voices[1].Volume, 0.09f, 0.11f);
        Assert.InRange(stageVoice.Volume, 0.09f, 0.11f);
    }

    [Fact]
    public void MissingCueAndAudioDeviceFailureAreNonFatal()
    {
        using var backend = new RecordingBackend { RejectPlayback = true };
        using var engine = new AudioCueEngine(CreateAssets(Cue("se-menu")), backend, new AudioSettings());

        Assert.False(engine.Play("missing", 0));
        Assert.False(engine.Play("se-menu", 1));

        Assert.Equal(1, engine.MissingCueCount);
        Assert.Equal(1, engine.DroppedCueCount);
        Assert.Equal(0, engine.ActiveVoiceCount);
    }

    [Fact]
    public void StageAudioTrackSwitchesMusicPlaysStingerAndDucksWithoutTreatingBgmAsSoundEffect()
    {
        using var backend = new RecordingBackend();
        using var engine = new AudioCueEngine(CreateAssets(
            Cue("bgm-track", "music", loop: true, ducking: 0.5f),
            Cue("warning", "effect")), backend, new AudioSettings());

        engine.Process(new IGameplayEvent[]
        {
            new StageAudioCueEvent(1, 0, "set-bgm", "bgm-track"),
            new StageAudioCueEvent(1, 1, "stinger", "warning"),
            new StageAudioCueEvent(1, 2, "duck", null, 60)
        });
        engine.Update(0.25f);

        Assert.Equal("bgm-track", engine.MusicCueId);
        Assert.Equal(["bgm-track", "warning"], backend.Plays.Select(static play => play.Id));
        Assert.InRange(backend.Voices[0].Volume, 0.39f, 0.41f);
    }

    private static AudioDefinition Cue(
        string id,
        string category = "effect",
        float baseVolume = 1,
        bool loop = false,
        float crossfade = 0,
        float ducking = 0,
        float pitchVariation = 0,
        float cooldown = 0) => new()
        {
            Id = id,
            AssetId = "tone://440/100",
            Category = category,
            BaseVolume = baseVolume,
            Loop = loop,
            CrossfadeSeconds = crossfade,
            Ducking = ducking,
            PitchVariation = pitchVariation,
            CooldownSeconds = cooldown
        };

    private static IReadOnlyDictionary<string, ResolvedAudioAsset> CreateAssets(params AudioDefinition[] cues) =>
        cues.ToDictionary(
            static cue => cue.Id,
            static cue => new ResolvedAudioAsset(cue, null, 440, 100),
            StringComparer.Ordinal);

    private sealed class RecordingBackend : IAudioCueBackend
    {
        public List<(string Id, float Volume, float Pitch, bool Loop)> Plays { get; } = new();
        public List<RecordingVoice> Voices { get; } = new();
        public bool RejectPlayback { get; init; }

        public bool TryPlay(
            ResolvedAudioAsset asset,
            float volume,
            float pitch,
            bool loop,
            out IAudioCueVoice? voice)
        {
            Plays.Add((asset.Definition.Id, volume, pitch, loop));
            if (RejectPlayback)
            {
                voice = null;
                return false;
            }
            var result = new RecordingVoice { Volume = volume };
            Voices.Add(result);
            voice = result;
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingVoice : IAudioCueVoice
    {
        public bool IsPlaying { get; private set; } = true;
        public float Volume { get; set; }
        public void Stop() => IsPlaying = false;
        public void Dispose()
        {
        }
    }
}
