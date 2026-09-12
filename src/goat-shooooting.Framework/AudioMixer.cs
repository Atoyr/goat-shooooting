using GoatShooooting.Platform;

namespace GoatShooooting.Framework;

public static class AudioMixer
{
    public static float CalculateEffectiveVolume(AudioSettings settings, float soundBaseVolume)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!float.IsFinite(soundBaseVolume))
        {
            throw new ArgumentOutOfRangeException(nameof(soundBaseVolume));
        }

        return settings.Muted
            ? 0
            : Math.Clamp(settings.MasterVolume, 0, 1) *
                Math.Clamp(settings.EffectsVolume, 0, 1) *
                Math.Clamp(soundBaseVolume, 0, 1);
    }
}
