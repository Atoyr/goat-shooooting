using System.Numerics;
using System.Text.Json;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class PresentationEffectSystemTests
{
    [Fact]
    public void SemanticEventsCreateAllRequiredEffectKinds()
    {
        var system = new PresentationEffectSystem(new PresentationSettings
        {
            ParticleDensity = 1,
            TrailsEnabled = false,
            MaximumEffects = 256
        });
        var events = new IGameplayEvent[]
        {
            new ProjectileSpawnedEvent(10, 0, 10, 1, ProjectileTeam.Player, "shot"),
            new ProjectileHitEvent(10, 1, 10, 2, 1),
            new EnemyDestroyedEvent(10, 2, 2, "enemy"),
            new ProjectileCancelledEvent(10, 3, 10),
            new ItemCollectedEvent(10, 4, 1, "power", 1),
            new BombUsedEvent(10, 5, 1),
            new SpecialActivatedEvent(10, 6, 1, 1, "special", "special"),
            new BossPhaseStartedEvent(10, 7, 2, "boss", "phase", 0, null)
        };

        system.ObserveTick(CreateSnapshot(), events);

        var kinds = Enumerable.Range(0, system.ActiveCount).Select(index => system.GetEffect(index).Kind).ToHashSet();
        Assert.Contains(PresentationEffectKind.Muzzle, kinds);
        Assert.Contains(PresentationEffectKind.Hit, kinds);
        Assert.Contains(PresentationEffectKind.Destroy, kinds);
        Assert.Contains(PresentationEffectKind.BulletCancel, kinds);
        Assert.Contains(PresentationEffectKind.ItemCollect, kinds);
        Assert.Contains(PresentationEffectKind.Bomb, kinds);
        Assert.Contains(PresentationEffectKind.Special, kinds);
        Assert.Contains(PresentationEffectKind.BossTransition, kinds);
        Assert.True(system.ScreenFlash > 0);
        Assert.True(system.CameraShake > 0);
        Assert.True(system.HitStopRemaining > 0);
    }

    [Fact]
    public void PoolDropsOverflowWithoutExceedingConfiguredCapacity()
    {
        var system = new PresentationEffectSystem(new PresentationSettings
        {
            MaximumEffects = 4,
            TrailsEnabled = false
        });

        system.ObserveTick(CreateSnapshot(), new IGameplayEvent[]
        {
            new BombUsedEvent(1, 0, 1),
            new EnemyDestroyedEvent(1, 1, 2, "enemy")
        });

        Assert.Equal(4, system.ActiveCount);
        Assert.True(system.DroppedEffects > 0);
    }

    [Fact]
    public void BombEffectsUseForwardDropPositionFromEvent()
    {
        var system = new PresentationEffectSystem(new PresentationSettings
        {
            TrailsEnabled = false
        });
        var dropPosition = new Vector2(100, 50);

        system.ObserveTick(CreateSnapshot(), new IGameplayEvent[]
        {
            new BombUsedEvent(1, 0, 1, EffectX: dropPosition.X, EffectY: dropPosition.Y)
        });

        var bombEffects = Enumerable.Range(0, system.ActiveCount)
            .Select(system.GetEffect)
            .Where(static effect => effect.Kind == PresentationEffectKind.Bomb)
            .ToArray();
        Assert.NotEmpty(bombEffects);
        Assert.All(bombEffects, effect => Assert.Equal(dropPosition, effect.Position));
    }

    [Fact]
    public void ZeroPresentationSettingsKeepInformationFallbackAndEmitNoEffects()
    {
        var system = new PresentationEffectSystem(new PresentationSettings
        {
            ParticleDensity = 0,
            FlashIntensity = 0,
            ShakeIntensity = 0,
            TrailsEnabled = false
        });
        var snapshot = CreateSnapshot();

        system.ObserveTick(snapshot, new IGameplayEvent[] { new BombUsedEvent(1, 0, 1) });

        Assert.Equal(0, system.ActiveCount);
        Assert.Equal(0, system.ScreenFlash);
        Assert.Equal(0, system.CameraShake);
    }

    [Fact]
    public void CompiledRecipeReadsEventFactsAndExpandsPresentationOnlyActions()
    {
        var compiled = CompileRecipe(new EffectRecipeDefinition
        {
            Id = "large-destroy",
            On = "enemy-destroyed",
            When = JsonDocument.Parse(
                """
                { "op": "and",
                  "left": { "op": "greater-than", "left": { "event": "baseScore" }, "right": 0 },
                  "right": { "op": "greater-than", "left": { "context": "particleDensity" }, "right": 0.5 } }
                """).RootElement.Clone(),
            Actions =
            [
                new EffectRecipeActionDefinition { Type = "particle", Kind = "custom", Count = 2 },
                new EffectRecipeActionDefinition { Type = "flash", Intensity = 0.4f },
                new EffectRecipeActionDefinition { Type = "shake", Intensity = 0.3f },
                new EffectRecipeActionDefinition { Type = "hit-stop", Duration = 0.1f },
                new EffectRecipeActionDefinition { Type = "audio", CueId = "impact" },
                new EffectRecipeActionDefinition
                { Type = "post-process", Pass = "bloom", Intensity = 0.6f, Duration = 0.2f }
            ]
        });
        var system = new PresentationEffectSystem(new PresentationSettings { TrailsEnabled = false });
        system.Configure(compiled.EffectRecipes);

        system.ObserveTick(CreateSnapshot(),
            [new EnemyDestroyedEvent(10, 0, 2, "enemy", BaseScore: 100)]);

        Assert.Equal(14, system.ActiveCount);
        Assert.Contains(Enumerable.Range(0, system.ActiveCount).Select(system.GetEffect),
            static effect => effect.Kind == PresentationEffectKind.Custom);
        Assert.True(system.ScreenFlash >= 0.4f);
        Assert.True(system.CameraShake >= 0.3f);
        Assert.True(system.HitStopRemaining >= 0.1f);
        Assert.Equal(["impact"], system.AudioCues);
        Assert.Equal(0.6f, system.PostProcessPasses["bloom"]);

        var filtered = new PresentationEffectSystem(new PresentationSettings { TrailsEnabled = false });
        filtered.Configure(compiled.EffectRecipes);
        filtered.ObserveTick(CreateSnapshot(),
            [new EnemyDestroyedEvent(10, 0, 2, "enemy", BaseScore: 0)]);
        Assert.Equal(12, filtered.ActiveCount);
        Assert.Empty(filtered.AudioCues);
        Assert.Empty(filtered.PostProcessPasses);

        var accessible = new PresentationEffectSystem(new PresentationSettings
        {
            ParticleDensity = 0.25f,
            TrailsEnabled = false
        });
        accessible.Configure(compiled.EffectRecipes);
        accessible.ObserveTick(CreateSnapshot(),
            [new EnemyDestroyedEvent(10, 0, 2, "enemy", BaseScore: 100)]);
        Assert.Empty(accessible.AudioCues);
        Assert.Empty(accessible.PostProcessPasses);
    }

    [Theory]
    [InlineData("full", 0)]
    [InlineData("touhou", 180)]
    [InlineData("donpachi", 180)]
    public void HudFitsEveryLogicalLayoutAndLetterbox(string screenLayout, int panelWidth)
    {
        var definition = new GameDefinition
        {
            Width = 640,
            Height = 720,
            ScreenLayout = screenLayout,
            HudPanelWidth = panelWidth,
            ScorePosition = screenLayout == "donpachi" ? "left-panel" :
                screenLayout == "touhou" ? "right-panel" : "playfield-top-right"
        };
        var layout = PrimitiveRenderLayout.CreateGameScreenLayout(definition);
        var hud = PrimitiveRenderLayout.CreatePresentationHudLayout(layout);

        Assert.True(layout.Window.Contains(hud.Statistics));
        Assert.True(layout.Window.Contains(hud.Boss));
        Assert.True(layout.Window.Contains(hud.Warning));
        var letterbox = DisplaySettingsApplicator.CalculateLetterbox(
            layout.Window.Width, layout.Window.Height, 1920, 1080);
        Assert.InRange(letterbox.Left, 0, 1920);
        Assert.InRange(letterbox.Right, 0, 1920);
        Assert.InRange(letterbox.Top, 0, 1080);
        Assert.InRange(letterbox.Bottom, 0, 1080);
    }

    private static FrameSnapshot CreateSnapshot() => new(
        10,
        new RenderItem[]
        {
            new(1, RenderKind.Player, new Vector2(100, 200), 5, 1),
            new(2, RenderKind.Enemy, new Vector2(300, 100), 12, 0.5f,
                BossName: "BOSS", BossPhaseName: "PHASE", BossRemainingTime: 20),
            new(10, RenderKind.EnemyBullet, new Vector2(250, 180), 3, 1)
        },
        1234,
        8,
        1.5,
        40,
        100,
        25,
        100,
        0.4,
        1,
        "stage",
        3,
        2,
        new BossHudSnapshot(2, "boss", "BOSS", "PHASE", 0.5f, 20, false));

    private static CompiledCatalog CompileRecipe(EffectRecipeDefinition recipe)
    {
        var definitions = new DefinitionCatalog(
            new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
            [new PlayerDefinition
            {
                Id = "player",
                Lives = 2,
                Bombs = 2,
                Speed = 200,
                WeaponId = "weapon",
                Radius = 10
            }],
            [new EnemyDefinition { Id = "enemy", Hp = 10, Radius = 10 }],
            [new BulletDefinition { Id = "bullet", Speed = 100, Damage = 1, Radius = 3, Lifetime = 5 }],
            [new WeaponDefinition { Id = "weapon", BulletId = "bullet", Cooldown = 0.5f }],
            [new StageDefinition
            {
                Id = "stage",
                Events = [new StageEventDefinition
                { Type = "spawn-enemy", EnemyId = "enemy", X = 10, Y = 10 }]
            }],
            audio: [new AudioDefinition { Id = "impact", AssetId = "impact.wav" }],
            effectRecipes: [recipe]);
        return new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
    }

}
