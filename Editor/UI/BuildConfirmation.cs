using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Confirmation dialog shown before any editor-triggered build. It lists what will actually run
    /// (profile id, platform, backend, language and where it came from, variant, flags, version, output,
    /// the final defines from the same stack the build step applies, and broken define rules) so a
    /// shortcut can never start a build by accident or with the wrong profile.
    /// <para>
    /// The UI is a UI Toolkit <see cref="BuildConfirmationWindow"/> (chips, colors and a scrollable
    /// define list) shown as a modal utility window. The old OS message box truncated long define
    /// lists, and this keeps the synchronous <c>bool</c> contract because
    /// <see cref="EditorWindow.ShowModalUtility"/> blocks until the window is closed.
    /// </para>
    /// </summary>
    internal static class BuildConfirmation
    {
        public static bool Confirm(BuildContext context, ProjectBuildConfig config, string title = "Confirm Build")
        {
            if (context == null)
                return false;

            if (Application.isBatchMode)
                return true;

            try
            {
                var window = BuildConfirmationWindow.Create(context, config, title);
                window.ShowModalUtility();
                return window.Confirmed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildPipeline] Não foi possível abrir a confirmação em UI Toolkit: " + ex.Message);
                return EditorUtility.DisplayDialog(title, FallbackMessage(context, config), "Build Now", "Cancelar");
            }
        }

        /// <summary>Confirmation for a batch (one line per planned build, "… +N" allowed).</summary>
        public static bool ConfirmBatch(string title, string summary)
        {
            if (Application.isBatchMode)
                return true;

            var lines = (summary ?? "")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            try
            {
                var window = BuildConfirmationWindow.Create(null, null, title, title, lines);
                window.ShowModalUtility();
                return window.Confirmed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildPipeline] Não foi possível abrir a confirmação em UI Toolkit: " + ex.Message);
                return EditorUtility.DisplayDialog(
                    title, summary + "\n\nIsso pode levar vários minutos. Continuar?", "Build Now", "Cancelar");
            }
        }

        /// <summary>Compact message used only when the UI Toolkit window cannot be shown.</summary>
        private static string FallbackMessage(BuildContext context, ProjectBuildConfig config)
        {
            var profile = context.PublisherProfile;
            string profileLine = profile != null
                ? $"{profile.EffectiveId} — {profile.displayName} ({(profile.isFullGame ? "Full" : "Free")})"
                : "NENHUM — a build usará os defaults do pipeline";

            var lines = new List<string>
            {
                $"Perfil: {profileLine}",
                $"Plataforma: {context.Platform}",
                $"Publisher: {context.Publisher}",
                $"Idioma: {BuildLanguageResolver.Describe(context.Language, context.LanguageSource)}",
                $"Flags: {(context.CheatMode || context.DevelopmentBuild || context.Demo || context.AutoRun ? "ver Console" : "nenhuma")}",
                $"Saída: {ResolveOutput(config, context)}",
                "",
                "Isso executa o pipeline completo (pre/post steps) e pode levar vários minutos. Continuar?"
            };

            return string.Join("\n", lines);
        }

        private static string ResolveOutput(ProjectBuildConfig config, BuildContext context)
        {
            try
            {
                return context.PublisherProfile != null
                    ? ProfileOutputPaths.ResolveFolder(config, context.PublisherProfile, context.Platform)
                    : "(perfil genérico)";
            }
            catch (Exception)
            {
                return "(não definida)";
            }
        }
    }
}
