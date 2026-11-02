using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Applies the profile's define stack (presets + scriptingDefines + pipeline auto defines + CLI extras) to the
    /// active <see cref="NamedBuildTarget"/> before the player build, then restores the original symbols in the
    /// post step so CI never leaves ProjectSettings dirty. The stack comes from <see cref="DefineEnvironment"/>,
    /// the same code the window and the catalog use, so what the UI shows is what the build gets.
    /// </summary>
    public class ApplyScriptingDefinesStep : IPreBuildStep, IPostBuildStep
    {
        // Run before ApplyPlayerSettingsStep (20) so a recompile, if triggered, settles early.
        public int Order => 15;

        private const string OriginalDefinesKey = "ApplyScriptingDefinesStep.OriginalDefines";

        public const string ProfileDefinesKey = "ApplyScriptingDefinesStep.ProfileDefines";
        public const string AppliedDefinesKey = "ApplyScriptingDefinesStep.AppliedDefines";
        public const string CliOverridesKey = "ApplyScriptingDefinesStep.CliOverrides";
        public const string StackKey = "ApplyScriptingDefinesStep.Stack";

        public bool ExecutePreBuild(BuildContext context)
        {
            var named = NamedBuildTarget.FromBuildTargetGroup(context.Platform.ToBuildTargetGroup());
            var original = PlayerSettings.GetScriptingDefineSymbols(named);
            context.ExtraData[OriginalDefinesKey] = original;

            var auto = DefineEnvironment.AutoDefines(context.PublisherProfile);
            var stack = DefineStackResolver.Resolve(
                context.Config,
                context.PublisherProfile,
                DefineStackResolver.Split(new[] { original }),
                auto,
                context.ScriptingDefines,
                named.TargetName);

            foreach (var define in auto)
                context.Log($"Auto-added '{define.Symbol}' ({define.Reason}).");

            context.ExtraData[StackKey] = stack;
            context.ExtraData[ProfileDefinesKey] = stack.Entries
                .Where(entry => (entry.layer == DefineLayer.Preset || entry.layer == DefineLayer.Profile) && !entry.removes)
                .Select(entry => entry.symbol)
                .Distinct()
                .ToList();
            context.ExtraData[AppliedDefinesKey] = new List<string>(stack.Final);
            context.ExtraData[CliOverridesKey] = new List<string>(stack.CliOverrides);

            if (stack.CliOverrides.Count > 0)
            {
                context.LogWarning("[BuildPipeline] -defines re-adiciona define(s) que o perfil remove: " +
                                   string.Join(", ", stack.CliOverrides) + " (o CLI é aplicado por último).");
            }

            if (stack.Added.Count == 0 && stack.Removed.Count == 0)
            {
                context.Log("No extra scripting defines for this profile.");
                return true;
            }

            PlayerSettings.SetScriptingDefineSymbols(named, stack.Final.ToArray());
            context.Log($"Applied scripting defines ({named.TargetName}): +[{string.Join(";", stack.Added)}] " +
                        $"-[{string.Join(";", stack.Removed)}]  ->  [{string.Join(";", stack.Final)}]");
            return true;
        }

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
