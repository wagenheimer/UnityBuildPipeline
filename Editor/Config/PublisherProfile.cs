using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Maps 1:1 to the Player Settings &gt; macOS &gt; Architecture dropdown (index-compatible with
    /// <c>PlayerSettings.SetArchitecture</c>'s int parameter: 0=x64, 1=ARM64, 2=Universal).</summary>
    public enum MacArchitecture
    {
        IntelOnly = 0,
        AppleSiliconOnly = 1,
        Universal = 2,
    }

    /// <summary>How a profile picks its build language.</summary>
    public enum ProfileLanguageMode
    {
        /// <summary>Uses the project default (normally AutoDetect) or the language picked for that build.</summary>
        Auto = 0,
        /// <summary>Always builds <see cref="PublisherProfile.lockedLanguage"/>; the game hides its language selector.</summary>
        Locked = 1,
    }

    [Serializable]
    public class PublisherProfile
    {
        [Tooltip("Stable slug used by CI (-buildProfile <id>). If empty, the publisher enum name is used.")]
        public string id = "";
        public Publisher publisher = Publisher.Default;
        public string displayName = "Default";
        public PlatformType platform = PlatformType.Windows64;
        public string bundleIdentifier = "";
        public bool requiresSplash = false;
        public bool isFullGame = true;
        public bool isDemo = false;

        [Tooltip("Esconde este perfil da lista do editor (não apaga nada e o CI continua conseguindo buildar por id). Use 'Mostrar ocultos' para trazê-lo de volta.")]
        public bool hidden = false;
        public ScriptingImplementation scriptingBackend = ScriptingImplementation.IL2CPP;
        [Tooltip("macOS only. Universal (Intel + Apple Silicon) is required by Apple for App Store submissions unless the minimum OS is raised to 13.0+. Defaults to Universal so CI builds pass altool validation without manual Player Settings changes.")]
        public MacArchitecture macArchitecture = MacArchitecture.Universal;
        [Tooltip("macOS only. Enables Unity's built-in Mac App Store receipt validation. WARNING: If enabled, the game will exit with error code 173 when launched outside the Mac App Store (local builds / testing). Apple does NOT require this setting to be enabled. Defaults to false.")]
        public bool macAppStoreValidation = false;
        [Tooltip("Extra scripting define symbols applied for this profile's build target, then restored afterwards (e.g. DEMO_FREE, NO_ADS).")]
        public List<string> scriptingDefines = new List<string>();

        [Tooltip("Ids of ProjectBuildConfig.definePresets expanded before scriptingDefines (e.g. 'free-ads', 'att').")]
        public List<string> definePresets = new List<string>();
        public string outputSubfolder = "Publishers/{Publisher}/{GameFolder}/";
        public bool zipAfterBuild = false;
        public string zipDestinationTemplate = @"E:\Documentos\Dropbox\Builds\{ProjectName}\{Publisher}\";

        /// <summary>Effective CI id: explicit <see cref="id"/> when set, otherwise the publisher enum name.</summary>
        public string EffectiveId => string.IsNullOrEmpty(id) ? publisher.ToString() : id;

        [Tooltip("Steam build (Windows/Mac/Linux). Enables the Steam section, the NativeSocial Steam define and the Steam cleanup steps.")]
        public bool isSteam = false;

        [Tooltip("Auto = project default or the language picked for the build. Locked = always this language (e.g. a German-only build for a publisher); the game hides its language selector.")]
        public ProfileLanguageMode languageMode = ProfileLanguageMode.Auto;

        [Tooltip("Language used when Language Mode is Locked.")]
        public GameLanguage lockedLanguage = GameLanguage.English;

        [Tooltip("Free notes: who asked for this build, why a define is here, store quirks.")]
        [TextArea(1, 5)]
        public string notes = "";

        /// <summary>True when this profile is a Steam build (explicit <see cref="isSteam"/> flag).</summary>
        public bool IsSteamProfile => isSteam;

        public bool IsLanguageLocked => languageMode == ProfileLanguageMode.Locked && lockedLanguage != GameLanguage.AutoDetect;

        /// <summary>
        /// The pre-2.0 heuristic (publisher Steam, or "steam" in id/displayName). Only used once, by the
        /// migration that fills <see cref="isSteam"/>.
        /// </summary>
        public static bool LooksLikeSteam(PublisherProfile profile) =>
            profile != null &&
            (profile.publisher == Publisher.Steam ||
             (!string.IsNullOrEmpty(profile.displayName) && profile.displayName.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0) ||
             (!string.IsNullOrEmpty(profile.id) && profile.id.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0));

        /// <summary>Scripting define required by UnityNativeSocial for Steam achievement/leaderboard calls to compile in.</summary>
        public const string NativeSocialSteamDefine = "WAGENHEIMER_NATIVESOCIAL_STEAM";

        public PublisherProfile() { }

        public PublisherProfile(Publisher pub, string name, PlatformType plat, bool splash = false, bool full = true, bool demo = false)
        {
            publisher = pub;
            displayName = name;
            platform = plat;
            requiresSplash = splash;
            isFullGame = full;
            isDemo = demo;
            outputSubfolder = $"Publishers/{pub}/{{GameFolder}}/";
            isSteam = pub == Publisher.Steam;
        }

        public static List<PublisherProfile> GetDefaultProfiles()
        {
            return new List<PublisherProfile>
            {
                // Desktop
                new PublisherProfile(Publisher.GreenSauceGames, "Green Sauce Games", PlatformType.Windows64, false, true, false),
                new PublisherProfile(Publisher.BigFish, "Big Fish Games", PlatformType.Windows64, true, true, false),
                new PublisherProfile(Publisher.GameHouse, "GameHouse", PlatformType.Windows64, true, true, false),
                new PublisherProfile(Publisher.IWIN, "iWin", PlatformType.Windows64, true, true, false),
                new PublisherProfile(Publisher.Alawar, "Alawar", PlatformType.Windows64, true, true, false),
                new PublisherProfile(Publisher.Wildtangent, "WildTangent", PlatformType.Windows64, true, true, false),
                new PublisherProfile(Publisher.Denda, "Denda", PlatformType.Windows64, true, true, false),
                new PublisherProfile(Publisher.LegacyGames, "Legacy Games", PlatformType.Windows64, true, true, false),
                new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64, false, true, false),
                new PublisherProfile(Publisher.ItchIO, "itch.io", PlatformType.Windows64, false, true, false),

                // Mobile Android
                new PublisherProfile(Publisher.GoogleAndroidFull, "Google Play (Full)", PlatformType.Android, false, true, false),
                new PublisherProfile(Publisher.GoogleAndroidFree, "Google Play (Free)", PlatformType.Android, false, false, false),
                new PublisherProfile(Publisher.AmazonAndroidFull, "Amazon (Full)", PlatformType.Android, false, true, false),
                new PublisherProfile(Publisher.AmazonAndroidFree, "Amazon (Free)", PlatformType.Android, false, false, false),

                // Mobile iOS
                new PublisherProfile(Publisher.iOSFull, "iOS (Full)", PlatformType.iOS, false, true, false),
                new PublisherProfile(Publisher.iOSFree, "iOS (Free)", PlatformType.iOS, false, false, false),

                // macOS
                new PublisherProfile(Publisher.MacAppStore, "Mac App Store", PlatformType.macOS, false, false, false),
                new PublisherProfile(Publisher.MacGameStore, "Mac Game Store", PlatformType.macOS, true, true, false)
            };
        }
    }

    [Serializable]
    public class LanguageProfile
    {
        public GameLanguage language = GameLanguage.English;
        public bool enabled = true;

        public LanguageProfile() { }

        public LanguageProfile(GameLanguage lang, bool isEnabled = true)
        {
            language = lang;
            enabled = isEnabled;
        }

        public string Code => language.AsLanguageCode();
        public string Name => language.AsLanguageName();

        public static List<LanguageProfile> GetDefaultLanguages()
        {
            return new List<LanguageProfile>
            {
                new LanguageProfile(GameLanguage.AutoDetect, true),
                new LanguageProfile(GameLanguage.English, true),
                new LanguageProfile(GameLanguage.French, true),
                new LanguageProfile(GameLanguage.German, true),
                new LanguageProfile(GameLanguage.Spanish, true),
                new LanguageProfile(GameLanguage.Dutch, true),
                new LanguageProfile(GameLanguage.Italian, true),
                new LanguageProfile(GameLanguage.Portuguese, true),
                new LanguageProfile(GameLanguage.Russian, true),
                new LanguageProfile(GameLanguage.Polish, false),
                new LanguageProfile(GameLanguage.Czech, false),
                new LanguageProfile(GameLanguage.Japanese, false),
                new LanguageProfile(GameLanguage.Chinese, false)
            };
        }
    }
}
