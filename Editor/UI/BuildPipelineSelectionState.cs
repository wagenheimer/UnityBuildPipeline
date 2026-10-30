using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Persists the Build Pipeline window's UI selections so closing and reopening the window (or restarting
    /// the editor) restores exactly what was picked last time: the active tab, the Matrix checkboxes
    /// (publishers, languages and game variants), the Matrix build options and the Quick Build variant.
    /// <para>
    /// Stored in EditorPrefs, keyed by the ProjectBuildConfig asset GUID — never inside the asset itself, so
    /// UI state cannot mark it dirty, cannot show up in git and never leaks between projects.
    /// </para>
    /// </summary>
    [Serializable]
    internal sealed class BuildPipelineSelectionState
    {
        private const string PrefsPrefix = "BuildPipeline.SelectionState.";
        private const int CurrentVersion = 1;

        public int version = CurrentVersion;

        [Tooltip("Active tab index of the Build Pipeline window.")]
        public int tab;

        [Tooltip("True once the user has picked publishers; before that the view keeps its first-run defaults.")]
        public bool hasPublisherSelection;
        public List<string> publishers = new List<string>();

        [Tooltip("True once the user has picked languages; before that config.languages[].enabled is used.")]
        public bool hasLanguageSelection;
        public List<string> languages = new List<string>();

        [Tooltip("True once the user has picked game variants; before that enabledByDefault is used.")]
        public bool hasVariantSelection;
        public List<string> variants = new List<string>();

        public bool matrixCheatOn;
        public bool matrixCheatOff = true;
        public bool matrixDevBuild;

        public string quickVariantId = "";

        // CLI & Automation tab: last command generator selections.
        public string cliPublisher = "";
        public string cliPlatform = "";
        public string cliLanguage = "";
        public bool cliCheat;
        public bool cliDevBuild;
        public string cliVariantId = "";

        public static BuildPipelineSelectionState Load(ProjectBuildConfig config)
        {
            var state = new BuildPipelineSelectionState();
            var key = PrefsKey(config);
            if (string.IsNullOrEmpty(key)) return state;

            var json = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json)) return state;

            try
            {
                var loaded = JsonUtility.FromJson<BuildPipelineSelectionState>(json);
                if (loaded != null) state = loaded;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Could not read saved window selection, using defaults: {ex.Message}");
            }

            state.Normalize();
            return state;
        }

        public void Save(ProjectBuildConfig config)
        {
            var key = PrefsKey(config);
            if (string.IsNullOrEmpty(key)) return;

            Normalize();
            EditorPrefs.SetString(key, JsonUtility.ToJson(this));
        }

        private static string PrefsKey(ProjectBuildConfig config)
        {
            if (config == null) return string.Empty;

            var path = AssetDatabase.GetAssetPath(config);
            var guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            return PrefsPrefix + (string.IsNullOrEmpty(guid) ? config.name : guid);
        }

        private void Normalize()
        {
            if (publishers == null) publishers = new List<string>();
            if (languages == null) languages = new List<string>();
            if (variants == null) variants = new List<string>();
            if (quickVariantId == null) quickVariantId = string.Empty;
            if (cliPublisher == null) cliPublisher = string.Empty;
            if (cliPlatform == null) cliPlatform = string.Empty;
            if (cliLanguage == null) cliLanguage = string.Empty;
            if (cliVariantId == null) cliVariantId = string.Empty;

            // JsonUtility does not always run field initializers, so a payload written by an older build
            // can arrive with the Matrix defaults zeroed out. Restore the documented defaults once.
            if (version < 1)
            {
                matrixCheatOff = true;
                version = CurrentVersion;
            }
        }

        public static string PublisherKey(PublisherProfile profile)
            => profile == null ? string.Empty : $"{profile.EffectiveId}|{profile.platform}";

        public static string LanguageKey(LanguageProfile profile)
            => profile == null ? string.Empty : profile.language.ToString();

        public static string VariantKey(BuildVariant variant)
            => variant == null ? string.Empty : variant.EffectiveId;

        // ---------------------------------------------------------------- Publishers

        public bool[] ResolvePublishers(ProjectBuildConfig config)
        {
            var count = config?.publishers?.Count ?? 0;
            var result = new bool[count];

            // First run keeps the historical behaviour: nothing pre-selected.
            if (!hasPublisherSelection) return result;

            for (int i = 0; i < count; i++)
                result[i] = publishers.Contains(PublisherKey(config.publishers[i]));

            return result;
        }

        public void CapturePublishers(ProjectBuildConfig config, bool[] selection)
        {
            if (config?.publishers == null || selection == null) return;

            hasPublisherSelection = true;
            publishers.Clear();
            for (int i = 0; i < config.publishers.Count && i < selection.Length; i++)
                if (selection[i]) publishers.Add(PublisherKey(config.publishers[i]));
        }

        // ----------------------------------------------------------------- Languages

        public bool[] ResolveLanguages(ProjectBuildConfig config)
        {
            var count = config?.languages?.Count ?? 0;
            var result = new bool[count];

            for (int i = 0; i < count; i++)
            {
                var profile = config.languages[i];
                result[i] = hasLanguageSelection
                    ? languages.Contains(LanguageKey(profile))
                    : profile != null && profile.enabled;
            }

            return result;
        }

        public void CaptureLanguages(ProjectBuildConfig config, bool[] selection)
        {
            if (config?.languages == null || selection == null) return;

            hasLanguageSelection = true;
            languages.Clear();
            for (int i = 0; i < config.languages.Count && i < selection.Length; i++)
                if (selection[i]) languages.Add(LanguageKey(config.languages[i]));
        }

        // ------------------------------------------------------------------ Variants

        public bool[] ResolveVariants(ProjectBuildConfig config)
        {
            var declared = config?.gameConfig?.buildVariants;
            if (declared == null || declared.Count == 0) return Array.Empty<bool>();

            var result = new bool[declared.Count];

            if (hasVariantSelection)
            {
                for (int i = 0; i < declared.Count; i++)
                {
                    var variant = declared[i];
                    result[i] = variant != null && variant.IsValid && variants.Contains(VariantKey(variant));
                }
                return result;
            }

            // First run: enabledByDefault, or every valid variant when none is flagged as default.
            var anyDefault = false;
            for (int i = 0; i < declared.Count; i++)
            {
                var variant = declared[i];
                result[i] = variant != null && variant.IsValid && variant.enabledByDefault;
                anyDefault |= result[i];
            }

            if (!anyDefault)
                for (int i = 0; i < declared.Count; i++)
                    result[i] = declared[i] != null && declared[i].IsValid;

            return result;
        }

        public void CaptureVariants(ProjectBuildConfig config, bool[] selection)
        {
            var declared = config?.gameConfig?.buildVariants;
            if (declared == null || declared.Count == 0 || selection == null) return;

            hasVariantSelection = true;
            variants.Clear();
            for (int i = 0; i < declared.Count && i < selection.Length; i++)
                if (selection[i] && declared[i] != null && declared[i].IsValid)
                    variants.Add(VariantKey(declared[i]));
        }

        /// <summary>The declared variant matching <see cref="quickVariantId"/>, or null when it no longer exists.</summary>
        public BuildVariant ResolveQuickVariant(ProjectBuildConfig config)
        {
            var declared = config?.gameConfig?.buildVariants;
            if (declared == null || string.IsNullOrEmpty(quickVariantId)) return null;

            foreach (var variant in declared)
            {
                if (variant != null && variant.IsValid && variant.EffectiveId == quickVariantId)
                    return variant;
            }

            return null;
        }
    }
}
