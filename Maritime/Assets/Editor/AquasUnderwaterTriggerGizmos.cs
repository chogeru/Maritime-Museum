// Editor-only gizmo drawer for AquasUnderwaterTrigger.
// Kept in Assets/Editor/ so UdonSharp never parses it.
// Shows the trigger volume, water surface line, and max-fog-depth marker
// in the Scene view whenever a trigger zone is selected.
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AquasUnderwaterTrigger))]
public class AquasUnderwaterTriggerEditor : Editor
{
    private void OnSceneGUI()
    {
        AquasUnderwaterTrigger t = (AquasUnderwaterTrigger)target;

        SerializedObject so = new SerializedObject(t);
        Transform waterSurface = so.FindProperty("waterSurface").objectReferenceValue as Transform;
        int zoneIndex = so.FindProperty("zoneIndex").intValue;
        UnderwaterZoneManager zoneManager = so.FindProperty("zoneManager").objectReferenceValue as UnderwaterZoneManager;

        float maxFogDepth = 40f;
        if (zoneManager != null)
        {
            SerializedObject zoneSo = new SerializedObject(zoneManager);
            SerializedProperty depths = zoneSo.FindProperty("zoneMaxFogDepth");
            if (depths != null && zoneIndex >= 0 && zoneIndex < depths.arraySize)
                maxFogDepth = depths.GetArrayElementAtIndex(zoneIndex).floatValue;
        }

        float surfY = waterSurface != null ? waterSurface.position.y : t.transform.position.y;
        Vector3 center = waterSurface != null
            ? new Vector3(t.transform.position.x, waterSurface.position.y, t.transform.position.z)
            : t.transform.position;
        float halfW = 20f;

        // Water surface cross
        Handles.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Handles.DrawLine(center + Vector3.left * halfW, center + Vector3.right * halfW);
        Handles.DrawLine(center + Vector3.forward * halfW, center + Vector3.back * halfW);
        Handles.Label(center + Vector3.right * halfW, "Water Surface");

        // Max fog depth cross
        Vector3 deepCenter = new Vector3(center.x, surfY - maxFogDepth, center.z);
        Handles.color = new Color(0f, 0.2f, 0.6f, 0.8f);
        Handles.DrawLine(deepCenter + Vector3.left * halfW, deepCenter + Vector3.right * halfW);
        Handles.DrawLine(deepCenter + Vector3.forward * halfW, deepCenter + Vector3.back * halfW);
        Handles.Label(deepCenter + Vector3.right * halfW, $"Max Fog Depth ({maxFogDepth}u)");

        // Vertical connector
        Handles.color = new Color(0.1f, 0.5f, 1f, 0.4f);
        Handles.DrawLine(center, deepCenter);
    }
}

// Draws a faint volume outline even when not selected.
public static class AquasUnderwaterTriggerGizmos
{
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
    static void DrawGizmo(AquasUnderwaterTrigger trigger, GizmoType gizmoType)
    {
        bool selected = (gizmoType & GizmoType.Selected) != 0;
        Collider col = trigger.GetComponent<Collider>();
        if (col == null) return;

        BoxCollider box = col as BoxCollider;
        if (box != null)
        {
            Gizmos.matrix = Matrix4x4.TRS(trigger.transform.position, trigger.transform.rotation, trigger.transform.lossyScale);
            Gizmos.color = selected
                ? new Color(0.1f, 0.5f, 1f, 0.25f)
                : new Color(0.1f, 0.5f, 1f, 0.06f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = selected
                ? new Color(0.1f, 0.5f, 1f, 0.85f)
                : new Color(0.1f, 0.5f, 1f, 0.3f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else
        {
            Gizmos.color = selected
                ? new Color(0.1f, 0.5f, 1f, 0.5f)
                : new Color(0.1f, 0.5f, 1f, 0.2f);
            Gizmos.DrawWireSphere(col.bounds.center, col.bounds.extents.magnitude);
        }
    }
}
