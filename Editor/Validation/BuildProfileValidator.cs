using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum BuildProfileIssueSeverity
    {
        Warning,
        Error
    }

    public sealed class BuildProfileIssue
    {
        public BuildProfileIssueSeverity Severity;
        public string ProfileId;
        public string Code;
        public string Message;

        public override string ToString() =>
            $"[{(Severity == BuildProfileIssueSeverity.Error ? "ERRO" : "AVISO")}] {ProfileId}: {Message}";
    }

    /// <summary>
    /// Validates the build profiles before a build exists: ids, bundle ids, output folders, define
    /// syntax and the Free/Full vs ad-define sanity. The ATT declaration rule itself stays with
    /// com.wagenheimer.tracking (single owner); here we only catch what breaks the build matrix.
    /// </summary>
    public static class BuildProfileValidator
    {
        public static List<BuildProfileIssue> Validate(ProjectBuildConfig config, BuildCatalogTracking tracking)
        {
            var issues = new List<BuildProfileIssue>();

            if (config?.publishers == null || config.publishers.Count == 0)
            {
                Add(issues, BuildProfileIssueSeverity.Error, "(config)", "no-profiles",
                    "Nenhum perfil de build declarado no ProjectBuildConfig.");
                return issues;
            }

            var byId = new Dictionary<string, PublisherProfile>(StringComparer.OrdinalIgnoreCase);
            var byBundle = new Dictionary<string, List<PublisherProfile>>(StringComparer.OrdinalIgnoreCase);
            var byOutput = new Dictionary<string, List<PublisherProfile>>(StringComparer.OrdinalIgnoreCase);

            foreach (var profile in config.publishers)
            {
                if (profile == null)
                    continue;

                string id = profile.EffectiveId;

                if (string.IsNullOrWhiteSpace(profile.id) &&
                    profile.platform is PlatformType.Android or PlatformType.iOS)
                {
                    Add(issues, BuildProfileIssueSeverity.Warning, id, "id-implicit",
                        "Perfil mobile sem 'id' explícito: usa o nome do publisher, e o AppDeployHub/CI dependem do id.");
                }

                if (byId.TryGetValue(id, out var duplicate))
                {
                    Add(issues, BuildProfileIssueSeverity.Error, id, "id-duplicate",
                        $"Id duplicado: '{duplicate.displayName}' e '{profile.displayName}' usam o mesmo id e um build vai resolver o perfil errado.");
                }
                else
                {
                    byId[id] = profile;
                }

                if (string.IsNullOrWhiteSpace(profile.displayName))
                    Add(issues, BuildProfileIssueSeverity.Warning, id, "name-missing", "Perfil sem displayName.");

                CheckPublisherPlatform(issues, profile, id);
                CheckBundle(issues, profile, id, byBundle);
                CheckOutput(issues, profile, id, byOutput);
                CheckDefines(issues, profile, id, tracking);
            }

            return issues;
        }

        /// <summary>Report ready for the console/CLI.</summary>
        public static string Format(IEnumerable<BuildProfileIssue> issues)
        {
            var list = (issues ?? Enumerable.Empty<BuildProfileIssue>()).ToList();
            int errors = list.Count(issue => issue.Severity == BuildProfileIssueSeverity.Error);

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("===== BUILD PROFILES =====");
            builder.AppendLine($"Resultado: {errors} erro(s), {list.Count - errors} aviso(s)");

            foreach (var issue in list)
                builder.AppendLine("  " + issue);

            if (list.Count == 0)
                builder.AppendLine("  Nenhum problema encontrado.");

            return builder.ToString();
        }

        private static void CheckPublisherPlatform(List<BuildProfileIssue> issues, PublisherProfile profile, string id)
        {
            string publisher = profile.publisher.ToString();

            if (Contains(publisher, "Android") && profile.platform != PlatformType.Android)
                Add(issues, BuildProfileIssueSeverity.Warning, id, "publisher-platform",
                    $"Publisher '{publisher}' sugere Android, mas a plataforma é {profile.platform}.");

            if (Contains(publisher, "iOS") && profile.platform != PlatformType.iOS)
                Add(issues, BuildProfileIssueSeverity.Warning, id, "publisher-platform",
                    $"Publisher '{publisher}' sugere iOS, mas a plataforma é {profile.platform}.");

            if (Contains(publisher, "Mac") && profile.platform != PlatformType.macOS)
                Add(issues, BuildProfileIssueSeverity.Warning, id, "publisher-platform",
                    $"Publisher '{publisher}' sugere macOS, mas a plataforma é {profile.platform}.");
        }

        private static void CheckBundle(
            List<BuildProfileIssue> issues, PublisherProfile profile, string id,
            Dictionary<string, List<PublisherProfile>> byBundle)
        {
            bool required = profile.platform is PlatformType.Android or PlatformType.iOS;

            if (string.IsNullOrWhiteSpace(profile.bundleIdentifier))
            {
                if (required)
                {
                    Add(issues, BuildProfileIssueSeverity.Error, id, "bundle-missing",
                        $"Plataforma {profile.platform} exige bundleIdentifier.");
                }

                // Desktop profiles legitimately inherit the default bundle id; not a finding.
                return;
            }

            // Same platform + same publisher family (Google Android Free/Full, iOS Free/Full...).
            // Different stores may intentionally share a bundle id (Amazon Full == Google Free).
            string key = profile.platform + "|" + PublisherFamily(profile.publisher) + "|" +
                         profile.bundleIdentifier.Trim();

            if (!byBundle.TryGetValue(key, out var list))
            {
                list = new List<PublisherProfile>();
                byBundle[key] = list;
            }

            if (list.Any(other => other.isFullGame != profile.isFullGame))
            {
                Add(issues, BuildProfileIssueSeverity.Error, id, "bundle-duplicate",
                    $"Free e Full da mesma loja compartilham o bundle '{profile.bundleIdentifier}' em {profile.platform}; a loja não aceita.");
            }

            list.Add(profile);
        }

        private static string PublisherFamily(Publisher publisher) =>
            publisher.ToString().Replace("Full", "").Replace("Free", "").Replace("Demo", "");

        private static void CheckOutput(
            List<BuildProfileIssue> issues, PublisherProfile profile, string id,
            Dictionary<string, List<PublisherProfile>> byOutput)
        {
            if (string.IsNullOrWhiteSpace(profile.outputSubfolder))
                return;

            string key = profile.outputSubfolder.Trim().TrimEnd('/').ToLowerInvariant();
            if (!byOutput.TryGetValue(key, out var list))
            {
                list = new List<PublisherProfile>();
                byOutput[key] = list;
            }

            if (list.Count > 0)
            {
                Add(issues, BuildProfileIssueSeverity.Warning, id, "output-duplicate",
                    $"outputSubfolder '{profile.outputSubfolder}' também é usado por '{list[0].displayName}'.");
            }

            list.Add(profile);
        }

        private static void CheckDefines(
            List<BuildProfileIssue> issues, PublisherProfile profile, string id, BuildCatalogTracking tracking)
        {
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var invalid = new List<string>();

            foreach (string raw in profile.scriptingDefines ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string value = raw.Trim();
                bool removal = value[0] == '!' || value[0] == '-';
                string name = removal ? value.Substring(1).Trim() : value;

                if (name.Length == 0 || name.Any(char.IsWhiteSpace) || name.Contains(',') || name.Contains(';'))
                {
                    invalid.Add(raw);
                    continue;
                }

                if (removal)
                    removed.Add(name);
                else
                    added.Add(name);
            }

            if (invalid.Count > 0)
            {
                Add(issues, BuildProfileIssueSeverity.Error, id, "define-invalid",
                    $"Define inválido: {string.Join(", ", invalid.Select(v => $"'{v}'"))}. Use um symbol por linha, sem espaço nem vírgula (ex.: '!FTDOTD_MONETIZATION').");
            }

            foreach (string name in added.Where(name => removed.Contains(name)))
            {
                Add(issues, BuildProfileIssueSeverity.Error, id, "define-conflict",
                    $"'{name}' está na lista para adicionar e para remover no mesmo perfil.");
            }

            if (tracking == null || string.IsNullOrWhiteSpace(tracking.adsDefine))
                return;

            bool hasAds = added.Contains(tracking.adsDefine) && !removed.Contains(tracking.adsDefine);

            if (profile.platform == PlatformType.iOS && profile.isFullGame && hasAds)
            {
                Add(issues, BuildProfileIssueSeverity.Error, id, "full-with-ads",
                    $"Perfil Full está aplicando o define de anúncio '{tracking.adsDefine}'.");
            }

            if (profile.platform == PlatformType.iOS && !profile.isFullGame && !hasAds)
            {
                Add(issues, BuildProfileIssueSeverity.Warning, id, "free-without-ads",
                    $"Perfil Free não aplica '{tracking.adsDefine}'; se a build tem anúncio, falta o define.");
            }

            if (!string.IsNullOrWhiteSpace(tracking.scriptingDefine) &&
                profile.platform != PlatformType.iOS &&
                (added.Contains(tracking.scriptingDefine) || removed.Contains(tracking.scriptingDefine)))
            {
                Add(issues, BuildProfileIssueSeverity.Warning, id, "tracking-off-ios",
                    $"'{tracking.scriptingDefine}' é de ATT (iOS) e aparece num perfil {profile.platform}.");
            }
        }

        private static bool Contains(string value, string token) =>
            value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static BuildProfileIssue Add(
            List<BuildProfileIssue> issues, BuildProfileIssueSeverity severity, string profileId, string code, string message)
        {
            var issue = new BuildProfileIssue
            {
                Severity = severity,
                ProfileId = profileId,
                Code = code,
                Message = message
            };

            issues.Add(issue);
            return issue;
        }
    }
}
