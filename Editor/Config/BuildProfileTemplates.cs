using System;
using System.Collections.Generic;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Defaults used by the "new profile" wizard: platform, id, display name, bundle and output.</summary>
    public static class BuildProfileTemplates
    {
        public static PlatformType PlatformFor(Publisher publisher)
        {
            if (publisher.IsAndroid()) return PlatformType.Android;
            if (publisher.IsIOS()) return PlatformType.iOS;
            if (publisher.IsMac()) return PlatformType.macOS;
            return PlatformType.Windows64;
        }

        public static string Family(Publisher publisher) =>
            publisher.ToString().Replace("Free", "").Replace("Full", "");

        public static string SuggestedId(Publisher publisher, bool full) =>
            Family(publisher) + (full ? "Full" : "Free");

        public static string SuggestedDisplayName(Publisher publisher, bool full)
        {
            string label = Family(publisher) switch
            {
                "GoogleAndroid" => "Google Play",
                "AmazonAndroid" => "Amazon",
                "iOS" => "iOS",
                "Samsung" => "Samsung",
                "MacAppStore" => "Mac App Store",
                "WindowsStore" => "Windows Store",
                _ => Family(publisher)
            };

            return $"{label} ({(full ? "Full" : "Free")})";
        }

        public static string SuggestedOutput(Publisher publisher, bool full) =>
            $"Builds/Publishers/{publisher}/{(full ? "Full" : "Free")}/";

        public static string SuggestedBundle(ProjectBuildConfig config, bool full)
        {
            string baseBundle = (config?.defaultBundleIdentifier ?? "").Trim();
            if (baseBundle.Length == 0)
                return "";

            return full && !baseBundle.EndsWith("Full", StringComparison.OrdinalIgnoreCase)
                ? baseBundle + "Full"
                : baseBundle;
        }

        public static PublisherProfile Create(
            Publisher publisher, bool full, string id, string bundle, string output, BuildCatalogTracking tracking)
        {
            var profile = new PublisherProfile(
                publisher, SuggestedDisplayName(publisher, full), PlatformFor(publisher), false, full, false)
            {
                id = string.IsNullOrWhiteSpace(id) ? SuggestedId(publisher, full) : id.Trim(),
                bundleIdentifier = bundle?.Trim() ?? "",
                outputSubfolder = string.IsNullOrWhiteSpace(output)
                    ? SuggestedOutput(publisher, full)
                    : output.Trim(),
                scriptingDefines = new List<string>()
            };

            if (!string.IsNullOrWhiteSpace(tracking?.adsDefine))
                profile.scriptingDefines.Add(full ? "!" + tracking.adsDefine : tracking.adsDefine);

            if (!full && profile.platform == PlatformType.iOS && !string.IsNullOrWhiteSpace(tracking?.scriptingDefine))
                profile.scriptingDefines.Add(tracking.scriptingDefine);

            return profile;
        }
    }
}
