using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Confirmation dialog shown before any editor-triggered build. It lists what will actually run
    /// (profile id, platform, language and where it came from, variant, flags, version, output, the final
    /// defines from the same stack the build step applies, and broken define rules) so a shortcut can never
    /// start a build by accident or with the wrong profile.
    /// </summary>
    internal static class BuildConfirmation
    {
        public static bool Confirm(BuildContext context, ProjectBuildConfig config, string title = "Confirm Build")
        {
            if (context == null)
                return false;

            var profile = context.PublisherProfile;

            var flags = new List<string>();
            if (context.CheatMode) flags.Add("Cheat");
            if (context.DevelopmentBuild) flags.Add("Development");
            if (context.Demo) flags.Add("Demo");
            if (context.Platform == PlatformType.Android) flags.Add(context.AppBundle ? ".aab" : ".apk");
            if (context.AutoRun) flags.Add("Auto Run");

            string output = profile != null
                ? ProfileOutputPaths.ResolveFolder(config, profile, context.Platform)
                : "(perfil genérico — nenhum PublisherProfile correspondente)";

            string version = "?";
            if (config?.gameConfig != null)
            {
                try { version = config.gameConfig.GameVersion.GameVersionAsText; }
                catch (Exception) { version = "?"; }
            }

            string profileLine = profile != null
                ? $"{profile.EffectiveId} — {profile.displayName} ({(profile.isFullGame ? "Full" : "Free")})"
                : "NENHUM — a build usará os defaults do pipeline";

            string definesText;
            string rulesText = "";
            try
            {
                var platformProfile = profile;
                var stack = DefineStackResolver.Resolve(config, platformProfile,
                    DefineEnvironment.GlobalDefines(context.Platform), DefineEnvironment.AutoDefines(platformProfile),
                    context.ScriptingDefines, DefineEnvironment.TargetGroupName(context.Platform));

                definesText = $"+[{string.Join(", ", stack.Added)}]" +
                              (stack.Removed.Count > 0 ? $"  −[{string.Join(", ", stack.Removed)}]" : "") +
                              $"\nFinal ({stack.TargetGroup}): {(stack.Final.Count == 0 ? "(nenhum)" : string.Join("; ", stack.Final))}";

                var violations = DefineRules.Check(DefineRegistry.Load(config), profile, stack);
                if (violations.Count > 0)
                    rulesText = "\n\n⛔ REGRAS DE DEFINES QUEBRADAS:\n" + string.Join("\n", violations.Select(v => "• " + v.Message));
            }
            catch (Exception ex)
            {
                definesText = "(não foi possível calcular: " + ex.Message + ")";
            }

            var message =
                $"Perfil: {profileLine}\n" +
                $"Plataforma: {context.Platform}\n" +
                $"Publisher: {context.Publisher}\n" +
                $"Idioma: {BuildLanguageResolver.Describe(context.Language, context.LanguageSource)}" +
                (string.IsNullOrEmpty(context.VariantId) ? "" : $"\nVariante: {context.VariantId}") + "\n" +
                $"Flags: {(flags.Count == 0 ? "(nenhuma)" : string.Join(", ", flags))}\n" +
                $"Versão: {version}\n" +
                $"Saída: {output}\n" +
                $"Defines: {definesText}" +
                rulesText +
                (profile == null
                    ? "\n\nATENÇÃO: sem perfil correspondente. O build não terá bundle id nem defines do perfil."
                    : "") +
                "\n\nIsso executa o pipeline completo (pre/post steps) e pode levar vários minutos. Continuar?";

            return EditorUtility.DisplayDialog(title, message, "Build Now", "Cancelar");
        }

        /// <summary>Confirmation for a batch.</summary>
        public static bool ConfirmBatch(string title, string summary)
        {
            return EditorUtility.DisplayDialog(
                title,
                summary + "\n\nIsso pode levar vários minutos. Continuar?",
                "Build Now",
                "Cancelar");
        }
    }
}
