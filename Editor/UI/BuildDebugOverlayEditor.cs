using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
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

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            BuildPipelineUIStyle.Apply(root);

            var card = BuildPipelineUIStyle.CreateCard("🔍 Build Debug Overlay", "In-game diagnostic HUD showing live publisher, SKU, version, and cheat mode.");
            InspectorElement.FillDefaultInspector(card, serializedObject, this);

            var callout = BuildPipelineUIStyle.CreateCallout(
                "BuildDebugOverlay automatically appears in the Unity Editor and Development Builds via [RuntimeInitializeOnLoadMethod].\n\n" +
                "Press F7 or tap the on-screen 'BUILD DBG' badge during Play Mode to inspect runtime Publisher, FullGame status, Package ID, and build numbers.",
                "info");
            callout.style.marginTop = 8;
            card.Add(callout);

            root.Add(card);
            return root;
        }
    }
}
