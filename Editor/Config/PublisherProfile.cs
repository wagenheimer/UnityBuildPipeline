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
        public ScriptingImplementation scriptingBackend = ScriptingImplementation.IL2CPP;
        [Tooltip("macOS only. Universal (Intel + Apple Silicon) is required by Apple for App Store submissions unless the minimum OS is raised to 13.0+. Defaults to Universal so CI builds pass altool validation without manual Player Settings changes.")]
        public MacArchitecture macArchitecture = MacArchitecture.Universal;
        [Tooltip("macOS only. Enables Unity's built-in Mac App Store receipt validation. WARNING: If enabled, the game will exit with error code 173 when launched outside the Mac App Store (local builds / testing). Apple does NOT require this setting to be enabled. Defaults to false.")]
        public bool macAppStoreValidation = false;
        [Tooltip("Extra scripting define symbols applied for this profile's build target, then restored afterwards (e.g. DEMO_FREE, NO_ADS).")]
        public List<string> scriptingDefines = new List<string>();
        public string outputSubfolder = "Builds/Publishers/{Publisher}/";
        public bool zipAfterBuild = false;
        public string zipDestinationTemplate = @"E:\Documentos\Dropbox\Builds\{ProjectName}\{Publisher}\";

        /// <summary>Effective CI id: explicit <see cref="id"/> when set, otherwise the publisher enum name.</summary>
        public string EffectiveId => string.IsNullOrEmpty(id) ? publisher.ToString() : id;

        public PublisherProfile() { }

        public PublisherProfile(Publisher pub, string name, PlatformType plat, bool splash = false, bool full = true, bool demo = false)
        {
            publisher = pub;
            displayName = name;
            platform = plat;
            requiresSplash = splash;
            isFullGame = full;
            isDemo = demo;
            outputSubfolder = $"Builds/Publishers/{pub}/";
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
