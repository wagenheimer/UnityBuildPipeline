using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    /// <summary>Small builders so each test reads as the profile matrix it describes.</summary>
    internal static class Profiles
    {
        public static PublisherProfile Mobile(
            Publisher publisher, string id, PlatformType platform, bool full,
            string bundle = "com.example.game", string output = "", params string[] defines)
        {
            return new PublisherProfile(publisher, id, platform, false, full, false)
            {
                id = id,
                bundleIdentifier = bundle,
                outputSubfolder = output,
                scriptingDefines = defines?.ToList() ?? new List<string>()
            };
        }

        public static ProjectBuildConfig Config(
            IEnumerable<BuildDefinePreset> presets, params PublisherProfile[] profiles)
        {
            var config = ScriptableObject.CreateInstance<ProjectBuildConfig>();
            config.publishers = profiles.ToList();
            config.definePresets = presets?.ToList() ?? new List<BuildDefinePreset>();
            return config;
        }

        public static BuildDefinePreset Preset(string id, params string[] defines)
        {
            return new BuildDefinePreset
            {
                id = id,
                displayName = id,
                defines = defines.ToList()
            };
        }

        public static BuildCatalogTracking Tracking(string adsDefine = "ADS", string scriptingDefine = "TRACK")
        {
            return new BuildCatalogTracking
            {
                requireTracking = true,
                adsDefine = adsDefine,
                scriptingDefine = scriptingDefine
            };
        }
    }
}
