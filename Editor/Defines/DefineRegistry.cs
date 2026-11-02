using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Every documented define of the project: the ones each installed package declares in its
    /// <c>package.json</c> (<c>"wagenheimer": { "defines": [...] }</c>) plus the game's own
    /// <see cref="ProjectBuildConfig.gameDefines"/>. Nothing is hard-coded here: a package that adds a
    /// define documents it in its own repository and the pipeline picks it up.
    /// </summary>
    public sealed class DefineRegistry
    {
        public readonly List<DefineDeclaration> Declarations = new List<DefineDeclaration>();

        /// <summary>Symbols declared by more than one source ("symbol → sources").</summary>
        public readonly Dictionary<string, List<string>> Conflicts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Manifests that could not be parsed ("package → error").</summary>
        public readonly Dictionary<string, string> Errors = new Dictionary<string, string>();

        public DefineDeclaration Find(string symbol) =>
            Declarations.FirstOrDefault(declaration => string.Equals(declaration.symbol, symbol, StringComparison.OrdinalIgnoreCase));

        public bool IsDeclared(string symbol) => Find(symbol) != null;

        public void Add(DefineDeclaration declaration)
        {
            if (declaration == null || !DefineUsageParser.IsIdentifier(declaration.symbol?.Trim()))
                return;

            declaration.symbol = declaration.symbol.Trim();
            var existing = Find(declaration.symbol);
            if (existing == null)
            {
                Declarations.Add(declaration);
                return;
            }

            if (!Conflicts.TryGetValue(declaration.symbol, out var sources))
            {
                sources = new List<string> { existing.source };
                Conflicts[declaration.symbol] = sources;
            }

            sources.Add(declaration.source);
        }

        /// <summary>Parses the <c>wagenheimer.defines</c> block of a package.json. Unknown fields are ignored.</summary>
        public static List<DefineDeclaration> ParsePackageManifest(string json, string source)
        {
            var result = new List<DefineDeclaration>();
            if (string.IsNullOrWhiteSpace(json) || json.IndexOf("\"wagenheimer\"", StringComparison.Ordinal) < 0)
                return result;

            var manifest = JsonUtility.FromJson<PackageManifest>(json);
            foreach (var declaration in manifest?.wagenheimer?.defines ?? new List<DefineDeclaration>())
            {
                if (declaration == null || string.IsNullOrWhiteSpace(declaration.symbol))
                    continue;

                declaration.source = source;
                Normalize(declaration);
                result.Add(declaration);
            }

            return result;
        }

        /// <summary>Builds the registry from (package name, package.json text) pairs plus the game's declarations.</summary>
        public static DefineRegistry Build(IEnumerable<KeyValuePair<string, string>> packageManifests, ProjectBuildConfig config)
        {
            var registry = new DefineRegistry();

            foreach (var pair in packageManifests ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                try
                {
                    foreach (var declaration in ParsePackageManifest(pair.Value, pair.Key))
                        registry.Add(declaration);
                }
                catch (Exception ex)
                {
                    registry.Errors[pair.Key] = ex.Message;
                }
            }

            foreach (var declaration in config?.gameDefines ?? new List<DefineDeclaration>())
            {
                if (declaration == null)
                    continue;

                var copy = JsonUtility.FromJson<DefineDeclaration>(JsonUtility.ToJson(declaration));
                copy.source = DefineDeclaration.GameSource;
                Normalize(copy);
                registry.Add(copy);
            }

            return registry;
        }

        /// <summary>Loads the registry from every package registered in the project (editor only).</summary>
        public static DefineRegistry Load(ProjectBuildConfig config)
        {
            var manifests = new List<KeyValuePair<string, string>>();

            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package == null || string.IsNullOrEmpty(package.resolvedPath))
                    continue;

                string path = Path.Combine(package.resolvedPath, "package.json");
                try
                {
                    if (File.Exists(path))
                        manifests.Add(new KeyValuePair<string, string>(package.name, File.ReadAllText(path)));
                }
                catch (Exception)
                {
                    // An unreadable manifest only means that package documents nothing.
                }
            }

            return Build(manifests, config);
        }

        private static void Normalize(DefineDeclaration declaration)
        {
            declaration.symbol = (declaration.symbol ?? "").Trim();
            declaration.scope = string.IsNullOrWhiteSpace(declaration.scope) ? DefineScopes.Global : declaration.scope.Trim().ToLowerInvariant();
            declaration.requiredWhen ??= new List<string>();
            declaration.forbiddenWhen ??= new List<string>();
            declaration.platforms ??= new List<string>();
            declaration.description ??= "";
            declaration.autoFromPackage ??= "";
            declaration.docsUrl ??= "";
            declaration.severity = string.IsNullOrWhiteSpace(declaration.severity) ? "error" : declaration.severity.Trim().ToLowerInvariant();
        }

        [Serializable]
        private sealed class PackageManifest
        {
            public WagenheimerBlock wagenheimer;
        }

        [Serializable]
        private sealed class WagenheimerBlock
        {
            public List<DefineDeclaration> defines = new List<DefineDeclaration>();
        }
    }

    /// <summary>One broken requiredWhen/forbiddenWhen rule for a profile.</summary>
    public sealed class DefineRuleViolation
    {
        public PublisherProfile Profile;
        public DefineDeclaration Declaration;
        public bool Required;
        public string Rule = "";

        public string Message => Required
            ? $"'{Declaration.symbol}' é obrigatório quando '{Rule}' (declarado por {Declaration.source}) e não está no build."
            : $"'{Declaration.symbol}' é proibido quando '{Rule}' (declarado por {Declaration.source}) e está no build.";
    }

    public static class DefineRules
    {
        /// <summary>Checks the registry rules against the final define set of one profile.</summary>
        public static List<DefineRuleViolation> Check(DefineRegistry registry, PublisherProfile profile, DefineStack stack)
        {
            var violations = new List<DefineRuleViolation>();
            if (registry == null || profile == null || stack == null)
                return violations;

            var tags = ProfileTags.For(profile);
            foreach (var declaration in registry.Declarations)
            {
                // An assembly-scoped symbol is never in the global set; its rules cannot be checked here.
                if (declaration.IsAssemblyScoped)
                    continue;

                bool present = stack.Has(declaration.symbol);

                string required = ProfileTags.FirstMatch(declaration.requiredWhen, tags);
                if (required != null && !present)
                    violations.Add(new DefineRuleViolation { Profile = profile, Declaration = declaration, Required = true, Rule = required });

                string forbidden = ProfileTags.FirstMatch(declaration.forbiddenWhen, tags);
                if (forbidden != null && present)
                    violations.Add(new DefineRuleViolation { Profile = profile, Declaration = declaration, Required = false, Rule = forbidden });
            }

            return violations;
        }
    }
}
