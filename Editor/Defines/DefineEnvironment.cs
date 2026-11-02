using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Editor side of the define stack: reads the PlayerSettings globals of the profile's target group and
    /// the pipeline's automatic defines, then delegates to the pure <see cref="DefineStackResolver"/>.
    /// </summary>
    public static class DefineEnvironment
    {
        private static bool? _steamworksPresent;

        public static string TargetGroupName(PlatformType platform) => platform.ToBuildTargetGroup().ToString();

        public static List<string> GlobalDefines(PlatformType platform)
        {
            var named = NamedBuildTarget.FromBuildTargetGroup(platform.ToBuildTargetGroup());
            return DefineStackResolver.Split(new[] { PlayerSettings.GetScriptingDefineSymbols(named) }).ToList();
        }

        /// <summary>
        /// Defines the pipeline injects for a profile. Today: UnityNativeSocial's Steam define on Steam profiles,
        /// only when Steamworks.NET is in the project (so non-Steam projects never get an unused symbol).
        /// </summary>
        public static List<AutoDefine> AutoDefines(PublisherProfile profile)
        {
            var result = new List<AutoDefine>();
            if (profile != null && profile.isSteam && IsSteamworksPresent())
            {
                bool declared = DefineStackResolver.Split(profile.scriptingDefines)
                    .Any(define => string.Equals(define, PublisherProfile.NativeSocialSteamDefine, StringComparison.OrdinalIgnoreCase));

                if (!declared)
                    result.Add(new AutoDefine(PublisherProfile.NativeSocialSteamDefine, "pipeline: perfil Steam + Steamworks.NET instalado"));
            }

            return result;
        }

        public static DefineStack StackFor(ProjectBuildConfig config, PublisherProfile profile, IEnumerable<string> cliDefines = null)
        {
            var platform = profile != null ? profile.platform : PlatformType.Windows64;
            return DefineStackResolver.Resolve(config, profile, GlobalDefines(platform), AutoDefines(profile), cliDefines,
                TargetGroupName(platform));
        }

        public static bool IsSteamworksPresent()
        {
            if (_steamworksPresent.HasValue)
                return _steamworksPresent.Value;

            _steamworksPresent = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            {
                try { return assembly.GetType("Steamworks.SteamUserStats", false) != null; }
                catch (Exception) { return false; }
            });

            return _steamworksPresent.Value;
        }

        /// <summary>Target groups the profiles build for (Standalone, Android, iOS, WebGL), distinct.</summary>
        public static List<PlatformType> RepresentativePlatforms(ProjectBuildConfig config)
        {
            var groups = new Dictionary<BuildTargetGroup, PlatformType>();
            foreach (var profile in config?.publishers ?? new List<PublisherProfile>())
            {
                if (profile == null)
                    continue;

                var group = profile.platform.ToBuildTargetGroup();
                if (!groups.ContainsKey(group))
                    groups[group] = profile.platform;
            }

            return groups.Values.ToList();
        }
    }
}
