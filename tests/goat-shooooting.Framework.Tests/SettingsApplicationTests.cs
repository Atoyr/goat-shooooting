using GoatShooooting.Framework;
using GoatShooooting.Platform;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class SettingsApplicationTests
{
    [Fact]
    public void DisplaySettingsApplyWindowedAndBorderlessSizes()
    {
        var target = new FakeDisplayTarget();
        var applicator = new DisplaySettingsApplicator(target, new DisplaySettings());

        Assert.True(applicator.TryApply(new DisplaySettings { WindowScale = 3 }, 320, 240));
        Assert.Equal(960, target.BackBufferWidth);
        Assert.Equal(720, target.BackBufferHeight);
        Assert.False(target.IsFullScreen);

        Assert.True(applicator.TryApply(
            new DisplaySettings { WindowMode = WindowMode.BorderlessFullscreen },
            320,
            240));
        Assert.Equal(1920, target.BackBufferWidth);
        Assert.Equal(1080, target.BackBufferHeight);
        Assert.True(target.IsFullScreen);
        Assert.False(target.HardwareModeSwitch);
    }

    [Fact]
    public void DisplayApplyFailureRestoresPreviousDeviceValues()
    {
        var target = new FakeDisplayTarget { BackBufferWidth = 640, BackBufferHeight = 480 };
        var applicator = new DisplaySettingsApplicator(target, new DisplaySettings());
        target.FailNextApply = true;

        var applied = applicator.TryApply(
            new DisplaySettings { WindowMode = WindowMode.BorderlessFullscreen },
            320,
            240);

        Assert.False(applied);
        Assert.Equal(640, target.BackBufferWidth);
        Assert.Equal(480, target.BackBufferHeight);
        Assert.False(target.IsFullScreen);
        Assert.Equal(new DisplaySettings(), applicator.CurrentSettings);
    }

    [Fact]
    public void LetterboxPreservesAspectRatioAndCentersOutput()
    {
        var viewport = DisplaySettingsApplicator.CalculateLetterbox(800, 600, 1920, 1080);

        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(240, 0, 1440, 1080), viewport);
    }

    [Fact]
    public void AudioVolumeCombinesMuteMasterEffectsAndBaseVolume()
    {
        var settings = new AudioSettings { MasterVolume = 0.5f, EffectsVolume = 0.4f };

        Assert.Equal(0.1f, AudioMixer.CalculateEffectiveVolume(settings, 0.5f), precision: 5);
        Assert.Equal(0, AudioMixer.CalculateEffectiveVolume(settings with { Muted = true }, 0.5f));
    }

    [Fact]
    public void ControllerVibrationCanBeDisabledAndPrioritizesPlayerFeedback()
    {
        var feedback = new SimulationFeedback(Hits: 1, EnemiesDestroyed: 1, PlayerHits: 1, BombsUsed: 0);

        var pulse = FeedbackVibration.GetPulse(feedback, enabled: true);

        Assert.Equal(new VibrationPulse(0.65f, 0.35f, 0.2f), pulse);
        Assert.Equal(default, FeedbackVibration.GetPulse(feedback, enabled: false));
    }

    private sealed class FakeDisplayTarget : IDisplaySettingsTarget
    {
        public int DesktopWidth => 1920;
        public int DesktopHeight => 1080;
        public int BackBufferWidth { get; set; }
        public int BackBufferHeight { get; set; }
        public bool IsFullScreen { get; set; }
        public bool HardwareModeSwitch { get; set; }
        public bool VSync { get; set; }
        public bool FailNextApply { get; set; }

        public void ApplyChanges()
        {
            if (FailNextApply)
            {
                FailNextApply = false;
                throw new InvalidOperationException("Simulated display failure.");
            }
        }
    }
}
