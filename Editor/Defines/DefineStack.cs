using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Where a define in the stack came from, in the order the build applies them.</summary>
    public enum DefineLayer
    {
        /// <summary>PlayerSettings scripting defines of the target group (shared by Win/Mac/Linux = Standalone).</summary>
        Global = 0,
        /// <summary>Define preset referenced by the profile.</summary>
        Preset = 1,
        /// <summary>The profile's own scriptingDefines.</summary>
        Profile = 2,
        /// <summary>Added by the pipeline itself (e.g. NativeSocial's Steam define on a Steam profile).</summary>
        Auto = 3,
        /// <summary>-defines on the command line (manual CI override).</summary>
        Cli = 4,
        /// <summary>asmdef versionDefine: automatic, visible ONLY inside its assembly. Not part of the final set.</summary>
        AssemblyScoped = 5
    }

    [Serializable]
    public sealed class DefineStackEntry
    {
        public string symbol = "";
        public DefineLayer layer;
        /// <summary>True when this entry removes the symbol ('!X' / '-X').</summary>
        public bool removes;
        /// <summary>Human origin: preset id, "perfil", package, "PlayerSettings (Android)", assembly name...</summary>
        public string source = "";
    }

    /// <summary>
    /// Every define a profile build sees, layer by layer, plus the final symbol set. Built by
    /// <see cref="DefineStackResolver"/>; the build step, the catalog, the validator and the UI all read
    /// the same stack, so what the window shows is what the build applies.
    /// </summary>
    public sealed class DefineStack
    {
        public readonly List<DefineStackEntry> Entries = new List<DefineStackEntry>();

        /// <summary>Final global symbols, in application order.</summary>
        public readonly List<string> Final = new List<string>();

        /// <summary>Symbols the stack adds on top of the globals (presets + profile + auto + CLI).</summary>
        public readonly List<string> Added = new List<string>();

        /// <summary>Symbols removed for this build ('!X' from presets/profile/CLI).</summary>
        public readonly List<string> Removed = new List<string>();

        /// <summary>Profile/preset removals that a CLI -defines entry put back.</summary>
        public readonly List<string> CliOverrides = new List<string>();

        public string TargetGroup = "";

        public bool Has(string symbol) => Final.Contains(symbol, StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FinalSet() => new HashSet<string>(Final, StringComparer.Ordinal);

        public IEnumerable<DefineStackEntry> For(string symbol) =>
            Entries.Where(entry => string.Equals(entry.symbol, symbol, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A define the pipeline injects by itself, with the reason shown to the user.</summary>
    public readonly struct AutoDefine
    {
        public readonly string Symbol;
        public readonly string Reason;

        public AutoDefine(string symbol, string reason)
        {
            Symbol = symbol;
            Reason = reason;
        }
    }

    /// <summary>Pure resolution of the define layers (no Unity API).</summary>
    public static class DefineStackResolver
    {
        public static DefineStack Resolve(
            ProjectBuildConfig config,
            PublisherProfile profile,
            IEnumerable<string> globalDefines,
            IEnumerable<AutoDefine> autoDefines = null,
            IEnumerable<string> cliDefines = null,
            string targetGroup = "")
        {
            var stack = new DefineStack { TargetGroup = targetGroup ?? "" };
            var removals = new List<string>();
            var additions = new List<string>();

            var globals = Split(globalDefines).Where(symbol => !IsRemoval(symbol)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string globalSource = string.IsNullOrEmpty(targetGroup) ? "PlayerSettings" : $"PlayerSettings ({targetGroup})";
            foreach (string symbol in globals)
                stack.Entries.Add(new DefineStackEntry { symbol = symbol, layer = DefineLayer.Global, source = globalSource });

            if (profile != null)
            {
                foreach (string presetId in profile.definePresets ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(presetId))
                        continue;

                    var preset = BuildDefineResolver.Find(config, presetId);
                    if (preset?.defines == null)
                        continue;

                    foreach (string raw in Split(preset.defines))
                        AddLayerEntry(stack, raw, DefineLayer.Preset, "preset '" + preset.id + "'", additions, removals);
                }

                foreach (string raw in Split(profile.scriptingDefines))
                    AddLayerEntry(stack, raw, DefineLayer.Profile, "perfil '" + profile.EffectiveId + "'", additions, removals);
            }

            foreach (var auto in autoDefines ?? Enumerable.Empty<AutoDefine>())
            {
                if (string.IsNullOrWhiteSpace(auto.Symbol))
                    continue;

                stack.Entries.Add(new DefineStackEntry { symbol = auto.Symbol.Trim(), layer = DefineLayer.Auto, source = auto.Reason ?? "pipeline" });
                additions.Add(auto.Symbol.Trim());
            }

            var cliAdds = new List<string>();
            foreach (string raw in Split(cliDefines))
            {
                AddLayerEntry(stack, raw, DefineLayer.Cli, "-defines (CLI)", additions, removals);
                if (!IsRemoval(raw))
                    cliAdds.Add(raw);
            }

            // Same order as the build step: removals first, then additions (so a CLI add wins over a profile removal).
            var final = new List<string>(globals);
            foreach (string removed in removals)
                final.RemoveAll(symbol => string.Equals(symbol, removed, StringComparison.OrdinalIgnoreCase));

            foreach (string added in additions)
            {
                if (!final.Contains(added, StringComparer.OrdinalIgnoreCase))
                    final.Add(added);
            }

            stack.Final.AddRange(final);
            stack.Added.AddRange(additions.Distinct(StringComparer.OrdinalIgnoreCase));
            stack.Removed.AddRange(removals.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(symbol => !final.Contains(symbol, StringComparer.OrdinalIgnoreCase)));
            stack.CliOverrides.AddRange(cliAdds.Where(symbol => removals.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase));

            return stack;
        }

        /// <summary>Adds the asmdef versionDefines of the project as informational, assembly-scoped entries.</summary>
        public static void AddAssemblyScoped(DefineStack stack, IEnumerable<DefineUsage> versionDefines)
        {
            if (stack == null || versionDefines == null)
                return;

            foreach (var usage in versionDefines.Where(u => u.kind == DefineUsageKind.AssemblyVersionDefine))
            {
                stack.Entries.Add(new DefineStackEntry
                {
                    symbol = usage.symbol,
                    layer = DefineLayer.AssemblyScoped,
                    source = $"{usage.assembly} (com {usage.versionDefinePackage})"
                });
            }
        }

        public static bool IsRemoval(string define) =>
            !string.IsNullOrEmpty(define) && (define[0] == '!' || define[0] == '-');

        public static IEnumerable<string> Split(IEnumerable<string> defines)
        {
            if (defines == null)
                yield break;

            foreach (string chunk in defines)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                foreach (string part in chunk.Split(';', ','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0)
                        yield return trimmed;
                }
            }
        }

        private static void AddLayerEntry(
            DefineStack stack, string raw, DefineLayer layer, string source, List<string> additions, List<string> removals)
        {
            bool removes = IsRemoval(raw);
            string symbol = removes ? raw.Substring(1).Trim() : raw.Trim();
            if (symbol.Length == 0)
                return;

            stack.Entries.Add(new DefineStackEntry { symbol = symbol, layer = layer, removes = removes, source = source });
            if (removes) removals.Add(symbol);
            else additions.Add(symbol);
        }
    }
}
