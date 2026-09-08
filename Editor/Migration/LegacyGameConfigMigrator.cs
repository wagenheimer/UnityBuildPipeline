using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public static class LegacyGameConfigMigrator
    {
        [MenuItem("Tools/Build Pipeline/Migrate or Create Project Config", priority = 20)]
        public static void MigrateOrCreate()
        {
            var config = FindOrCreateProjectBuildConfig();
            if (config != null)
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            if (!Application.isBatchMode) EditorUtility.DisplayDialog(
                "Build Pipeline Migration",
                $"ProjectBuildConfig successfully configured!\n\nAsset: {AssetDatabase.GetAssetPath(config)}\nLinked GameConfig: {(config.gameConfig != null ? config.gameConfig.name : "None")}",
                "OK"
            );
        }

        public static ProjectBuildConfig FindOrCreateProjectBuildConfig()
        {
            var guids = AssetDatabase.FindAssets("t:ProjectBuildConfig");
            ProjectBuildConfig config = null;

            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                config = AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(path);
            }

            if (config == null)
            {
                var settingsDir = "Assets/_Game/Settings";
                if (!Directory.Exists(settingsDir))
                {
                    settingsDir = "Assets/Settings";
                    if (!Directory.Exists(settingsDir))
                        Directory.CreateDirectory("Assets/_Game/Settings");
                    settingsDir = "Assets/_Game/Settings";
                }

                var assetPath = $"{settingsDir}/ProjectBuildConfig.asset";
                config = ScriptableObject.CreateInstance<ProjectBuildConfig>();
                config.PopulateDefaults();
                AssetDatabase.CreateAsset(config, assetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[BuildPipeline] Created new ProjectBuildConfig at {assetPath}");
            }

            // Look for existing GameConfig
            if (config.gameConfig == null)
            {
                var gcGuids = AssetDatabase.FindAssets("t:GameConfig");
                if (gcGuids.Length > 0)
                {
                    var gcPath = AssetDatabase.GUIDToAssetPath(gcGuids[0]);
                    config.gameConfig = AssetDatabase.LoadAssetAtPath<GameConfig>(gcPath);
                    Debug.Log($"[BuildPipeline] Linked existing GameConfig: {gcPath}");
                }
            }

            // Sync properties from GameConfig if available
            if (config.gameConfig != null)
            {
                var gc = config.gameConfig;
                if (!string.IsNullOrEmpty(gc.BuildFolderName))
                    config.projectName = gc.BuildFolderName;

                if (!string.IsNullOrEmpty(gc.DefaultBundleIdentifier))
                    config.defaultBundleIdentifier = gc.DefaultBundleIdentifier;

                if (!string.IsNullOrEmpty(gc.GameNameJapanese))
                    config.gameNameJapanese = gc.GameNameJapanese;

                // Sync bundle IDs to publisher profiles
                foreach (var pub in config.publishers)
                {
                    switch (pub.publisher)
                    {
                        case Publisher.GoogleAndroidFull:
                            if (!string.IsNullOrEmpty(gc.AndroidFull)) pub.bundleIdentifier = gc.AndroidFull;
                            break;
                        case Publisher.GoogleAndroidFree:
                            if (!string.IsNullOrEmpty(gc.AndroidFree)) pub.bundleIdentifier = gc.AndroidFree;
                            break;
                        case Publisher.AmazonAndroidFull:
                            if (!string.IsNullOrEmpty(gc.AmazonFull)) pub.bundleIdentifier = gc.AmazonFull;
                            break;
                        case Publisher.AmazonAndroidFree:
                            if (!string.IsNullOrEmpty(gc.AmazonFree)) pub.bundleIdentifier = gc.AmazonFree;
                            break;
                        case Publisher.SamsungFull:
                            if (!string.IsNullOrEmpty(gc.SamsungFull)) pub.bundleIdentifier = gc.SamsungFull;
                            break;
                        case Publisher.SamsungFree:
                            if (!string.IsNullOrEmpty(gc.SamsungFree)) pub.bundleIdentifier = gc.SamsungFree;
                            break;
                        case Publisher.iOSFull:
                            if (!string.IsNullOrEmpty(gc.IOSFull)) pub.bundleIdentifier = gc.IOSFull;
                            break;
                        case Publisher.iOSFree:
                            if (!string.IsNullOrEmpty(gc.IOSFree)) pub.bundleIdentifier = gc.IOSFree;
                            break;
                    }
                }

                EditorUtility.SetDirty(config);
            }

            return config;
        }
    }
}
