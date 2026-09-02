
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shows each item's name, transform, highlight light, and narration clip on one row.
/// </summary>
[CustomEditor(typeof(PresentationTourLibrary))]
public class PresentationTourLibraryEditor : Editor
{
    private SerializedProperty namesProp;
    private SerializedProperty targetsProp;
    private SerializedProperty lightsProp;
    private SerializedProperty clipsProp;

    private void OnEnable()
    {
        namesProp = serializedObject.FindProperty("planetNames");
        targetsProp = serializedObject.FindProperty("targets");
        lightsProp = serializedObject.FindProperty("highlightLights");
        clipsProp = serializedObject.FindProperty("narrationClips");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        int count = Mathf.Max(namesProp.arraySize, Mathf.Max(targetsProp.arraySize, Mathf.Max(lightsProp.arraySize, clipsProp.arraySize)));

        EditorGUILayout.LabelField("Items (presentation order)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        for (int i = 0; i < count; i++)
        {
            EnsureSize(namesProp, i + 1);
            EnsureSize(targetsProp, i + 1);
            EnsureSize(lightsProp, i + 1);
            EnsureSize(clipsProp, i + 1);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("#" + i, GUILayout.Width(28));

            var nameEl = namesProp.GetArrayElementAtIndex(i);
            nameEl.stringValue = EditorGUILayout.TextField(nameEl.stringValue);

            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                namesProp.DeleteArrayElementAtIndex(i);
                targetsProp.DeleteArrayElementAtIndex(i);
                lightsProp.DeleteArrayElementAtIndex(i);
                clipsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(targetsProp.GetArrayElementAtIndex(i), new GUIContent("Target"));
            EditorGUILayout.PropertyField(lightsProp.GetArrayElementAtIndex(i), new GUIContent("Highlight Light"));
            EditorGUILayout.PropertyField(clipsProp.GetArrayElementAtIndex(i), new GUIContent("Narration Clip (" + (string.IsNullOrEmpty(nameEl.stringValue) ? "unnamed" : nameEl.stringValue) + ")"));

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Add Item"))
        {
            int i = count;
            namesProp.InsertArrayElementAtIndex(i);
            targetsProp.InsertArrayElementAtIndex(i);
            lightsProp.InsertArrayElementAtIndex(i);
            clipsProp.InsertArrayElementAtIndex(i);
            namesProp.GetArrayElementAtIndex(i).stringValue = "";
            targetsProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
            lightsProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
            clipsProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void EnsureSize(SerializedProperty arrayProp, int size)
    {
        while (arrayProp.arraySize < size)
            arrayProp.InsertArrayElementAtIndex(arrayProp.arraySize);
    }
}
