using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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

        /// <summary>
        /// Batch entry point for CI: validates the profile matrix and exits 1 on errors.
        /// <c>-executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.ValidateProfiles</c>
        /// </summary>
        public static void ValidateProfiles()
        {
            var config = FindOrCreateConfig();

            // Batch mode exits right after this method, so the editor-load hook may not have run.
            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(assetPath) && !BuildProfileCatalog.IsUpToDate(config, assetPath))
                BuildProfileCatalog.Write(config, assetPath);

            var issues = BuildProfileValidator.Validate(config, BuildProfileCatalog.ReadTrackingPolicy());
            string report = BuildProfileValidator.Format(issues);
            int errors = issues.Count(issue => issue.Severity == BuildProfileIssueSeverity.Error);

            if (errors > 0)
                Debug.LogError(report);
            else
                Debug.Log(report);

            if (Application.isBatchMode)
                EditorApplication.Exit(errors > 0 ? 1 : 0);
        }

        /// <summary>
        /// Writes the derived profile catalog for AppDeployHub/CI.
        /// <c>-executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.ExportProfileCatalog -output path</c>
        /// </summary>
        public static void ExportProfileCatalog()
        {
            var config = FindOrCreateConfig();
            string assetPath = AssetDatabase.GetAssetPath(config);
            string output = CommandLineArgs.Get("output", "");

            string catalog = BuildProfileCatalog.Write(
                config, assetPath, string.IsNullOrEmpty(output) ? null : output);

            if (string.IsNullOrEmpty(catalog))
            {
                Debug.LogError("[BuildPipeline] Não foi possível gerar o catálogo de perfis.");
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);

                return;
            }

            Debug.Log($"[BuildPipeline] Catálogo de perfis: {catalog}");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// Prints (or applies with <c>-apply</c>) the Free/Full counterpart of a profile.
        /// <c>-executeMethod ...BuildCLI.DuplicateProfileAsCounterpart -profile iOSFree [-apply]</c>
        /// </summary>
        public static void DuplicateProfileAsCounterpart()
        {
            var config = FindOrCreateConfig();
            string id = CommandLineArgs.Get("profile", "");
            bool apply = CommandLineArgs.GetBool("apply", false);

            var source = config.publishers.FirstOrDefault(profile =>
                string.Equals(profile.EffectiveId, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.displayName, id, StringComparison.OrdinalIgnoreCase));

            if (source == null)
            {
                Debug.LogError($"[BuildPipeline] Perfil '{id}' não encontrado. Ids: " +
                               string.Join(", ", config.publishers.Select(profile => profile.EffectiveId)));
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);

                return;
            }

            var tracking = BuildProfileCatalog.ReadTrackingPolicy();
            var clone = PublisherProfileDuplicator.Create(
                source, tracking?.adsDefine ?? "", tracking?.scriptingDefine ?? "");

            if (clone == null)
            {
                Debug.LogError($"[BuildPipeline] '{source.EffectiveId}' não tem contraparte Free/Full.");
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);

                return;
            }

            string report = $"[BuildPipeline] {source.EffectiveId} -> {clone.id}\n" +
                            $"  displayName : {clone.displayName}\n" +
                            $"  publisher   : {clone.publisher}\n" +
                            $"  isFullGame  : {clone.isFullGame}\n" +
                            $"  bundle      : {clone.bundleIdentifier}\n" +
                            $"  output      : {clone.outputSubfolder}\n" +
                            $"  defines     : {(clone.scriptingDefines.Count == 0 ? "(nenhum)" : string.Join(", ", clone.scriptingDefines))}";

            if (apply)
            {
                config.publishers.Add(clone);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();

                string assetPath = AssetDatabase.GetAssetPath(config);
                if (!string.IsNullOrEmpty(assetPath))
                    BuildProfileCatalog.Write(config, assetPath);

                report += "\n  aplicado: perfil adicionado ao ProjectBuildConfig.";
            }
            else
            {
                report += "\n  dry-run: use -apply para adicionar.";
            }

            Debug.Log(report);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        public static void Build()
        {
            Debug.Log("[BuildCLI] Starting Single Headless Build...");
            var config = FindOrCreateConfig();

            ApplyKeystoreSourceArg(config);

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

            // Intercept virtual/composite Steam all-in-one profile from Forge
            if (string.Equals(buildProfileId, "steam-all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(buildProfileId, "steam-deploy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(buildProfileId, "steam-all-upload", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[BuildCLI] -buildProfile steam-all detected: running full Steam deploy (Build Windows, Mac, Linux + SteamCmd Upload)...");
                BuildAndUploadSteam();
                return;
            }
            var pubStr = CommandLineArgs.Get("publisher", "");

            PublisherProfile profile = null;
            Publisher publisher = Publisher.Default;

            if (!string.IsNullOrEmpty(buildProfileId))
            {
                profile = FindPublisherProfile(config, buildProfileId, out publisher)
                          ?? FindDecoratedProfile(config, buildProfileId, out publisher);
                if (profile == null)
                    Debug.LogWarning($"[BuildCLI] -buildProfile '{buildProfileId}' not found in ProjectBuildConfig; attempting resolution via -publisher.");
            }

            if (profile == null && !string.IsNullOrEmpty(pubStr))
            {
                profile = FindPublisherProfile(config, pubStr, out publisher);
            }

            // An explicit -buildProfile that does not resolve is a configuration error (the dashboard
            // is out of sync with ProjectBuildConfig). It used to continue with -platform and compile
            // the target WITHOUT the profile defines: no bundle id, no ad define, no tracking define.
            if (profile == null && !string.IsNullOrEmpty(buildProfileId))
            {
                var available = string.Join(", ", config.publishers
                    .Select(p => p.EffectiveId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)));

                var message = $"[BuildCLI] -buildProfile '{buildProfileId}' não existe em '{config.name}'. " +
                              $"Ids disponíveis: {available}";

                if (CommandLineArgs.GetBool("allowMissingProfile", false))
                    Debug.LogWarning(message + " (-allowMissingProfile: seguindo com -publisher/-platform).");
                else
                    throw new BuildFailedException(message);
            }

            if (profile != null)
            {
                publisher = profile.publisher;
                var resolvedDefines = BuildDefineResolver.Resolve(config, profile);
                var definesLabel = resolvedDefines.Count == 0 ? "(none)" : string.Join(",", resolvedDefines);
                Debug.Log($"[BuildCLI] ✅ Resolved Profile: '{profile.displayName}' (Publisher: {publisher}, Platform: {profile.platform}, FullGame: {profile.isFullGame}, BundleId: {profile.bundleIdentifier}, Defines: {definesLabel})");
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

            // Language: -language is a one-off override; without it the profile lock / project default apply.
            var langArg = CommandLineArgs.Get("language", "");
            GameLanguage? languageOverride = BuildLanguageResolver.Parse(langArg);
            if (!string.IsNullOrEmpty(langArg) && languageOverride == null)
                Debug.LogWarning($"[BuildCLI] -language '{langArg}' não reconhecido; usando o idioma do perfil/projeto.");
            if (languageOverride.HasValue && profile != null && profile.IsLanguageLocked && profile.lockedLanguage != languageOverride.Value)
                Debug.LogWarning($"[BuildCLI] Perfil '{profile.EffectiveId}' é travado em {profile.lockedLanguage}, mas -language pede {languageOverride.Value}. O build usa {languageOverride.Value} (override registrado no manifest).");

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

            // Only the CLI extras go into the context: the step always applies the profile's own stack.
            var cliDefines = CommandLineArgs.Get("defines", "");
            var extraDefines = string.IsNullOrEmpty(cliDefines) ? new List<string>() : cliDefines.Split(';', ',').ToList();

            ApplyVersionAndBuildNumberOverrides(config);

            // Game-specific build variant (-variant ce). Falls back to the legacy -edition flag when it matches
            // a declared variant id, so existing CI scripts keep working.
            var variantArg = CommandLineArgs.Get("variant", CommandLineArgs.Get("edition", ""));
            if (string.Equals(variantArg, "both", StringComparison.OrdinalIgnoreCase)) variantArg = "";

            BuildVariant variant = null;
            var variantId = "";
            if (config.gameConfig != null && !string.IsNullOrEmpty(variantArg))
            {
                variant = config.gameConfig.FindBuildVariant(variantArg);
                variantId = variant != null ? variant.EffectiveId : variantArg.Trim();
                if (variant == null)
                    Debug.LogWarning($"[BuildCLI] -variant '{variantArg}' is not declared in '{config.gameConfig.name}'. " +
                                     "ApplyBuildVariant() still receives it, but no output suffix will be added.");
                else
                    Debug.Log($"[BuildCLI] Build variant: {variant.EffectiveDisplayName} ('{variantId}')");
            }

            var context = BuildContextFactory.Create(config, profile, new BuildContextFactory.Options
            {
                Platform = platform,
                Publisher = publisher,
                LanguageOverride = languageOverride,
                ExtraDefines = extraDefines,
                CheatMode = CommandLineArgs.GetBool("cheat", false),
                DevelopmentBuild = CommandLineArgs.GetBool("development", false),
                Demo = CommandLineArgs.Has("demo") ? CommandLineArgs.GetBool("demo", false) : (bool?)null,
                LevelEditor = CommandLineArgs.GetBool("levelEditor", false),
                LogLevelsInfo = CommandLineArgs.GetBool("logLevelsInfo", true),
                AppBundle = CommandLineArgs.GetBool("appBundle", true),
                AutoRun = CommandLineArgs.GetBool("autoRun", false),
                CustomOutputPath = CommandLineArgs.Get("outputPath", ""),
                VariantId = variantId,
                Variant = variant
            });
            if (profile == null && !string.IsNullOrEmpty(buildProfileId))
                context.ProfileId = buildProfileId;
            Debug.Log($"[BuildCLI] Idioma: {BuildLanguageResolver.Describe(context.Language, context.LanguageSource)}");

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

            ApplyKeystoreSourceArg(config);

            ApplyVersionAndBuildNumberOverrides(config);

            var pubArg = CommandLineArgs.Get("matrixPublishers", "");
            var profileArg = CommandLineArgs.Get("matrixProfiles", "");
            var langArg = CommandLineArgs.Get("matrixLanguages", "");
            var variantArg = CommandLineArgs.Get("matrixVariants", CommandLineArgs.Get("variant", ""));
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
                pubsToBuild = config.publishers.Where(p => p.platform is PlatformType.Windows64 or PlatformType.Windows32).ToList();
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

            // Game-specific variants: -matrixVariants "ce,se" (falls back to the GameConfig defaults).
            var variantsToBuild = new List<BuildVariant>();
            if (config.gameConfig != null && config.gameConfig.HasBuildVariants)
            {
                if (!string.IsNullOrEmpty(variantArg))
                {
                    foreach (var raw in variantArg.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var variant = config.gameConfig.FindBuildVariant(raw.Trim());
                        if (variant != null) variantsToBuild.Add(variant);
                        else Debug.LogWarning($"[BuildCLI] matrixVariants: variant '{raw.Trim()}' not declared in '{config.gameConfig.name}'.");
                    }
                }
                else
                {
                    variantsToBuild = config.gameConfig.GetDefaultBuildVariants();
                }
            }

            if (variantsToBuild.Count == 0) variantsToBuild.Add(null);

            // A profile locked to one language builds that language once, whatever -matrixLanguages says.
            var plan = new List<KeyValuePair<PublisherProfile, GameLanguage>>();
            foreach (var lang in langsToBuild)
                foreach (var pub in pubsToBuild)
                    if (!pub.IsLanguageLocked)
                        plan.Add(new KeyValuePair<PublisherProfile, GameLanguage>(pub, lang));
            foreach (var pub in pubsToBuild.Where(p => p.IsLanguageLocked))
                plan.Add(new KeyValuePair<PublisherProfile, GameLanguage>(pub, pub.lockedLanguage));

            var total = plan.Count * ((cheatOn ? 1 : 0) + (cheatOff ? 1 : 0)) * variantsToBuild.Count;
            var variantNames = string.Join(",", variantsToBuild.Select(v => v != null ? v.EffectiveId : "none"));
            Debug.Log($"[BuildCLI] Matrix planned: {total} builds ({pubsToBuild.Count} publishers x {langsToBuild.Count} languages x {variantsToBuild.Count} variants [{variantNames}]).");

            var allSuccess = true;
            foreach (var entry in plan)
            {
                var pub = entry.Key;
                var lang = entry.Value;
                {
                    foreach (var variant in variantsToBuild)
                    {
                        if (cheatOff)
                        {
                            var res = RunMatrixEntry(config, pub, lang, dev, false, manifest, variant);
                            if (!res) allSuccess = false;
                        }

                        if (cheatOn)
                        {
                            var res = RunMatrixEntry(config, pub, lang, dev, true, manifest, variant);
                            if (!res) allSuccess = false;
                        }
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
            bool dev, bool cheat, BuildManifest manifest, BuildVariant variant = null)
        {
            var ctx = BuildContextFactory.Create(config, pub, new BuildContextFactory.Options
            {
                LanguageOverride = pub.IsLanguageLocked ? (GameLanguage?)null : lang,
                CheatMode = cheat,
                DevelopmentBuild = dev,
                VariantId = variant != null ? variant.EffectiveId : "",
                Variant = variant
            });
            var res = BuildPipelineRunner.Execute(ctx);
            manifest.Add(BuildManifestEntry.From(ctx, res));
            return res.Success;
        }

        /// <summary>-keystoreSource LocalDisk|AppDeployHubVault ("RemoteVault", the 1.x name, maps to the vault).</summary>
        private static void ApplyKeystoreSourceArg(ProjectBuildConfig config)
        {
            if (!CommandLineArgs.Has("keystoreSource"))
                return;

            string value = CommandLineArgs.Get("keystoreSource");
            if (string.Equals(value, "RemoteVault", StringComparison.OrdinalIgnoreCase))
                value = nameof(KeystoreSource.AppDeployHubVault);

            if (Enum.TryParse<KeystoreSource>(value, true, out var source))
                config.keystoreSource = source;
            else
                Debug.LogWarning($"[BuildCLI] -keystoreSource '{value}' desconhecido (use LocalDisk ou AppDeployHubVault); mantendo {config.keystoreSource}.");
        }

        /// <summary>
        /// Id "decorado" com a plataforma, como "GameHouse (Win32)" (rótulo antigo do dashboard): tenta o nome sem o
        /// sufixo, pelo id ou pelo displayName, exigindo que a plataforma do sufixo bata e que haja UM candidato só.
        /// Sem isso o build abortava mesmo com o perfil certo no ProjectBuildConfig. Ambíguo = null (continua falhando).
        /// </summary>
        public static PublisherProfile FindDecoratedProfile(ProjectBuildConfig config, string identifier, out Publisher resolvedPublisher)
        {
            resolvedPublisher = Publisher.Default;
            if (config?.publishers == null || string.IsNullOrWhiteSpace(identifier))
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(identifier.Trim(), @"^(?<name>.+?)\s*\((?<suffix>[^()]+)\)$");
            if (!match.Success)
                return null;

            var name = match.Groups["name"].Value.Trim();
            var platform = PlatformFromLabel(match.Groups["suffix"].Value.Trim());
            var candidates = config.publishers.Where(p =>
                    string.Equals(p.EffectiveId, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.displayName, name, StringComparison.OrdinalIgnoreCase))
                .Where(p => platform == null || p.platform == platform.Value)
                .ToList();
            if (candidates.Count != 1)
                return null;

            var profile = candidates[0];
            resolvedPublisher = profile.publisher;
            Debug.LogWarning($"[BuildCLI] -buildProfile '{identifier}' não existe; usando '{profile.EffectiveId}' ({profile.platform}). " +
                             $"Corrija o id do perfil no dashboard para '{profile.EffectiveId}'.");
            return profile;
        }

        private static PlatformType? PlatformFromLabel(string label)
        {
            switch (label.Replace(" ", "").Replace("-", "").ToLowerInvariant())
            {
                case "win32": case "windows32": case "x86": case "windowsx86": return PlatformType.Windows32;
                case "win64": case "windows64": case "windows": case "x64": return PlatformType.Windows64;
                case "macos": case "mac": case "osx": return PlatformType.macOS;
                case "linux": case "linux64": return PlatformType.Linux64;
                case "android": return PlatformType.Android;
                case "ios": return PlatformType.iOS;
                case "webgl": return PlatformType.WebGL;
                default: return null;
            }
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

        /// <summary>
        /// Compiles all configured Steam profiles (steam-windows, steam-mac, steam-linux) in headless batch mode.
        /// <c>-executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildSteam</c>
        /// </summary>
        public static void BuildSteam()
        {
            Debug.Log("[BuildCLI] Starting Steam Batch Build...");
            var config = FindOrCreateConfig();
            var steamProfiles = config.publishers.Where(p => p != null && p.isSteam && !p.hidden).ToList();
            if (steamProfiles.Count == 0)
            {
                Debug.LogError("[BuildCLI] No active Steam profiles found in ProjectBuildConfig.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            int failed = 0;
            foreach (var profile in steamProfiles)
            {
                Debug.Log($"[BuildCLI] Compiling Steam profile: {profile.displayName} ({profile.platform})...");
                var context = BuildContextFactory.Create(config, profile, new BuildContextFactory.Options());
                var result = BuildPipelineRunner.Execute(context);
                if (!result.Success)
                {
                    failed++;
                    Debug.LogError($"[BuildCLI] Build failed for {profile.displayName}: {result.ErrorMessage}");
                }
                else
                {
                    var contentRoot = Path.GetDirectoryName(result.OutputPath);
                    switch (profile.platform)
                    {
                        case PlatformType.Windows64: config.steamUpload.lastBuiltPathWindows = contentRoot; break;
                        case PlatformType.macOS: config.steamUpload.lastBuiltPathMac = contentRoot; break;
                        case PlatformType.Linux64: config.steamUpload.lastBuiltPathLinux = contentRoot; break;
                    }
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildCLI] Steam Build Batch finished. Succeeded: {steamProfiles.Count - failed}, Failed: {failed}.");
            if (Application.isBatchMode) EditorApplication.Exit(failed > 0 ? 1 : 0);
        }

        /// <summary>
        /// Runs SteamCmd to upload the configured depots headlessly.
        /// <c>-executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.UploadToSteam [-steamBranch beta] [-steamUsername user] [-steamPassword pwd]</c>
        /// </summary>
        public static void UploadToSteam()
        {
            Debug.Log("[BuildCLI] Starting Steam Headless Upload...");
            var config = FindOrCreateConfig();

            string branch = CommandLineArgs.Get("steamBranch", "");
            string username = CommandLineArgs.Get("steamUsername", "");
            string password = CommandLineArgs.Get("steamPassword", "");

            var (success, summary) = SteamUploadRunner.UploadHeadless(config, branch, username, password);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        /// <summary>
        /// Compiles all configured Steam standalone targets and immediately uploads them to Steam via SteamCmd.
        /// <c>-executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildAndUploadSteam [-steamBranch beta] [-steamUsername user] [-steamPassword pwd]</c>
        /// </summary>
        public static void BuildAndUploadSteam()
        {
            Debug.Log("[BuildCLI] Starting Full Build and Upload Steam Pipeline...");
            var config = FindOrCreateConfig();

            var manifestPath = CommandLineArgs.Get("manifest", "");
            var manifest = !string.IsNullOrEmpty(manifestPath) ? new BuildManifest() : null;

            // 1. Build all Steam profiles
            var steamProfiles = config.publishers.Where(p => p != null && p.isSteam && !p.hidden).ToList();
            if (steamProfiles.Count == 0)
            {
                Debug.LogError("[BuildCLI] No active Steam profiles found in ProjectBuildConfig.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            int failedCount = 0;
            foreach (var profile in steamProfiles)
            {
                Debug.Log($"[BuildCLI] Compiling {profile.displayName} ({profile.platform})...");
                var context = BuildContextFactory.Create(config, profile, new BuildContextFactory.Options());
                var result = BuildPipelineRunner.Execute(context);
                if (manifest != null)
                    manifest.Add(BuildManifestEntry.From(context, result));

                if (!result.Success)
                {
                    failedCount++;
                    Debug.LogError($"[BuildCLI] Build failed for {profile.displayName}: {result.ErrorMessage}");
                }
                else
                {
                    var contentRoot = Path.GetDirectoryName(result.OutputPath);
                    switch (profile.platform)
                    {
                        case PlatformType.Windows64: config.steamUpload.lastBuiltPathWindows = contentRoot; break;
                        case PlatformType.macOS: config.steamUpload.lastBuiltPathMac = contentRoot; break;
                        case PlatformType.Linux64: config.steamUpload.lastBuiltPathLinux = contentRoot; break;
                    }
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            if (manifest != null && !string.IsNullOrEmpty(manifestPath))
                manifest.WriteTo(manifestPath);

            if (failedCount > 0)
            {
                Debug.LogError($"[BuildCLI] {failedCount} Steam profile(s) failed to compile. Aborting Steam upload.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // 2. Upload to Steam
            string branch = CommandLineArgs.Get("steamBranch", "");
            string username = CommandLineArgs.Get("steamUsername", "");
            string password = CommandLineArgs.Get("steamPassword", "");

            var (uploadOk, uploadSummary) = SteamUploadRunner.UploadHeadless(config, branch, username, password);
            if (Application.isBatchMode) EditorApplication.Exit(uploadOk ? 0 : 1);
        }
    }
}
