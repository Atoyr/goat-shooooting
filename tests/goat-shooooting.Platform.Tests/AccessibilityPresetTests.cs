using Xunit;

namespace GoatShooooting.Platform.Tests;

public sealed class AccessibilityPresetTests
{
    [Fact]
    public void ApplyAccessibilityPresetReducesMotionAndKeepsBulletCuesVisible()
    {
        var original = new GameSettings
        {
            Locale = "ja",
            Audio = new AudioSettings { MusicVolume = 0.4f }
        };

        var adjusted = original.ApplyAccessibilityPreset();

        Assert.Equal(0, adjusted.Gameplay.ScreenShakeStrength);
        Assert.Equal(0.2f, adjusted.Gameplay.FlashIntensity);
        Assert.Equal(0.5f, adjusted.Gameplay.ParticleDensity);
        Assert.Equal(0.65f, adjusted.Gameplay.BackgroundBrightness);
        Assert.True(adjusted.Gameplay.BulletOutline);
        Assert.Equal("high-contrast", adjusted.Gameplay.BulletPalette);
        Assert.Equal(1.25f, adjusted.Gameplay.HudScale);
        Assert.False(adjusted.Gameplay.ControllerVibration);
        Assert.Equal("ja", adjusted.Locale);
        Assert.Equal(0.4f, adjusted.Audio.MusicVolume);
    }

    [Fact]
    public void ApplyAccessibilityPresetIsIdempotent()
    {
        var once = new GameSettings().ApplyAccessibilityPreset();

        Assert.Equal(once, once.ApplyAccessibilityPreset());
    }
}
