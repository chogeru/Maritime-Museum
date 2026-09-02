
using UdonSharp;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// Shared occupancy counter for the AquasUnderwaterTrigger volumes, and single source of truth
// for the per-zone underwater look (fog color/density, ambient darkening, deep-pressure weight).
// The dive path deliberately crosses several overlapping water-volume triggers
// (Harbor -> OpenOcean -> DeepSea) so there's never a gap in coverage. Without the occupancy
// counter each trigger cached/restored global RenderSettings (fog/skybox/ambient) independently,
// so leaving the outer zone while still inside an overlapping inner zone would force-restore the
// pre-underwater state and kill the effect mid-dive. Routing enter/exit through a reference count
// here means the global state is only restored once every zone has actually been exited.
//
// Zone color/density/ambient tuning used to live duplicated on each of the 3 AquasUnderwaterTrigger
// volumes. It's centralized here instead, as parallel arrays indexed by zoneIndex (0 = Harbor,
// 1 = OpenOcean, 2 = DeepSea), so the whole dive's color grading can be tuned from one place.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UnderwaterZoneManager : UdonSharpBehaviour
{
    [Header("Per-zone underwater look (index 0 = Harbor, 1 = OpenOcean, 2 = DeepSea)")]
    [Tooltip("Fog/skybox tint at the water surface (depth = 0), one entry per zone.")]
    [SerializeField] private Color[] zoneFogColorShallow;
    [Tooltip("Fog/skybox tint at each zone's Max Fog Depth, one entry per zone.")]
    [SerializeField] private Color[] zoneFogColorDeep;
    [SerializeField] private float[] zoneFogDensityShallow;
    [SerializeField] private float[] zoneFogDensityDeep;
    [Tooltip("Ambient light intensity multiplier at the surface, one entry per zone.")]
    [SerializeField] private float[] zoneAmbientIntensityShallow;
    [Tooltip("Ambient light intensity multiplier at each zone's Max Fog Depth.")]
    [SerializeField] private float[] zoneAmbientIntensityDeep;
    [Tooltip("Depth (below the local water surface) at which each zone reaches its 'deep' color/density/ambient values.")]
    [SerializeField] private float[] zoneMaxFogDepth;
    [Tooltip("Maximum weight the shared deep-pressure PostProcess volume reaches at each zone's Max Fog Depth.")]
    [SerializeField] private float[] zoneDeepPressureMaxWeight;

    private int occupantCount;

    private bool cachedOriginal;
    private bool originalFogEnabled;
    private Color originalFogColor;
    private FogMode originalFogMode;
    private float originalFogDensity;

    private bool cachedSkybox;
    private Material originalSkybox;

    private bool cachedAmbient;
    private float originalAmbientIntensity;

    // Returns true only on the transition from "in no zone" to "in one zone" - callers use this
    // to gate one-shot dive splash/particle/ambience so overlapping zone boundaries crossed mid-dive
    // don't replay the "entering water" cues at depth.
    public bool EnterZone()
    {
        occupantCount++;
        bool isFirstEntry = occupantCount == 1;
        if (!isFirstEntry) return false;

        if (!cachedOriginal)
        {
            originalFogEnabled = RenderSettings.fog;
            originalFogColor = RenderSettings.fogColor;
            originalFogMode = RenderSettings.fogMode;
            originalFogDensity = RenderSettings.fogDensity;
            cachedOriginal = true;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;

        if (!cachedAmbient)
        {
            originalAmbientIntensity = RenderSettings.ambientIntensity;
            cachedAmbient = true;
        }
        return true;
    }

    // Returns true only on the transition from "in one zone" to "in no zone" - the real surfacing,
    // as opposed to stepping from one overlapping underwater volume into another.
    public bool ExitZone()
    {
        occupantCount = occupantCount > 0 ? occupantCount - 1 : 0;
        bool isLastExit = occupantCount == 0 && cachedOriginal;
        if (!isLastExit) return false;

        RenderSettings.fog = originalFogEnabled;
        RenderSettings.fogMode = originalFogMode;
        RenderSettings.fogColor = originalFogColor;
        RenderSettings.fogDensity = originalFogDensity;
        if (cachedSkybox) RenderSettings.skybox = originalSkybox;
        if (cachedAmbient) RenderSettings.ambientIntensity = originalAmbientIntensity;
        return true;
    }

    public void CacheSkyboxIfNeeded()
    {
        if (cachedSkybox) return;
        originalSkybox = RenderSettings.skybox;
        cachedSkybox = true;
    }

    // Applies this zone's fog/ambient/deep-pressure look for the given depth below its water
    // surface. Called by an AquasUnderwaterTrigger on entry (depth 0, i.e. the shallow values)
    // and every frame while a player is inside it - this is the one place that reads the
    // per-zone arrays above, so retuning the whole dive's color grading only means editing them.
    public void ApplyZoneColor(int zoneIndex, float depth, Material underwaterSkybox, PostProcessVolume deepPressureVolume)
    {
        if (zoneFogColorShallow == null || zoneIndex < 0 || zoneIndex >= zoneFogColorShallow.Length) return;

        float maxDepth = zoneMaxFogDepth != null && zoneIndex < zoneMaxFogDepth.Length ? zoneMaxFogDepth[zoneIndex] : 40f;
        float t = Mathf.Clamp01(depth / Mathf.Max(0.01f, maxDepth));

        RenderSettings.fogDensity = Mathf.Lerp(zoneFogDensityShallow[zoneIndex], zoneFogDensityDeep[zoneIndex], t);

        Color fogColorNow = Color.Lerp(zoneFogColorShallow[zoneIndex], zoneFogColorDeep[zoneIndex], t);
        RenderSettings.fogColor = fogColorNow;
        if (underwaterSkybox != null) underwaterSkybox.SetColor("_SkyTint", fogColorNow);

        RenderSettings.ambientIntensity = Mathf.Lerp(zoneAmbientIntensityShallow[zoneIndex], zoneAmbientIntensityDeep[zoneIndex], t);

        if (deepPressureVolume != null)
        {
            float maxWeight = zoneDeepPressureMaxWeight != null && zoneIndex < zoneDeepPressureMaxWeight.Length ? zoneDeepPressureMaxWeight[zoneIndex] : 0.3f;
            deepPressureVolume.weight = t * maxWeight;
        }
    }
}
