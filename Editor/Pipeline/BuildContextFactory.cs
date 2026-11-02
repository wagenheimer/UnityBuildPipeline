using System.Collections.Generic;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// The only place a <see cref="BuildContext"/> is assembled from a profile, used by Quick Build, Matrix,
    /// the inspector, Steam and the CLI. Before 2.0 each entry point built its own context and the editor
    /// ones forgot the profile defines; now the defines come from the profile inside
    /// <see cref="ApplyScriptingDefinesStep"/> and the language from <see cref="BuildLanguageResolver"/>.
    /// </summary>
    public static class BuildContextFactory
    {
        public sealed class Options
        {
            public bool CheatMode;
            public bool DevelopmentBuild;
            public bool AppBundle = true;
            public bool AutoRun;
            public bool LevelEditor;
            public bool LogLevelsInfo = true;
            /// <summary>Null = the profile's own Demo flag.</summary>
            public bool? Demo;
            /// <summary>One-off language for this build only (never saved). Null = profile lock / project default.</summary>
            public GameLanguage? LanguageOverride;
            public string VariantId = "";
            public BuildVariant Variant;
            /// <summary>Extra defines on top of the profile (CLI -defines). '!X' removes.</summary>
            public List<string> ExtraDefines = new List<string>();
            public string CustomOutputPath = "";
            /// <summary>Platform when there is no profile (CLI -platform). Ignored when a profile is given.</summary>
            public PlatformType? Platform;
            /// <summary>Publisher when there is no profile.</summary>
            public Publisher? Publisher;
        }

        public static BuildContext Create(ProjectBuildConfig config, PublisherProfile profile, Options options = null)
        {
            options ??= new Options();

            var language = BuildLanguageResolver.Resolve(config, profile, options.LanguageOverride, out var source);

            var variant = options.Variant;
            if (variant == null && !string.IsNullOrEmpty(options.VariantId) && config?.gameConfig != null)
                variant = config.gameConfig.FindBuildVariant(options.VariantId);

            return new BuildContext
            {
                Config = config,
                PublisherProfile = profile,
                Publisher = profile != null ? profile.publisher : options.Publisher ?? global::Publisher.Default,
                Platform = profile != null ? options.Platform ?? profile.platform : options.Platform ?? PlatformType.Windows64,
                ProfileId = profile != null ? profile.EffectiveId : (options.Publisher?.ToString() ?? ""),
                Language = language,
                LanguageSource = source,
                CheatMode = options.CheatMode,
                DevelopmentBuild = options.DevelopmentBuild,
                Demo = options.Demo ?? (profile != null && profile.isDemo),
                LevelEditor = options.LevelEditor,
                LogLevelsInfo = options.LogLevelsInfo,
                AppBundle = !options.AutoRun && options.AppBundle,
                AutoRun = options.AutoRun,
                VariantId = variant != null ? variant.EffectiveId : options.VariantId ?? "",
                Variant = variant,
                ScriptingDefines = options.ExtraDefines != null ? new List<string>(options.ExtraDefines) : new List<string>(),
                CustomOutputPath = options.CustomOutputPath ?? ""
            };
        }
    }
}
