using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// One-click fixes for the validator findings shown in the Profile Matrix. Only unambiguous cases
    /// are fixed; anything that needs a human decision stays manual.
    /// </summary>
    public static class BuildProfileFixer
    {
        public static bool CanFix(string code) => code is
            "id-implicit" or "full-with-ads" or "free-without-ads" or
            "bundle-missing" or "bundle-duplicate" or "output-duplicate" or
            "preset-missing" or "define-conflict";

        public static bool TryFix(
            ProjectBuildConfig config, PublisherProfile profile, BuildProfileIssue issue, BuildCatalogTracking tracking)
        {
            if (profile == null || issue == null)
                return false;

            switch (issue.Code)
            {
                case "id-implicit":
                    profile.id = profile.publisher.ToString();
                    return true;

                case "full-with-ads":
                    if (string.IsNullOrWhiteSpace(tracking?.adsDefine))
                        return false;
                    RemoveDefine(profile, tracking.adsDefine);
                    RemoveDefine(profile, "-" + tracking.adsDefine);
                    AddDefine(profile, "!" + tracking.adsDefine);
                    return true;

                case "free-without-ads":
                    if (string.IsNullOrWhiteSpace(tracking?.adsDefine))
                        return false;
                    RemoveDefine(profile, "!" + tracking.adsDefine);
                    RemoveDefine(profile, "-" + tracking.adsDefine);
                    AddDefine(profile, tracking.adsDefine);
                    return true;

                case "bundle-missing":
                {
                    string baseBundle = (config?.defaultBundleIdentifier ?? "").Trim();
                    if (baseBundle.Length == 0)
                        return false;

                    profile.bundleIdentifier = profile.isFullGame && !baseBundle.EndsWith("Full", StringComparison.OrdinalIgnoreCase)
                        ? baseBundle + "Full"
                        : baseBundle;
                    return true;
                }

                case "bundle-duplicate":
                    profile.bundleIdentifier = (profile.bundleIdentifier ?? "").Trim() +
                                               (profile.isFullGame ? "Full" : "Free");
                    return true;

                case "output-duplicate":
                    profile.outputSubfolder = AppendMarker(profile.outputSubfolder, profile.isFullGame ? "Full" : "Free");
                    return true;

                case "preset-missing":
                {
                    if (config == null)
                        return false;

                    var unknown = BuildDefineResolver.UnknownPresets(config, profile);
                    if (unknown.Count == 0)
                        return false;

                    profile.definePresets.RemoveAll(id => unknown.Any(u =>
                        string.Equals(id?.Trim(), u, StringComparison.OrdinalIgnoreCase)));
                    return true;
                }

                case "define-conflict":
                {
                    if (profile.scriptingDefines == null)
                        return false;

                    var additions = profile.scriptingDefines
                        .Where(define => !IsRemoval(define))
                        .Select(define => define.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    int removed = profile.scriptingDefines.RemoveAll(define =>
                        IsRemoval(define) && additions.Contains(define.TrimStart('!', '-').Trim()));

                    return removed > 0;
                }

                default:
                    return false;
            }
        }

        /// <summary>Applies every fixable issue of one profile. Returns how many were fixed.</summary>
        public static int FixAll(
            ProjectBuildConfig config, PublisherProfile profile, IEnumerable<BuildProfileIssue> issues,
            BuildCatalogTracking tracking)
        {
            int fixedCount = 0;

            foreach (var issue in issues.ToList())
            {
                if (TryFix(config, profile, issue, tracking))
                    fixedCount++;
            }

            return fixedCount;
        }

        private static void AddDefine(PublisherProfile profile, string define)
        {
            profile.scriptingDefines ??= new List<string>();

            bool exists = profile.scriptingDefines.Any(value =>
                string.Equals(value?.Trim(), define, StringComparison.OrdinalIgnoreCase));

            if (!exists)
                profile.scriptingDefines.Add(define);
        }

        private static void RemoveDefine(PublisherProfile profile, string define)
        {
            profile.scriptingDefines?.RemoveAll(value =>
                string.Equals(value?.Trim(), define, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsRemoval(string define)
        {
            string value = define?.Trim();
            return !string.IsNullOrEmpty(value) && (value[0] == '!' || value[0] == '-');
        }

        private static string AppendMarker(string output, string marker)
        {
            string value = (output ?? "").Trim();
            if (value.Length == 0)
                return $"Builds/Publishers/{marker}/";

            string trimmed = value.TrimEnd('/', '\\');
            string trailing = value.EndsWith("/") ? "/" : value.EndsWith("\\") ? "\\" : "";
            return $"{trimmed} {marker}{trailing}";
        }
    }
}
