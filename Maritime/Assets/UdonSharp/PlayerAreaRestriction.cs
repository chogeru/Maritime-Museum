
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Applies world-launch-safe player restrictions on join: disables jumping (this world is a
/// small fixed observation deck bounded by invisible walls, see PlayArea) and clamps avatar
/// eye height so oversized/undersized avatars can't break the confined space.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class PlayerAreaRestriction : UdonSharpBehaviour
{
    [Tooltip("Jump impulse applied to the local player. 0 disables jumping entirely.")]
    public float jumpImpulse = 0f;

    [Tooltip("Minimum allowed avatar eye height, in meters.")]
    public float minAvatarEyeHeight = 1.0f;

    [Tooltip("Maximum allowed avatar eye height, in meters.")]
    public float maxAvatarEyeHeight = 2.0f;

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (!player.isLocal) return;

        player.SetJumpImpulse(jumpImpulse);
        player.SetAvatarEyeHeightMinimumByMeters(minAvatarEyeHeight);
        player.SetAvatarEyeHeightMaximumByMeters(maxAvatarEyeHeight);
    }
}
