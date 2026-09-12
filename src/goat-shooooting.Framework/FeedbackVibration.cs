using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public readonly record struct VibrationPulse(float LeftMotor, float RightMotor, float Duration);

public static class FeedbackVibration
{
    public static VibrationPulse GetPulse(SimulationFeedback feedback, bool enabled)
    {
        if (!enabled)
        {
            return default;
        }

        if (feedback.BombsUsed > 0)
        {
            return new VibrationPulse(0.8f, 0.55f, 0.3f);
        }

        if (feedback.PlayerHits > 0)
        {
            return new VibrationPulse(0.65f, 0.35f, 0.2f);
        }

        if (feedback.EnemiesDestroyed > 0)
        {
            return new VibrationPulse(0.2f, 0.35f, 0.08f);
        }

        return default;
    }
}
