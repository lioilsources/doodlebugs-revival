using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Coroutine tweens on unscaled time (ROADMAP Phase 0 "Tween util", Prompts/30
/// §5). No third-party package. Every call returns the coroutine so a caller
/// can stop it; the runner lives on a hidden DontDestroyOnLoad object.
/// </summary>
public static class UiTween
{
    public enum Ease { Linear, OutCubic, OutBack, InOutSine }

    private class Runner : MonoBehaviour { }

    private static Runner _runner;

    private static Runner R
    {
        get
        {
            if (_runner == null)
            {
                var go = new GameObject("UiTween") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<Runner>();
            }
            return _runner;
        }
    }

    public static float Apply(Ease ease, float t)
    {
        t = Mathf.Clamp01(t);
        switch (ease)
        {
            case Ease.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
            case Ease.OutBack:
                const float c1 = 1.70158f, c3 = c1 + 1f;
                return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            case Ease.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
            default: return t;
        }
    }

    public static Coroutine Run(float seconds, Action<float> step, Ease ease = Ease.OutCubic,
        float delay = 0f, Action done = null)
    {
        return R.StartCoroutine(Routine(seconds, step, ease, delay, done));
    }

    public static void Stop(Coroutine c)
    {
        if (c != null && _runner != null) _runner.StopCoroutine(c);
    }

    private static IEnumerator Routine(float seconds, Action<float> step, Ease ease, float delay, Action done)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        float t = 0f;
        while (t < seconds)
        {
            step(Apply(ease, t / seconds));
            yield return null;
            t += Time.unscaledDeltaTime;
        }
        step(1f);
        done?.Invoke();
    }

    // --- shortcuts ---------------------------------------------------------

    public static Coroutine Scale(Transform target, Vector3 from, Vector3 to, float seconds,
        Ease ease = Ease.OutBack, float delay = 0f)
    {
        if (target == null) return null;
        target.localScale = from;
        return Run(seconds, k => { if (target != null) target.localScale = Vector3.LerpUnclamped(from, to, k); }, ease, delay);
    }

    public static Coroutine Fade(CanvasGroup group, float from, float to, float seconds,
        Ease ease = Ease.OutCubic, float delay = 0f)
    {
        if (group == null) return null;
        group.alpha = from;
        return Run(seconds, k => { if (group != null) group.alpha = Mathf.LerpUnclamped(from, to, k); }, ease, delay);
    }

    public static Coroutine Move(RectTransform target, Vector2 from, Vector2 to, float seconds,
        Ease ease = Ease.OutCubic, float delay = 0f)
    {
        if (target == null) return null;
        target.anchoredPosition = from;
        return Run(seconds, k => { if (target != null) target.anchoredPosition = Vector2.LerpUnclamped(from, to, k); }, ease, delay);
    }

    /// <summary>Scale up by `amount` and back, once.</summary>
    public static Coroutine Punch(Transform target, float amount = 0.08f, float seconds = 0.18f)
    {
        if (target == null) return null;
        Vector3 baseScale = Vector3.one;
        return Run(seconds, k =>
        {
            if (target != null) target.localScale = baseScale * (1f + amount * Mathf.Sin(k * Mathf.PI));
        }, Ease.Linear);
    }

    /// <summary>Card entrance: slide in from the right with a fade, staggered by index.</summary>
    public static void EnterFromRight(RectTransform target, int index, float distance = 24f)
    {
        if (target == null) return;
        var group = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();
        Vector2 end = target.anchoredPosition;
        float delay = 0.06f * index;
        group.alpha = 0f;
        Move(target, end + new Vector2(distance, 0f), end, 0.28f, Ease.OutBack, delay);
        Fade(group, 0f, 1f, 0.2f, Ease.OutCubic, delay);
    }
}
