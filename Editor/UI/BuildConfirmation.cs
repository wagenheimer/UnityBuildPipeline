using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Confirmation dialog shown before any editor-triggered build. It lists what will actually run
    /// (profile id, platform, language, variant, flags, version, output and effective defines) so a
    /// shortcut can never start a build by accident or with the wrong profile.
    /// </summary>
    internal static class BuildConfirmation
    {
        public static bool Confirm(BuildContext context, ProjectBuildConfig config, string title = "Confirm Build")
        {
            if (context == null)
                return false;

            var profile = context.PublisherProfile;
            var defines = BuildDefineResolver.Resolve(config, profile);

            var flags = new List<string>();
            if (context.CheatMode) flags.Add("Cheat");
            if (context.DevelopmentBuild) flags.Add("Development");
            if (context.Demo) flags.Add("Demo");
            if (context.Platform == PlatformType.Android) flags.Add(context.AppBundle ? ".aab" : ".apk");
            if (context.AutoRun) flags.Add("Auto Run");

            string output = profile != null
                ? PublisherProfilesSectionBuilder.ResolveQuickBuildOutputPreview(config, profile, context.Platform)
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

            var message =
                $"Perfil: {profileLine}\n" +
                $"Plataforma: {context.Platform}\n" +
                $"Publisher: {context.Publisher}\n" +
                $"Idioma: {context.Language}" +
                (string.IsNullOrEmpty(context.VariantId) ? "" : $"\nVariante: {context.VariantId}") + "\n" +
                $"Flags: {(flags.Count == 0 ? "(nenhuma)" : string.Join(", ", flags))}\n" +
                $"Versão: {version}\n" +
                $"Saída: {output}\n" +
                $"Defines: {(defines.Count == 0 ? "(nenhum)" : string.Join(", ", defines))}" +
                (profile == null
                    ? "\n\nATENÇÃO: sem perfil correspondente. O build não terá bundle id nem defines do perfil."
                    : "") +
                "\n\nIsso executa o pipeline completo (pre/post steps) e pode levar vários minutos. Continuar?";

            return EditorUtility.DisplayDialog(title, message, "Build Now", "Cancelar");
        }

        /// <summary>Confirmation for a batch (Steam's three platforms, etc.).</summary>
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
