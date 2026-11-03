using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// In-place upgrades of <see cref="ProjectBuildConfig"/> data. Runs on load (OnEnable), so the CLI and
    /// the window always see migrated values; the asset is marked dirty so the change reaches disk on save.
    /// What changed is kept in <see cref="Notes"/> for the migration assistant to show once.
    /// </summary>
    public static class ProjectBuildConfigMigrator
    {
        /// <summary>2 = explicit isSteam flag, per-profile language, game defines, AppDeployHub vault.</summary>
        public const int CurrentSchema = 3;

        /// <summary>Human-readable changes made by migrations in this editor session.</summary>
        public static readonly List<string> Notes = new List<string>();

        /// <summary>Returns true when something changed.</summary>
        public static bool Migrate(ProjectBuildConfig config)
        {
            if (config == null || config.schemaVersion >= CurrentSchema)
                return false;

            int from = config.schemaVersion;

            // Assets created before the field existed deserialize schemaVersion as 0.
            if (config.schemaVersion < 2)
            {
                var flagged = new List<string>();
                foreach (var profile in config.publishers ?? new List<PublisherProfile>())
                {
                    if (profile != null && !profile.isSteam && PublisherProfile.LooksLikeSteam(profile))
                    {
                        profile.isSteam = true;
                        flagged.Add(profile.EffectiveId);
                    }
                }

                config.gameDefines ??= new List<DefineDeclaration>();

                if (config.steamUpload != null && !config.steamUpload.steamEnabled &&
                    (!string.IsNullOrEmpty(config.steamUpload.appId) || flagged.Count > 0))
                {
                    config.steamUpload.steamEnabled = true;
                }

                // Only real 1.x assets get notes (a brand-new asset also starts at 0 but has no history).
                bool upgradedAsset = (config.publishers?.Count ?? 0) > 0 && !string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(config));
                if (upgradedAsset)
                {
                    Notes.Add($"ProjectBuildConfig atualizado do schema {from} para {CurrentSchema} (Build Pipeline 2.0).");
                    if (flagged.Count > 0)
                        Notes.Add("Perfis marcados como Steam pela regra antiga (nome/publisher): " + string.Join(", ", flagged) + ". Confira em Perfis.");
                    if (config.keystoreSource == KeystoreSource.AppDeployHubVault && string.IsNullOrEmpty(config.vaultKeystoreCredentialId))
                        Notes.Add("O cofre de keystore antigo (wagenheimer.com) foi aposentado: escolha a credencial do keystore em Assinatura (cofre do AppDeployHub), senão o build Android pelo editor falha.");
                    if (config.publishers.Any(p => p != null && (p.scriptingDefines?.Count ?? 0) > 0))
                        Notes.Add("Builds pelo editor agora aplicam os defines do perfil (na 1.x só o CLI aplicava). Revise em Defines.");
                }
            }

            
            if (config.schemaVersion < 3)
            {
                if (string.IsNullOrWhiteSpace(config.gameFolderName))
                {
                    config.gameFolderName = ProjectBuildConfig.SanitizeFolderName(config.projectName);
                }

                // Normalize publisher subfolders: remove duplicate 'Builds/' prefix and ensure {GameFolder} token
                foreach (var profile in config.publishers ?? new List<PublisherProfile>())
                {
                    if (profile == null) continue;
                    if (!string.IsNullOrEmpty(profile.outputSubfolder))
                    {
                        var norm = profile.outputSubfolder.Replace('\\', '/');
                        if (norm.StartsWith("Builds/Publishers/", StringComparison.OrdinalIgnoreCase))
                        {
                            norm = norm.Substring(7); // "Builds/" stripped -> "Publishers/..."
                        }
                        if (profile.isSteam && !norm.Contains("{GameFolder}", StringComparison.OrdinalIgnoreCase))
                        {
                            norm = "Publishers/Steam/{GameFolder}/";
                        }
                        profile.outputSubfolder = norm;
                    }
                }

                // Auto-sync Steam upload depot paths if unconfigured or pointing to old format
                if (config.steamUpload != null && config.steamUpload.steamEnabled)
                {
                    var winProfile = config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.Windows64);
                    if (winProfile != null && string.IsNullOrEmpty(config.steamUpload.lastBuiltPathWindows))
                    {
                        config.steamUpload.lastBuiltPathWindows = ProfileOutputPaths.ResolveFolder(config, winProfile, PlatformType.Windows64);
                    }
                    var macProfile = config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.macOS);
                    if (macProfile != null && string.IsNullOrEmpty(config.steamUpload.lastBuiltPathMac))
                    {
                        config.steamUpload.lastBuiltPathMac = ProfileOutputPaths.ResolveFolder(config, macProfile, PlatformType.macOS);
                    }
                    var linProfile = config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.Linux64);
                    if (linProfile != null && string.IsNullOrEmpty(config.steamUpload.lastBuiltPathLinux))
                    {
                        config.steamUpload.lastBuiltPathLinux = ProfileOutputPaths.ResolveFolder(config, linProfile, PlatformType.Linux64);
                    }
                }

                Notes.Add($"ProjectBuildConfig updated to schema {CurrentSchema}: clean gameFolderName '{config.GetEffectiveGameFolderName()}' and normalized Publishers/ output paths.");
            }

            config.schemaVersion = CurrentSchema;
            return true;
        }
    }
}
