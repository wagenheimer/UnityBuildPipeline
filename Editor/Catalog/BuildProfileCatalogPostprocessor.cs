using System;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Keeps <c>ProjectBuildConfig.catalog.json</c> next to the asset. The JSON is derived data for
    /// AppDeployHub/CI (profiles + defines already resolved); the .asset remains the source of truth.
    /// </summary>
    internal sealed class BuildProfileCatalogPostprocessor : AssetPostprocessor
    {
        private static bool writing;

        /// <summary>
        /// Guarantees the catalog exists right after the project opens (first checkout, package
        /// upgrade, or a config edited outside Unity).
        /// </summary>
        [InitializeOnLoadMethod]
        private static void EnsureCatalogOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                var guids = AssetDatabase.FindAssets("t:ProjectBuildConfig");
                if (guids == null || guids.Length == 0)
                    return;

                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var config = AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(path);
                if (config == null || BuildProfileCatalog.IsUpToDate(config, path))
                    return;

                writing = true;
                try
                {
                    BuildProfileCatalog.Write(config, path);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[BuildPipeline] Falha ao gerar o catálogo de perfis: {exception.Message}");
                }
                finally
                {
                    writing = false;
                }
            };
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (writing || importedAssets == null || importedAssets.Length == 0)
                return;

            foreach (string path in importedAssets)
            {
                if (!path.EndsWith("/ProjectBuildConfig.asset", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(path, "ProjectBuildConfig.asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var config = AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(path);
                if (config == null)
                    continue;

                writing = true;
                try
                {
                    string catalog = BuildProfileCatalog.Write(config, path);
                    if (!string.IsNullOrEmpty(catalog))
                        Debug.Log($"[BuildPipeline] Catálogo de perfis atualizado: {catalog}");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[BuildPipeline] Falha ao gerar o catálogo de perfis: {exception.Message}");
                }
                finally
                {
                    writing = false;
                }
            }
        }
    }

    internal static class BuildProfilesMenu
    {
        [MenuItem("Tools/Build Pipeline/Export Profile Catalog", priority = 200)]
        private static void ExportCatalog()
        {
            var config = BuildCLI.FindOrCreateConfig();
            string assetPath = AssetDatabase.GetAssetPath(config);

            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[BuildPipeline] Nenhum ProjectBuildConfig encontrado para exportar.");
                return;
            }

            string catalog = BuildProfileCatalog.Write(config, assetPath);
            Debug.Log($"[BuildPipeline] Catálogo de perfis: {catalog}");
            EditorUtility.RevealInFinder(catalog);
        }

        [MenuItem("Tools/Build Pipeline/Validate Profiles", priority = 201)]
        private static void ValidateProfiles()
        {
            var config = BuildCLI.FindOrCreateConfig();
            var issues = BuildProfileValidator.Validate(config, BuildProfileCatalog.ReadTrackingPolicy());
            Debug.Log(BuildProfileValidator.Format(issues));
        }
    }
}
