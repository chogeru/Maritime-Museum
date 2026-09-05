
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Lightweight fish-schooling controller. One instance drives every fish in
// "fish" - no per-fish scripts. Uses velocity + inertia (not instant direction
// snapping) so fish glide instead of twitching, plus a simple O(n^2) separation
// pass (cheap at typical school sizes of 10-30) so bodies don't overlap.
// Drop the FishSchool prefab into a scene, resize the "schoolRadius" gizmo cube
// to the space you want it to roam in - fish are picked up automatically from
// this object's children.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class FishSchool : UdonSharpBehaviour
{
    [Header("Setup (auto-fills from children if left empty)")]
    [SerializeField] private Transform[] fish;

    [Header("Roaming volume")]
    [SerializeField] private float schoolRadius = 5f;
    [SerializeField] private float verticalRatio = 0.5f; // roam volume is flattened vertically by this much

    [Tooltip("Yaw correction for models whose nose is not built along +Z. Several of these fish are " +
             "authored facing backwards, so steering them by their velocity points the tail the way " +
             "they are travelling - set this to 180 for those. Verified per group by placing a camera " +
             "on a fish's own right: a correctly built model shows its nose on the right of frame.")]
    [Header("Model Orientation")]
    [SerializeField] private float modelYawOffset = 0f;

    [Tooltip("Optional yaw correction per fish, in fish-array/child order. Falls back to Model Yaw Offset.")]
    [SerializeField] private float[] modelYawOffsets;

    [Header("Movement feel")]
    [SerializeField] private float swimSpeed = 1.0f;
    [SerializeField] private float speedVariance = 0.3f;
    [SerializeField] private float turnSpeed = 1.6f;
    [SerializeField] private float acceleration = 1.5f;
    [SerializeField] private float centerWanderSpeed = 0.08f;
    [SerializeField] private float individualWanderSpeed = 0.15f;
    [SerializeField] private float individualSpread = 1.8f;

    [Header("Separation (avoid overlapping bodies)")]
    [SerializeField] private float personalSpace = 0.6f;
    [SerializeField] private float separationStrength = 2f;
    [Tooltip("Neighbours checked per fish per frame. Keeps cost linear on big schools instead of O(n^2).")]
    [SerializeField] private int separationSamples = 12;

    [Header("Formation")]
    [SerializeField] private float formationSpread = 2.5f;

    [Tooltip("Fish re-plan their wander target on a rotating schedule spread over this many frames. Movement stays smooth every frame; only the (expensive) noise sampling is time-sliced.")]
    [SerializeField] private int targetUpdateFrames = 6;

    [Header("Player Avoidance")]
    [Tooltip("Fish within this distance of the local player scatter away instead of following the school's wander target.")]
    [SerializeField] private float fleeRadius = 4f;
    [Tooltip("How strongly the flee-away direction overrides normal seek/separation once inside Flee Radius.")]
    [SerializeField] private float fleeStrength = 3f;
    [Tooltip("Speed multiplier applied while fleeing, on top of the fish's normal swim speed.")]
    [SerializeField] private float fleeSpeedMultiplier = 2.2f;

    [Tooltip("Optional predators (sharks, etc.) the school scatters away from exactly like the local player, so a big fish swimming through breaks the school up instead of it swimming obliviously in place.")]
    [SerializeField] private Transform[] predators;
    [SerializeField] private float predatorFleeRadius = 9f;

    [Header("Circulation")]
    [Tooltip("Perlin wander alone leaves each fish parked on its slot: the seek direction flips every " +
             "frame and the velocity smoothing averages it to nothing (measured 0.02 m/s against a 0.45 m/s " +
             "setting). Orbiting the whole school around its home keeps the target permanently moving, so " +
             "the fish are always travelling somewhere. Radians per second.")]
    [SerializeField] private float orbitSpeed = 0f;
    [SerializeField] private float orbitRadius = 0f;
    [Tooltip("Vertical bob of the orbit, in metres.")]
    [SerializeField] private float orbitBob = 0f;

    [Header("Containment")]
    [Tooltip("Hard limits, not steering. There is no collision test here - the guarantee that fish " +
             "never enter the seabed or a rock comes from keeping them inside a volume that was " +
             "checked clear in the editor. Fleeing can otherwise carry a fish well past its wander range.")]
    [SerializeField] private float maxRadius = 0f;   // 0 = unlimited
    [SerializeField] private float minY = -10000f;
    [SerializeField] private float maxY = 10000f;

    private Quaternion[] modelCorrections;
    private Vector3 homeCenter;
    private Vector3[] velocity;
    private Vector3[] slotOffset;
    private Vector3[] positions;
    private Vector3[] wanderTarget;
    private float[] speedSeed;
    private float[] phaseX;
    private float[] phaseY;
    private float[] phaseZ;

    private void Start()
    {
        if (fish == null || fish.Length == 0)
        {
            int count = transform.childCount;
            fish = new Transform[count];
            for (int i = 0; i < count; i++) fish[i] = transform.GetChild(i);
        }

        homeCenter = transform.position;

        int n = fish.Length;
        modelCorrections = new Quaternion[n];
        velocity = new Vector3[n];
        slotOffset = new Vector3[n];
        positions = new Vector3[n];
        wanderTarget = new Vector3[n];
        speedSeed = new float[n];
        phaseX = new float[n];
        phaseY = new float[n];
        phaseZ = new float[n];

        // Give every fish its own persistent slot in the formation, spread on a
        // spiral (golden-angle) so they fan out evenly instead of all chasing
        // the same point and clumping into a ball.
        float golden = 2.39996f;
        for (int i = 0; i < n; i++)
        {
            float frac = n > 1 ? (float)i / (float)(n - 1) : 0f;
            float radius = Mathf.Sqrt(frac) * formationSpread;
            float angle = i * golden;
            slotOffset[i] = new Vector3(
                Mathf.Cos(angle) * radius,
                (frac - 0.5f) * formationSpread * verticalRatio,
                Mathf.Sin(angle) * radius);

            speedSeed[i] = Random.Range(-1f, 1f);
            phaseX[i] = Random.Range(0f, 100f);
            phaseY[i] = Random.Range(0f, 100f);
            phaseZ[i] = Random.Range(0f, 100f);
            float yaw = modelYawOffsets != null && i < modelYawOffsets.Length ? modelYawOffsets[i] : modelYawOffset;
            modelCorrections[i] = Quaternion.Euler(0f, yaw, 0f);
            Vector3 nose = Quaternion.Inverse(modelCorrections[i]) * Vector3.forward;
            velocity[i] = fish[i] != null ? fish[i].rotation * nose * 0.1f : Vector3.forward * 0.1f;
            wanderTarget[i] = slotOffset[i];
        }
    }

    private void Update()
    {
        if (fish == null || fish.Length == 0) return;

        float t = Time.time;
        float dt = Time.deltaTime;

        // The whole school drifts slowly around its home point using smooth noise
        // so it wanders instead of just circling.
        float cx = (Mathf.PerlinNoise(t * centerWanderSpeed, 0f) - 0.5f) * 2f;
        float cy = (Mathf.PerlinNoise(0f, t * centerWanderSpeed) - 0.5f) * 2f;
        float cz = (Mathf.PerlinNoise(t * centerWanderSpeed, t * centerWanderSpeed) - 0.5f) * 2f;
        Vector3 schoolCenter = homeCenter + new Vector3(
            cx * schoolRadius * 0.6f,
            cy * schoolRadius * 0.6f * verticalRatio,
            cz * schoolRadius * 0.6f);

        // A steady orbit on top of the wander. This is what actually keeps the shoal in motion.
        if (orbitRadius > 0f)
        {
            float a = t * orbitSpeed;
            schoolCenter += new Vector3(
                Mathf.Cos(a) * orbitRadius,
                Mathf.Sin(a * 0.6f) * orbitBob,
                Mathf.Sin(a) * orbitRadius);
        }

        int n = fish.Length;

        // Cache positions once - transform.position reads are the expensive part
        // at large school sizes, and the separation pass would otherwise re-read
        // them n times each.
        for (int i = 0; i < n; i++)
        {
            if (fish[i] != null) positions[i] = fish[i].position;
        }

        int stride = separationSamples > 0 ? separationSamples : 12;
        int slice = targetUpdateFrames > 0 ? targetUpdateFrames : 1;
        int sliceThisFrame = Time.frameCount % slice;

        bool hasPlayer = false;
        Vector3 playerPos = Vector3.zero;
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer != null)
        {
            hasPlayer = true;
            playerPos = localPlayer.GetPosition();
        }

        for (int i = 0; i < n; i++)
        {
            Transform f = fish[i];
            if (f == null) continue;

            // Re-plan this fish's wander target only on its own slot in the
            // rotation. Perlin sampling is by far the most expensive part under
            // Udon, and the target barely changes frame to frame anyway.
            if (i % slice == sliceThisFrame)
            {
                float ox = (Mathf.PerlinNoise(phaseX[i], t * individualWanderSpeed) - 0.5f) * 2f;
                float oy = (Mathf.PerlinNoise(phaseY[i], t * individualWanderSpeed) - 0.5f) * 2f;
                float oz = (Mathf.PerlinNoise(phaseZ[i], t * individualWanderSpeed) - 0.5f) * 2f;
                wanderTarget[i] = slotOffset[i] + new Vector3(
                    ox * individualSpread,
                    oy * individualSpread * verticalRatio,
                    oz * individualSpread);
            }
            Vector3 target = schoolCenter + wanderTarget[i];

            Vector3 myPos = positions[i];
            Vector3 seek = (target - myPos).normalized;

            // Push apart from schoolmates so bodies never overlap/clip. Only a
            // fixed number of neighbours is sampled per frame (walking the array
            // with a stride offset by frame count), so cost stays linear in n
            // while every pair still gets checked regularly over a few frames.
            Vector3 separation = Vector3.zero;
            int checkedCount = 0;
            int j = (i + Time.frameCount) % n;
            while (checkedCount < stride && checkedCount < n - 1)
            {
                if (j != i)
                {
                    Vector3 away = myPos - positions[j];
                    float d = away.magnitude;
                    if (d > 0.001f && d < personalSpace)
                    {
                        separation += (away / d) * (1f - d / personalSpace);
                    }
                }
                j++;
                if (j >= n) j = 0;
                checkedCount++;
            }

            float speed = swimSpeed * (1f + speedSeed[i] * speedVariance);
            Vector3 desiredVelocity = (seek + separation * separationStrength).normalized * speed;

            if (hasPlayer)
            {
                Vector3 away = myPos - playerPos;
                float distToPlayer = away.magnitude;
                if (distToPlayer < fleeRadius && distToPlayer > 0.001f)
                {
                    float fleeAmount = 1f - (distToPlayer / fleeRadius);
                    Vector3 fleeDir = away / distToPlayer;
                    Vector3 fleeVelocity = fleeDir * speed * fleeSpeedMultiplier;
                    desiredVelocity = Vector3.Lerp(desiredVelocity, fleeVelocity, fleeAmount * fleeStrength * 0.5f);
                }
            }

            if (predators != null && predators.Length > 0)
            {
                for (int p = 0; p < predators.Length; p++)
                {
                    Transform pred = predators[p];
                    if (pred == null) continue;
                    Vector3 awayPred = myPos - pred.position;
                    float distToPred = awayPred.magnitude;
                    if (distToPred < predatorFleeRadius && distToPred > 0.001f)
                    {
                        float fleeAmount = 1f - (distToPred / predatorFleeRadius);
                        Vector3 fleeDir = awayPred / distToPred;
                        Vector3 fleeVelocity = fleeDir * speed * fleeSpeedMultiplier;
                        desiredVelocity = Vector3.Lerp(desiredVelocity, fleeVelocity, fleeAmount * fleeStrength * 0.6f);
                    }
                }
            }

            velocity[i] = Vector3.Lerp(velocity[i], desiredVelocity, acceleration * dt);

            Vector3 next = myPos + velocity[i] * dt;
            if (maxRadius > 0f)
            {
                Vector3 fromHome = next - homeCenter;
                float outDist = fromHome.magnitude;
                if (outDist > maxRadius)
                {
                    next = homeCenter + fromHome * (maxRadius / outDist);
                    // bleed off the outward speed so the fish turns back instead of grinding the wall
                    velocity[i] -= fromHome * (Vector3.Dot(velocity[i], fromHome) / (outDist * outDist));
                }
            }
            if (next.y < minY) { next.y = minY; if (velocity[i].y < 0f) velocity[i].y = 0f; }
            if (next.y > maxY) { next.y = maxY; if (velocity[i].y > 0f) velocity[i].y = 0f; }
            f.position = next;

            if (velocity[i].sqrMagnitude > 0.0004f)
            {
                Quaternion wantRot = Quaternion.LookRotation(velocity[i].normalized, Vector3.up)
                                     * modelCorrections[i];
                f.rotation = Quaternion.Slerp(f.rotation, wantRot, turnSpeed * dt);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.35f);
        Vector3 size = new Vector3(schoolRadius * 2f, schoolRadius * 2f * verticalRatio, schoolRadius * 2f);
        Gizmos.DrawWireCube(transform.position, size);
    }
}
