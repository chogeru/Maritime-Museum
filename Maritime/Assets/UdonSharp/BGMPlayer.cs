
using UnityEngine;
using UdonSharp;

/// <summary>
/// Plays the world's BGM tracks as an endless shuffled playlist: picks a random track
/// (never repeating the same one twice in a row), plays it once, and when it ends picks
/// another random track. Runs as ambient background music (2D, not spatialized).
///
/// While a narration clip is playing on either tour controller's AudioSource, the BGM
/// volume automatically ducks down so the narration stays clear, then eases back up
/// once the narration stops.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class BGMPlayer : UdonSharpBehaviour
{
    public AudioClip[] tracks;
    public AudioSource audioSource;

    [Range(0f, 1f)]
    public float volume = 0.4f;

    [Tooltip("Silence gap between tracks, in seconds.")]
    public float gapBetweenTracks = 2f;

    [Header("Ducking")]
    [Tooltip("AudioSource used by PresentationTourController for narration.")]
    public AudioSource planetNarrationSource;

    [Tooltip("AudioSource used by RevealTourController for narration.")]
    public AudioSource constellationNarrationSource;

    [Range(0f, 1f)]
    [Tooltip("BGM volume multiplier while narration is playing.")]
    public float duckedVolumeMultiplier = 0.12f;

    [Tooltip("How fast the duck transition happens (higher = snappier). At 6, a full duck takes ~1/6s so BGM doesn't bleed over the start of the narration.")]
    public float duckSpeed = 6f;

    private int lastIndex = -1;
    private float currentMultiplier = 1f;
    private float targetMultiplier = 1f;

    private void Start()
    {
        if (audioSource == null) return;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = volume;
        PlayRandomTrack();
    }

    public void PlayRandomTrack()
    {
        if (tracks == null || tracks.Length == 0 || audioSource == null) return;

        int index = 0;
        if (tracks.Length == 1)
        {
            index = 0;
        }
        else
        {
            index = lastIndex;
            while (index == lastIndex)
            {
                index = Random.Range(0, tracks.Length);
            }
        }
        lastIndex = index;

        AudioClip clip = tracks[index];
        audioSource.clip = clip;
        audioSource.Play();

        SendCustomEventDelayedSeconds(nameof(PlayRandomTrack), clip.length + gapBetweenTracks);
    }

    private void Update()
    {
        if (audioSource == null) return;

        bool narrationPlaying = (planetNarrationSource != null && planetNarrationSource.isPlaying)
            || (constellationNarrationSource != null && constellationNarrationSource.isPlaying);

        targetMultiplier = narrationPlaying ? duckedVolumeMultiplier : 1f;
        currentMultiplier = Mathf.MoveTowards(currentMultiplier, targetMultiplier, Time.deltaTime * duckSpeed);
        audioSource.volume = volume * currentMultiplier;
    }
}
