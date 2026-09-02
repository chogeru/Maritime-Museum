
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// A subtitle caption that stays anchored in front of the local player's view, like a
/// closed-caption HUD. PresentationTourController / RevealTourController call
/// PlaySequence() whenever a narration clip starts, passing the clip's clauses pre-timed
/// to that clip's actual silence gaps, so lines change in sync with the voice rather than
/// showing one static block of text for the whole clip.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SubtitleDisplay : UdonSharpBehaviour
{
    [Tooltip("Text component that shows the current subtitle line.")]
    public UnityEngine.UI.Text label;

    [Tooltip("Root GameObject to toggle on/off (usually the canvas holding Label).")]
    public GameObject root;

    [Tooltip("Distance in front of the player's head the caption is held at.")]
    public float distance = 2.2f;

    [Tooltip("Vertical offset from head height (negative = lower, toward the bottom of view).")]
    public float verticalOffset = -0.6f;

    [Tooltip("How quickly the caption catches up to the player's head (higher = snappier/less smoothing).")]
    public float followSpeed = 8f;

    [Header("Line Change Flourish")]
    [Tooltip("Seconds for a new line to fade/settle in.")]
    public float lineFadeInDuration = 0.35f;

    [Tooltip("Caption scale at the very start of a line's fade-in (settles to 1x), for a soft pop like the planet name labels.")]
    public float lineStartScale = 0.92f;

    [Tooltip("Small upward drift (world units) a line eases down from as it fades in.")]
    public float lineRiseDistance = 0.05f;

    private bool visible;
    private bool hasPosition;

    // Timed sequence state: lines change as playback elapses past each entry's start time.
    private string[] seqLines;
    private float[] seqStartTimes;
    private float seqTotalDuration;
    private float seqElapsed;
    private int seqIndex;

    // Per-line fade/settle-in state.
    private float lineTimer;
    private Vector3 labelBaseScale;
    private bool labelBaseScaleCaptured;

    private void Start()
    {
        if (root != null) root.SetActive(false);
    }

    // lines/startTimes must be the same length, startTimes ascending and startTimes[0] == 0.
    // totalDuration is the full narration clip length - the caption hides once elapsed passes it.
    public void PlaySequence(string[] lines, float[] startTimes, float totalDuration)
    {
        if (lines == null || lines.Length == 0) return;

        seqLines = lines;
        seqStartTimes = startTimes;
        seqTotalDuration = totalDuration;
        seqElapsed = 0f;
        seqIndex = 0;
        lineTimer = 0f;

        if (label != null)
        {
            if (!labelBaseScaleCaptured)
            {
                labelBaseScale = label.transform.localScale;
                labelBaseScaleCaptured = true;
            }
            label.text = FormatForWrap(seqLines[0]);
        }
        if (root != null) root.SetActive(true);
        visible = true;
    }

    public void HideSubtitle()
    {
        visible = false;
        if (root != null) root.SetActive(false);
    }

    private void Update()
    {
        if (visible)
        {
            seqElapsed += Time.deltaTime;
            lineTimer += Time.deltaTime;

            if (seqElapsed >= seqTotalDuration)
            {
                HideSubtitle();
            }
            else
            {
                int nextIndex = seqIndex + 1;
                if (nextIndex < seqLines.Length && seqElapsed >= seqStartTimes[nextIndex])
                {
                    seqIndex = nextIndex;
                    lineTimer = 0f;
                    if (label != null) label.text = FormatForWrap(seqLines[seqIndex]);
                }

                ApplyLineFlourish();
            }
        }

        if (!visible) return;

        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;

        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

        Vector3 flatForward = head.rotation * Vector3.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 targetPos = head.position + flatForward * distance + Vector3.up * verticalOffset;

        if (!hasPosition)
        {
            transform.position = targetPos;
            hasPosition = true;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);
        }

        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
    }

    // Fades a freshly-changed line in from slightly below/smaller, like a soft caption dissolve,
    // instead of the text just snapping to the next sentence.
    private void ApplyLineFlourish()
    {
        if (label == null) return;

        float t = lineFadeInDuration > 0f ? Mathf.Clamp01(lineTimer / lineFadeInDuration) : 1f;
        float e = 1f - (1f - t) * (1f - t); // ease-out quad

        Color c = label.color;
        c.a = e;
        label.color = c;

        label.transform.localScale = Vector3.LerpUnclamped(labelBaseScale * lineStartScale, labelBaseScale, e);

        // Label's RectTransform is stretch-anchored, so anchoredPosition (not localPosition) is
        // the correct offset knob; scale by the label's own lossy scale, not the (unscaled) root's.
        float riseLocalUnits = Mathf.LerpUnclamped(-lineRiseDistance, 0f, e) / Mathf.Max(0.0001f, label.transform.lossyScale.y);
        RectTransform labelRect = label.rectTransform;
        Vector2 anchoredPos = labelRect.anchoredPosition;
        anchoredPos.y = riseLocalUnits;
        labelRect.anchoredPosition = anchoredPos;
    }

    // Unity's legacy Text has no Japanese kinsoku (line-breaking) rules, so its auto-wrap can
    // start a line with a lone 、 or 。, which reads badly. Only 、/。 are valid break points here;
    // a greedy packer then fills each line with as many comma/period-delimited chunks as fit
    // within maxCharsPerLine, so a break is used only when it's actually needed rather than at
    // every single comma - which previously left short 2-3 character orphan lines everywhere.
    // A chunk longer than a whole line on its own is re-split evenly (not left for Unity's own
    // wrap to handle) so it doesn't strand a tiny 1-2 character widow on the next line.
    [Tooltip("Roughly how many full-width characters fit on one caption line before the line-packer starts a new line. Kept a little under the caption box's real capacity so Unity's own auto-wrap never has to kick in and create ragged short lines.")]
    public int maxCharsPerLine = 18;

    private string wrapResult;
    private string wrapCurrentLine;

    private string FormatForWrap(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        wrapResult = "";
        wrapCurrentLine = "";
        string pendingChunk = "";

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            pendingChunk += c;

            if (c == '、' || c == '。')
            {
                FlushChunk(pendingChunk);
                pendingChunk = "";
            }
        }

        if (pendingChunk.Length > 0) FlushChunk(pendingChunk);

        wrapResult += wrapCurrentLine;
        return wrapResult;
    }

    private void FlushChunk(string chunk)
    {
        if (chunk.Length > maxCharsPerLine)
        {
            if (wrapCurrentLine.Length > 0)
            {
                wrapResult += wrapCurrentLine + "\n";
                wrapCurrentLine = "";
            }

            int pieceCount = Mathf.CeilToInt((float)chunk.Length / maxCharsPerLine);
            int pieceLen = Mathf.CeilToInt((float)chunk.Length / pieceCount);
            int pos = 0;
            for (int p = 0; p < pieceCount; p++)
            {
                int len = Mathf.Min(pieceLen, chunk.Length - pos);
                string piece = chunk.Substring(pos, len);
                pos += len;
                if (p < pieceCount - 1)
                    wrapResult += piece + "\n";
                else
                    wrapCurrentLine = piece;
            }
            return;
        }

        if (wrapCurrentLine.Length > 0 && wrapCurrentLine.Length + chunk.Length > maxCharsPerLine)
        {
            wrapResult += wrapCurrentLine + "\n";
            wrapCurrentLine = chunk;
        }
        else
        {
            wrapCurrentLine += chunk;
        }
    }
}
