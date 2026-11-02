using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>ATT policy mirrored from com.wagenheimer.tracking (null when the package is absent).</summary>
    [Serializable]
    public sealed class BuildCatalogTracking
    {
        public bool requireTracking;
        public string scriptingDefine = "";
        public string adsDefine = "";
    }

    /// <summary>One build profile as consumed by AppDeployHub/CI.</summary>
    [Serializable]
    public sealed class BuildCatalogProfile
    {
        public string id = "";
        public string publisher = "";
        public int publisherValue;
        public string displayName = "";
        public string platform = "";
        public int platformValue;
        public bool isFullGame;
        public bool isDemo;
        public string bundleIdentifier = "";
        public string outputSubfolder = "";

        /// <summary>Defines exactly as typed (including the '!DEFINE' removals).</summary>
        public List<string> scriptingDefines = new List<string>();

        /// <summary>Preset ids referenced by the profile (expanded into scriptingDefines).</summary>
        public List<string> presetIds = new List<string>();

        /// <summary>Defines that survive after removals — what the build ends up with from the profile.</summary>
        public List<string> effectiveDefines = new List<string>();

        /// <summary>Defines the profile removes.</summary>
        public List<string> removedDefines = new List<string>();
    }

    /// <summary>
    /// Machine-readable snapshot of ProjectBuildConfig, written next to the asset on every save and by
    /// the CLI. It is derived data: the .asset remains the source of truth. AppDeployHub reads this
    /// file instead of parsing YAML, which removes the regex drift between the two sides.
    /// </summary>
    [Serializable]
    public sealed class BuildProfileCatalog
    {
        public const int CurrentSchema = 2;
        public const string FileSuffix = ".catalog.json";

        public int schema = CurrentSchema;
        public string generatedAt = "";
        public string unityVersion = "";
        public string configPath = "";
        public string configHash = "";
        public BuildCatalogTracking tracking;
        public List<BuildCatalogProfile> profiles = new List<BuildCatalogProfile>();

        public static string GetCatalogPath(string configAssetPath)
        {
            if (string.IsNullOrEmpty(configAssetPath))
                return string.Empty;

            string directory = Path.GetDirectoryName(configAssetPath) ?? "Assets";
            string name = Path.GetFileNameWithoutExtension(configAssetPath);
            return (directory + "/" + name + FileSuffix).Replace('\\', '/');
        }

        /// <summary>Builds the catalog from the config (no I/O except reading the asset bytes for the hash).</summary>
        public static BuildProfileCatalog Build(ProjectBuildConfig config, string configAssetPath)
        {
            var catalog = new BuildProfileCatalog
            {
                generatedAt = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                configPath = configAssetPath ?? "",
                configHash = HashFile(configAssetPath),
                tracking = ReadTrackingPolicy()
            };

            if (config?.publishers == null)
                return catalog;

            foreach (var profile in config.publishers)
            {
                if (profile == null)
                    continue;

                catalog.profiles.Add(ToCatalogProfile(config, profile));
            }

            return catalog;
        }

        public static BuildCatalogProfile ToCatalogProfile(ProjectBuildConfig config, PublisherProfile profile)
        {
            var entry = new BuildCatalogProfile
            {
                id = profile.EffectiveId,
                publisher = profile.publisher.ToString(),
                publisherValue = (int)profile.publisher,
                displayName = profile.displayName,
                platform = profile.platform.ToString(),
                platformValue = (int)profile.platform,
                isFullGame = profile.isFullGame,
                isDemo = profile.isDemo,
                bundleIdentifier = profile.bundleIdentifier ?? "",
                outputSubfolder = profile.outputSubfolder ?? "",
                presetIds = (profile.definePresets ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .ToList()
            };

            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Presets expand before the profile's own list; the catalog carries the final list.
            foreach (string raw in BuildDefineResolver.Resolve(config, profile))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string value = raw.Trim();
                entry.scriptingDefines.Add(value);

                if (value[0] == '!' || value[0] == '-')
                {
                    string name = value.Substring(1).Trim();
                    if (name.Length > 0)
                    {
                        removed.Add(name);
                        entry.removedDefines.Add(name);
                    }
                }
            }

            entry.effectiveDefines = entry.scriptingDefines
                .Where(value => value.Length > 0 && value[0] != '!' && value[0] != '-')
                .Where(value => !removed.Contains(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return entry;
        }

        /// <summary>Reads the catalog path for the project config and writes it. Returns the path or empty.</summary>
        public static string Write(ProjectBuildConfig config, string configAssetPath, string outputPath = null)
        {
            if (config == null || string.IsNullOrEmpty(configAssetPath))
                return string.Empty;

            string path = string.IsNullOrEmpty(outputPath) ? GetCatalogPath(configAssetPath) : outputPath;
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var catalog = Build(config, configAssetPath);
            string json = JsonUtility.ToJson(catalog, true);

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, json, new UTF8Encoding(false));

            if (path.StartsWith("Assets/", StringComparison.Ordinal))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            return path;
        }

        /// <summary>True when the catalog exists and matches the current schema + asset hash.</summary>
        public static bool IsUpToDate(ProjectBuildConfig config, string configAssetPath)
        {
            string path = GetCatalogPath(configAssetPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                var existing = JsonUtility.FromJson<BuildProfileCatalog>(File.ReadAllText(path));
                return existing != null &&
                       existing.schema == CurrentSchema &&
                       existing.configHash == HashFile(configAssetPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads requireTracking/scriptingDefine/adsDefine from TrackingSettings.asset through
        /// SerializedObject, so the pipeline has no compile-time dependency on the tracking package.
        /// </summary>
        public static BuildCatalogTracking ReadTrackingPolicy()
        {
            try
            {
                var guids = AssetDatabase.FindAssets("t:TrackingSettings");
                if (guids == null || guids.Length == 0)
                    return null;

                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null)
                    return null;

                var serialized = new SerializedObject(asset);
                var require = serialized.FindProperty("requireTracking");
                var trackingDefine = serialized.FindProperty("scriptingDefine");
                var ads = serialized.FindProperty("adsDefine");

                return new BuildCatalogTracking
                {
                    requireTracking = require != null && require.boolValue,
                    scriptingDefine = trackingDefine != null ? trackingDefine.stringValue : "",
                    adsDefine = ads != null ? ads.stringValue : ""
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string HashFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return "";

                using (var sha = SHA1.Create())
                using (var stream = File.OpenRead(path))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    var builder = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash)
                        builder.Append(b.ToString("x2"));

                    return builder.ToString();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
