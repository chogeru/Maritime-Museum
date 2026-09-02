
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Central hub for the two observation scenarios. Owns the selection cubes and
/// the root objects for the planets / constellations, and hands control off to
/// the two tour controllers. Cubes grow in / shrink out instead of popping instantly.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class ExperienceManager : UdonSharpBehaviour
{
    [Header("Selection Cubes")]
    public GameObject planetCube;
    public GameObject constellationCube;

    [Tooltip("One-shot particle burst played when each cube reappears after a tour ends.")]
    public ParticleSystem planetCubeBurst;
    public ParticleSystem constellationCubeBurst;

    [Tooltip("The cubes' own MaterialPulse - given a brief bright flash in sync with the burst so the cube itself looks like the source of the energy, not just a nearby particle effect.")]
    public MaterialPulse planetCubePulse;
    public MaterialPulse constellationCubePulse;
    public float cubeReappearFlashMultiplier = 3.5f;

    [Tooltip("Played when each cube shrinks away at the start of a tour - particles converge inward instead of bursting outward.")]
    public ParticleSystem planetCubeCollapse;
    public ParticleSystem constellationCubeCollapse;

    [Header("Idle Float")]
    [Tooltip("How far the cubes gently bob up and down while idle (waiting to be picked).")]
    public float idleBobHeight = 0.03f;
    public float idleBobSpeed = 1.1f;
    [Tooltip("How fast the cubes slowly spin while idle.")]
    public float idleSpinSpeed = 12f;

    [Header("Ground Ring")]
    [Tooltip("Ambient glow decal(s) that should stay under the local player's feet instead of sitting at one fixed spot.")]
    public Transform[] groundRingFollowers;
    public float groundRingOffset = 0.02f;

    [Header("Scene Roots")]
    public GameObject solarSystemRoot;
    public GameObject constellationsRoot;

    [Header("Tour Controllers")]
    public PresentationTourController planetTour;
    public RevealTourController constellationTour;

    [Tooltip("How long the cube grow/shrink animation takes, in seconds.")]
    public float cubeAnimDuration = 0.4f;

    [Tooltip("How strongly the cubes overshoot and settle as they appear (0 = no overshoot).")]
    public float cubeShowOvershoot = 1.4f;

    [Tooltip("The cubes' normal (fully shown) scale. Fixed here instead of being read from the "
        + "transform at runtime, since that reads whatever scale happened to be set at the moment "
        + "Start() ran and could race against an early StartPlanetMode/StartConstellationMode call.")]
    public Vector3 cubeFullScale = new Vector3(0.6f, 0.6f, 0.6f);

    // 0 = hidden, 1 = shown; currentFraction is the last displayed value, used as the tween's
    // start point so interrupting a show/hide mid-flight restarts smoothly from where it is.
    private float currentFraction = 1f;
    private float animStart = 1f;
    private float animEnd = 1f;
    private float animElapsed = 0f;
    private bool animating = false;

    // Guards against a cube being interacted with twice (e.g. double-click while it's
    // still shrinking) from starting a second, overlapping tour.
    private bool tourInProgress = false;

    private Vector3 planetCubeBasePos;
    private Vector3 constellationCubeBasePos;

    // ----- Unity lifecycle -----

    private void Start()
    {
        if (planetCube != null) planetCubeBasePos = planetCube.transform.localPosition;
        if (constellationCube != null) constellationCubeBasePos = constellationCube.transform.localPosition;

        SetCubesActiveImmediate(true);
        if (solarSystemRoot != null) solarSystemRoot.SetActive(true);
        if (constellationsRoot != null) constellationsRoot.SetActive(false);
    }

    private void Update()
    {
        // Gentle idle bob/spin while a cube is on screen, waiting to be picked - runs
        // independently of the scale pop-in/out animation below (position/rotation vs scale).
        if (planetCube != null && planetCube.activeSelf)
        {
            float bob = Mathf.Sin(Time.time * idleBobSpeed) * idleBobHeight;
            planetCube.transform.localPosition = planetCubeBasePos + new Vector3(0f, bob, 0f);
            planetCube.transform.localRotation = Quaternion.Euler(0f, Time.time * idleSpinSpeed, 0f);
        }
        if (constellationCube != null && constellationCube.activeSelf)
        {
            float bob = Mathf.Sin(Time.time * idleBobSpeed + 1.7f) * idleBobHeight;
            constellationCube.transform.localPosition = constellationCubeBasePos + new Vector3(0f, bob, 0f);
            constellationCube.transform.localRotation = Quaternion.Euler(0f, Time.time * idleSpinSpeed, 0f);
        }

        if (groundRingFollowers != null)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local))
            {
                Vector3 feet = local.GetPosition();
                Vector3 followPos = new Vector3(feet.x, feet.y + groundRingOffset, feet.z);
                for (int i = 0; i < groundRingFollowers.Length; i++)
                {
                    if (groundRingFollowers[i] != null) groundRingFollowers[i].position = followPos;
                }
            }
        }

        if (!animating) return;

        animElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(animElapsed / Mathf.Max(cubeAnimDuration, 0.01f));
        bool showing = animEnd > animStart;
        float e = showing ? TweenEase.OutBack(t, cubeShowOvershoot) : TweenEase.InCubic(t);
        currentFraction = Mathf.LerpUnclamped(animStart, animEnd, e);
        ApplyCubeScale(currentFraction);

        if (t >= 1f)
        {
            animating = false;
            currentFraction = animEnd;
            ApplyCubeScale(currentFraction);
            if (animEnd <= 0f)
            {
                if (planetCube != null) planetCube.SetActive(false);
                if (constellationCube != null) constellationCube.SetActive(false);
            }
        }
    }

    // ----- Cube show/hide scale-tween helpers -----

    private void ApplyCubeScale(float t)
    {
        if (planetCube != null) planetCube.transform.localScale = cubeFullScale * t;
        if (constellationCube != null) constellationCube.transform.localScale = cubeFullScale * t;
    }

    private void SetCubesActiveImmediate(bool active)
    {
        animating = false;
        currentFraction = active ? 1f : 0f;
        if (planetCube != null) { planetCube.SetActive(active); }
        if (constellationCube != null) { constellationCube.SetActive(active); }
        ApplyCubeScale(currentFraction);
    }

    // ----- Public API: begins a tour (currently nothing in the scene calls these - see note below) -----

    public void StartPlanetMode()
    {
        if (tourInProgress) return;
        tourInProgress = true;

        HideCubes();
        if (solarSystemRoot != null) solarSystemRoot.SetActive(true);
        if (constellationsRoot != null) constellationsRoot.SetActive(false);
        if (planetTour != null) planetTour.BeginTour();
    }

    public void StartConstellationMode()
    {
        if (tourInProgress) return;
        tourInProgress = true;

        HideCubes();
        if (solarSystemRoot != null) solarSystemRoot.SetActive(false);
        if (constellationsRoot != null) constellationsRoot.SetActive(true);
        if (constellationTour != null) constellationTour.BeginTour();
    }

    // ----- Public API: called by the tour controllers when a tour finishes -----

    public void OnPlanetTourComplete()
    {
        tourInProgress = false;
        ShowCubes();
    }

    public void OnConstellationTourComplete()
    {
        tourInProgress = false;
        if (solarSystemRoot != null) solarSystemRoot.SetActive(true);
        if (constellationsRoot != null) constellationsRoot.SetActive(false);
        ShowCubes();
    }

    // ----- Internal show/hide implementation -----

    private void ShowCubes()
    {
        if (planetCube != null) planetCube.SetActive(true);
        if (constellationCube != null) constellationCube.SetActive(true);
        currentFraction = 0f;
        ApplyCubeScale(currentFraction);
        animStart = 0f;
        animEnd = 1f;
        animElapsed = 0f;
        animating = true;

        if (planetCubeBurst != null) { planetCubeBurst.Clear(); planetCubeBurst.Play(); }
        if (constellationCubeBurst != null) { constellationCubeBurst.Clear(); constellationCubeBurst.Play(); }
        if (planetCubePulse != null) planetCubePulse.TriggerFlash(cubeReappearFlashMultiplier);
        if (constellationCubePulse != null) constellationCubePulse.TriggerFlash(cubeReappearFlashMultiplier);
    }

    private void HideCubes()
    {
        animStart = currentFraction;
        animEnd = 0f;
        animElapsed = 0f;
        animating = true;

        if (planetCubeCollapse != null) { planetCubeCollapse.Clear(); planetCubeCollapse.Play(); }
        if (constellationCubeCollapse != null) { constellationCubeCollapse.Clear(); constellationCubeCollapse.Play(); }
    }
}
