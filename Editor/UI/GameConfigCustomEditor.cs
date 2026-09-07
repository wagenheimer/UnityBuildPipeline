using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(GameConfig))]
    public class GameConfigCustomEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var config = (GameConfig)target;

            // Header Banner
            EditorGUILayout.Space(4);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            headerStyle.normal.textColor = new Color(0.2f, 0.8f, 0.9f);
            EditorGUILayout.LabelField("GAME CONFIGURATION", headerStyle);

            var subHeaderStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            var verText = config.GameVersion != null ? config.GameVersion.GameVersionAsTextWithBetaLabel : "1.0";
            var dateText = config.VersionDate != null ? config.VersionDate.AsText : "";
            EditorGUILayout.LabelField($"{verText} - {dateText}", subHeaderStyle);
            EditorGUILayout.Space(6);

            // Action Button: Open Modern Build Pipeline Window
            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fixedHeight = 32
            };
            GUI.backgroundColor = new Color(0.2f, 0.55f, 0.85f);
            if (GUILayout.Button("OPEN BUILD PIPELINE WINDOW", btnStyle))
            {
                BuildPipelineWindow.ShowWindow();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(8);

            // Version Controls
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quick Version Increment", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button($"+ Major ({config.GameVersion.Major})"))
            {
                config.GameVersion.Major++;
                config.GameVersion.Minor = 0;
                config.GameVersion.Build = 0;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button($"+ Minor ({config.GameVersion.Minor})"))
            {
                config.GameVersion.Minor++;
                config.GameVersion.Build = 0;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button($"+ Build ({config.GameVersion.Build})"))
            {
                config.GameVersion.Build++;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button("Today"))
            {
                var now = System.DateTime.Now;
                config.VersionDate.Day = now.Day;
                config.VersionDate.Month = now.Month;
                config.VersionDate.Year = now.Year;
                EditorUtility.SetDirty(config);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Active Target Preview
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Current Target Preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Publisher:", config.Publisher.ToString());
            EditorGUILayout.LabelField("Language:", config.GameLanguage.ToString());
            EditorGUILayout.LabelField("Cheat Mode:", config.CheatMode ? "ENABLED" : "Disabled");
            EditorGUILayout.LabelField("Full Game:", config.FullGame ? "Yes (Full)" : "No (Free/Demo)");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Draw all default properties cleanly
            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
