
using UnityEngine;
using UdonSharp;

// Press the start cube once to begin the dive observation:
// Phase 1 (open water) - caustics + light shafts are on, fog is a bright blue, one fish at a
// time swims in from its resting spot to a point around the player, holds while its narration
// plays, then swims back and the next one comes forward (same shape as the planetarium tour,
// adapted for fish + voice narration instead of subtitles).
// Phase 2 (deep sea) - once every Phase 1 fish has been shown, the caustics/light shafts snap
// off and the fog/ambient light fades down to a dark, pressure-deep look over a few seconds,
// then the Phase 2 fish are presented the same way.
// At the very end the environment eases back to the Phase 1 look and the start cube reappears
// so the next visitor can run the tour again.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UnderwaterObservationController : UdonSharpBehaviour
{
    [Header("Start Cube")]
    [SerializeField] private GameObject startCube;

    [Header("Presentation points (fish[i] swims to presentPoints[i % presentPoints.Length])")]
    [SerializeField] private Transform[] presentPoints;

    [Header("Phase 1 - Open Ocean")]
    [SerializeField] private Transform[] phase1Fish;
    [SerializeField] private AudioSource[] phase1NarrationSources;
    [SerializeField] private AudioClip[] phase1NarrationClips;

    [Header("Phase 1 FX (caustics, light shafts - snapped off at the transition)")]
    [SerializeField] private GameObject[] phase1FxObjects;

    [Header("Phase 2 - Deep Sea")]
    [SerializeField] private Transform[] phase2Fish;
    [SerializeField] private AudioSource[] phase2NarrationSources;
    [SerializeField] private AudioClip[] phase2NarrationClips;

    [Header("Timing")]
    [SerializeField] private float swimInDuration = 3f;
    [SerializeField] private float holdDuration = 6f;
    [SerializeField] private float swimOutDuration = 2.5f;
    [SerializeField] private float gapBetweenFish = 1.5f;
    [SerializeField] private float startDelay = 1.5f;
    [SerializeField] private float noClipHoldDuration = 5f;
    [SerializeField] private float phaseGap = 1f;
    [SerializeField] private float resetDelay = 3f;

    [Header("Hold Bob (gentle idle motion while a fish is being presented)")]
    [SerializeField] private float holdBobHeight = 0.12f;
    [SerializeField] private float holdBobSpeed = 1.1f;

    [Header("Environment - Phase 1 (shallow / open ocean)")]
    [SerializeField] private Color fogColorPhase1 = new Color(0.25f, 0.55f, 0.7f);
    [SerializeField] private float fogDensityPhase1 = 0.02f;
    [SerializeField] private float ambientIntensityPhase1 = 1.2f;
    [SerializeField] private float sunIntensityPhase1 = 1f;

    [Header("Environment - Phase 2 (deep sea)")]
    [SerializeField] private Color fogColorPhase2 = new Color(0.02f, 0.05f, 0.09f);
    [SerializeField] private float fogDensityPhase2 = 0.06f;
    [SerializeField] private float ambientIntensityPhase2 = 0.15f;
    [SerializeField] private float sunIntensityPhase2 = 0.08f;
    [SerializeField] private float environmentTransitionDuration = 5f;

    [Tooltip("Skybox material (Maritime/UnderwaterSkybox) whose _SkyTint is kept in sync with the fog color. Optional.")]
    [SerializeField] private Material underwaterSkybox;

    
[Tooltip("Directional/sun light whose intensity is faded alongside the fog/ambient change. Optional.")]
    [SerializeField] private Light sunLight;

    private bool started;
    private bool inPhase2;
    private int currentIndex;

    // Which fish/audio set is currently being advanced through.
    private Transform[] activeFish;
    private AudioSource[] activeSources;
    private AudioClip[] activeClips;
    private Vector3[] activeHomePositions;
    private Quaternion[] activeHomeRotations;

    private Vector3[] phase1HomePositions;
    private Quaternion[] phase1HomeRotations;
    private Vector3[] phase2HomePositions;
    private Quaternion[] phase2HomeRotations;

    // Incoming (current fish approaching) tween state.
    private bool inActive;
    private Transform inTransform;
    private Vector3 inFrom;
    private Vector3 inTo;
    private Quaternion inFromRot;
    private Quaternion inToRot;
    private float inTimer;
    private float inDuration;

    // Outgoing (previous fish returning home) tween state.
    private bool outActive;
    private Transform outTransform;
    private Vector3 outFrom;
    private Vector3 outTo;
    private Quaternion outFromRot;
    private Quaternion outToRot;
    private float outTimer;
    private float outDuration;

    // Holding-in-place bob state for whichever fish is currently presented.
    private bool holdActive;
    private Transform holdTransform;
    private Vector3 holdBasePos;

    // Environment fade (Phase 1 -> Phase 2, and the final ease back to Phase 1).
    private bool envFading;
    private float envTimer;
    private float envDuration;
    private Color envFogFrom;
    private Color envFogTo;
    private float envFogDensityFrom;
    private float envFogDensityTo;
    private float envAmbientFrom;
    private float envAmbientTo;
    private float envSunFrom;
    private float envSunTo;

    private void Start()
    {
        ApplyEnvironmentImmediate(fogColorPhase1, fogDensityPhase1, ambientIntensityPhase1, sunIntensityPhase1);
        SetActiveAll(phase1FxObjects, true);
        SetActiveAll(phase2Fish, false);
    }

    public override void Interact()
    {
        BeginObservation();
    }

    public void BeginObservation()
    {
        if (started) return;
        started = true;
        SetCubeVisible(false);

        phase1HomePositions = CapturePositions(phase1Fish);
        phase1HomeRotations = CaptureRotations(phase1Fish);
        phase2HomePositions = CapturePositions(phase2Fish);
        phase2HomeRotations = CaptureRotations(phase2Fish);

        inPhase2 = false;
        SetActiveAll(phase2Fish, false);
        activeFish = phase1Fish;
        activeSources = phase1NarrationSources;
        activeClips = phase1NarrationClips;
        activeHomePositions = phase1HomePositions;
        activeHomeRotations = phase1HomeRotations;
        currentIndex = -1;

        SendCustomEventDelayedSeconds(nameof(AdvanceToNext), startDelay);
    }

    private Vector3[] CapturePositions(Transform[] items)
    {
        if (items == null) return new Vector3[0];
        Vector3[] result = new Vector3[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null) result[i] = items[i].position;
        }
        return result;
    }

    private Quaternion[] CaptureRotations(Transform[] items)
    {
        if (items == null) return new Quaternion[0];
        Quaternion[] result = new Quaternion[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null) result[i] = items[i].rotation;
        }
        return result;
    }

    private void SetCubeVisible(bool visible)
    {
        if (startCube == null) return;
        Renderer r = startCube.GetComponent<Renderer>();
        if (r != null) r.enabled = visible;
        Collider c = startCube.GetComponent<Collider>();
        if (c != null) c.enabled = visible;
    }

    
