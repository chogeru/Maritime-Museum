
using UnityEngine;

/// <summary>
/// Data asset listing the items a PresentationTourController should present, in order.
/// Kept as plain parallel arrays so UdonSharp can read the fields directly at runtime.
/// </summary>
[CreateAssetMenu(fileName = "PresentationTourLibrary", menuName = "Guided Tour Experience/Presentation Tour Library")]
public class PresentationTourLibrary : ScriptableObject
{
    [Tooltip("Display name for each planet, in presentation order.")]
    public string[] planetNames;

    [Tooltip("Planet transform, same order as Planet Names.")]
    public Transform[] targets;

    [Tooltip("Highlight light (child of the planet) toggled on while presenting it, same order as Planet Names.")]
    public Light[] highlightLights;

    [Tooltip("Narration clip for each planet, same order as Planet Names. Leave empty to use the controller's fallback duration.")]
    public AudioClip[] narrationClips;

    [Header("Subtitles (auto-timed to narration clip pauses)")]
    [Tooltip("All subtitle clauses across every planet, flattened in order (Planet 0's clauses, then Planet 1's, ...). Split/timed by analyzing silence gaps in each narration clip so lines change in sync with the voice.")]
    [TextArea(2, 4)]
    public string[] subtitleLines;

    [Tooltip("Start time (seconds from that planet's narration clip start) for each entry in Subtitle Lines, same order/length.")]
    public float[] subtitleStartTimes;

    [Tooltip("How many consecutive entries in Subtitle Lines / Subtitle Start Times belong to each planet, same order/length as Planet Names.")]
    public int[] subtitleCounts;
}
