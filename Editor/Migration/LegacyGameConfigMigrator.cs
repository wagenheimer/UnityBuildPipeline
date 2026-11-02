using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public static class LegacyGameConfigMigrator
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Migrate or Create Project Config", priority = 113)]
        public static void MigrateOrCreateFromMenu()
        {
            MigrateOrCreate();
            BuildPipelineWindow.ShowSection(BuildPipelineSections.Project);
        }

        /// <summary>The project's ProjectBuildConfig, or null. Never creates nor changes anything.</summary>
        public static ProjectBuildConfig FindProjectBuildConfig()
        {
            var guids = AssetDatabase.FindAssets("t:ProjectBuildConfig");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        /// <summary>True when the project has a GameConfig asset (a candidate for migration).</summary>
        public static bool HasGameConfigAsset() => AssetDatabase.FindAssets("t:GameConfig").Length > 0;

        /// <summary>
        /// Creates (or completes) the ProjectBuildConfig from the GameConfig — links it, copies the project name,
        /// bundle ids and the per-store bundle ids into the profiles — saves it and returns it.
        /// </summary>
        public static ProjectBuildConfig MigrateOrCreate()
        {
            var config = FindOrCreateProjectBuildConfig();
            if (config != null)
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                string path = AssetDatabase.GetAssetPath(config);
                if (!string.IsNullOrEmpty(path))
                    BuildProfileCatalog.Write(config, path);
            }

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            LegacyMigrationRecord.MarkUnityStepDone();
            Debug.Log($"[BuildPipeline] Migração: ProjectBuildConfig em {AssetDatabase.GetAssetPath(config)} (GameConfig: {(config != null && config.gameConfig != null ? config.gameConfig.name : "nenhum")}).");
            return config;
        }

        public static ProjectBuildConfig FindOrCreateProjectBuildConfig()
        {
            var guids = AssetDatabase.FindAssets("t:ProjectBuildConfig");
            ProjectBuildConfig config = null;
            var configChanged = false;

            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                config = AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(path);
            }

            if (config == null)
            {
                var settingsDir = "Assets/_Game/Settings";
                if (AssetDatabase.IsValidFolder(settingsDir))
                {
                    // already exists
                }
                else if (AssetDatabase.IsValidFolder("Assets/Settings"))
                {
                    settingsDir = "Assets/Settings";
                }
                else
                {
                    AssetDatabase.CreateFolder("Assets/_Game", "Settings");
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
                    configChanged = true;
                    Debug.Log($"[BuildPipeline] Linked existing GameConfig: {gcPath}");
                }
            }

            // Sync properties from GameConfig if available. Only fields that actually differ are written,
            // and the asset is only marked dirty when something changed — otherwise merely opening the
            // Build Pipeline window would show a permanent "changes in memory" warning on ProjectBuildConfig.
            if (config.gameConfig != null)
            {
                var gc = config.gameConfig;

                if (!string.IsNullOrEmpty(gc.BuildFolderName) && config.projectName != gc.BuildFolderName)
                {
                    config.projectName = gc.BuildFolderName;
                    configChanged = true;
                }

                if (!string.IsNullOrEmpty(gc.DefaultBundleIdentifier) && config.defaultBundleIdentifier != gc.DefaultBundleIdentifier)
                {
                    config.defaultBundleIdentifier = gc.DefaultBundleIdentifier;
                    configChanged = true;
                }

                if (!string.IsNullOrEmpty(gc.GameNameJapanese) && config.gameNameJapanese != gc.GameNameJapanese)
                {
                    config.gameNameJapanese = gc.GameNameJapanese;
                    configChanged = true;
                }

                // Sync bundle IDs to publisher profiles
                foreach (var pub in config.publishers)
                {
                    string expectedBundleId = null;
                    switch (pub.publisher)
                    {
                        case Publisher.GoogleAndroidFull:
                            expectedBundleId = gc.AndroidFull;
                            break;
                        case Publisher.GoogleAndroidFree:
                            expectedBundleId = gc.AndroidFree;
                            break;
                        case Publisher.AmazonAndroidFull:
                            expectedBundleId = gc.AmazonFull;
                            break;
                        case Publisher.AmazonAndroidFree:
                            expectedBundleId = gc.AmazonFree;
                            break;
                        case Publisher.SamsungFull:
                            expectedBundleId = gc.SamsungFull;
                            break;
                        case Publisher.SamsungFree:
                            expectedBundleId = gc.SamsungFree;
                            break;
                        case Publisher.iOSFull:
                            expectedBundleId = gc.IOSFull;
                            break;
                        case Publisher.iOSFree:
                            expectedBundleId = gc.IOSFree;
                            break;
                    }

                    if (!string.IsNullOrEmpty(expectedBundleId) && pub.bundleIdentifier != expectedBundleId)
                    {
                        pub.bundleIdentifier = expectedBundleId;
                        configChanged = true;
                    }
                }

                if (configChanged)
                    EditorUtility.SetDirty(config);
            }

            return config;
        }
    }
}
