
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Rotates this object to always face the local player's camera, like a billboard.
/// Used for name labels so they stay readable no matter where the player is standing.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class FaceLocalPlayer : UdonSharpBehaviour
{
    private void Update()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;
        Vector3 targetPos = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(-dir);
    }
}
