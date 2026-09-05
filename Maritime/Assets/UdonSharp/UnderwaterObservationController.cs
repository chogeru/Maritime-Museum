
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

    [Tooltip("Only the POSITION of these is used. Their rotation is ignored: heading is taken from " +
             "the path each creature actually swims, because these points were not oriented " +
             "consistently with where their creatures approach from and several of them pointed " +
             "the arriving creature backwards.")]
    [Header("Presentation points (fish[i] swims to presentPoints[i % presentPoints.Length])")]
    [SerializeField] private Transform[] presentPoints;

    [Tooltip("Where the viewer stands. Each present point keeps its authored direction, but the " +
             "distance along it is recomputed from the creature's own size, so a 14 cm clownfish " +
             "and a 4 m barracuda shoal both fill a comparable part of the view. Without this the " +
             "same eight points serve both phases, and the small deep sea species end up as specks. " +
             "Leave empty to measure from the world origin, which is what the ring was laid out around.")]
    [Header("Framing")]
    [SerializeField] private Transform viewerAnchor;
    [Tooltip("Creature length divided by its viewing distance. Higher brings everything closer.")]
    [SerializeField] private float targetApparentSize = 0.38f;
    [SerializeField] private float minPresentDistance = 0.85f;
    [SerializeField] private float maxPresentDistance = 5f;

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
    [SerializeField] private float phaseGap = 1f;
    [SerializeField] private float resetDelay = 3f;

    [Tooltip("How much a creature's size stretches its beat. Lengths at or below the small value " +
             "get the small scales, at or above the large value the large ones.")]
    [Header("Pacing")]
    [SerializeField] private float smallCreatureLength = 0.2f;
    [SerializeField] private float largeCreatureLength = 2f;
    [SerializeField] private float smallSwimScale = 0.9f;
    [SerializeField] private float largeSwimScale = 1.3f;
    [SerializeField] private float smallHoldScale = 0.75f;
    [SerializeField] private float largeHoldScale = 1.5f;

    [Tooltip("Beat of empty black water held after the descent finishes, before the first deep sea " +
             "creature comes forward. Without it the abyss arrives and is immediately populated, " +
             "and the drop has nowhere to land.")]
    [SerializeField] private float abyssHoldDuration = 2.5f;

    [Tooltip("Point in the descent, as a fraction, where the bioluminescence starts to come up. " +
             "Blooming it in step with the fog just dims everything together; holding it back means " +
             "the glow emerges out of water that has already gone dark.")]
    [SerializeField] private float bioBloomStart = 0.5f;

    [Header("Hold Bob (gentle idle motion while a fish is being presented)")]
    [SerializeField] private float holdBobHeight = 0.12f;
    [SerializeField] private float holdBobSpeed = 1.1f;

    [Header("Idle Roaming")]
    [Tooltip("While it waits its turn a creature swims a slow circuit around its resting spot rather " +
             "than holding station there, taking its heading from the circuit's own tangent so it " +
             "always travels head-first. This value is the angular rate of that lap in radians per " +
             "second, which means it IS the creature's turn rate - at 0.55 every creature was " +
             "grinding round at 33 degrees a second, reading as a toy on a turntable. Travel speed " +
             "is radius x this, so widen the circuit rather than raising this to move faster.")]
    [SerializeField] private float idleSpeed = 0.22f;
    [SerializeField] private float idleRadiusPerLength = 0.9f;
    [SerializeField] private float idleRadiusMin = 1.2f;
    [SerializeField] private float idleRadiusMax = 4f;
    [SerializeField] private float idleBob = 0.15f;
    [Tooltip("Roll into the lap, like an aircraft in a steady turn. Without it the creature stays " +
             "dead level while continuously changing heading, which is what makes the turn look wrong.")]
    [SerializeField] private float idleBankDegrees = 11f;
    [Tooltip("Yaw sway used while a creature is being presented, where it holds the viewer's eye " +
             "instead of roaming.")]
    [SerializeField] private float idleYawDegrees = 12f;

    [Tooltip("A fish crossing 15 m of open water in a dead straight line at a symmetrical speed " +
             "reads as a prop on a rail. The approach is a curve instead: it sweeps out to one " +
             "side and slightly up, the body points along the path tangent rather than at a fixed " +
             "heading, it rolls into its turns, and the speed surges and glides.")]
    [Header("Swim Realism")]
    [SerializeField] private float arcSideways = 0.22f;   // as a fraction of the distance travelled
    [SerializeField] private float arcRise = 0.12f;
    [SerializeField] private float bankDegrees = 25f;
    [SerializeField] private float bankPerDegreePerSecond = 0.35f;
    [SerializeField] private float bankSmoothing = 5f;
    [SerializeField] private float surgeAmount = 0.07f;
    [SerializeField] private float surgeCycles = 3f;
    [Tooltip("Fraction of the swim spent easing between the resting pose and the travelling pose, " +
             "so the creature turns onto and off its path instead of snapping.")]
    [SerializeField] private float headingBlend = 0.25f;

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

    [Tooltip("Seabed material. Sunlit sand is the other thing that gives away that the abyss " +
             "is just the reef dimmed, so its tint is driven down alongside the fog.")]
    [SerializeField] private Material seafloorMaterial;
    [SerializeField] private Color seafloorTintPhase1 = Color.white;
    [SerializeField] private Color seafloorTintPhase2 = new Color(0.32f, 0.36f, 0.40f, 1f);

    [Tooltip("Bioluminescent coral material. Left glowing all the time it reads as neon in " +
             "daylight, so the emission is driven with the phase: near-dark on the reef, " +
             "bright enough to bloom once the abyss takes over.")]
    [SerializeField] private Material bioluminescenceMaterial;
    [SerializeField] private Color bioEmissionPhase1 = new Color(0.02f, 0.09f, 0.12f);
    [SerializeField] private Color bioEmissionPhase2 = new Color(0.175f, 0.90f, 1.15f);

    
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


    // Resting spots captured once at Start, before the idle drift has moved anything, so a
    // fish always returns to its authored spot instead of to wherever the drift happened to
    // leave it when the tour began.
    private Vector3[] anchorsPhase1;
    private Quaternion[] anchorRotsPhase1;
    private Vector3[] anchorsPhase2;
    private Quaternion[] anchorRotsPhase2;

    private float[] lengthsPhase1;
    private float[] lengthsPhase2;
    private float[] activeLengths;
    private float[] radiiPhase1;
    private float[] radiiPhase2;

    private int inIdleIndex = -1;
    private int outIdleIndex = -1;
    private int holdIdleIndex = -1;

    // Curve control points and per-swim banking state.
    private Vector3 inControl;
    private Vector3 outControl;
    private Quaternion inStartRot;
    private Vector3 inPrevHeading;
    private Vector3 outPrevHeading;
    private float inBank;
    private float outBank;

    // Incoming (current fish approaching) tween state.
    private bool inActive;
    private Transform inTransform;
    private Vector3 inFrom;
    private Vector3 inTo;
    private float inTimer;
    private float inDuration;

    // Outgoing (previous fish returning home) tween state.
    private bool outActive;
    private Transform outTransform;
    private Vector3 outFrom;
    private Vector3 outTo;
    private float outTimer;
    private float outDuration;

    // Holding-in-place bob state for whichever fish is currently presented.
    private bool holdActive;
    private Transform holdTransform;
    private Vector3 holdBasePos;
    private Quaternion holdRot;

    // Environment fade (Phase 1 -> Phase 2, and the final ease back to Phase 1).
    private bool envFading;
    private float envTimer;
    private float envDuration;
    private Color envFogFrom;
    private Color envFogTo;
    private Color envFloorFrom;
    private Color envFloorTo;
    private Color envBioFrom;
    private Color envBioTo;
    private float envFogDensityFrom;
    private float envFogDensityTo;
    private float envAmbientFrom;
    private float envAmbientTo;
    private float envSunFrom;
    private float envSunTo;

    // Kept separate from Start so a tour triggered before this behaviour's Start has run still
    // has resting spots to return its creatures to, rather than a null array.
    private void EnsureAnchors()
    {
        if (anchorsPhase1 == null)
        {
            anchorsPhase1 = CapturePositions(phase1Fish);
            anchorRotsPhase1 = CaptureRotations(phase1Fish);
        }
        if (anchorsPhase2 == null)
        {
            anchorsPhase2 = CapturePositions(phase2Fish);
            anchorRotsPhase2 = CaptureRotations(phase2Fish);
        }
        if (lengthsPhase1 == null) { lengthsPhase1 = CaptureLengths(phase1Fish); radiiPhase1 = BuildRadii(lengthsPhase1); }
        if (lengthsPhase2 == null) { lengthsPhase2 = CaptureLengths(phase2Fish); radiiPhase2 = BuildRadii(lengthsPhase2); }
    }

    // Longest world-space dimension of each creature, used to decide how far away it should stop.
    // Phase 2 creatures are switched off in the scene, so each is briefly enabled to be measured
    // and put straight back.
    private float[] CaptureLengths(Transform[] items)
    {
        if (items == null) return new float[0];
        float[] result = new float[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            result[i] = 1f;
            if (items[i] == null) continue;

            bool wasActive = items[i].gameObject.activeSelf;
            items[i].gameObject.SetActive(true);

            Renderer[] renderers = items[i].GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Vector3 mn = renderers[0].bounds.min;
                Vector3 mx = renderers[0].bounds.max;
                for (int r = 1; r < renderers.Length; r++)
                {
                    mn = Vector3.Min(mn, renderers[r].bounds.min);
                    mx = Vector3.Max(mx, renderers[r].bounds.max);
                }
                Vector3 size = mx - mn;
                result[i] = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }

            items[i].gameObject.SetActive(wasActive);
        }
        return result;
    }

    // Keeps the present point's authored bearing, but sets the distance from the creature's size.
    private Vector3 PresentPositionFor(Transform point, float length)
    {
        Vector3 centre = viewerAnchor != null ? viewerAnchor.position : Vector3.zero;
        centre.y = point.position.y;

        Vector3 flat = point.position - centre;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) return point.position;

        float distance = Mathf.Clamp(length / Mathf.Max(0.01f, targetApparentSize),
            minPresentDistance, maxPresentDistance);
        return centre + flat.normalized * distance;
    }

    [Header("Authored model orientation (same order as phase fish)")]
    [SerializeField] private Vector3[] phase1ModelRotationOffsets;
    [SerializeField] private Vector3[] phase2ModelRotationOffsets;

    private Quaternion ModelCorrection(Transform target)
    {
        if (phase1Fish != null && phase1ModelRotationOffsets != null)
            for (int i = 0; i < phase1Fish.Length && i < phase1ModelRotationOffsets.Length; i++)
                if (phase1Fish[i] == target) return Quaternion.Euler(phase1ModelRotationOffsets[i]);
        if (phase2Fish != null && phase2ModelRotationOffsets != null)
            for (int i = 0; i < phase2Fish.Length && i < phase2ModelRotationOffsets.Length; i++)
                if (phase2Fish[i] == target) return Quaternion.Euler(phase2ModelRotationOffsets[i]);
        return Quaternion.identity;
    }

    private void Start()
    {
        EnsureAnchors();

        ApplyEnvironmentImmediate(fogColorPhase1, fogDensityPhase1, ambientIntensityPhase1, sunIntensityPhase1, seafloorTintPhase1, bioEmissionPhase1);
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

        EnsureAnchors();

        inPhase2 = false;
        SetActiveAll(phase2Fish, false);
        activeFish = phase1Fish;
        activeSources = phase1NarrationSources;
        activeClips = phase1NarrationClips;
        activeHomePositions = anchorsPhase1;
        activeHomeRotations = anchorRotsPhase1;
        activeLengths = lengthsPhase1;
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
            outIdleIndex = currentIndex;
            BeginSwimOut(activeFish[currentIndex], activeFish[currentIndex].position, activeHomePositions[currentIndex]);
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

        // Heading comes from the path itself (see Update), never from the present point's own
        // forward vector - those points aren't oriented consistently with where their creatures
        // approach from, which is what made several of them swim backwards.
        float length = (activeLengths != null && currentIndex < activeLengths.Length) ? activeLengths[currentIndex] : 1f;
        inIdleIndex = currentIndex;

        // Sixteen creatures on an identical three-beat rhythm reads as a queue. Scaling the beat
        // off the creature itself gives the tour some phrasing: a clownfish darts in and is gone,
        // a barracuda shoal takes its time and is allowed to hang there.
        float sizeFactor = Mathf.Clamp01(Mathf.InverseLerp(smallCreatureLength, largeCreatureLength, length));
        float swimTime = swimInDuration * Mathf.Lerp(smallSwimScale, largeSwimScale, sizeFactor);

        BeginSwimIn(current, activeHomePositions[currentIndex], PresentPositionFor(point, length), swimTime);

        float wait;
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
            wait = holdDuration * Mathf.Lerp(smallHoldScale, largeHoldScale, sizeFactor);
        }

        float total = swimTime + wait + gapBetweenFish;
        SendCustomEventDelayedSeconds(nameof(AdvanceToNext), total);
    }

    private Transform PresentPointFor(int index)
    {
        if (presentPoints == null || presentPoints.Length == 0) return null;
        return presentPoints[index % presentPoints.Length];
    }

    // Control point for the quadratic curve the creature swims along: out to one side of the
    // straight line and a little above it. Alternating the side by index keeps consecutive
    // arrivals from tracing the same sweep.
    private Vector3 ArcControl(Vector3 from, Vector3 to, int index)
    {
        Vector3 mid = (from + to) * 0.5f;
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.01f) return mid;
        Vector3 dir = delta / dist;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        else side = side.normalized;
        float sign = (index % 2 == 0) ? 1f : -1f;
        return mid + side * (dist * arcSideways * sign) + Vector3.up * (dist * arcRise);
    }

    private Vector3 CurvePoint(Vector3 a, Vector3 c, Vector3 b, float e)
    {
        float u = 1f - e;
        return (u * u) * a + (2f * u * e) * c + (e * e) * b;
    }

    private Vector3 CurveTangent(Vector3 a, Vector3 c, Vector3 b, float e)
    {
        return (2f * (1f - e)) * (c - a) + (2f * e) * (b - c);
    }

    // Surge and glide. The bump vanishes at both ends and stays small enough that progress
    // never reverses, so the creature pulses forward instead of tracking a metronome.
    private float Surge(float e)
    {
        return Mathf.Clamp01(e + surgeAmount * Mathf.Sin(e * surgeCycles * 6.2831853f) * e * (1f - e));
    }

    // Degrees per second of horizontal turn, signed, used to decide which way to roll.
    private float TurnRate(Vector3 prevDir, Vector3 newDir, float dt)
    {
        if (dt <= 0f) return 0f;
        Vector3 a = new Vector3(prevDir.x, 0f, prevDir.z);
        Vector3 b = new Vector3(newDir.x, 0f, newDir.z);
        if (a.sqrMagnitude < 0.000001f || b.sqrMagnitude < 0.000001f) return 0f;
        a = a.normalized;
        b = b.normalized;
        float s = Vector3.Dot(Vector3.Cross(a, b), Vector3.up);
        float c = Vector3.Dot(a, b);
        return Mathf.Atan2(s, c) * Mathf.Rad2Deg / dt;
    }

    private void BeginSwimIn(Transform target, Vector3 from, Vector3 to, float duration)
    {
        inTransform = target;
        inFrom = from;
        inTo = to;
        inControl = ArcControl(from, to, inIdleIndex);
        inStartRot = target.rotation;
        inPrevHeading = CurveTangent(from, inControl, to, 0f).normalized;
        inBank = 0f;
        inTimer = 0f;
        inDuration = Mathf.Max(0.01f, duration);
        inActive = true;
    }

    private void BeginSwimOut(Transform target, Vector3 from, Vector3 to)
    {
        outTransform = target;
        outFrom = from;
        outTo = to;
        outControl = ArcControl(from, to, outIdleIndex + 1);
        outPrevHeading = CurveTangent(from, outControl, to, 0f).normalized;
        outBank = 0f;
        outTimer = 0f;
        outDuration = Mathf.Max(0.01f, swimOutDuration);
        outActive = true;
    }

    public void BeginPhase2Transition()
    {
        // The phase-1 effects (light shafts, caustics, the water surface itself) used to be
        // switched off on this frame, while the scene was still at full brightness - the
        // ceiling and every sunbeam vanished in an instant and only then did the darkening
        // start, which read as the world breaking rather than as a descent. Hold them until
        // the fade is nearly done, by which point the fog and dimmed ambient hide their exit.
        BeginEnvironmentFade(fogColorPhase2, fogDensityPhase2, ambientIntensityPhase2, sunIntensityPhase2, environmentTransitionDuration, seafloorTintPhase2, bioEmissionPhase2);
        SendCustomEventDelayedSeconds(nameof(HidePhase1Fx), environmentTransitionDuration * 0.85f);
        // The deep sea cast used to switch on at full brightness, so a visitor watching the descent
        // saw the abyss populate itself before it was dark. They appear once the water has gone.
        SendCustomEventDelayedSeconds(nameof(RevealPhase2Cast), environmentTransitionDuration * 0.9f);
        SendCustomEventDelayedSeconds(nameof(StartPhase2Fish), environmentTransitionDuration + abyssHoldDuration);
    }

    public void RevealPhase2Cast()
    {
        SetActiveAll(phase2Fish, true);
    }

    public void HidePhase1Fx()
    {
        SetActiveAll(phase1FxObjects, false);
    }

    public void StartPhase2Fish()
    {
        inPhase2 = true;
        activeFish = phase2Fish;
        activeSources = phase2NarrationSources;
        activeClips = phase2NarrationClips;
        EnsureAnchors();
        activeHomePositions = anchorsPhase2;
        activeHomeRotations = anchorRotsPhase2;
        activeLengths = lengthsPhase2;
        currentIndex = -1;
        AdvanceToNext();
    }

    public void EndObservation()
    {
        SetActiveAll(phase2Fish, false);
        // Bring the surface and its light back while it is still dark, so the rising
        // brightness reveals them. Waiting until ReopenCube meant they snapped on against
        // an already-bright scene - the same jarring pop as the descent, in reverse.
        SetActiveAll(phase1FxObjects, true);
        BeginEnvironmentFade(fogColorPhase1, fogDensityPhase1, ambientIntensityPhase1, sunIntensityPhase1, environmentTransitionDuration, seafloorTintPhase1, bioEmissionPhase1);
        SendCustomEventDelayedSeconds(nameof(ReopenCube), environmentTransitionDuration + resetDelay);
    }

    public void ReopenCube()
    {
        SetActiveAll(phase1FxObjects, true);   // safety net if the tour was interrupted
        SetCubeVisible(true);
        started = false;
        inPhase2 = false;
        currentIndex = -1;
    }

    private void ApplyEnvironmentImmediate(Color fogColor, float fogDensity, float ambientIntensity, float sunIntensity, Color floorTint, Color bioEmission)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientIntensity = ambientIntensity;
        if (sunLight != null) sunLight.intensity = sunIntensity;
        if (underwaterSkybox != null) underwaterSkybox.SetColor("_SkyTint", fogColor);
        if (seafloorMaterial != null) seafloorMaterial.SetColor("_Color", floorTint);
        if (bioluminescenceMaterial != null) bioluminescenceMaterial.SetColor("_EmissionColor", bioEmission);
    }

    private void BeginEnvironmentFade(Color fogColor, float fogDensity, float ambientIntensity, float sunIntensity, float duration, Color floorTint, Color bioEmission)
    {
        envFloorFrom = seafloorMaterial != null ? seafloorMaterial.GetColor("_Color") : Color.white;
        envFloorTo = floorTint;
        envBioFrom = bioluminescenceMaterial != null ? bioluminescenceMaterial.GetColor("_EmissionColor") : Color.black;
        envBioTo = bioEmission;
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

    // Slow, per-creature drift so nothing in the cast is ever perfectly still. The index seeds
    // the phase, so neighbours never sway in lockstep. Expressed in body space - mostly along
    // the fish's own length - so it looks like station-keeping rather than sliding sideways.
    private float[] BuildRadii(float[] lengths)
    {
        if (lengths == null) return new float[0];
        float[] result = new float[lengths.Length];
        for (int i = 0; i < lengths.Length; i++)
            result[i] = Mathf.Clamp(lengths[i] * idleRadiusPerLength, idleRadiusMin, idleRadiusMax);
        return result;
    }

    // A lap around the resting spot: a circle with a second, slower circle laid over it so the
    // path wanders instead of repeating exactly, plus a gentle rise and fall.
    private Vector3 IdleOffsetAt(int index, float radius, float t)
    {
        float a = t * idleSpeed + index * 1.7f;
        float b = t * idleSpeed * 0.37f + index * 2.3f;
        return new Vector3(
            Mathf.Cos(a) * radius + Mathf.Cos(b) * radius * 0.35f,
            Mathf.Sin(t * idleSpeed * 0.65f + index * 2.2f) * idleBob,
            Mathf.Sin(a) * radius + Mathf.Sin(b) * radius * 0.35f);
    }

    // Heading is measured from where the circuit is actually about to go, so the creature can
    // never end up travelling tail-first no matter how the two circles combine.
    private Quaternion IdleHeading(int index, float radius, float t, Quaternion fallback)
    {
        Vector3 here = IdleOffsetAt(index, radius, t);
        Vector3 ahead = IdleOffsetAt(index, radius, t + 0.08f) - here;
        if (ahead.sqrMagnitude < 0.0000001f) return fallback;
        Vector3 dir = ahead.normalized;

        // Which way the lap is bending, taken a little further along the same path, so the
        // creature leans into its turn instead of staying rigidly level while rotating.
        Vector3 later = IdleOffsetAt(index, radius, t + 0.32f) - IdleOffsetAt(index, radius, t + 0.24f);
        float bank = 0f;
        if (later.sqrMagnitude > 0.0000001f)
        {
            Vector3 side = Vector3.Cross(dir, later.normalized);
            bank = -Mathf.Clamp(side.y * 6f, -1f, 1f) * idleBankDegrees;
        }
        return Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
    }

    private Quaternion ActiveAnchorRotation(int index)
    {
        Quaternion[] rots = inPhase2 ? anchorRotsPhase2 : anchorRotsPhase1;
        if (rots == null || index < 0 || index >= rots.Length) return Quaternion.identity;
        return rots[index];
    }

    private float ActiveRadius(int index)
    {
        float[] radii = inPhase2 ? radiiPhase2 : radiiPhase1;
        if (radii == null || index < 0 || index >= radii.Length) return idleRadiusMin;
        return radii[index];
    }

    // The circuit is faded out across the swim in and back in across the swim out, so the creature
    // arrives exactly on its presentation point and rejoins its lap seamlessly afterwards.
    private Vector3 IdleOffsetScaled(int index, float scale)
    {
        if (index < 0 || scale <= 0f) return Vector3.zero;
        return IdleOffsetAt(index, ActiveRadius(index), Time.time) * scale;
    }

    // Drives every resting creature. Whoever is currently swimming or being presented is
    // skipped here and gets the same offset layered onto its tween instead, so the handover
    // in either direction is seamless.
    private void ApplyIdle(Transform[] fish, Vector3[] anchors, Quaternion[] anchorRots, float[] radii)
    {
        if (fish == null || anchors == null || anchorRots == null || radii == null) return;
        float t = Time.time;
        for (int i = 0; i < fish.Length; i++)
        {
            Transform f = fish[i];
            if (f == null) continue;
            if (inActive && f == inTransform) continue;
            if (outActive && f == outTransform) continue;
            if (holdActive && f == holdTransform) continue;
            f.position = anchors[i] + IdleOffsetAt(i, radii[i], t);
            f.rotation = IdleHeading(i, radii[i], t, anchorRots[i]) * ModelCorrection(f);
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        ApplyIdle(phase1Fish, anchorsPhase1, anchorRotsPhase1, radiiPhase1);
        ApplyIdle(phase2Fish, anchorsPhase2, anchorRotsPhase2, radiiPhase2);

        if (inActive && inTransform != null)
        {
            inTimer += dt;
            float t = Mathf.Clamp01(inTimer / inDuration);
            float e = Surge(TweenEase.InOutCubic(t));
            inTransform.position = CurvePoint(inFrom, inControl, inTo, e) + IdleOffsetScaled(inIdleIndex, 1f - t);

            Vector3 tangent = CurveTangent(inFrom, inControl, inTo, e);
            if (tangent.sqrMagnitude > 0.000001f)
            {
                Vector3 dir = tangent.normalized;
                float turn = TurnRate(inPrevHeading, dir, dt);
                inPrevHeading = dir;
                inBank = Mathf.Lerp(inBank, Mathf.Clamp(-turn * bankPerDegreePerSecond, -bankDegrees, bankDegrees),
                    Mathf.Clamp01(bankSmoothing * dt));
                Quaternion travelRot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, inBank) * ModelCorrection(inTransform);
                // Turn onto the path over the opening slice instead of snapping out of the
                // resting pose the drift left it in.
                inTransform.rotation = Quaternion.Slerp(inStartRot, travelRot, Mathf.Clamp01(t / Mathf.Max(0.01f, headingBlend)));
            }

            if (t >= 1f)
            {
                inActive = false;
                holdActive = true;
                holdTransform = inTransform;
                holdBasePos = inTo;
                holdIdleIndex = inIdleIndex;
                holdRot = inTransform.rotation;
            }
        }

        if (outActive && outTransform != null)
        {
            outTimer += dt;
            float t = Mathf.Clamp01(outTimer / outDuration);
            float e = Surge(TweenEase.InOutCubic(t));
            outTransform.position = CurvePoint(outFrom, outControl, outTo, e) + IdleOffsetScaled(outIdleIndex, t);

            Vector3 tangent = CurveTangent(outFrom, outControl, outTo, e);
            if (tangent.sqrMagnitude > 0.000001f)
            {
                Vector3 dir = tangent.normalized;
                float turn = TurnRate(outPrevHeading, dir, dt);
                outPrevHeading = dir;
                outBank = Mathf.Lerp(outBank, Mathf.Clamp(-turn * bankPerDegreePerSecond, -bankDegrees, bankDegrees),
                    Mathf.Clamp01(bankSmoothing * dt));
                Quaternion travelRot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, outBank) * ModelCorrection(outTransform);
                // Settle back onto exactly the pose the resting drift will pick up, so handing
                // back over at the end of the swim is invisible.
                Quaternion restRot = IdleHeading(outIdleIndex, ActiveRadius(outIdleIndex), Time.time, ActiveAnchorRotation(outIdleIndex)) * ModelCorrection(outTransform);
                outTransform.rotation = Quaternion.Slerp(restRot, travelRot, Mathf.Clamp01((1f - t) / Mathf.Max(0.01f, headingBlend)));
            }

            if (t >= 1f) outActive = false;
        }

        if (holdActive && holdTransform != null)
        {
            float bob = Mathf.Sin(Time.time * holdBobSpeed) * holdBobHeight;
            // While it is the exhibit, the creature holds the viewer's eye instead of roaming:
            // it hovers on the spot and keeps its arrival heading, so it never turns its tail to
            // the person it is being shown to.
            holdTransform.position = holdBasePos + new Vector3(0f, bob, 0f);
            // Hold the arrival heading but keep finning, so the creature is never a rigid prop
            // while its narration plays.
            Quaternion correction = ModelCorrection(holdTransform);
            holdTransform.rotation = holdRot * Quaternion.Inverse(correction) * Quaternion.Euler(
                Mathf.Sin(Time.time * holdBobSpeed * 0.6f) * idleYawDegrees * 0.25f,
                Mathf.Sin(Time.time * idleSpeed * 0.9f) * idleYawDegrees * 0.6f,
                0f) * correction;
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
            if (seafloorMaterial != null) seafloorMaterial.SetColor("_Color", Color.Lerp(envFloorFrom, envFloorTo, t));
            if (bioluminescenceMaterial != null)
            {
                float bioT = Mathf.Clamp01((t - bioBloomStart) / Mathf.Max(0.01f, 1f - bioBloomStart));
                bioluminescenceMaterial.SetColor("_EmissionColor", Color.Lerp(envBioFrom, envBioTo, bioT));
            }
            if (t >= 1f) envFading = false;
        }
    }
}
