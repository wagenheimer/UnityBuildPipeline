using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Tags that describe a profile for the <see cref="DefineDeclaration.requiredWhen"/> /
    /// <see cref="DefineDeclaration.forbiddenWhen"/> rules ("steam", "ios+free", "android+!full").
    /// </summary>
    public static class ProfileTags
    {
        public static HashSet<string> For(PublisherProfile profile)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile == null)
                return tags;

            tags.Add(profile.isFullGame ? "full" : "free");
            if (profile.isDemo) tags.Add("demo");
            if (profile.isSteam) tags.Add("steam");

            switch (profile.platform)
            {
                case PlatformType.Android: tags.Add("android"); tags.Add("mobile"); break;
                case PlatformType.iOS: tags.Add("ios"); tags.Add("mobile"); break;
                case PlatformType.macOS: tags.Add("mac"); tags.Add("desktop"); break;
                case PlatformType.Windows64:
                case PlatformType.Windows32: tags.Add("windows"); tags.Add("desktop"); break;
                case PlatformType.Linux64: tags.Add("linux"); tags.Add("desktop"); break;
                case PlatformType.WebGL: tags.Add("webgl"); break;
            }

            tags.Add("publisher:" + profile.publisher);
            tags.Add("id:" + profile.EffectiveId);
            return tags;
        }

        /// <summary>True when any rule matches. A rule is tags joined by '+' (AND); '!' negates a tag.</summary>
        public static bool MatchesAny(IEnumerable<string> rules, ISet<string> tags)
        {
            return FirstMatch(rules, tags) != null;
        }

        /// <summary>The first rule that matches (for messages), or null.</summary>
        public static string FirstMatch(IEnumerable<string> rules, ISet<string> tags)
        {
            if (rules == null)
                return null;

            foreach (string rule in rules)
            {
                if (Matches(rule, tags))
                    return rule.Trim();
            }

            return null;
        }

        public static bool Matches(string rule, ISet<string> tags)
        {
            if (string.IsNullOrWhiteSpace(rule))
                return false;

            var parts = rule.Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
            if (parts.Count == 0)
                return false;

            foreach (string part in parts)
            {
                bool negate = part[0] == '!';
                string tag = negate ? part.Substring(1).Trim() : part;
                bool has = tags.Contains(tag);
                if (has == negate)
                    return false;
            }

            return true;
        }
    }
}
