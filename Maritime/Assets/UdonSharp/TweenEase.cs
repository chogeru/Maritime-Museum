
using UnityEngine;

/// <summary>
/// Shared easing curves for hand-rolled tweens (UdonSharp can't sample AnimationCurve
/// assets at runtime the way regular C# can, so these are plain math instead).
/// </summary>
public static class TweenEase
{
    /// Overshoots past 1 then settles back — good for an arrival/reveal with a bit of punch.
    public static float OutBack(float t, float overshoot)
    {
        float t1 = t - 1f;
        return t1 * t1 * ((overshoot + 1f) * t1 + overshoot) + 1f;
    }

    /// Smooth accelerate-then-decelerate — good for a plain, non-flashy move.
    public static float InOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    /// Starts slow, accelerates into the end — good for a quick shrink/exit.
    public static float InCubic(float t)
    {
        return t * t * t;
    }
}
