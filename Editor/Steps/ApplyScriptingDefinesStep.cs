using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Applies the build's extra scripting define symbols (<see cref="BuildContext.ScriptingDefines"/>)
    /// to the active <see cref="NamedBuildTarget"/> before the player build, then restores the original
    /// symbols in the post step so CI never leaves ProjectSettings dirty.
    /// This is what enables compile-time Free/Full/Demo variants (e.g. <c>DEMO_FREE</c>).
    /// </summary>
    public class ApplyScriptingDefinesStep : IPreBuildStep, IPostBuildStep
    {
        // Run before ApplyPlayerSettingsStep (20) so a recompile, if triggered, settles early.
        public int Order => 15;

        private const string OriginalDefinesKey = "ApplyScriptingDefinesStep.OriginalDefines";

        public const string ProfileDefinesKey = "ApplyScriptingDefinesStep.ProfileDefines";
        public const string AppliedDefinesKey = "ApplyScriptingDefinesStep.AppliedDefines";
        public const string CliOverridesKey = "ApplyScriptingDefinesStep.CliOverrides";

        public bool ExecutePreBuild(BuildContext context)
        {
            var raw = (context.ScriptingDefines ?? new List<string>())
                .SelectMany(d => d.Split(';', ','))
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .Distinct()
                .ToList();

            // A define prefixed with '!' or '-' is REMOVED for this build instead of added
            // (e.g. "!DISABLESTEAMWORKS" on a Steam profile). It is restored afterwards like any other define.
            var extra = raw.Where(d => !IsRemoval(d)).ToList();
            var removals = raw.Where(IsRemoval)
                .Select(d => d.Substring(1))
                .Where(d => d.Length > 0)
                .Distinct()
                .ToList();

            // Safety net: a Steam profile always needs UnityNativeSocial's Steam define to compile achievement/
            // leaderboard calls in, even if nobody remembered to add it to the profile's own Scripting Defines
            // list. Only auto-added when Steamworks.NET is actually present, so non-Steam/non-NativeSocial
            // projects using this pipeline never get an unused define injected.
            if (context.PublisherProfile != null && context.PublisherProfile.IsSteamProfile &&
                !extra.Contains(PublisherProfile.NativeSocialSteamDefine) &&
                IsTypeAvailable("Steamworks.SteamUserStats"))
            {
                extra.Add(PublisherProfile.NativeSocialSteamDefine);
                context.Log($"Auto-added '{PublisherProfile.NativeSocialSteamDefine}' for Steam profile '{context.PublisherProfile.displayName}' (Steamworks.NET detected).");
            }

            var named = NamedBuildTarget.FromBuildTargetGroup(context.Platform.ToBuildTargetGroup());
            var original = PlayerSettings.GetScriptingDefineSymbols(named);
            context.ExtraData[OriginalDefinesKey] = original;

            var merged = original
                .Split(';', ',')
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .ToList();

            if (extra.Count == 0 && removals.Count == 0)
            {
                context.Log("No extra scripting defines for this profile.");
                RecordDrift(context, merged, extra, removals);
                return true;
            }

            foreach (var d in removals)
                merged.RemoveAll(m => string.Equals(m, d, StringComparison.OrdinalIgnoreCase));

            foreach (var d in extra)
                if (!merged.Contains(d))
                    merged.Add(d);

            PlayerSettings.SetScriptingDefineSymbols(named, merged.ToArray());
            RecordDrift(context, merged, extra, removals);
            context.Log($"Applied scripting defines ({named.TargetName}): +[{string.Join(";", extra)}] -[{string.Join(";", removals)}]  ->  [{string.Join(";", merged)}]");
            return true;
        }

        /// <summary>
        /// Records the declared/applied defines for the build manifest and warns when the CLI
        /// (<c>-defines</c>, i.e. the dashboard) re-adds a define the profile removes. This is the
        /// runtime proof that the matrix and the build agree.
        /// </summary>
        private static void RecordDrift(
            BuildContext context, List<string> applied, List<string> extra, List<string> removals)
        {
            var profileDefines = (context.PublisherProfile?.scriptingDefines ?? new List<string>())
                .SelectMany(d => d.Split(';', ','))
                .Select(d => d.Trim())
                .Where(d => d.Length > 0 && !IsRemoval(d))
                .ToList();

            var cliOverrides = extra
                .Where(d => removals.Contains(d, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            context.ExtraData[ProfileDefinesKey] = profileDefines;
            context.ExtraData[AppliedDefinesKey] = new List<string>(applied);
            context.ExtraData[CliOverridesKey] = cliOverrides;

            if (cliOverrides.Count > 0)
            {
                context.LogWarning("[BuildPipeline] -defines re-adiciona define(s) que o perfil remove: " +
                                   string.Join(", ", cliOverrides) + " (o build aplica o painel por último).");
            }
        }

        /// <summary>A define prefixed with '!' or '-' means "remove this define for the build".</summary>
        private static bool IsRemoval(string define) =>
            !string.IsNullOrEmpty(define) && (define[0] == '!' || define[0] == '-');

        private static bool IsTypeAvailable(string typeFullName) =>
            System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Array.Empty<System.Type>(); } })
                .Any(t => t.FullName == typeFullName);

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            if (!context.ExtraData.TryGetValue(OriginalDefinesKey, out var originalObj) || !(originalObj is string original))
                return true;

            var named = NamedBuildTarget.FromBuildTargetGroup(context.Platform.ToBuildTargetGroup());
            if (PlayerSettings.GetScriptingDefineSymbols(named) != original)
            {
                PlayerSettings.SetScriptingDefineSymbols(named, original.Split(';', ','));
                context.Log($"Restored original scripting defines ({named.TargetName}).");
            }
            return true;
        }
    }
}
