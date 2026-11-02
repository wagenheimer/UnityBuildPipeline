using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Expands profile define presets into the final list used by the build, catalog and validator.</summary>
    public static class BuildDefineResolver
    {
        /// <summary>Preset defines (in order) followed by the profile's own list, deduplicated.</summary>
        public static List<string> Resolve(ProjectBuildConfig config, PublisherProfile profile)
        {
            var result = new List<string>();
            if (profile == null)
                return result;

            foreach (string presetId in profile.definePresets ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(presetId))
                    continue;

                var preset = Find(config, presetId);
                if (preset?.defines == null)
                    continue;

                foreach (string define in preset.defines)
                {
                    if (!string.IsNullOrWhiteSpace(define))
                        result.Add(define.Trim());
                }
            }

            foreach (string define in profile.scriptingDefines ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(define))
                    result.Add(define.Trim());
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Preset ids referenced by the profile that do not exist in the config.</summary>
        public static List<string> UnknownPresets(ProjectBuildConfig config, PublisherProfile profile)
        {
            var unknown = new List<string>();

            foreach (string presetId in profile?.definePresets ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(presetId))
                    continue;

                if (Find(config, presetId) == null)
                    unknown.Add(presetId.Trim());
            }

            return unknown;
        }

        public static BuildDefinePreset Find(ProjectBuildConfig config, string id)
        {
            if (config?.definePresets == null || string.IsNullOrWhiteSpace(id))
                return null;

            return config.definePresets.FirstOrDefault(preset =>
                preset != null && string.Equals(preset.id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
