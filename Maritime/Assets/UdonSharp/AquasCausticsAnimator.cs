
using UdonSharp;
using UnityEngine;

// Portable replacement for AQUAS_Caustics (a plain MonoBehaviour, not usable in VRChat).
// Cycles the Projector's caustics texture frames and keeps _WaterLevel/_DepthFade
// aligned to the water plane's transform, same as the original script did.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class AquasCausticsAnimator : UdonSharpBehaviour
{
    [SerializeField] private Texture2D[] frames;
    [SerializeField] private float fps = 24f;
    [SerializeField] private Transform waterTransform;
    [SerializeField] private float maxCausticDepth = 10f;
    [SerializeField] private Material projectorMaterial;

    [Header("Drift")]
    [Tooltip("Frame flipping alone reads as a slideshow playing in place. Drifting the projection " +
             "on top of it makes the caustics travel like the surface above them. In metres per " +
             "second: the shader builds its UV from world position, so this converts cleanly.")]
    [SerializeField] private Vector2 driftSpeed = new Vector2(0.35f, 0.22f);

    [Tooltip("Slow back-and-forth sway added to the drift, in metres, so it never looks like a straight conveyor.")]
    [SerializeField] private float swayAmount = 0.6f;
    [SerializeField] private float swaySpeed = 0.13f;

    private int frameIndex;
    private float timer;

    private void Start()
    {
        ApplyFrame();
        ApplyWaterLevel();
    }

    private void Update()
    {
        // Drift runs every frame, independent of the frame-flip rate, so the caustics
        // keep moving smoothly even between texture changes.
        ApplyDrift();

        if (frames == null || frames.Length == 0) return;
        timer += Time.deltaTime;
        float interval = fps > 0f ? 1f / fps : 1f;
        if (timer < interval) return;
        timer -= interval;
        frameIndex = (frameIndex + 1) % frames.Length;
        ApplyFrame();
        ApplyWaterLevel();
    }

    private void ApplyDrift()
    {
        if (projectorMaterial == null) return;

        // The caustics shader derives its UV as worldPos.xz * 0.1 * _CausticsScale, without
        // TRANSFORM_TEX, so the material's texture offset is ignored entirely. Feed the
        // shader's own _DriftOffset instead, converting metres to that UV scale.
        float uvPerMetre = 0.1f * projectorMaterial.GetFloat("_CausticsScale");

        float t = Time.time;
        Vector2 metres = new Vector2(
            t * driftSpeed.x + Mathf.Sin(t * swaySpeed) * swayAmount,
            t * driftSpeed.y + Mathf.Cos(t * swaySpeed * 0.77f) * swayAmount);

        projectorMaterial.SetVector("_DriftOffset",
            new Vector4(metres.x * uvPerMetre, metres.y * uvPerMetre, 0f, 0f));
    }

    private void ApplyFrame()
    {
        if (projectorMaterial == null || frames == null || frames.Length == 0) return;
        projectorMaterial.SetTexture("_Texture", frames[frameIndex]);
    }

    private void ApplyWaterLevel()
    {
        if (projectorMaterial == null || waterTransform == null) return;
        float level = waterTransform.position.y;
        projectorMaterial.SetFloat("_WaterLevel", level);
        projectorMaterial.SetFloat("_DepthFade", level - maxCausticDepth);
    }
}
