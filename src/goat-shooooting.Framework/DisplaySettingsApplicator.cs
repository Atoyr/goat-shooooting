using GoatShooooting.Platform;
using Microsoft.Xna.Framework;

namespace GoatShooooting.Framework;

public interface IDisplaySettingsTarget
{
    int DesktopWidth { get; }
    int DesktopHeight { get; }
    int BackBufferWidth { get; set; }
    int BackBufferHeight { get; set; }
    bool IsFullScreen { get; set; }
    bool HardwareModeSwitch { get; set; }
    bool VSync { get; set; }
    void ApplyChanges();
}

public sealed class DisplaySettingsApplicator
{
    private readonly IDisplaySettingsTarget _target;

    public DisplaySettingsApplicator(IDisplaySettingsTarget target, DisplaySettings initialSettings)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        CurrentSettings = initialSettings ?? throw new ArgumentNullException(nameof(initialSettings));
    }

    public DisplaySettings CurrentSettings { get; private set; }

    public bool TryApply(DisplaySettings requested, int logicalWidth, int logicalHeight)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (logicalWidth <= 0 || logicalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }

        var normalized = requested with
        {
            WindowScale = Math.Clamp(
                requested.WindowScale,
                DisplaySettings.MinimumWindowScale,
                DisplaySettings.MaximumWindowScale)
        };
        var previous = CaptureTarget();
        try
        {
            Configure(normalized, logicalWidth, logicalHeight);
            _target.ApplyChanges();
            CurrentSettings = normalized;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            RestoreTarget(previous);
            try
            {
                _target.ApplyChanges();
            }
            catch (Exception restoreException) when (IsRecoverable(restoreException))
            {
                // The original values remain assigned even if the underlying device cannot reapply them.
            }

            return false;
        }
    }

    public static Rectangle CalculateLetterbox(int logicalWidth, int logicalHeight, int outputWidth, int outputHeight)
    {
        if (logicalWidth <= 0 || logicalHeight <= 0 || outputWidth <= 0 || outputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }

        var scale = Math.Min((float)outputWidth / logicalWidth, (float)outputHeight / logicalHeight);
        var width = Math.Max(1, (int)MathF.Round(logicalWidth * scale));
        var height = Math.Max(1, (int)MathF.Round(logicalHeight * scale));
        return new Rectangle((outputWidth - width) / 2, (outputHeight - height) / 2, width, height);
    }

    private void Configure(DisplaySettings settings, int logicalWidth, int logicalHeight)
    {
        _target.HardwareModeSwitch = false;
        _target.IsFullScreen = settings.WindowMode == WindowMode.BorderlessFullscreen;
        _target.VSync = settings.VSync;
        _target.BackBufferWidth = _target.IsFullScreen
            ? _target.DesktopWidth
            : logicalWidth * settings.WindowScale;
        _target.BackBufferHeight = _target.IsFullScreen
            ? _target.DesktopHeight
            : logicalHeight * settings.WindowScale;
    }

    private TargetState CaptureTarget() => new(
        _target.BackBufferWidth,
        _target.BackBufferHeight,
        _target.IsFullScreen,
        _target.HardwareModeSwitch,
        _target.VSync);

    private void RestoreTarget(TargetState state)
    {
        _target.BackBufferWidth = state.Width;
        _target.BackBufferHeight = state.Height;
        _target.IsFullScreen = state.IsFullScreen;
        _target.HardwareModeSwitch = state.HardwareModeSwitch;
        _target.VSync = state.VSync;
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException;

    private sealed record TargetState(
        int Width,
        int Height,
        bool IsFullScreen,
        bool HardwareModeSwitch,
        bool VSync);
}
