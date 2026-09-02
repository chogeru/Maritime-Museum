
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Gently rotates the object around its local up axis. Reads server time once at
/// startup (so every client starts from roughly the same angle) and then advances
/// with the smooth local Time.time each frame - calling GetServerTimeInSeconds()
/// every Update instead produces a visibly stepped/jerky rotation in VRChat, since
/// server time only advances in network-sync-sized increments rather than every frame.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SlowRotate : UdonSharpBehaviour
{
    [Tooltip("Degrees per second.")]
    public float degreesPerSecond = 3f;

    private float startAngle;
    private float startLocalTime;

    private void Start()
    {
        startAngle = (float)Networking.GetServerTimeInSeconds() * degreesPerSecond;
        startLocalTime = Time.time;
    }

    private void Update()
    {
        float angle = startAngle + (Time.time - startLocalTime) * degreesPerSecond;
        transform.localRotation = Quaternion.Euler(0f, angle, 0f);
    }
}
