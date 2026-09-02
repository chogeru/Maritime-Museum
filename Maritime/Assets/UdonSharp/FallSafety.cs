
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Watches the local player's height and teleports them back to the world spawn
/// if they ever fall below the platform (e.g. by walking off the edge of the observation deck).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class FallSafety : UdonSharpBehaviour
{
    [Tooltip("If the local player's Y position drops below this value, they will be teleported back to spawn.")]
    public float fallHeightThreshold = -10f;

    [Tooltip("Position to teleport the player back to. Leave at (0,0,0) to use this object's position instead.")]
    public Transform respawnPoint;

    [Tooltip("How often (in seconds) to check the player's height.")]
    public float checkInterval = 0.5f;

    private void Start()
    {
        SendCustomEventDelayedSeconds(nameof(CheckFall), checkInterval);
    }

    public void CheckFall()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (Utilities.IsValid(localPlayer))
        {
            Vector3 pos = localPlayer.GetPosition();
            if (pos.y < fallHeightThreshold)
            {
                Vector3 targetPos = respawnPoint != null ? respawnPoint.position : transform.position;
                Quaternion targetRot = respawnPoint != null ? respawnPoint.rotation : transform.rotation;
                localPlayer.TeleportTo(targetPos, targetRot);
            }
        }

        SendCustomEventDelayedSeconds(nameof(CheckFall), checkInterval);
    }
}
