using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>What one profile looks like to a define: whether it ends up in the build, and the profile's tags.</summary>
    public sealed class DefineProfileFootprint
    {
        public string ProfileId = "";
        public string DisplayName = "";
        public string PlatformGroup = "";
        public ISet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool Has;
    }

    /// <summary>A proposed declaration for an undocumented define, with the evidence behind it.</summary>
    public sealed class DefineSuggestion
    {
        public DefineDeclaration Declaration = new DefineDeclaration();

        /// <summary>Why the define needs documentation (one line each).</summary>
        public readonly List<string> Reasons = new List<string>();

        /// <summary>Why each suggested field has its value (rules, scope, platforms).</summary>
        public readonly List<string> Evidence = new List<string>();

        public int UsageCount;
        public int FileCount;
        public List<string> SampleFiles = new List<string>();
    }

    /// <summary>
    /// Turns raw facts (which profiles carry a define, where the code uses it) into a documented declaration:
    /// a description, the requiredWhen/forbiddenWhen rules the profile layout implies, the platforms and whether
    /// a package turns it on automatically. Pure logic: no Unity API, covered by EditMode tests.
    /// </summary>
    public static class DefineAdvisor
    {
        private static readonly string[] GenericTags =
        {
            "full", "free", "demo", "steam", "android", "ios", "mac", "windows", "linux", "webgl", "mobile", "desktop"
        };

        private static readonly Dictionary<string, string> Opposites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "free", "full" }, { "full", "free" }, { "mobile", "desktop" }, { "desktop", "mobile" }
        };

        // Name fragments → what a define with that fragment usually switches on.
        private static readonly KeyValuePair<string, string>[] NameMeanings =
        {
            new KeyValuePair<string, string>("MONETIZATION", "a monetização (anúncios e compras)"),
            new KeyValuePair<string, string>("ADS", "os anúncios"),
            new KeyValuePair<string, string>("ADMOB", "o AdMob"),
            new KeyValuePair<string, string>("LEVELPLAY", "o LevelPlay (mediação de anúncios)"),
            new KeyValuePair<string, string>("IAP", "as compras no app"),
            new KeyValuePair<string, string>("TRACKING", "o rastreamento/ATT (App Tracking Transparency)"),
            new KeyValuePair<string, string>("ATT", "o ATT (App Tracking Transparency)"),
            new KeyValuePair<string, string>("CONSENT", "o consentimento de privacidade"),
            new KeyValuePair<string, string>("STEAM", "a integração com a Steam"),
            new KeyValuePair<string, string>("ITCH", "a build do itch.io"),
            new KeyValuePair<string, string>("DEMO", "o comportamento da versão demo"),
            new KeyValuePair<string, string>("FREE", "a versão gratuita"),
            new KeyValuePair<string, string>("FULL", "a versão completa"),
            new KeyValuePair<string, string>("DEBUG", "recursos de depuração"),
            new KeyValuePair<string, string>("CHEAT", "atalhos/cheats de teste"),
            new KeyValuePair<string, string>("DEV", "recursos de desenvolvimento"),
            new KeyValuePair<string, string>("TEST", "código de teste"),
            new KeyValuePair<string, string>("ANALYTICS", "a telemetria/analytics"),
            new KeyValuePair<string, string>("SENTRY", "o Sentry (relatório de erros)"),
            new KeyValuePair<string, string>("LOG", "logs extras"),
            new KeyValuePair<string, string>("PROFIL", "ferramentas de profiling"),
            new KeyValuePair<string, string>("SOCIAL", "os recursos sociais (conquistas, ranking)"),
            new KeyValuePair<string, string>("ACHIEVEMENT", "as conquistas"),
            new KeyValuePair<string, string>("CLOUD", "o save na nuvem"),
            new KeyValuePair<string, string>("LOCALIZ", "a localização"),
            new KeyValuePair<string, string>("EDITOR", "código que só existe no editor"),
            new KeyValuePair<string, string>("SPLASH", "a tela de splash"),
            new KeyValuePair<string, string>("TOUCH", "os controles de toque"),
            new KeyValuePair<string, string>("GAMEPAD", "o suporte a gamepad")
        };

        public static DefineSuggestion Suggest(
            string symbol,
            IList<DefineProfileFootprint> profiles,
            IEnumerable<DefineUsage> usages,
            IEnumerable<DefineUsage> versionDefines)
        {
            var suggestion = new DefineSuggestion();
            var declaration = suggestion.Declaration;
            declaration.symbol = symbol;
            profiles ??= new List<DefineProfileFootprint>();

            var codeUses = (usages ?? Enumerable.Empty<DefineUsage>())
                .Where(u => u.symbol == symbol && (u.kind == DefineUsageKind.Condition ||
                                                    u.kind == DefineUsageKind.ConditionalAttribute ||
                                                    u.kind == DefineUsageKind.AssemblyConstraint))
                .ToList();

            suggestion.UsageCount = codeUses.Count;
            var files = codeUses.Select(u => u.file).Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();
            suggestion.FileCount = files.Count;
            suggestion.SampleFiles = files.Take(4).Select(FileName).ToList();

            var withIt = profiles.Where(p => p.Has).ToList();

            // 1. Why it is flagged.
            if (withIt.Count > 0)
                suggestion.Reasons.Add($"Entra no build de {withIt.Count} perfil(is) ({Join(withIt)}), mas nenhum registro o documenta.");
            if (suggestion.UsageCount > 0)
                suggestion.Reasons.Add($"O código o usa em {suggestion.UsageCount} ponto(s) de {suggestion.FileCount} arquivo(s).");
            suggestion.Reasons.Add("Sem declaração, o pipeline não consegue avisar quando um perfil novo esquece este define (ou o recebe sem dever).");

            // 2. Automatic from a package (asmdef versionDefine).
            var versionDefine = (versionDefines ?? Enumerable.Empty<DefineUsage>())
                .FirstOrDefault(u => u.symbol == symbol && !string.IsNullOrEmpty(u.versionDefinePackage));
            if (versionDefine != null)
            {
                declaration.scope = DefineScopes.Assembly;
                declaration.autoFromPackage = versionDefine.versionDefinePackage;
                suggestion.Evidence.Add($"Escopo 'assembly': um asmdef o define sozinho quando '{versionDefine.versionDefinePackage}' está instalado.");
            }

            // 3. Rules implied by which profiles carry it.
            InferRules(symbol, profiles, declaration, suggestion.Evidence);

            // 4. Platforms.
            var groupsWith = withIt.Select(p => p.PlatformGroup).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            var allGroups = profiles.Select(p => p.PlatformGroup).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            if (groupsWith.Count > 0 && groupsWith.Count < allGroups.Count)
            {
                declaration.platforms = groupsWith;
                suggestion.Evidence.Add($"Plataformas: só os perfis {string.Join(", ", groupsWith)} o têm.");
            }

            declaration.description = BuildDescription(symbol, suggestion, withIt, profiles.Count);
            return suggestion;
        }

        private static void InferRules(string symbol, IList<DefineProfileFootprint> profiles, DefineDeclaration declaration, List<string> evidence)
        {
            if (profiles.Count < 2)
                return;

            var with = profiles.Where(p => p.Has).ToList();
            if (with.Count == 0 || with.Count == profiles.Count)
                return; // Present everywhere (or nowhere): no rule distinguishes anything.

            var tagPool = profiles.SelectMany(p => p.Tags).Where(t => GenericTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            string rule = null;
            foreach (string tag in tagPool)
            {
                if (Matches(profiles, tag) == with.Count && AllMatchHave(profiles, tag))
                {
                    rule = tag;
                    break;
                }
            }

            if (rule == null)
            {
                foreach (string a in tagPool)
                {
                    foreach (string b in tagPool)
                    {
                        if (string.CompareOrdinal(a, b) >= 0 || Opposites.TryGetValue(a, out var opposite) && opposite == b)
                            continue;

                        string pair = a + "+" + b;
                        if (Matches(profiles, pair) == with.Count && AllMatchHave(profiles, pair))
                        {
                            rule = pair;
                            break;
                        }
                    }

                    if (rule != null)
                        break;
                }
            }

            if (rule == null)
                return;

            declaration.requiredWhen = new List<string> { rule };
            evidence.Add($"requiredWhen '{rule}': os {with.Count} perfil(is) com '{rule}' o têm, e nenhum outro. Se um perfil novo '{rule}' esquecer, a validação avisa.");

            if (Opposites.TryGetValue(rule, out var counter) && profiles.Any(p => p.Tags.Contains(counter)) &&
                profiles.Where(p => p.Tags.Contains(counter)).All(p => !p.Has))
            {
                declaration.forbiddenWhen = new List<string> { counter };
                evidence.Add($"forbiddenWhen '{counter}': nenhum perfil '{counter}' o tem, então ele não deve vazar para builds '{counter}'.");
            }
        }

        // How many profiles match the rule (tags joined by '+').
        private static int Matches(IList<DefineProfileFootprint> profiles, string rule) =>
            profiles.Count(p => RuleMatches(p, rule));

        private static bool AllMatchHave(IList<DefineProfileFootprint> profiles, string rule) =>
            profiles.Where(p => RuleMatches(p, rule)).All(p => p.Has);

        private static bool RuleMatches(DefineProfileFootprint profile, string rule) =>
            rule.Split('+').All(tag => profile.Tags.Contains(tag.Trim()));

        private static string BuildDescription(string symbol, DefineSuggestion suggestion, List<DefineProfileFootprint> withIt, int profileCount)
        {
            var parts = new List<string>();
            var meanings = MeaningOf(symbol);
            parts.Add(meanings.Count > 0
                ? "Liga " + string.Join(" e ", meanings) + "."
                : $"Ativa o código condicionado por {symbol}.");

            if (suggestion.UsageCount > 0)
                parts.Add($"Controla {suggestion.UsageCount} ponto(s) de código em {suggestion.FileCount} arquivo(s) (ex.: {string.Join(", ", suggestion.SampleFiles)}).");

            if (withIt.Count > 0 && profileCount > 0)
                parts.Add(withIt.Count == profileCount
                    ? "Ativo em todos os perfis."
                    : $"Ativo em {withIt.Count} de {profileCount} perfis ({Join(withIt)}).");

            if (!string.IsNullOrEmpty(suggestion.Declaration.autoFromPackage))
                parts.Add($"Definido automaticamente pelo pacote {suggestion.Declaration.autoFromPackage}.");

            return string.Join(" ", parts);
        }

        public static List<string> MeaningOf(string symbol)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(symbol))
                return result;

            var tokens = symbol.ToUpperInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
            foreach (var meaning in NameMeanings)
            {
                bool hit = tokens.Any(t => t == meaning.Key || (meaning.Key.Length > 4 && t.StartsWith(meaning.Key, StringComparison.Ordinal)));
                if (hit && !result.Contains(meaning.Value))
                    result.Add(meaning.Value);
            }

            return result.Take(2).ToList();
        }

        /// <summary>The known symbol closest to <paramref name="symbol"/> (case/typo tolerant), or null.</summary>
        public static string NearestSymbol(string symbol, IEnumerable<string> known, int maxDistance = 2)
        {
            if (string.IsNullOrEmpty(symbol) || known == null)
                return null;

            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string candidate in known)
            {
                if (string.IsNullOrEmpty(candidate) || candidate == symbol)
                    continue;

                int distance = Levenshtein(symbol.ToUpperInvariant(), candidate.ToUpperInvariant());
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            // Tiny symbols match everything; scale the tolerance with the length.
            int allowed = Math.Min(maxDistance, Math.Max(1, symbol.Length / 5));
            return best != null && bestDistance <= allowed ? best : null;
        }

        public static int Levenshtein(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }

                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        private static string Join(IEnumerable<DefineProfileFootprint> profiles)
        {
            var ids = profiles.Select(p => p.ProfileId).ToList();
            return ids.Count <= 5 ? string.Join(", ", ids) : string.Join(", ", ids.Take(5)) + $" +{ids.Count - 5}";
        }

        private static string FileName(string path)
        {
            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }
    }
}
