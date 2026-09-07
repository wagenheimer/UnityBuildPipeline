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

            // Parse Publisher
            var pubStr = CommandLineArgs.Get("publisher", "Default");
            if (!Enum.TryParse(pubStr, true, out Publisher publisher))
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
            var profile = config.publishers.FirstOrDefault(p => p.publisher == publisher);
            var platform = PlatformType.Windows64;
            if (!string.IsNullOrEmpty(platStr) && Enum.TryParse(platStr, true, out PlatformType pt))
            {
                platform = pt;
            }
            else if (profile != null)
            {
                platform = profile.platform;
            }

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
            var langArg = CommandLineArgs.Get("matrixLanguages", "");
            var cheatOn = CommandLineArgs.GetBool("cheatOn", false);
            var cheatOff = CommandLineArgs.GetBool("cheatOff", true);
            var dev = CommandLineArgs.GetBool("development", false);

            var pubsToBuild = new List<PublisherProfile>();
            if (!string.IsNullOrEmpty(pubArg))
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
                        var ctx = new BuildContext
                        {
                            Config = config,
                            Publisher = pub.publisher,
                            PublisherProfile = pub,
                            Language = lang,
                            Platform = pub.platform,
                            CheatMode = false,
                            DevelopmentBuild = dev,
                            Demo = pub.isDemo
                        };
                        var res = BuildPipelineRunner.Execute(ctx);
                        if (!res.Success) allSuccess = false;
                    }

                    if (cheatOn)
                    {
                        var ctx = new BuildContext
                        {
                            Config = config,
                            Publisher = pub.publisher,
                            PublisherProfile = pub,
                            Language = lang,
                            Platform = pub.platform,
                            CheatMode = true,
                            DevelopmentBuild = dev,
                            Demo = pub.isDemo
                        };
                        var res = BuildPipelineRunner.Execute(ctx);
                        if (!res.Success) allSuccess = false;
                    }
                }
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(allSuccess ? 0 : 1);
            }
        }
    }
}
