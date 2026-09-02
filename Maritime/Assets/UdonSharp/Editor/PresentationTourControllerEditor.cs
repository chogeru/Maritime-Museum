
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PresentationTourController))]
public class PresentationTourControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, true, true)) return;

        DrawDefaultInspector();

        var controller = (PresentationTourController)target;
        var libRef = controller.GetComponent<PresentationTourLibraryRef>();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Library (editor-only reference)", EditorStyles.boldLabel);
        if (libRef == null)
        {
            EditorGUILayout.HelpBox("No PresentationTourLibraryRef found on this GameObject. Add one to pick a library asset.", MessageType.Info);
            if (GUILayout.Button("Add PresentationTourLibraryRef"))
            {
                Undo.AddComponent<PresentationTourLibraryRef>(controller.gameObject);
            }
        }
        else
        {
            var so = new SerializedObject(libRef);
            var libProp = so.FindProperty("library");
            EditorGUILayout.PropertyField(libProp);
            so.ApplyModifiedProperties();

            if (GUILayout.Button("Sync From Library"))
            {
                if (libRef.library == null)
                {
                    EditorUtility.DisplayDialog("No Library Assigned", "Assign a PresentationTourLibrary asset first.", "OK");
                }
                else
                {
                    Undo.RecordObject(controller, "Sync Presentation Tour Library");
                    controller.targets = (Transform[])libRef.library.targets.Clone();
                    controller.planetNames = (string[])libRef.library.planetNames.Clone();
                    controller.highlightLights = (Light[])libRef.library.highlightLights.Clone();
                    controller.narrationClips = (AudioClip[])libRef.library.narrationClips.Clone();
                    controller.subtitleLines = (string[])libRef.library.subtitleLines.Clone();
                    controller.subtitleStartTimes = (float[])libRef.library.subtitleStartTimes.Clone();
                    controller.subtitleCounts = (int[])libRef.library.subtitleCounts.Clone();
                    EditorUtility.SetDirty(controller);
                }
            }
        }
    }
}
