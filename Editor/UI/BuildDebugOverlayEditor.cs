using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(BuildDebugOverlay))]
    public class BuildDebugOverlayEditor : UnityEditor.Editor
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Add Build Debug Overlay to Scene", priority = 112)]
        public static void AddOverlayToScene()
        {
            var existing = Object.FindObjectOfType<BuildDebugOverlay>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                EditorUtility.DisplayDialog("Build Pipeline", "BuildDebugOverlay already exists in the scene.", "OK");
                return;
            }

            var go = new GameObject("BuildDebugOverlay", typeof(BuildDebugOverlay));
            Undo.RegisterCreatedObjectUndo(go, "Create Build Debug Overlay");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            EditorUtility.DisplayDialog("Build Pipeline", "BuildDebugOverlay added to scene. It will automatically activate in Editor and Development Builds.", "OK");
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "BuildDebugOverlay automatically appears in the Unity Editor and Development Builds via [RuntimeInitializeOnLoadMethod].\n\n" +
                "Press F7 or click the 'BUILD DBG' on-screen button during Play Mode to inspect the runtime Publisher, FullGame status, Package ID, and build numbers.",
                MessageType.Info);
        }
    }
}

