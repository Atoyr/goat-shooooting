using System.Numerics;
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

}
