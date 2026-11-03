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

        /// <summary>Hidden from the editor list; still present in the asset and buildable by id.</summary>
        public bool hidden;
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

        /// <summary>Auto | Locked. Locked profiles always build <see cref="lockedLanguage"/>.</summary>
        public string languageMode = "Auto";
        public string lockedLanguage = "";

        /// <summary>Explicit Steam flag (drives the Steam section, define and cleanup).</summary>
        public bool isSteam;

        public string notes = "";

        /// <summary>Final symbol set the build gets (PlayerSettings globals + stack), at catalog time.</summary>
        public List<string> finalDefines = new List<string>();

        /// <summary>Every layer with its origin, in application order.</summary>
        public List<BuildCatalogDefine> defineStack = new List<BuildCatalogDefine>();

        /// <summary>Broken requiredWhen/forbiddenWhen rules of the define registry.</summary>
        public List<string> defineRuleViolations = new List<string>();
    }

    [Serializable]
    public sealed class BuildCatalogDefine
    {
        public string symbol = "";
        /// <summary>Global | Preset | Profile | Auto | Cli</summary>
        public string layer = "";
        public bool removes;
        public string source = "";
    }

    /// <summary>A documented define (package manifest or game declaration).</summary>
    [Serializable]
    public sealed class BuildCatalogDefineDeclaration
    {
        public string symbol = "";
        public string description = "";
        public string source = "";
        public string scope = "";
        public string autoFromPackage = "";
        public List<string> requiredWhen = new List<string>();
        public List<string> forbiddenWhen = new List<string>();
        public List<string> platforms = new List<string>();
        public string docsUrl = "";
    }

    [Serializable]
    public sealed class BuildCatalogGlobalGroup
    {
        public string group = "";
        public List<string> defines = new List<string>();
    }

    /// <summary>One validator finding exported to the catalog (AppDeployHub shows it before queueing).</summary>
    [Serializable]
    public sealed class BuildCatalogIssue
    {
        public string severity = "";
        public string profileId = "";
        public string code = "";
        public string message = "";
    }

    /// <summary>Validator result computed when the catalog is generated.</summary>
    [Serializable]
    public sealed class BuildCatalogValidation
    {
        public int errors;
        public int warnings;
        public List<BuildCatalogIssue> issues = new List<BuildCatalogIssue>();
    }

    /// <summary>Steam deployment metadata exported for AppDeployHub / CI.</summary>
    [Serializable]
    public sealed class BuildCatalogSteamUpload
    {
        public bool enabled;
        public string appId = "";
        public string branch = "";
        public string username = "";
        public string depotIdWindows = "";
        public string depotIdMac = "";
        public string depotIdLinux = "";
        public bool hasWindowsDepot;
        public bool hasMacDepot;
        public bool hasLinuxDepot;
        public bool steamCmdConfigured;
    }

    /// <summary>
    /// Machine-readable snapshot of ProjectBuildConfig, written next to the asset on every save and by
    /// the CLI. It is derived data: the .asset remains the source of truth. AppDeployHub reads this
    /// file instead of parsing YAML, which removes the regex drift between the two sides.
    /// </summary>
    [Serializable]
    public sealed class BuildProfileCatalog
    {
        public const int CurrentSchema = 4;
        public const string FileSuffix = ".catalog.json";

        public int schema = CurrentSchema;
        public string generatedAt = "";
        public string unityVersion = "";
        public string configPath = "";
        public string configHash = "";
        public BuildCatalogTracking tracking;
        public BuildCatalogValidation validation = new BuildCatalogValidation();
        public BuildCatalogSteamUpload steamUpload;
        public List<BuildCatalogProfile> profiles = new List<BuildCatalogProfile>();

        /// <summary>PlayerSettings scripting defines per target group the profiles use.</summary>
        public List<BuildCatalogGlobalGroup> globalDefines = new List<BuildCatalogGlobalGroup>();

        /// <summary>Documented defines (package.json → wagenheimer.defines + ProjectBuildConfig.gameDefines).</summary>
        public List<BuildCatalogDefineDeclaration> defineRegistry = new List<BuildCatalogDefineDeclaration>();

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

            var registry = SafeLoadRegistry(config);
            foreach (var declaration in registry?.Declarations ?? new List<DefineDeclaration>())
            {
                catalog.defineRegistry.Add(new BuildCatalogDefineDeclaration
                {
                    symbol = declaration.symbol,
                    description = declaration.description,
                    source = declaration.source,
                    scope = declaration.scope,
                    autoFromPackage = declaration.autoFromPackage,
                    requiredWhen = new List<string>(declaration.requiredWhen),
                    forbiddenWhen = new List<string>(declaration.forbiddenWhen),
                    platforms = new List<string>(declaration.platforms),
                    docsUrl = declaration.docsUrl
                });
            }

            foreach (var platform in DefineEnvironment.RepresentativePlatforms(config))
            {
                catalog.globalDefines.Add(new BuildCatalogGlobalGroup
                {
                    group = DefineEnvironment.TargetGroupName(platform),
                    defines = DefineEnvironment.GlobalDefines(platform)
                });
            }

            if (config?.publishers != null)
            {
                foreach (var profile in config.publishers)
                {
                    if (profile == null)
                        continue;

                    var entry = ToCatalogProfile(config, profile);
                    var stack = DefineEnvironment.StackFor(config, profile);
                    entry.finalDefines = new List<string>(stack.Final);
                    entry.defineStack = stack.Entries.Select(item => new BuildCatalogDefine
                    {
                        symbol = item.symbol,
                        layer = item.layer.ToString(),
                        removes = item.removes,
                        source = item.source
                    }).ToList();
                    entry.defineRuleViolations = DefineRules.Check(registry, profile, stack).Select(v => v.Message).ToList();
                    catalog.profiles.Add(entry);
                }
            }

            if (config?.steamUpload != null)
            {
                catalog.steamUpload = new BuildCatalogSteamUpload
                {
                    enabled = config.steamUpload.steamEnabled,
                    appId = config.steamUpload.appId ?? "",
                    branch = config.steamUpload.setLiveBranch ?? "",
                    username = config.steamUpload.steamUsername ?? "",
                    depotIdWindows = config.steamUpload.depotIdWindows ?? "",
                    depotIdMac = config.steamUpload.depotIdMac ?? "",
                    depotIdLinux = config.steamUpload.depotIdLinux ?? "",
                    hasWindowsDepot = config.steamUpload.HasWindowsDepot,
                    hasMacDepot = config.steamUpload.HasMacDepot,
                    hasLinuxDepot = config.steamUpload.HasLinuxDepot,
                    steamCmdConfigured = config.steamUpload.IsSteamCmdInstalled()
                };
            }

            // The linter result travels with the catalog so the dashboard can show it before queueing.
            foreach (var issue in BuildProfileValidator.Validate(config, catalog.tracking, registry))
            {
                catalog.validation.issues.Add(new BuildCatalogIssue
                {
                    severity = issue.Severity == BuildProfileIssueSeverity.Error ? "error" : "warning",
                    profileId = issue.ProfileId,
                    code = issue.Code,
                    message = issue.Message
                });
            }

            catalog.validation.errors = catalog.validation.issues.Count(issue => issue.severity == "error");
            catalog.validation.warnings = catalog.validation.issues.Count(issue => issue.severity == "warning");

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
                hidden = profile.hidden,
                bundleIdentifier = profile.bundleIdentifier ?? "",
                outputSubfolder = profile.outputSubfolder ?? "",
                languageMode = profile.languageMode.ToString(),
                lockedLanguage = profile.IsLanguageLocked ? profile.lockedLanguage.ToString() : "",
                isSteam = profile.isSteam,
                notes = profile.notes ?? "",
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

        private static DefineRegistry SafeLoadRegistry(ProjectBuildConfig config)
        {
            try
            {
                return DefineRegistry.Load(config);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildPipeline] Registro de defines indisponível: " + ex.Message);
                return DefineRegistry.Build(null, config);
            }
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
