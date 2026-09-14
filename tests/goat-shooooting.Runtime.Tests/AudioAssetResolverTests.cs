using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class AudioAssetResolverTests
{
    [Fact]
    public void ToneCueResolvesWithoutExternalFile()
    {
        using var directory = new TemporaryDirectory();
        var definitions = CreateDefinitions(new AudioDefinition
        {
            Id = "se-hit",
            AssetId = "tone://440/120",
            Category = "effect",
            MaximumInstances = 2,
            CooldownSeconds = 0.05f,
            PitchVariation = 0.1f
        });

        var assets = AudioAssetResolver.Resolve(directory.Path, definitions);

        Assert.Equal(440, assets["se-hit"].ToneFrequency);
        Assert.Equal(120, assets["se-hit"].ToneDurationMilliseconds);
        Assert.Null(assets["se-hit"].ResolvedPath);
    }

    [Theory]
    [InlineData("../escape.wav")]
    [InlineData("tone://10/100")]
    [InlineData("tone://440/2")]
    public void InvalidOrEscapingAssetIsRejected(string assetId)
    {
        using var directory = new TemporaryDirectory();
        var definitions = CreateDefinitions(new AudioDefinition { Id = "cue", AssetId = assetId });

        Assert.Throws<DefinitionValidationException>(() => AudioAssetResolver.Resolve(directory.Path, definitions));
    }

    [Fact]
    public void InvalidLoopRangeIsRejectedByDefinitionValidation()
    {
        Assert.Throws<DefinitionValidationException>(() => CreateDefinitions(new AudioDefinition
        {
            Id = "bgm",
            AssetId = "tone://110/1000",
            Category = "music",
            Loop = true,
            LoopStartSeconds = 0.8,
            LoopEndSeconds = 0.2
        }));
    }

    private static DefinitionCatalog CreateDefinitions(params AudioDefinition[] audio) => new(
        new GameDefinition { PlayerId = "player", StageId = "stage", Width = 640, Height = 720 },
        new[]
        {
            new PlayerDefinition
            {
                Id = "player", Lives = 3, Bombs = 3, BombDamage = 20, Speed = 200,
                WeaponId = "weapon", X = 320, Y = 650, Radius = 4
            }
        },
        new[] { new EnemyDefinition { Id = "enemy", Hp = 10, Speed = 20, Radius = 8 } },
        new[] { new BulletDefinition { Id = "bullet", Speed = 200, Damage = 1, Radius = 2, Lifetime = 2 } },
        new[] { new WeaponDefinition { Id = "weapon", BulletId = "bullet", Cooldown = 0.2f } },
        new[]
        {
            new StageDefinition
            {
                Id = "stage",
                Events = new[]
                {
                    new StageEventDefinition
                    {
                        Time = 100, Type = "spawn-enemy", EnemyId = "enemy", X = 320, Y = 40
                    }
                }
            }
        },
        audio: audio);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goat-audio-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
