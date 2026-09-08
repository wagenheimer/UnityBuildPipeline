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

        public bool ExecutePreBuild(BuildContext context)
        {
            var extra = (context.ScriptingDefines ?? new List<string>())
                .SelectMany(d => d.Split(';', ','))
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .Distinct()
                .ToList();

            var named = NamedBuildTarget.FromBuildTargetGroup(context.Platform.ToBuildTargetGroup());
            var original = PlayerSettings.GetScriptingDefineSymbols(named);
            context.ExtraData[OriginalDefinesKey] = original;

            if (extra.Count == 0)
            {
                context.Log("No extra scripting defines for this profile.");
                return true;
            }

            var merged = original
                .Split(';', ',')
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .ToList();

            foreach (var d in extra)
                if (!merged.Contains(d))
                    merged.Add(d);

            PlayerSettings.SetScriptingDefineSymbols(named, merged.ToArray());
            context.Log($"Applied scripting defines ({named.TargetName}): {string.Join(";", extra)}  ->  [{string.Join(";", merged)}]");
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
