using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Checks that the project has the build profiles the Steam tab needs and creates the missing ones.
    /// One Steam profile is enough to build Windows, Mac and Linux (the platform is chosen per build and the
    /// output gets an automatic Windows/Mac/Linux subfolder), so the only hard requirement is that at least
    /// one profile is marked as Steam. Pure checks are separated from the asset writes so they can be tested.
    /// </summary>
    public static class SteamProfileAdvisor
    {
        public const string DefaultId = "steam";

        public sealed class Gap
        {
            public string Title = "";
            public string Why = "";
            public PlatformType Platform = PlatformType.Windows64;
        }

        public static List<PublisherProfile> SteamProfiles(ProjectBuildConfig config) =>
            (config?.publishers ?? new List<PublisherProfile>()).Where(p => p != null && p.isSteam).ToList();

        /// <summary>What is missing for Steam builds to work. Empty when the profiles are in place.</summary>
        public static List<Gap> FindGaps(ProjectBuildConfig config)
        {
            var gaps = new List<Gap>();
            if (config == null)
                return gaps;

            var steam = SteamProfiles(config);
            if (steam.Count == 0)
            {
                gaps.Add(new Gap
                {
                    Title = "Nenhum perfil Steam",
                    Why = "Sem um perfil marcado como Steam não há como gerar o build nem enviar para a Steam: o perfil define os defines " +
                          "(inclui o do NativeSocial), a pasta de saída (Windows/Mac/Linux) e a limpeza de arquivos Steam. " +
                          "Um único perfil Steam compila os três sistemas.",
                    Platform = PlatformType.Windows64
                });
                return gaps;
            }

            if (steam.All(p => p.hidden))
            {
                gaps.Add(new Gap
                {
                    Title = "Todos os perfis Steam estão ocultos",
                    Why = "Os perfis Steam existem mas estão marcados como ocultos, então não aparecem para build. Reative um deles em Perfis.",
                    Platform = steam[0].platform
                });
            }

            var desktop = new[] { PlatformType.Windows64, PlatformType.Windows32, PlatformType.macOS, PlatformType.Linux64 };
            if (steam.All(p => !desktop.Contains(p.platform)))
            {
                gaps.Add(new Gap
                {
                    Title = "Perfil Steam numa plataforma sem Steam",
                    Why = "Os perfis Steam apontam para uma plataforma que não é desktop. Crie um perfil Steam para Windows (o mesmo perfil gera Mac e Linux).",
                    Platform = PlatformType.Windows64
                });
            }

            return gaps;
        }

        /// <summary>Builds the profile (not yet added to the config). The id is unique among the existing profiles.</summary>
        public static PublisherProfile Create(ProjectBuildConfig config, PlatformType platform = PlatformType.Windows64)
        {
            var profile = new PublisherProfile(Publisher.Steam, "Steam", platform, false, true, false)
            {
                id = UniqueId(config),
                bundleIdentifier = BuildProfileTemplates.SuggestedBundle(config, true),
                scriptingDefines = new List<string>()
            };

            return profile;
        }

        public static string UniqueId(ProjectBuildConfig config)
        {
            var taken = new HashSet<string>(
                (config?.publishers ?? new List<PublisherProfile>()).Where(p => p != null).Select(p => p.EffectiveId),
                StringComparer.OrdinalIgnoreCase);

            if (!taken.Contains(DefaultId))
                return DefaultId;

            for (int i = 2; i < 100; i++)
            {
                string candidate = DefaultId + "-" + i;
                if (!taken.Contains(candidate))
                    return candidate;
            }

            return DefaultId + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Creates the Steam profile in the config asset and refreshes the profile catalog. Returns it.</summary>
        public static PublisherProfile AddProfile(ProjectBuildConfig config, PlatformType platform = PlatformType.Windows64)
        {
            if (config == null)
                return null;

            config.publishers ??= new List<PublisherProfile>();
            var profile = Create(config, platform);
            config.publishers.Add(profile);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(assetPath))
                BuildProfileCatalog.Write(config, assetPath);

            UnityEngine.Debug.Log($"[BuildPipeline] Perfil Steam criado: {profile.EffectiveId} ({profile.platform}).");
            return profile;
        }
    }
}
