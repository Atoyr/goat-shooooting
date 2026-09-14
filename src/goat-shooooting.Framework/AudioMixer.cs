using GoatShooooting.Platform;

namespace GoatShooooting.Framework;

public static class AudioMixer
{
    public static float CalculateEffectiveVolume(AudioSettings settings, float soundBaseVolume) =>
        CalculateEffectiveVolume(settings, "effect", soundBaseVolume);

    public static float CalculateEffectiveVolume(AudioSettings settings, string category, float soundBaseVolume)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!float.IsFinite(soundBaseVolume))
        {
            throw new ArgumentOutOfRangeException(nameof(soundBaseVolume));
        }

        var categoryVolume = category switch
        {
            "music" => settings.MusicVolume,
            "voice" => settings.VoiceVolume,
            _ => settings.EffectsVolume
        };
        return settings.Muted
            ? 0
            : Math.Clamp(settings.MasterVolume, 0, 1) *
                Math.Clamp(categoryVolume, 0, 1) *
                Math.Clamp(soundBaseVolume, 0, 1);
    }
}
