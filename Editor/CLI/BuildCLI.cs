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

            // CI: keystore Android por args (o Forge injeta o keystore central). Passwords vêm das envs
            // ANDROID_KEYSTORE_PASS / ANDROID_KEYALIAS_PASS já lidas por GetEffective*Password().
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

            // Resolve profile: -buildProfile <id> takes precedence over -publisher <enum>
            var buildProfileId = CommandLineArgs.Get("buildProfile", "");
            PublisherProfile profileById = null;
            if (!string.IsNullOrEmpty(buildProfileId))
            {
                profileById = config.publishers.FirstOrDefault(p =>
                    string.Equals(p.EffectiveId, buildProfileId, StringComparison.OrdinalIgnoreCase));
                if (profileById == null)
                    Debug.LogWarning($"[BuildCLI] -buildProfile '{buildProfileId}' not found in ProjectBuildConfig; falling back to -publisher.");
            }

            // Parse Publisher
            var pubStr = CommandLineArgs.Get("publisher", "Default");
            Publisher publisher;
            if (profileById != null)
                publisher = profileById.publisher;
            else if (!Enum.TryParse(pubStr, true, out publisher))
                publisher = Publisher.Default;

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

            // Parse Platform
            var platStr = CommandLineArgs.Get("platform", "");
            var profile = profileById ?? config.publishers.FirstOrDefault(p => p.publisher == publisher);
            var platform = PlatformType.Windows64;
            if (!string.IsNullOrEmpty(platStr) && Enum.TryParse(platStr, true, out PlatformType pt))
            {
                platform = pt;
            }
            else if (profile != null)
            {
                platform = profile.platform;
            }

            // Extra scripting defines: profile defines + CLI -defines override (merged in the step)
            var defines = new List<string>();
            if (profile != null && profile.scriptingDefines != null)
                defines.AddRange(profile.scriptingDefines);
            var cliDefines = CommandLineArgs.Get("defines", "");
            if (!string.IsNullOrEmpty(cliDefines))
                defines.AddRange(cliDefines.Split(';', ','));

            // Version override if specified
            if (CommandLineArgs.Has("version") && config.gameConfig != null)
            {
                var parts = CommandLineArgs.Get("version").Split('.');
                if (parts.Length >= 2)
                {
                    int.TryParse(parts[0], out config.gameConfig.GameVersion.Major);
                    int.TryParse(parts[1], out config.gameConfig.GameVersion.Minor);
                    if (parts.Length >= 3)
                        int.TryParse(parts[2], out config.gameConfig.GameVersion.Build);
                }
            }

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
                    if (Enum.TryParse(name.Trim(), true, out Publisher pub))
                    {
                        var prof = config.publishers.FirstOrDefault(p => p.publisher == pub)
                            ?? new PublisherProfile(pub, pub.ToString(), PlatformType.Windows64);
                        pubsToBuild.Add(prof);
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
    }
}
