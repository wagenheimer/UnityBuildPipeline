using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Documents one scripting define: what it does, who owns it and when a profile must (or must not)
    /// have it. Packages declare theirs in <c>package.json</c> under <c>"wagenheimer": { "defines": [...] }</c>;
    /// the game declares its own in <see cref="ProjectBuildConfig.gameDefines"/>. Same schema in both places.
    /// </summary>
    [Serializable]
    public sealed class DefineDeclaration
    {
        [Tooltip("Symbol exactly as used in #if (e.g. WAGENHEIMER_NATIVESOCIAL_STEAM).")]
        public string symbol = "";

        [Tooltip("What the symbol turns on. Shown in the Defines center and exported to the Forge.")]
        [TextArea(1, 4)]
        public string description = "";

        [Tooltip("'global' = PlayerSettings/profile define, visible to every assembly. " +
                 "'assembly' = versionDefine of an asmdef, only visible inside that assembly.")]
        public string scope = DefineScopes.Global;

        [Tooltip("UPM package whose presence turns the symbol on automatically (versionDefine). Empty = manual.")]
        public string autoFromPackage = "";

        [Tooltip("Conditions that REQUIRE the symbol. Each entry is a rule; '+' joins tags (AND), '!' negates. " +
                 "Ex.: 'steam', 'ios+free', 'android+!full'. Tags: steam, free, full, demo, android, ios, mac, " +
                 "windows, linux, webgl, mobile, desktop, publisher:<Name>, id:<profileId>.")]
        public List<string> requiredWhen = new List<string>();

        [Tooltip("Conditions that FORBID the symbol (same syntax as requiredWhen). Ex.: 'full' for an ads define.")]
        public List<string> forbiddenWhen = new List<string>();

        [Tooltip("Build target groups where the symbol matters (Standalone, Android, iOS, WebGL). Empty = all.")]
        public List<string> platforms = new List<string>();

        [Tooltip("Link to the package documentation for this symbol.")]
        public string docsUrl = "";

        [Tooltip("'error' (default) or 'warning': how a broken requiredWhen/forbiddenWhen rule is reported.")]
        public string severity = "error";

        /// <summary>Who declared it: a package name (com.wagenheimer.nativesocial) or <see cref="GameSource"/>.</summary>
        [NonSerialized] public string source = "";

        public const string GameSource = "game";

        public bool IsAssemblyScoped => string.Equals(scope, DefineScopes.Assembly, StringComparison.OrdinalIgnoreCase);

        public bool IsAutomatic => !string.IsNullOrWhiteSpace(autoFromPackage);

        public bool RuleIsWarning => string.Equals(severity?.Trim(), "warning", StringComparison.OrdinalIgnoreCase);

        public bool AppliesToGroup(string targetGroup)
        {
            if (platforms == null || platforms.Count == 0 || string.IsNullOrEmpty(targetGroup))
                return true;

            foreach (string platform in platforms)
            {
                if (string.Equals(platform?.Trim(), targetGroup, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    public static class DefineScopes
    {
        public const string Global = "global";
        public const string Assembly = "assembly";
    }
}
