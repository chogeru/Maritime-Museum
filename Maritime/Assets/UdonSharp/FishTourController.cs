
using UnityEngine;
using UdonSharp;

// Guided-tour version of the deep sea exhibit: at scripted intervals, one creature at a
// time leaves its resting spot, swims up to a fixed viewing point in front of the
// player, holds there while its caption plays, then swims back and the next one comes
// forward. Reuses SubtitleDisplay/TweenEase from the planetarium tour so captions and
// motion feel consistent with the rest of the world.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class FishTourController : UdonSharpBehaviour
{
    [Tooltip("Creatures in presentation order. Each is tweened from its resting spot to Approach Point and back.")]
    [SerializeField] private Transform[] fish;

    [Tooltip("One caption line per entry in Fish, same order/length.")]
    [SerializeField] private string[] captions;

    [Tooltip("World-space spot the current fish swims up to and faces the player from.")]
    [SerializeField] private Transform approachPoint;

    [SerializeField] private SubtitleDisplay subtitleDisplay;

    [Header("Timing")]
    [SerializeField] private float swimInDuration = 3f;
    [SerializeField] private float holdDuration = 6f;
    [SerializeField] private float swimOutDuration = 2.5f;
    [SerializeField] private float gapBetweenFish = 1.5f;
    [SerializeField] private float startDelay = 2f;

    [Tooltip("Only run the tour once per world visit rather than every time the trigger volume is entered again.")]
    [SerializeField] private bool onlyOnce = true;

    private bool started;
    private bool everStarted;
    private int currentIndex = -1;

    private Vector3[] homePositions;
    private Quaternion[] homeRotations;

    // Tween state for the incoming fish (approaching the player).
    private bool tweenActive;
    private Transform tweenTarget;
    private Vector3 tweenFrom;
    private Vector3 tweenTo;
    private Quaternion tweenFromRot;
    private Quaternion tweenToRot;
    private float tweenTimer;
    private float tweenDuration;

    // Separate tween state for the outgoing fish (returning home).
    // Without this, BeginSwim for the outgoing fish is immediately overwritten
    // by the incoming fish's BeginSwim, leaving the previous fish stuck at the
    // approach point while the next one arrives alongside it.
    private bool outTweenActive;
    private Transform outTweenTarget;
    private Vector3 outTweenFrom;
    private Vector3 outTweenTo;
    private Quaternion outTweenFromRot;
    private Quaternion outTweenToRot;
    private float outTweenTimer;
    private float outTweenDuration;

    public override void OnPlayerTriggerEnter(VRC.SDKBase.VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        if (started) return;
        if (onlyOnce && everStarted) return;
        BeginTour();
    }

    public void BeginTour()
    {
        if (fish == null || fish.Length == 0) return;
        CaptureHomes();
        started = true;
        everStarted = true;
        currentIndex = -1;
        SendCustomEventDelayedSeconds(nameof(AdvanceToNext), startDelay);
    }

    private void CaptureHomes()
    {
        if (homePositions != null && homePositions.Length == fish.Length) return;
        homePositions = new Vector3[fish.Length];
        homeRotations = new Quaternion[fish.Length];
        for (int i = 0; i < fish.Length; i++)
        {
            if (fish[i] == null) continue;
            homePositions[i] = fish[i].position;
            homeRotations[i] = fish[i].rotation;
        }
    }

    public void AdvanceToNext()
    {
        // Send the previous fish home before bringing the next one forward.
        if (currentIndex >= 0 && currentIndex < fish.Length && fish[currentIndex] != null)
        {
            BeginSwimOut(fish[currentIndex], fish[currentIndex].position, homePositions[currentIndex],
                fish[currentIndex].rotation, homeRotations[currentIndex], swimOutDuration);
        }

        currentIndex++;

        if (fish == null || currentIndex >= fish.Length)
        {
            started = false;
            if (subtitleDisplay != null) subtitleDisplay.HideSubtitle();
            return;
        }

        Transform current = fish[currentIndex];
        if (current == null || approachPoint == null)
        {
            SendCustomEventDelayedSeconds(nameof(AdvanceToNext), 0.1f);
            return;
        }

        Quaternion faceRot = Quaternion.LookRotation((approachPoint.forward), Vector3.up);
        BeginSwim(current, homePositions[currentIndex], approachPoint.position,
            homeRotations[currentIndex], faceRot, swimInDuration);

        if (subtitleDisplay != null && captions != null && currentIndex < captions.Length && !string.IsNullOrEmpty(captions[currentIndex]))
        {
            string[] lines = new string[] { captions[currentIndex] };
            float[] times = new float[] { 0f };
            subtitleDisplay.PlaySequence(lines, times, swimInDuration + holdDuration);
        }

        float wait = swimInDuration + holdDuration + gapBetweenFish;
        SendCustomEventDelayedSeconds(nameof(AdvanceToNext), wait);
    }

    private void BeginSwim(Transform target, Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot, float duration)
    {
        tweenTarget = target;
        tweenFrom = from;
        tweenTo = to;
        tweenFromRot = fromRot;
        tweenToRot = toRot;
        tweenTimer = 0f;
        tweenDuration = Mathf.Max(0.01f, duration);
        tweenActive = true;
    }

    private void BeginSwimOut(Transform target, Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot, float duration)
    {
        outTweenTarget = target;
        outTweenFrom = from;
        outTweenTo = to;
        outTweenFromRot = fromRot;
        outTweenToRot = toRot;
        outTweenTimer = 0f;
        outTweenDuration = Mathf.Max(0.01f, duration);
        outTweenActive = true;
    }

    private void Update()
    {
        if (tweenActive && tweenTarget != null)
        {
            tweenTimer += Time.deltaTime;
            float t = Mathf.Clamp01(tweenTimer / tweenDuration);
            float e = TweenEase.InOutCubic(t);
            tweenTarget.position = Vector3.LerpUnclamped(tweenFrom, tweenTo, e);
            tweenTarget.rotation = Quaternion.Slerp(tweenFromRot, tweenToRot, e);
            if (t >= 1f) tweenActive = false;
        }

        if (outTweenActive && outTweenTarget != null)
        {
            outTweenTimer += Time.deltaTime;
            float t = Mathf.Clamp01(outTweenTimer / outTweenDuration);
            float e = TweenEase.InOutCubic(t);
            outTweenTarget.position = Vector3.LerpUnclamped(outTweenFrom, outTweenTo, e);
            outTweenTarget.rotation = Quaternion.Slerp(outTweenFromRot, outTweenToRot, e);
            if (t >= 1f) outTweenActive = false;
        }
    }
}
