
using UnityEngine;
using UdonSharp;

// Some of the observation-tour creatures are not one body but a shoal parented under a single
// transform (Barracuda, Lanternfish, Jellyfish). The tour moves that parent, so without this the
// whole shoal slides through the water welded into a rigid block - the members never change
// position relative to one another, which is the giveaway that it is a prop rather than a group
// of animals. This gives each member its own slow drift in the parent's local space, so the
// formation loosens and tightens as it travels while the tour keeps full control of where the
// group as a whole goes.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SchoolMemberDrift : UdonSharpBehaviour
{
    [Tooltip("Metres of drift, before the fore/aft bias below is applied.")]
    [SerializeField] private float amplitude = 0.22f;
    [SerializeField] private float speed = 0.7f;
    [Tooltip("Normally zero. Any yaw tilts a member off the shoal's heading, and its sideways drift " +
             "then has a component pointing out of its own tail - the same reverse-swimming artefact " +
             "the fore/aft term causes, just smaller. Members keeping the shoal's exact heading makes " +
             "the sideways drift perfectly perpendicular, so it can never read as backwards.")]
    [SerializeField] private float yawDegrees = 0f;

    [Tooltip("Fore/aft component, normally zero. Members keep the shoal's heading, so any motion " +
             "along their own length spends half its cycle travelling tail-first - measurably so: " +
             "at 0.15 the shoal logged 1.44 m of reverse travel in under eight seconds. Sideways " +
             "and vertical drift loosens the formation without ever backing a fish up.")]
    [SerializeField] private float alongBodyBias = 0f;

    [Tooltip("Rotation that aligns the authored model nose (or jellyfish bell) with movement +Z.")]
    [SerializeField] private Vector3 modelRotationOffset;
    private Quaternion modelCorrection;

    private Transform[] members;
    private Vector3[] homeLocalPos;
    private Quaternion[] homeLocalRot;
    private Vector3[] lastWorldPos;
    private bool hasLastPos;

    private void Start()
    {
        modelCorrection = Quaternion.Euler(modelRotationOffset);
        int count = transform.childCount;
        members = new Transform[count];
        homeLocalPos = new Vector3[count];
        homeLocalRot = new Quaternion[count];
        lastWorldPos = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Transform m = transform.GetChild(i);
            members[i] = m;
            homeLocalPos[i] = m.localPosition;
            homeLocalRot[i] = m.localRotation;
        }
    }

    private void LateUpdate()
    {
        if (members == null) return;
        float t = Time.time;
        for (int i = 0; i < members.Length; i++)
        {
            Transform m = members[i];
            if (m == null) continue;

            // Golden-angle phase spacing so neighbours in the array never pulse together.
            float phase = i * 2.39996f;
            Vector3 offset = new Vector3(
                Mathf.Sin(t * speed * 0.9f + phase) * amplitude * 0.9f,
                Mathf.Sin(t * speed * 0.7f + phase * 1.7f) * amplitude * 0.8f,
                Mathf.Sin(t * speed + phase * 0.6f) * amplitude * alongBodyBias);

            // Rotated into the member's own frame first. Members are individually yawed within the
            // shoal, so a "sideways" offset expressed in the parent's axes points diagonally
            // backwards for any member that is not facing exactly along the parent - which is what
            // was still backing fish up after the fore/aft term had already been zeroed.
            m.localPosition = homeLocalPos[i] + homeLocalRot[i] * offset;

            // Point each member along the way it is actually travelling. Reasoning about which
            // frame the drift should live in was not enough to stop members occasionally sliding
            // tail-first - three separate attempts left the measured reverse travel unchanged -
            // so the heading is now derived from the member's own measured motion, which makes
            // backwards travel arithmetically impossible rather than merely unlikely.
            if (hasLastPos)
            {
                Vector3 travelled = m.position - lastWorldPos[i];
                if (travelled.sqrMagnitude > 0.0000004f)
                {
                    m.rotation = Quaternion.LookRotation(travelled.normalized, Vector3.up) * modelCorrection;
                }
            }
            lastWorldPos[i] = m.position;
        }
        hasLastPos = true;
    }
}
