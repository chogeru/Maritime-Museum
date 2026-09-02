
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Generic "bring it to the player" tour: the current item glides in front of the
/// player, highlighted (a glow light gently pulses, with a bright flash on arrival), and
/// named on a floating label while its narration plays. When it's done it glides back to
/// its resting position and the next item takes its place, before advancing to the next.
/// Originally built for a planet-viewing exhibit, but nothing here is astronomy-specific -
/// it works for any set of "bring close and highlight" exhibits.
///
/// Data is authored in a PresentationTourLibrary asset for convenience (name / target /
/// highlight light / narration clip together on one row), then copied into the arrays
/// below via the "Sync From Library" button in this component's inspector. UdonSharp
/// cannot read fields off a plain ScriptableObject at runtime, so the arrays here are
/// what actually get used in-game.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class PresentationTourController : UdonSharpBehaviour
{
    [Tooltip("Planets in presentation order (e.g. Sun, Mercury, Venus, ... Neptune).")]
    public Transform[] targets;

    [Tooltip("Display name per target, same order/length as Targets.")]
    public string[] planetNames;

    [Tooltip("One highlight Light per target, same order/length as Targets. Intensity is pulsed on/off.")]
    public Light[] highlightLights;

    public AudioSource audioSource;

    [Tooltip("One narration clip per target, same order/length as Targets. Leave an entry empty to fall back to Fallback Duration for that planet.")]
    public AudioClip[] narrationClips;

    [Header("Subtitles (auto-timed to narration clip pauses)")]
    [Tooltip("All subtitle clauses across every planet, flattened in order. Populated by 'Sync From Library'.")]
    public string[] subtitleLines;

    [Tooltip("Start time (seconds from that planet's narration clip start) for each entry in Subtitle Lines, same order/length.")]
    public float[] subtitleStartTimes;

    [Tooltip("How many consecutive entries in Subtitle Lines belong to each planet, same order/length as Targets.")]
    public int[] subtitleCounts;

    [Tooltip("Caption HUD that displays subtitles while narration plays. Optional - leave empty to disable subtitles.")]
    public SubtitleDisplay subtitleDisplay;

    [Tooltip("How long to show a planet if it has no narration clip assigned.")]
    public float fallbackDuration = 6f;

    [Tooltip("Silent pause after a planet's narration ends before moving on to the next one.")]
    public float narrationCooldown = 1.5f;

    [Header("Highlight Pulse")]
    public float highlightIntensityMin = 1.2f;
    public float highlightIntensityMax = 4f;
    public float pulseSpeed = 1.6f;
    [Tooltip("Extra intensity multiplier for the brief flash the instant a planet arrives.")]
    public float arrivalFlashMultiplier = 2.2f;
    [Tooltip("How long the arrival flash takes to settle back into the normal pulse.")]
    public float arrivalFlashDecay = 0.35f;

    [Header("Name Label")]
    [Tooltip("Single shared label shown only above whichever planet is currently being explained.")]
    public UnityEngine.UI.Text nameLabel;
    public Vector3 nameLabelOffset = new Vector3(0f, 1.5f, 0f);
    [Tooltip("Label scale at the start of its fade-in (multiplier of its authored scale), so it settles in with a bit of pop instead of just appearing.")]
    public float nameLabelStartScale = 0.6f;

    [Header("Bring Planet In Front")]
    [Tooltip("Move the current planet in front of the player while it's being presented.")]
    public bool bringToFront = true;
    public float presentationDistanceMultiplier = 1.8f;
    public float presentationMinDistance = 4f;
    public float presentationMaxDistance = 15f;
    [Tooltip("Largest diameter (world units) a planet is allowed to appear at while presented. Huge planets like Jupiter/Saturn are scaled down to this so they don't engulf the player; smaller planets are unaffected. 0 disables the cap.")]
    public float maxPresentationDiameter = 7f;

    [Header("Warp / Move Feel")]
    [Tooltip("Seconds for the planet to glide into presentation position.")]
    public float moveInDuration = 1.1f;
    [Tooltip("Seconds for the planet to glide back to its orbital position.")]
    public float moveOutDuration = 0.8f;
    [Tooltip("How strongly the incoming planet overshoots and settles (0 = no overshoot). Kept low - a high value makes the planet visibly fly past its spot and spring back, which reads as a glitch rather than 'impact'.")]
    public float arrivalOvershoot = 0.4f;
    [Tooltip("Peak scale multiplier applied to the planet as it lands, for a bit of punch.")]
    public float arrivalScalePunch = 1.04f;

    [Tooltip("Small one-shot particle puff played at the spot a planet settles back into orbit.")]
    public ParticleSystem departurePuff;

    public ExperienceManager manager;

    private int currentIndex = -1;
    private Light activeLight;
    private Transform activeTarget;
    private Vector3[] originalLocalPositions;

    // Planets that start inactive in the scene (e.g. Earth) are switched on only while they're
    // being presented, then switched back off - everything else stays exactly as authored.
    private bool[] originalActiveStates;
    private bool outWasHidden;

    // Incoming (current planet flying to presentation position) tween state.
    private bool inActive;
    private Transform inTransform;
    private Vector3 inStartLocalPos;
    private Vector3 inEndLocalPos;
    private Vector3 inBaseScale;
    private Vector3 inPresentationScale;
    private float inTimer;
    private float inDuration;
    private float inRadius;
    private TrailRenderer inTrail;

    // Outgoing (previous planet returning home) tween state.
    private bool outActive;
    private Transform outTransform;
    private Vector3 outStartLocalPos;
    private Vector3 outEndLocalPos;
    private Light outLight;
    private float outLightStartIntensity;
    private float outTimer;
    private float outDuration;
    private TrailRenderer outTrail;
    private Vector3 outScaleStart;
    private Vector3 outScaleEnd;

    // Arrival flash state (decays into the normal pulse).
    private float flashTimer;

    // Name label fade/scale-in state (captured once so we know its authored/"full" scale).
    private Vector3 nameLabelBaseScale;
    private bool nameLabelBaseScaleCaptured;
    private bool outLabelActive;
    private float outLabelStartAlpha;

    // The next planet waits until the previous one has fully returned home (outActive finishes)
    // before it starts flying in - otherwise the two can visibly overlap/clip through each other
    // near the presentation spot, and the new narration can start before the old one has settled.
    private bool pendingIncoming;

    public void BeginTour()
    {
        inActive = false;
        outActive = false;
        pendingIncoming = false;
        outLabelActive = false;
        HideCurrentImmediate();
        currentIndex = -1;
        CaptureOriginalPositions();
        Advance();
    }

    private void CaptureOriginalPositions()
    {
        if (targets == null) return;
        if (originalLocalPositions != null && originalLocalPositions.Length == targets.Length) return;
        originalLocalPositions = new Vector3[targets.Length];
        originalActiveStates = new bool[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
            {
                originalLocalPositions[i] = targets[i].localPosition;
                originalActiveStates[i] = targets[i].gameObject.activeSelf;
            }
        }
    }

    public void Advance()
    {
        BeginReturnHome();

        currentIndex++;

        if (targets == null || currentIndex >= targets.Length)
        {
            if (manager != null) manager.OnPlanetTourComplete();
            return;
        }

        // Wait for the previous planet's return trip to finish before the next one starts -
        // avoids the two overlapping in space or their narrations overlapping in time.
        if (outActive)
        {
            pendingIncoming = true;
        }
        else
        {
            StartIncoming();
        }
    }

    private void StartIncoming()
    {
        Transform current = targets[currentIndex];
        activeTarget = current;

        if (originalActiveStates != null && currentIndex < originalActiveStates.Length && !originalActiveStates[currentIndex])
        {
            current.gameObject.SetActive(true);
        }

        if (highlightLights != null && currentIndex < highlightLights.Length && highlightLights[currentIndex] != null)
        {
            activeLight = highlightLights[currentIndex];
            activeLight.intensity = highlightIntensityMin;
        }
        flashTimer = 0f;

        if (nameLabel != null)
        {
            if (!nameLabelBaseScaleCaptured)
            {
                nameLabelBaseScale = nameLabel.transform.localScale;
                nameLabelBaseScaleCaptured = true;
            }
            if (planetNames != null && currentIndex < planetNames.Length) nameLabel.text = planetNames[currentIndex];
            Color c = nameLabel.color;
            c.a = 0f;
            nameLabel.color = c;
            nameLabel.transform.localScale = nameLabelBaseScale * nameLabelStartScale;
            nameLabel.gameObject.SetActive(true);
            outLabelActive = false;
        }

        if (bringToFront)
        {
            BeginMoveIn(current);
        }
        else if (nameLabel != null)
        {
            float radius = current.localScale.x * 0.5f;
            nameLabel.transform.position = current.position + Vector3.up * (radius + 1.2f);
        }

        float wait = fallbackDuration;
        if (audioSource != null && narrationClips != null && currentIndex < narrationClips.Length && narrationClips[currentIndex] != null)
        {
            audioSource.clip = narrationClips[currentIndex];
            audioSource.Play();
            wait = narrationClips[currentIndex].length + narrationCooldown;

            PlaySubtitlesFor(currentIndex, narrationClips[currentIndex].length);
        }

        // Make sure the reveal (glide + flash) never gets cut off by a very short narration clip.
        if (bringToFront && wait < moveInDuration + 0.5f) wait = moveInDuration + 0.5f;

        SendCustomEventDelayedSeconds(nameof(Advance), wait);
    }

    // Slices this planet's clauses out of the flattened Subtitle Lines/Start Times arrays
    // (using Subtitle Counts to find its run) and hands them to the caption HUD.
    private void PlaySubtitlesFor(int planetIndex, float clipLength)
    {
        if (subtitleDisplay == null || subtitleCounts == null || planetIndex >= subtitleCounts.Length) return;

        int start = 0;
        for (int i = 0; i < planetIndex; i++) start += subtitleCounts[i];
        int count = subtitleCounts[planetIndex];
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

    private void BeginMoveIn(Transform current)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;

        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        float diameter = current.localScale.x;

        // Huge planets (Jupiter, Saturn, ...) get scaled down just for the presentation so they
        // don't engulf the player when brought up close; the distance placement uses this
        // presentation size too, so a shrunk planet is placed close rather than far and tiny.
        float presentationScaleFactor = 1f;
        if (maxPresentationDiameter > 0f && diameter > maxPresentationDiameter)
        {
            presentationScaleFactor = maxPresentationDiameter / diameter;
        }
        float presentationDiameter = diameter * presentationScaleFactor;

        float distance = Mathf.Clamp(presentationDiameter * presentationDistanceMultiplier, presentationMinDistance, presentationMaxDistance);
        Vector3 forward = head.rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 targetWorldPos = head.position + forward * distance;

        Vector3 targetLocalPos = current.parent != null
            ? current.parent.InverseTransformPoint(targetWorldPos)
            : targetWorldPos;

        inTransform = current;
        inStartLocalPos = current.localPosition;
        inEndLocalPos = targetLocalPos;
        inBaseScale = current.localScale;
        inPresentationScale = inBaseScale * presentationScaleFactor;
        inRadius = presentationDiameter * 0.5f;
        inTimer = 0f;
        inDuration = Mathf.Max(0.01f, moveInDuration);
        inActive = true;

        inTrail = current.GetComponent<TrailRenderer>();
        if (inTrail != null)
        {
            inTrail.Clear();
            inTrail.emitting = true;
        }
    }

    private void BeginReturnHome()
    {
        // Cancel/finish any in-flight incoming tween immediately so it doesn't fight the outgoing one.
        if (inActive && inTransform != null)
        {
            inTransform.localPosition = inEndLocalPos;
            inTransform.localScale = inPresentationScale;
            inActive = false;
            if (inTrail != null) inTrail.emitting = false;
        }

        if (nameLabel != null && nameLabel.gameObject.activeSelf)
        {
            outLabelActive = true;
            outLabelStartAlpha = nameLabel.color.a;
        }

        if (bringToFront && activeTarget != null && originalLocalPositions != null
            && currentIndex >= 0 && currentIndex < originalLocalPositions.Length)
        {
            outTransform = activeTarget;
            outStartLocalPos = activeTarget.localPosition;
            outEndLocalPos = originalLocalPositions[currentIndex];
            outScaleStart = activeTarget.localScale;
            outScaleEnd = inBaseScale;
            outLight = activeLight;
            outLightStartIntensity = activeLight != null ? activeLight.intensity : 0f;
            outTimer = 0f;
            outDuration = Mathf.Max(0.01f, moveOutDuration);
            outActive = true;

            outTrail = activeTarget.GetComponent<TrailRenderer>();
            if (outTrail != null)
            {
                outTrail.Clear();
                outTrail.emitting = true;
            }

            outWasHidden = originalActiveStates != null && currentIndex < originalActiveStates.Length && !originalActiveStates[currentIndex];
        }

        activeLight = null;
        activeTarget = null;
    }

    private void HideCurrentImmediate()
    {
        if (activeLight != null) activeLight.intensity = 0f;
        activeLight = null;

        if (nameLabel != null) nameLabel.gameObject.SetActive(false);

        if (bringToFront && activeTarget != null && originalLocalPositions != null
            && currentIndex >= 0 && currentIndex < originalLocalPositions.Length)
        {
            activeTarget.localPosition = originalLocalPositions[currentIndex];
        }
        activeTarget = null;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (inActive)
        {
            inTimer += dt;
            float t = Mathf.Clamp01(inTimer / inDuration);
            float e = TweenEase.OutBack(t, arrivalOvershoot);
            inTransform.localPosition = Vector3.LerpUnclamped(inStartLocalPos, inEndLocalPos, e);

            // Shrinks down to the (possibly capped) presentation size while flying in, then
            // punches up slightly alongside the overshoot for a bit of landing impact.
            float scaleWave = 1f + (arrivalScalePunch - 1f) * SmoothPunch(t);
            inTransform.localScale = Vector3.LerpUnclamped(inBaseScale, inPresentationScale, e) * scaleWave;

            if (nameLabel != null)
            {
                nameLabel.transform.position = inTransform.position + Vector3.up * (inRadius + 1.2f);

                // Label fades and scales in alongside the planet's own arrival, using the same
                // clamped easing curve so it settles with a matching bit of pop.
                float labelE = Mathf.Clamp01(e);
                Color c = nameLabel.color;
                c.a = labelE;
                nameLabel.color = c;
                nameLabel.transform.localScale = Vector3.LerpUnclamped(nameLabelBaseScale * nameLabelStartScale, nameLabelBaseScale, e);
            }

            // Stop emitting before the overshoot settle: EaseOutBack briefly reverses direction
            // there, and a TrailRenderer can't render that reversal without folding on itself.
            if (inTrail != null) inTrail.emitting = t < 0.82f;

            if (t >= 1f)
            {
                inTransform.localPosition = inEndLocalPos;
                inTransform.localScale = inPresentationScale;
                inActive = false;
                flashTimer = arrivalFlashDecay;

                if (nameLabel != null)
                {
                    Color c = nameLabel.color;
                    c.a = 1f;
                    nameLabel.color = c;
                    nameLabel.transform.localScale = nameLabelBaseScale;
                }
            }
        }

        if (outActive)
        {
            outTimer += dt;
            float t = Mathf.Clamp01(outTimer / outDuration);
            float e = TweenEase.InOutCubic(t);
            outTransform.localPosition = Vector3.Lerp(outStartLocalPos, outEndLocalPos, e);
            outTransform.localScale = Vector3.Lerp(outScaleStart, outScaleEnd, e);
            if (outLight != null) outLight.intensity = Mathf.Lerp(outLightStartIntensity, 0f, e);

            if (outLabelActive && nameLabel != null)
            {
                Color c = nameLabel.color;
                c.a = Mathf.Lerp(outLabelStartAlpha, 0f, e);
                nameLabel.color = c;
            }

            if (t >= 1f)
            {
                outTransform.localPosition = outEndLocalPos;
                outTransform.localScale = outScaleEnd;
                if (outLight != null) outLight.intensity = 0f;
                if (outTrail != null) outTrail.emitting = false;
                if (departurePuff != null)
                {
                    departurePuff.transform.position = outTransform.position;
                    // Tint the puff to match this planet's own theme color (reusing the
                    // highlight light's color, already set per-planet) instead of one
                    // generic white puff for every planet.
                    if (outLight != null)
                    {
                        ParticleSystem.MainModule puffMain = departurePuff.main;
                        puffMain.startColor = new ParticleSystem.MinMaxGradient(outLight.color);
                    }
                    departurePuff.Clear();
                    departurePuff.Play();
                }
                if (outWasHidden) outTransform.gameObject.SetActive(false);
                if (outLabelActive && nameLabel != null)
                {
                    nameLabel.gameObject.SetActive(false);
                    outLabelActive = false;
                }
                outActive = false;
                outLight = null;
                outTransform = null;
                outTrail = null;
            }
        }

        if (pendingIncoming && !outActive)
        {
            pendingIncoming = false;
            StartIncoming();
        }

        if (activeLight == null) return;
        float wave = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // 0..1
        float baseIntensity = Mathf.Lerp(highlightIntensityMin, highlightIntensityMax, wave);

        if (flashTimer > 0f)
        {
            flashTimer -= dt;
            float flashT = Mathf.Clamp01(flashTimer / arrivalFlashDecay);
            float flashBoost = (highlightIntensityMax * arrivalFlashMultiplier - highlightIntensityMax) * flashT;
            activeLight.intensity = baseIntensity + flashBoost;
        }
        else
        {
            activeLight.intensity = baseIntensity;
        }
    }

    // Smooth 0->1->0 hump peaking near the arrival overshoot, used to drive the scale punch.
    private static float SmoothPunch(float t)
    {
        float x = Mathf.Clamp01(t * 1.4f);
        return Mathf.Sin(x * Mathf.PI) * (1f - t * 0.3f);
    }
}