private void SetActiveAll(GameObject[] objects, bool active)
    {
        if (objects == null) return;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null) objects[i].SetActive(active);
        }
    }

    private void SetActiveAll(Transform[] items, bool active)
    {
        if (items == null) return;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null) items[i].gameObject.SetActive(active);
        }
    }

    public void AdvanceToNext()
    {
        holdActive = false;

        // Send the previous fish home before bringing the next one forward.
        if (currentIndex >= 0 && activeFish != null && currentIndex < activeFish.Length && activeFish[currentIndex] != null)
        {
            BeginSwimOut(activeFish[currentIndex], activeFish[currentIndex].position, activeHomePositions[currentIndex],
                activeFish[currentIndex].rotation, activeHomeRotations[currentIndex]);
        }

        currentIndex++;

        if (activeFish == null || currentIndex >= activeFish.Length)
        {
            if (!inPhase2)
            {
                SendCustomEventDelayedSeconds(nameof(BeginPhase2Transition), swimOutDuration + phaseGap);
            }
            else
            {
                SendCustomEventDelayedSeconds(nameof(EndObservation), swimOutDuration + phaseGap);
            }
            return;
        }

        Transform current = activeFish[currentIndex];
        Transform point = PresentPointFor(currentIndex);
        if (current == null || point == null)
        {
            SendCustomEventDelayedSeconds(nameof(AdvanceToNext), 0.1f);
            return;
        }

        Quaternion faceRot = Quaternion.LookRotation(point.forward, Vector3.up);
        BeginSwimIn(current, activeHomePositions[currentIndex], point.position, activeHomeRotations[currentIndex], faceRot);

        float wait = noClipHoldDuration;
        if (activeSources != null && activeClips != null
            && currentIndex < activeSources.Length && currentIndex < activeClips.Length
            && activeSources[currentIndex] != null && activeClips[currentIndex] != null)
        {
            activeSources[currentIndex].clip = activeClips[currentIndex];
            activeSources[currentIndex].Play();
            wait = activeClips[currentIndex].length;
        }
        else
        {
            wait = holdDuration;
        }

        float total = swimInDuration + wait + gapBetweenFish;
        SendCustomEventDelayedSeconds(nameof(AdvanceToNext), total);
    }

    private Transform PresentPointFor(int index)
    {
        if (presentPoints == null || presentPoints.Length == 0) return null;
        return presentPoints[index % presentPoints.Length];
    }

    private void BeginSwimIn(Transform target, Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot)
    {
        inTransform = target;
        inFrom = from;
        inTo = to;
        inFromRot = fromRot;
        inToRot = toRot;
        inTimer = 0f;
        inDuration = Mathf.Max(0.01f, swimInDuration);
        inActive = true;
    }

    private void BeginSwimOut(Transform target, Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot)
    {
        outTransform = target;
        outFrom = from;
        outTo = to;
        outFromRot = fromRot;
        outToRot = toRot;
        outTimer = 0f;
        outDuration = Mathf.Max(0.01f, swimOutDuration);
        outActive = true;
    }

    public void BeginPhase2Transition()
    {
        SetActiveAll(phase1FxObjects, false);
        BeginEnvironmentFade(fogColorPhase2, fogDensityPhase2, ambientIntensityPhase2, sunIntensityPhase2, environmentTransitionDuration);
        SetActiveAll(phase2Fish, true);
        SendCustomEventDelayedSeconds(nameof(StartPhase2Fish), environmentTransitionDuration);
    }

    public void StartPhase2Fish()
    {
        inPhase2 = true;
        activeFish = phase2Fish;
        activeSources = phase2NarrationSources;
        activeClips = phase2NarrationClips;
        activeHomePositions = phase2HomePositions;
        activeHomeRotations = phase2HomeRotations;
        currentIndex = -1;
        AdvanceToNext();
    }

    public void EndObservation()
    {
        SetActiveAll(phase2Fish, false);
        BeginEnvironmentFade(fogColorPhase1, fogDensityPhase1, ambientIntensityPhase1, sunIntensityPhase1, environmentTransitionDuration);
        SendCustomEventDelayedSeconds(nameof(ReopenCube), environmentTransitionDuration + resetDelay);
    }

    public void ReopenCube()
    {
        SetActiveAll(phase1FxObjects, true);
        SetCubeVisible(true);
        started = false;
        inPhase2 = false;
        currentIndex = -1;
    }

    private void ApplyEnvironmentImmediate(Color fogColor, float fogDensity, float ambientIntensity, float sunIntensity)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientIntensity = ambientIntensity;
        if (sunLight != null) sunLight.intensity = sunIntensity;
        if (underwaterSkybox != null) underwaterSkybox.SetColor("_SkyTint", fogColor);
    }

    private void BeginEnvironmentFade(Color fogColor, float fogDensity, float ambientIntensity, float sunIntensity, float duration)
    {
        envFogFrom = RenderSettings.fogColor;
        envFogTo = fogColor;
        envFogDensityFrom = RenderSettings.fogDensity;
        envFogDensityTo = fogDensity;
        envAmbientFrom = RenderSettings.ambientIntensity;
        envAmbientTo = ambientIntensity;
        envSunFrom = sunLight != null ? sunLight.intensity : 0f;
        envSunTo = sunIntensity;
        envTimer = 0f;
        envDuration = Mathf.Max(0.01f, duration);
        envFading = true;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (inActive && inTransform != null)
        {
            inTimer += dt;
            float t = Mathf.Clamp01(inTimer / inDuration);
            float e = TweenEase.InOutCubic(t);
            inTransform.position = Vector3.LerpUnclamped(inFrom, inTo, e);
            inTransform.rotation = Quaternion.Slerp(inFromRot, inToRot, e);
            if (t >= 1f)
            {
                inActive = false;
                holdActive = true;
                holdTransform = inTransform;
                holdBasePos = inTo;
            }
        }

        if (outActive && outTransform != null)
        {
            outTimer += dt;
            float t = Mathf.Clamp01(outTimer / outDuration);
            float e = TweenEase.InOutCubic(t);
            outTransform.position = Vector3.LerpUnclamped(outFrom, outTo, e);
            outTransform.rotation = Quaternion.Slerp(outFromRot, outToRot, e);
            if (t >= 1f) outActive = false;
        }

        if (holdActive && holdTransform != null)
        {
            float bob = Mathf.Sin(Time.time * holdBobSpeed) * holdBobHeight;
            holdTransform.position = holdBasePos + new Vector3(0f, bob, 0f);
        }

        if (envFading)
        {
            envTimer += dt;
            float t = Mathf.Clamp01(envTimer / envDuration);
            RenderSettings.fogColor = Color.Lerp(envFogFrom, envFogTo, t);
            RenderSettings.fogDensity = Mathf.Lerp(envFogDensityFrom, envFogDensityTo, t);
            RenderSettings.ambientIntensity = Mathf.Lerp(envAmbientFrom, envAmbientTo, t);
            if (sunLight != null) sunLight.intensity = Mathf.Lerp(envSunFrom, envSunTo, t);
            if (underwaterSkybox != null) underwaterSkybox.SetColor("_SkyTint", RenderSettings.fogColor);
            if (t >= 1f) envFading = false;
        }
    }
}
