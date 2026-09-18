using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline;

namespace Wagenheimer.BuildPipeline.Editor
{
    public static class BuildCLI
    {
        public static ProjectBuildConfig FindOrCreateConfig()
        {
            var configPath = CommandLineArgs.Get("config", "");
            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(configPath);
                if (asset != null) return asset;
            }

            // Search for any ProjectBuildConfig asset in the project
            var guids = AssetDatabase.FindAssets("t:ProjectBuildConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var cfg = AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(path);
                if (cfg != null) return cfg;
            }

            // Fallback: create temporary instance
            var fallback = ScriptableObject.CreateInstance<ProjectBuildConfig>();
            fallback.PopulateDefaults();

            // Link existing GameConfig if found
            var gcGuids = AssetDatabase.FindAssets("t:GameConfig");
            if (gcGuids.Length > 0)
            {
                fallback.gameConfig = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(gcGuids[0]));
            }

            return fallback;
        }

        public static void Build()
        {
            Debug.Log("[BuildCLI] Starting Single Headless Build...");
            var config = FindOrCreateConfig();

            // Override Vault settings via CLI if provided
            if (CommandLineArgs.Has("vaultUrl"))
                config.vaultUrl = CommandLineArgs.Get("vaultUrl");
            if (CommandLineArgs.Has("vaultProfile"))
                config.vaultProfileId = CommandLineArgs.Get("vaultProfile");
            if (CommandLineArgs.Has("vaultToken"))
                config.vaultTokenFallback = CommandLineArgs.Get("vaultToken");
            if (CommandLineArgs.Has("keystoreSource") && Enum.TryParse<KeystoreSource>(CommandLineArgs.Get("keystoreSource"), true, out var ks))
                config.keystoreSource = ks;

            // CI: Android keystore via args (Forge injects the central keystore). Passwords come from the
            // ANDROID_KEYSTORE_PASS / ANDROID_KEYALIAS_PASS env vars, already read by GetEffective*Password().
            if (CommandLineArgs.Has("keystorePath"))
            {
                var ksPath = CommandLineArgs.Get("keystorePath");
                config.keystoreSource = KeystoreSource.LocalDisk;
                config.androidKeystorePath = ksPath;
                config.androidKeystorePathMac = ksPath;
                if (CommandLineArgs.Has("keystoreAlias"))
                    config.androidKeyAlias = CommandLineArgs.Get("keystoreAlias");
                Debug.Log($"[BuildCLI] Keystore Android via CLI: {ksPath} (alias: {config.androidKeyAlias})");
            }

            // Resolve profile: -buildProfile <id> takes precedence over -publisher
            var buildProfileId = CommandLineArgs.Get("buildProfile", "");
            var pubStr = CommandLineArgs.Get("publisher", "");

            PublisherProfile profile = null;
            Publisher publisher = Publisher.Default;

            if (!string.IsNullOrEmpty(buildProfileId))
            {
                profile = FindPublisherProfile(config, buildProfileId, out publisher);
                if (profile == null)
                    Debug.LogWarning($"[BuildCLI] -buildProfile '{buildProfileId}' not found in ProjectBuildConfig; attempting resolution via -publisher.");
            }

            if (profile == null && !string.IsNullOrEmpty(pubStr))
            {
                profile = FindPublisherProfile(config, pubStr, out publisher);
            }

            if (profile != null)
            {
                publisher = profile.publisher;
                Debug.Log($"[BuildCLI] ✅ Resolved Profile: '{profile.displayName}' (Publisher: {publisher}, Platform: {profile.platform}, FullGame: {profile.isFullGame}, BundleId: {profile.bundleIdentifier})");
            }
            else
            {
                if (!string.IsNullOrEmpty(pubStr) && Enum.TryParse(pubStr, true, out Publisher parsedPub))
                {
                    publisher = parsedPub;
                    profile = config.publishers.FirstOrDefault(p => p.publisher == publisher);
                }
                else if (!string.IsNullOrEmpty(pubStr))
                {
                    Debug.LogError($"[BuildCLI] ❌ Could not resolve publisher from '{pubStr}'! Falling back to Default ({publisher}). Available in ProjectBuildConfig: " +
                        string.Join(", ", config.publishers.Select(p => $"{p.publisher} ('{p.displayName}')")));
                }
            }

            // Parse Language
            var langStr = CommandLineArgs.Get("language", "AutoDetect");
            var language = GameLanguage.AutoDetect;
            foreach (GameLanguage gl in Enum.GetValues(typeof(GameLanguage)))
            {
                if (gl.ToString().Equals(langStr, StringComparison.OrdinalIgnoreCase) ||
                    gl.AsLanguageCode().Equals(langStr, StringComparison.OrdinalIgnoreCase))
                {
                    language = gl;
                    break;
                }
            }

            // Parse Platform: explicit CLI argument takes precedence, otherwise use profile's platform
            var platStr = CommandLineArgs.Get("platform", "");
            if (string.IsNullOrEmpty(platStr))
                platStr = CommandLineArgs.Get("buildTarget", "");

            var platform = PlatformType.Windows64;
            if (!string.IsNullOrEmpty(platStr) && Enum.TryParse(platStr, true, out PlatformType pt))
            {
                platform = pt;
                Debug.Log($"[BuildCLI] Platform explicitly set via CLI: {platform}");
            }
            else if (profile != null)
            {
                platform = profile.platform;
                Debug.Log($"[BuildCLI] Platform resolved from profile '{profile.displayName}': {platform}");
            }
            else
            {
                Debug.LogWarning($"[BuildCLI] No platform specified and no profile matched; defaulting to {platform}.");
            }

            // Extra scripting defines: profile defines + CLI -defines override (merged in the step)
            var defines = new List<string>();
            if (profile != null && profile.scriptingDefines != null)
                defines.AddRange(profile.scriptingDefines);
            var cliDefines = CommandLineArgs.Get("defines", "");
            if (!string.IsNullOrEmpty(cliDefines))
                defines.AddRange(cliDefines.Split(';', ','));

            ApplyVersionAndBuildNumberOverrides(config);

            var context = new BuildContext
            {
                Config = config,
                Publisher = publisher,
                PublisherProfile = profile,
                Language = language,
                Platform = platform,
                ProfileId = profile != null ? profile.EffectiveId : (string.IsNullOrEmpty(buildProfileId) ? publisher.ToString() : buildProfileId),
                ScriptingDefines = defines,
                CheatMode = CommandLineArgs.GetBool("cheat", false),
                DevelopmentBuild = CommandLineArgs.GetBool("development", false),
                Demo = CommandLineArgs.GetBool("demo", profile != null && profile.isDemo),
                LevelEditor = CommandLineArgs.GetBool("levelEditor", false),
                LogLevelsInfo = CommandLineArgs.GetBool("logLevelsInfo", true),
                AppBundle = CommandLineArgs.GetBool("autoRun", false) ? false : CommandLineArgs.GetBool("appBundle", true),
                AutoRun = CommandLineArgs.GetBool("autoRun", false),
                CustomOutputPath = CommandLineArgs.Get("outputPath", "")
            };

            var result = BuildPipelineRunner.Execute(context);

            var manifestPath = CommandLineArgs.Get("manifest", "");
            if (!string.IsNullOrEmpty(manifestPath))
            {
                var manifest = new BuildManifest();
                manifest.Add(BuildManifestEntry.From(context, result));
                manifest.WriteTo(manifestPath);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(result.Success ? 0 : 1);
            }
        }

        public static void BuildMatrix()
        {
            Debug.Log("[BuildCLI] Starting Headless Matrix Build...");
            var config = FindOrCreateConfig();

            if (CommandLineArgs.Has("vaultUrl"))
                config.vaultUrl = CommandLineArgs.Get("vaultUrl");
            if (CommandLineArgs.Has("vaultProfile"))
                config.vaultProfileId = CommandLineArgs.Get("vaultProfile");
            if (CommandLineArgs.Has("vaultToken"))
                config.vaultTokenFallback = CommandLineArgs.Get("vaultToken");
            if (CommandLineArgs.Has("keystoreSource") && Enum.TryParse<KeystoreSource>(CommandLineArgs.Get("keystoreSource"), true, out var ks))
                config.keystoreSource = ks;

            ApplyVersionAndBuildNumberOverrides(config);

            var pubArg = CommandLineArgs.Get("matrixPublishers", "");
            var profileArg = CommandLineArgs.Get("matrixProfiles", "");
            var langArg = CommandLineArgs.Get("matrixLanguages", "");
            var cheatOn = CommandLineArgs.GetBool("cheatOn", false);
            var cheatOff = CommandLineArgs.GetBool("cheatOff", true);
            var dev = CommandLineArgs.GetBool("development", false);
            var manifestPath = CommandLineArgs.Get("manifest", "");
            var manifest = new BuildManifest();

            var pubsToBuild = new List<PublisherProfile>();
            if (!string.IsNullOrEmpty(profileArg))
            {
                // Preferred CI path: resolve by stable profile id.
                var ids = profileArg.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawId in ids)
                {
                    var id = rawId.Trim();
                    var prof = config.publishers.FirstOrDefault(p =>
                        string.Equals(p.EffectiveId, id, StringComparison.OrdinalIgnoreCase));
                    if (prof != null)
                        pubsToBuild.Add(prof);
                    else
                        Debug.LogWarning($"[BuildCLI] matrixProfiles: id '{id}' not found in ProjectBuildConfig.");
                }
            }
            else if (!string.IsNullOrEmpty(pubArg))
            {
                var pubNames = pubArg.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var name in pubNames)
                {
                    var prof = FindPublisherProfile(config, name.Trim(), out var pub);
                    if (prof != null)
                    {
                        pubsToBuild.Add(prof);
                    }
                    else if (pub != Publisher.Default)
                    {
                        pubsToBuild.Add(new PublisherProfile(pub, pub.ToString(), PlatformType.Windows64));
                    }
                    else
                    {
                        Debug.LogWarning($"[BuildCLI] matrixPublishers: could not resolve publisher or profile '{name.Trim()}'.");
                    }
                }
            }
            else
            {
                pubsToBuild = config.publishers.Where(p => p.platform == PlatformType.Windows64).ToList();
            }

            var langsToBuild = new List<GameLanguage>();
            if (!string.IsNullOrEmpty(langArg))
            {
                var langNames = langArg.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var name in langNames)
                {
                    foreach (GameLanguage gl in Enum.GetValues(typeof(GameLanguage)))
                    {
                        if (gl.ToString().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                            gl.AsLanguageCode().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            langsToBuild.Add(gl);
                            break;
                        }
                    }
                }
            }
            else
            {
                langsToBuild = config.languages.Where(l => l.enabled).Select(l => l.language).ToList();
            }

            var total = pubsToBuild.Count * langsToBuild.Count * ((cheatOn ? 1 : 0) + (cheatOff ? 1 : 0));
            Debug.Log($"[BuildCLI] Matrix planned: {total} builds ({pubsToBuild.Count} publishers x {langsToBuild.Count} languages).");

            var allSuccess = true;
            foreach (var lang in langsToBuild)
            {
                foreach (var pub in pubsToBuild)
                {
                    if (cheatOff)
                    {
                        var res = RunMatrixEntry(config, pub, lang, dev, false, manifest);
                        if (!res) allSuccess = false;
                    }

                    if (cheatOn)
                    {
                        var res = RunMatrixEntry(config, pub, lang, dev, true, manifest);
                        if (!res) allSuccess = false;
                    }
                }
            }

            if (!string.IsNullOrEmpty(manifestPath))
                manifest.WriteTo(manifestPath);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(allSuccess ? 0 : 1);
            }
        }

        private static bool RunMatrixEntry(ProjectBuildConfig config, PublisherProfile pub, GameLanguage lang,
            bool dev, bool cheat, BuildManifest manifest)
        {
            var ctx = new BuildContext
            {
                Config = config,
                Publisher = pub.publisher,
                PublisherProfile = pub,
                Language = lang,
                Platform = pub.platform,
                ProfileId = pub.EffectiveId,
                ScriptingDefines = pub.scriptingDefines != null ? new List<string>(pub.scriptingDefines) : new List<string>(),
                CheatMode = cheat,
                DevelopmentBuild = dev,
                Demo = pub.isDemo
            };
            var res = BuildPipelineRunner.Execute(ctx);
            manifest.Add(BuildManifestEntry.From(ctx, res));
            return res.Success;
        }

        public static PublisherProfile FindPublisherProfile(ProjectBuildConfig config, string identifier, out Publisher resolvedPublisher)
        {
            resolvedPublisher = Publisher.Default;
            if (config == null || config.publishers == null || config.publishers.Count == 0)
                return null;

            if (string.IsNullOrEmpty(identifier))
                return null;

            // 1. Exact match on EffectiveId or id
            var match = config.publishers.FirstOrDefault(p =>
                string.Equals(p.EffectiveId, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.id, identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                resolvedPublisher = match.publisher;
                return match;
            }

            // 2. Exact match on enum name or integer value
            if (Enum.TryParse<Publisher>(identifier, true, out var parsedEnum))
            {
                match = config.publishers.FirstOrDefault(p => p.publisher == parsedEnum);
                resolvedPublisher = parsedEnum;
                return match;
            }

            // 3. Match on displayName (e.g. "Google Play (Free)")
            match = config.publishers.FirstOrDefault(p =>
                string.Equals(p.displayName, identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                resolvedPublisher = match.publisher;
                return match;
            }

            // 4. Normalized match (strip non-alphanumeric and compare lowercase)
            string Clean(string s) => string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var cleanId = Clean(identifier);

            match = config.publishers.FirstOrDefault(p =>
                Clean(p.EffectiveId) == cleanId ||
                Clean(p.displayName) == cleanId ||
                Clean(p.publisher.ToString()) == cleanId);

            if (match != null)
            {
                resolvedPublisher = match.publisher;
                return match;
            }

            // 5. Common aliases / shorthand:
            // "googlefree" -> GoogleAndroidFree
            // "googlefull" -> GoogleAndroidFull
            if (cleanId.Contains("google") && cleanId.Contains("free"))
            {
                match = config.publishers.FirstOrDefault(p => p.publisher == Publisher.GoogleAndroidFree);
                if (match != null) { resolvedPublisher = match.publisher; return match; }
            }
            if (cleanId.Contains("google") && cleanId.Contains("full"))
            {
                match = config.publishers.FirstOrDefault(p => p.publisher == Publisher.GoogleAndroidFull);
                if (match != null) { resolvedPublisher = match.publisher; return match; }
            }
            if (cleanId.Contains("amazon") && cleanId.Contains("free"))
            {
                match = config.publishers.FirstOrDefault(p => p.publisher == Publisher.AmazonAndroidFree);
                if (match != null) { resolvedPublisher = match.publisher; return match; }
            }
            if (cleanId.Contains("amazon") && cleanId.Contains("full"))
            {
                match = config.publishers.FirstOrDefault(p => p.publisher == Publisher.AmazonAndroidFull);
                if (match != null) { resolvedPublisher = match.publisher; return match; }
            }
            if (cleanId.Contains("ios") && cleanId.Contains("free"))
            {
                match = config.publishers.FirstOrDefault(p => p.publisher == Publisher.iOSFree);
                if (match != null) { resolvedPublisher = match.publisher; return match; }
            }
            if (cleanId.Contains("ios") && cleanId.Contains("full"))
            {
                match = config.publishers.FirstOrDefault(p => p.publisher == Publisher.iOSFull);
                if (match != null) { resolvedPublisher = match.publisher; return match; }
            }

            return null;
        }

        private static void ApplyVersionAndBuildNumberOverrides(ProjectBuildConfig config)
        {
            var ver = CommandLineArgs.Get("appVersion", CommandLineArgs.Get("bundleVersion", CommandLineArgs.Get("version", "")));
            if (!string.IsNullOrEmpty(ver))
            {
                PlayerSettings.bundleVersion = ver;
                if (config != null && config.gameConfig != null)
                {
                    var parts = ver.Split('.');
                    if (parts.Length >= 2)
                    {
                        int.TryParse(parts[0], out config.gameConfig.GameVersion.Major);
                        int.TryParse(parts[1], out config.gameConfig.GameVersion.Minor);
                        if (parts.Length >= 3)
                            int.TryParse(parts[2], out config.gameConfig.GameVersion.Build);
                        EditorUtility.SetDirty(config.gameConfig);
                    }
                }
                Debug.Log($"[BuildCLI] Version set to: {ver}");
            }

            var bnStr = CommandLineArgs.Get("appBuildNumber", CommandLineArgs.Get("buildNumber", ""));
            if (!string.IsNullOrEmpty(bnStr) && int.TryParse(bnStr, out var bnInt) && bnInt > 0)
            {
                // Never let a CLI-supplied build number regress a store-facing version field (Apple/Google
                // both reject a re-upload whose build number isn't strictly higher than the last accepted
                // one). Floors bnInt against what's already loaded from ProjectSettings.asset so a bad CLI
                // value (e.g. mangled by a CI wrapper's arg passthrough) can't silently downgrade it.
                int.TryParse(PlayerSettings.iOS.buildNumber, out var currentIosBuild);
                int.TryParse(PlayerSettings.macOS.buildNumber, out var currentMacBuild);
                var effectiveBnInt = Math.Max(bnInt, Math.Max(PlayerSettings.Android.bundleVersionCode, Math.Max(currentIosBuild, currentMacBuild)));
                var effectiveBnStr = effectiveBnInt.ToString();

                PlayerSettings.Android.bundleVersionCode = effectiveBnInt;
                PlayerSettings.iOS.buildNumber = effectiveBnStr;
                PlayerSettings.macOS.buildNumber = effectiveBnStr;
                if (config != null && config.gameConfig != null)
                {
                    config.gameConfig.AndroidBundleVersionCode = effectiveBnInt;
                    config.gameConfig.iOSBuildNumber = effectiveBnStr;
                    EditorUtility.SetDirty(config.gameConfig);
                }
                Debug.Log($"[BuildCLI] Build number set to: #{effectiveBnStr}");
            }

            AssetDatabase.SaveAssets();
        }
    }
}
