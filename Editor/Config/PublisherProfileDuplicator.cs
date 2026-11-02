using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Creates the Free/Full counterpart of a mobile profile: flips the publisher and isFullGame,
    /// adjusts id/display name, bundle id and output folder, and fixes the ad/tracking defines so the
    /// new profile is born valid instead of being retyped by hand.
    /// </summary>
    public static class PublisherProfileDuplicator
    {
        private static readonly Dictionary<Publisher, Publisher> Counterparts = new Dictionary<Publisher, Publisher>
        {
            [Publisher.GoogleAndroidFree] = Publisher.GoogleAndroidFull,
            [Publisher.GoogleAndroidFull] = Publisher.GoogleAndroidFree,
            [Publisher.AmazonAndroidFree] = Publisher.AmazonAndroidFull,
            [Publisher.AmazonAndroidFull] = Publisher.AmazonAndroidFree,
            [Publisher.iOSFree] = Publisher.iOSFull,
            [Publisher.iOSFull] = Publisher.iOSFree,
            [Publisher.SamsungFree] = Publisher.SamsungFull,
            [Publisher.SamsungFull] = Publisher.SamsungFree,
            [Publisher.MacAppStore] = Publisher.MacAppStoreFull,
            [Publisher.MacAppStoreFull] = Publisher.MacAppStore,
            [Publisher.WindowsStore] = Publisher.WindowsStoreFull,
            [Publisher.WindowsStoreFull] = Publisher.WindowsStore
        };

        public static bool TryGetCounterpart(Publisher publisher, out Publisher counterpart) =>
            Counterparts.TryGetValue(publisher, out counterpart);

        /// <summary>Builds a new profile (used by the CLI).</summary>
        public static PublisherProfile Create(PublisherProfile source, string adsDefine, string trackingDefine)
        {
            if (source == null)
                return null;

            var clone = new PublisherProfile
            {
                id = source.id,
                publisher = source.publisher,
                displayName = source.displayName,
                platform = source.platform,
                bundleIdentifier = source.bundleIdentifier,
                requiresSplash = source.requiresSplash,
                isFullGame = source.isFullGame,
                isDemo = source.isDemo,
                scriptingBackend = source.scriptingBackend,
                macArchitecture = source.macArchitecture,
                macAppStoreValidation = source.macAppStoreValidation,
                scriptingDefines = new List<string>(source.scriptingDefines ?? new List<string>()),
                outputSubfolder = source.outputSubfolder,
                zipAfterBuild = source.zipAfterBuild,
                zipDestinationTemplate = source.zipDestinationTemplate
            };

            if (!Apply(source, clone, adsDefine, trackingDefine))
                return null;

            return clone;
        }

        /// <summary>Transforms <paramref name="target"/> (already a copy of <paramref name="source"/>).</summary>
        public static bool Apply(PublisherProfile source, PublisherProfile target, string adsDefine, string trackingDefine)
        {
            if (source == null || target == null || !TryGetCounterpart(source.publisher, out var counterpart))
                return false;

            bool toFull = !source.isFullGame;
            string marker = toFull ? "Full" : "Free";

            target.publisher = counterpart;
            target.isFullGame = toFull;
            target.isDemo = source.isDemo;
            target.platform = source.platform;

            target.id = SwapId(source.id, marker);
            target.displayName = SwapName(source.displayName, marker);
            target.bundleIdentifier = SwapBundle(source.bundleIdentifier, marker);
            target.outputSubfolder = SwapOutput(source.outputSubfolder, marker);
            target.scriptingDefines = BuildDefines(source, target.platform, toFull, adsDefine, trackingDefine);

            return true;
        }

        private static string SwapId(string id, string marker)
        {
            string value = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
            string swapped = Regex.Replace(value, "(Free|Full)$", marker, RegexOptions.IgnoreCase);

            if (!string.Equals(swapped, value, StringComparison.Ordinal))
                return swapped;

            return value + marker;
        }

        private static string SwapName(string displayName, string marker)
        {
            string value = string.IsNullOrWhiteSpace(displayName) ? "" : displayName.Trim();
            string swapped = Regex.Replace(value, "\\((Free|Full)\\)", $"({marker})", RegexOptions.IgnoreCase);

            if (!string.Equals(swapped, value, StringComparison.Ordinal))
                return swapped;

            return value + $" ({marker})";
        }

        private static string SwapBundle(string bundle, string marker)
        {
            string value = string.IsNullOrWhiteSpace(bundle) ? "" : bundle.Trim();
            if (value.Length == 0)
                return value;

            bool toFull = marker == "Full";

            if (toFull)
            {
                if (value.EndsWith("Full", StringComparison.OrdinalIgnoreCase))
                    return value;

                if (value.EndsWith("Free", StringComparison.OrdinalIgnoreCase))
                    return value[..^"Free".Length] + "Full";

                return value + "Full";
            }

            if (value.EndsWith("Full", StringComparison.OrdinalIgnoreCase))
            {
                string stripped = value[..^"Full".Length];
                return stripped.Length == 0 ? value : stripped;
            }

            return value;
        }

        private static string SwapOutput(string output, string marker)
        {
            string value = string.IsNullOrWhiteSpace(output) ? "" : output.Trim();
            if (value.Length == 0)
                return value;

            // "Builds/iOS Free/" <-> "Builds/iOS Full/", "Google Full/" <-> "Google Free/"
            string swapped = Regex.Replace(value, "(Free|Full)", marker, RegexOptions.IgnoreCase);
            if (!string.Equals(swapped, value, StringComparison.Ordinal))
                return swapped;

            // No Free/Full marker in the path (e.g. "Builds/iOS/"): add one so the two variants do
            // not share the same output folder.
            string trimmed = value.TrimEnd('/', '\\');
            string trailing = value.EndsWith("/") ? "/" : value.EndsWith("\\") ? "\\" : "";
            return $"{trimmed} {marker}{trailing}";
        }

        private static List<string> BuildDefines(
            PublisherProfile source, PlatformType platform, bool toFull, string adsDefine, string trackingDefine)
        {
            var result = new List<string>();

            foreach (string raw in source.scriptingDefines ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string value = raw.Trim();
                string name = value.TrimStart('!', '-').Trim();

                // The ad/tracking defines are rebuilt for the target profile.
                if (!string.IsNullOrEmpty(adsDefine) && string.Equals(name, adsDefine, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(trackingDefine) && string.Equals(name, trackingDefine, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(value);
            }

            if (!string.IsNullOrEmpty(adsDefine))
            {
                // Full profiles remove the ad define (Android inherits it from ProjectSettings);
                // Free profiles apply it.
                result.Add(toFull ? "!" + adsDefine : adsDefine);
            }

            if (platform == PlatformType.iOS && !toFull && !string.IsNullOrEmpty(trackingDefine))
                result.Add(trackingDefine);

            return result
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
