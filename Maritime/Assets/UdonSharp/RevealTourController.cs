
using UnityEngine;
using UdonSharp;

/// <summary>
/// Generic "reveal one at a time" tour: only the current item is shown (which makes it
/// stand out on its own), its narration plays, then it shrinks away and the next one
/// grows in. Originally built for a constellation-viewing exhibit, but the mechanic
/// (and this class) has nothing astronomy-specific about it - it works for any set of
/// "reveal in place" exhibits (constellations, display cases, dioramas, ...).
///
/// Data is authored in a RevealTourLibrary asset for convenience (name / target /
/// narration clip together on one row), then copied into the arrays below via the
/// "Sync From Library" button in this component's inspector. UdonSharp cannot read
/// fields off a plain ScriptableObject at runtime, so the arrays here are what actually
/// get used in-game.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class RevealTourController : UdonSharpBehaviour
{
    [Tooltip("Constellation root objects in presentation order. Each is scaled in/out and toggled active/inactive.")]
    public GameObject[] constellations;

    public AudioSource audioSource;

    [Tooltip("One narration clip per constellation, same order/length as Constellations. Leave an entry empty to fall back to Fallback Duration for that constellation.")]
    public AudioClip[] narrationClips;

    [Header("Subtitles (auto-timed to narration clip pauses)")]
    [Tooltip("All subtitle clauses across every constellation, flattened in order. Populated by 'Sync From Library'.")]
    public string[] subtitleLines;

    [Tooltip("Start time (seconds from that constellation's narration clip start) for each entry in Subtitle Lines, same order/length.")]
    public float[] subtitleStartTimes;

    [Tooltip("How many consecutive entries in Subtitle Lines belong to each constellation, same order/length as Constellations.")]
    public int[] subtitleCounts;

    [Tooltip("Caption HUD that displays subtitles while narration plays. Optional - leave empty to disable subtitles.")]
    public SubtitleDisplay subtitleDisplay;

    [Tooltip("How long to show a constellation if it has no narration clip assigned.")]
    public float fallbackDuration = 8f;

    [Tooltip("Silent pause after a constellation's narration ends before moving on to the next one.")]
    public float narrationCooldown = 1.5f;

    [Header("Reveal / Hide Feel")]
    [Tooltip("Seconds for a constellation to grow in from nothing.")]
    public float revealDuration = 0.9f;
    [Tooltip("Seconds for a constellation to shrink away.")]
    public float hideDuration = 0.5f;
    [Tooltip("How strongly the reveal overshoots and settles (0 = no overshoot). Kept low - a high value makes the whole constellation visibly pop past full size and shrink back, which reads as a glitch.")]
    public float revealOvershoot = 0.4f;

    [Header("Star Line Draw")]
    [Tooltip("Seconds for one constellation line to draw from its start star to its end star.")]
    public float lineDrawDuration = 0.7f;
    [Tooltip("Delay before the next line starts drawing, so lines connect star-by-star instead of all at once.")]
    public float lineStagger = 0.12f;

    [Header("Sparkle")]
    [Tooltip("Optional - the MaterialPulse driving all constellation stars' shared glow. Given a brief bright flash the instant a new constellation begins revealing.")]
    public MaterialPulse starPulse;
    public float revealSparkleMultiplier = 3f;

    public ExperienceManager manager;

    private int currentIndex = -1;
    private Vector3[] originalScales;

    // Incoming (current constellation growing in) tween state.
    private bool inActive;
    private Transform inTransform;
    private Vector3 inTargetScale;
    private float inTimer;
    private float inDuration;

    // Outgoing (previous constellation shrinking away) tween state.
    private bool outActive;
    private GameObject outObject;
    private Transform outTransform;
    private Vector3 outStartScale;
    private float outTimer;
    private float outDuration;

    // Star-by-star line draw state for the current constellation.
    private bool linesActive;
    private LineRenderer[] activeLines;
    private Vector3[] activeLineStarts;
    private Vector3[] activeLineEnds;
    private Color[] activeLineColors;
    private float lineTimer;
    private float lineTotalDuration;

    // Lines wait until the pop-in scale settles before they start drawing.
    private Transform pendingLineDrawTarget;

    public void BeginTour()
    {
        HideAllImmediate();
        currentIndex = -1;
        CaptureOriginalScales();
        Advance();
    }

    private void CaptureOriginalScales()
    {
        if (constellations == null) return;
        if (originalScales != null && originalScales.Length == constellations.Length) return;
        originalScales = new Vector3[constellations.Length];
        for (int i = 0; i < constellations.Length; i++)
        {
            if (constellations[i] != null) originalScales[i] = constellations[i].transform.localScale;
        }
    }

    private void HideAllImmediate()
    {
        inActive = false;
        outActive = false;
        outObject = null;
        outTransform = null;
        linesActive = false;
        activeLines = null;

        if (constellations == null) return;
        for (int i = 0; i < constellations.Length; i++)
        {
            if (constellations[i] != null) constellations[i].SetActive(false);
        }
    }

    public void Advance()
    {
        BeginHideCurrent();

        currentIndex++;

        if (constellations == null || currentIndex >= constellations.Length)
        {
            if (manager != null) manager.OnConstellationTourComplete();
            return;
        }

        GameObject current = constellations[currentIndex];
        float wait = fallbackDuration;
        float estimatedLineDuration = 0f;

        if (current != null && originalScales != null && currentIndex < originalScales.Length)
        {
            current.transform.localScale = Vector3.zero;
            current.SetActive(true);
            BeginReveal(current.transform, originalScales[currentIndex]);

            // Collapse the lines to zero-length right away so the constellation never briefly
            // shows as already-complete during the pop-in - only the actual staggered drawing
            // (triggered once the pop-in settles, see Update) grows them back out.
            PrepareLineDraw(current.transform);
            pendingLineDrawTarget = current.transform;
            if (activeLines != null && activeLines.Length > 0)
            {
                estimatedLineDuration = lineDrawDuration + (activeLines.Length - 1) * lineStagger;
            }

            if (starPulse != null) starPulse.TriggerFlash(revealSparkleMultiplier);
        }

        if (audioSource != null && narrationClips != null && currentIndex < narrationClips.Length && narrationClips[currentIndex] != null)
        {
            audioSource.clip = narrationClips[currentIndex];
            audioSource.Play();
            wait = narrationClips[currentIndex].length + narrationCooldown;

            PlaySubtitlesFor(currentIndex, narrationClips[currentIndex].length);
        }

        // Make sure the reveal (pop-in, then star-by-star line draw) never gets cut off by a short narration clip.
        float minWait = revealDuration + estimatedLineDuration + 0.3f;
        if (wait < minWait) wait = minWait;

        SendCustomEventDelayedSeconds(nameof(Advance), wait);
    }

    // Slices this constellation's clauses out of the flattened Subtitle Lines/Start Times
    // arrays (using Subtitle Counts to find its run) and hands them to the caption HUD.
    private void PlaySubtitlesFor(int constellationIndex, float clipLength)
    {
        if (subtitleDisplay == null || subtitleCounts == null || constellationIndex >= subtitleCounts.Length) return;

        int start = 0;
        for (int i = 0; i < constellationIndex; i++) start += subtitleCounts[i];
        int count = subtitleCounts[constellationIndex];
        if (count <= 0 || subtitleLines == null || subtitleStartTimes == null || start + count > subtitleLines.Length) return;

        string[] lines = new string[count];
        float[] times = new float[count];
        for (int i = 0; i < count; i++)
        {
            lines[i] = subtitleLines[start + i];
            times[i] = subtitleStartTimes[start + i];
        }

        subtitleDisplay.PlaySequence(lines, times, clipLength);
    }

    private void BeginReveal(Transform target, Vector3 targetScale)
    {
        inTransform = target;
        inTargetScale = targetScale;
        inTimer = 0f;
        inDuration = Mathf.Max(0.01f, revealDuration);
        inActive = true;
    }

    // Gathers this constellation's lines and immediately collapses them (zero length, zero
    // alpha) so nothing looks "already drawn" while it's still popping in.
    private void PrepareLineDraw(Transform root)
    {
        activeLines = root.GetComponentsInChildren<LineRenderer>(true);

        if (activeLines == null || activeLines.Length == 0)
        {
            linesActive = false;
            lineTotalDuration = 0f;
            return;
        }

        activeLineStarts = new Vector3[activeLines.Length];
        activeLineEnds = new Vector3[activeLines.Length];
        activeLineColors = new Color[activeLines.Length];

        for (int i = 0; i < activeLines.Length; i++)
        {
            LineRenderer lr = activeLines[i];
            if (lr == null) continue;
            Vector3 p0 = lr.GetPosition(0);
            Vector3 p1 = lr.GetPosition(1);
            activeLineStarts[i] = p0;
            activeLineEnds[i] = p1;
            activeLineColors[i] = lr.startColor;
            lr.SetPosition(1, p0);

            Color faded = activeLineColors[i];
            faded.a = 0f;
            lr.startColor = faded;
            lr.endColor = faded;
        }

        lineTotalDuration = lineDrawDuration + (activeLines.Length - 1) * lineStagger;
        linesActive = false;
    }

    // Kicks off the actual staggered growth of lines already collapsed by PrepareLineDraw.
    private void StartLineDraw()
    {
        if (activeLines == null || activeLines.Length == 0) return;
        lineTimer = 0f;
        linesActive = true;
    }

    private void FinishLineDrawImmediate()
    {
        if (!linesActive || activeLines == null) return;
        for (int i = 0; i < activeLines.Length; i++)
        {
            if (activeLines[i] == null) continue;
            activeLines[i].SetPosition(1, activeLineEnds[i]);
            activeLines[i].startColor = activeLineColors[i];
            activeLines[i].endColor = activeLineColors[i];
        }
        linesActive = false;
    }

    private void BeginHideCurrent()
    {
        // Cancel/finish any in-flight reveal immediately so it doesn't fight the outgoing tween.
        if (inActive && inTransform != null)
        {
            inTransform.localScale = inTargetScale;
            inActive = false;
        }
        pendingLineDrawTarget = null;
        FinishLineDrawImmediate();

        if (constellations == null) return;
        if (currentIndex < 0 || currentIndex >= constellations.Length) return;

        GameObject current = constellations[currentIndex];
        if (current == null) return;

        outObject = current;
        outTransform = current.transform;
        outStartScale = current.transform.localScale;
        outTimer = 0f;
        outDuration = Mathf.Max(0.01f, hideDuration);
        outActive = true;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (inActive)
        {
            inTimer += dt;
            float t = Mathf.Clamp01(inTimer / inDuration);
            float e = TweenEase.OutBack(t, revealOvershoot);
            inTransform.localScale = Vector3.LerpUnclamped(Vector3.zero, inTargetScale, e);

            if (t >= 1f)
            {
                inTransform.localScale = inTargetScale;
                inActive = false;
            }
        }

        if (!inActive && pendingLineDrawTarget != null)
        {
            StartLineDraw();
            pendingLineDrawTarget = null;
        }

        if (outActive)
        {
            outTimer += dt;
            float t = Mathf.Clamp01(outTimer / outDuration);
            float e = TweenEase.InCubic(t);
            outTransform.localScale = Vector3.LerpUnclamped(outStartScale, Vector3.zero, e);

            if (t >= 1f)
            {
                outTransform.localScale = Vector3.zero;
                outObject.SetActive(false);
                outActive = false;
                outObject = null;
                outTransform = null;
            }
        }

        if (linesActive)
        {
            lineTimer += dt;
            bool anyStillDrawing = false;
            for (int i = 0; i < activeLines.Length; i++)
            {
                LineRenderer lr = activeLines[i];
                if (lr == null) continue;
                float localT = Mathf.Clamp01((lineTimer - i * lineStagger) / lineDrawDuration);
                lr.SetPosition(1, Vector3.Lerp(activeLineStarts[i], activeLineEnds[i], localT));

                Color faded = activeLineColors[i];
                faded.a *= localT;
                lr.startColor = faded;
                lr.endColor = faded;

                if (localT < 1f) anyStillDrawing = true;
            }
            if (!anyStillDrawing) linesActive = false;
        }
    }
}
