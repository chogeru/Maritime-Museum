
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// A diving-bell "station" the player sits in at the surface. Once seated it glides slowly
// along a fixed path down through the reef -> open ocean -> deep sea zones (a diagonal
// path, not a straight vertical drop, so it actually crosses each zone's fog trigger on
// the way down instead of staying in one X range) so a passenger experiences the whole
// depth progression hands-free, then can exit and swim once at the bottom, or ride back up.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SubmersibleElevator : UdonSharpBehaviour
{
    [SerializeField] private Transform surfaceDock;
    [SerializeField] private Transform seafloorDock;
    [SerializeField] private float descentDuration = 400f;
    [SerializeField] private float ascentDuration = 120f;

    [Tooltip("A seated rider's own collider is disabled by VRChat while in a Station, so the water trigger volumes never fire for them - this bell drives the underwater effect directly for the whole ride instead of relying on trigger collision.")]
    [SerializeField] private AquasUnderwaterTrigger underwaterEffect;

    private bool moving;
    private bool goingDown;
    private float timer;
    private float duration;
    private Vector3 fromPos;
    private Vector3 toPos;

    private void Start()
    {
        if (surfaceDock != null) transform.position = surfaceDock.position;
    }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        BeginMove(true);
        if (underwaterEffect != null) underwaterEffect.ForceEnter();
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        moving = false;
    }

    private void BeginMove(bool descending)
    {
        goingDown = descending;
        fromPos = transform.position;
        toPos = descending
            ? (seafloorDock != null ? seafloorDock.position : transform.position)
            : (surfaceDock != null ? surfaceDock.position : transform.position);
        duration = Mathf.Max(1f, descending ? descentDuration : ascentDuration);
        timer = 0f;
        moving = true;
    }

    // Called (e.g. from a button/trigger at the bottom) to send the bell back to the surface.
    public void Surface()
    {
        BeginMove(false);
    }

    private void Update()
    {
        if (!moving) return;
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);
        float e = t * t * (3f - 2f * t); // smoothstep - gentle ease in/out, no jolt at either end
        transform.position = Vector3.LerpUnclamped(fromPos, toPos, e);

        if (t >= 1f)
        {
            moving = false;
            // Only the surface arrival should clear the effect - reaching the seafloor leaves it
            // on, since the rider is still deep underwater and may get out to swim around there.
            if (!goingDown && underwaterEffect != null) underwaterEffect.ForceExit();
        }
    }
}
