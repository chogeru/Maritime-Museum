
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RevealTourController))]
public class RevealTourControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, true, true)) return;

        DrawDefaultInspector();

        var controller = (RevealTourController)target;
        var libRef = controller.GetComponent<RevealTourLibraryRef>();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Library (editor-only reference)", EditorStyles.boldLabel);
        if (libRef == null)
        {
            EditorGUILayout.HelpBox("No RevealTourLibraryRef found on this GameObject. Add one to pick a library asset.", MessageType.Info);
            if (GUILayout.Button("Add RevealTourLibraryRef"))
            {
                Undo.AddComponent<RevealTourLibraryRef>(controller.gameObject);
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
                    EditorUtility.DisplayDialog("No Library Assigned", "Assign a RevealTourLibrary asset first.", "OK");
                }
                else
                {
                    Undo.RecordObject(controller, "Sync Reveal Tour Library");
                    controller.constellations = (GameObject[])libRef.library.targets.Clone();
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
