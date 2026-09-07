using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum KeystoreSource
    {
        LocalDisk,
        RemoteVault
    }

    [CreateAssetMenu(fileName = "ProjectBuildConfig", menuName = "Build Pipeline/Project Build Config", order = 1)]
    public class ProjectBuildConfig : ScriptableObject
    {
        [Header("Runtime GameConfig Reference")]
        [Tooltip("Reference to the project's GameConfig.asset instance for runtime synchronization.")]
        public GameConfig gameConfig;

        [Header("Project Identity")]
        public string projectName = "";
        public string defaultBundleIdentifier = "";
        public string gameNameJapanese = "";

        [Header("Output & Paths")]
        public string buildOutputRoot = "E:/Games/";
        public string macBuildOutputRoot = "/Volumes/Sandisk/Projects/Builds/";
        public string splashMasterFolder = "E:/Games/Unity/games/";

        [Header("Scenes Setup")]
        public string publisherSplashScenePath = "Assets/_Game/Scenes/publishersplash.unity";
        public string defaultSplashScenePath = "Assets/_Game/Scenes/splash.unity";
        public string levelEditorScenePath = "Assets/_Game/Scenes/editor.unity";

        [Header("Android Keystore Mode")]
        public KeystoreSource keystoreSource = KeystoreSource.RemoteVault;

        [Header("Android Keystore (Local Disk)")]
        public string androidKeystorePath = "E:/Games/Unity/Android Keystore/GreenSauceKeystore (Upload Certificate).keystore";
        public string androidKeystorePathMac = "/Volumes/Dados (HD ANTIGO)/Games/Unity/Android Keystore/GreenSauceKeystore (Upload Certificate).keystore";
        public string androidKeyAlias = "upload key";
        [Tooltip("If empty or not found in environment, uses fallback value below.")]
        public string androidKeystorePassEnvVar = "ANDROID_KEYSTORE_PASS";
        public string androidKeystorePassFallback = "greengeisha";
        public string androidKeyaliasPassEnvVar = "ANDROID_KEYALIAS_PASS";
        public string androidKeyaliasPassFallback = "greengeisha";

        [Header("Remote Keystore Vault")]
        [Tooltip("URL of the Keystore Vault API endpoint (e.g. https://cezar.dev/api/vault/keystore or http://localhost:5000/api/vault/keystore)")]
        public string vaultUrl = "https://cezar.dev/api/vault/keystore";
        [Tooltip("Profile ID in the vault (e.g. greensauce-master). If empty, auto-detects by bundle ID.")]
        public string vaultProfileId = "greensauce-master";
        [Tooltip("Environment variable name to read Bearer token from (default: VAULT_SECRET_TOKEN).")]
        public string vaultTokenEnvVar = "VAULT_SECRET_TOKEN";
        [Tooltip("Fallback Bearer token when env variable is not set.")]
        public string vaultTokenFallback = "cw-vault-sec-9a8f4c21e0b573d82a1f9e4c";

        [Header("Archiving & Compression")]
        public bool enableZipArchiving = false;
        public bool useExternalSevenZip = false;
        public string sevenZipExecutable = @"C:\Program Files\7-Zip\7zg.exe";

        [Header("Publishers & Stores")]
        public List<PublisherProfile> publishers = new List<PublisherProfile>();

        [Header("Languages")]
        public List<LanguageProfile> languages = new List<LanguageProfile>();

        public string GetEffectiveKeystorePassword()
        {
            var env = Environment.GetEnvironmentVariable(androidKeystorePassEnvVar);
            return !string.IsNullOrEmpty(env) ? env : androidKeystorePassFallback;
        }

        public string GetEffectiveKeyaliasPassword()
        {
            var env = Environment.GetEnvironmentVariable(androidKeyaliasPassEnvVar);
            return !string.IsNullOrEmpty(env) ? env : androidKeyaliasPassFallback;
        }

        public string GetEffectiveBuildOutputRoot(PlatformType platform)
        {
#if UNITY_EDITOR_OSX
            return !string.IsNullOrEmpty(macBuildOutputRoot) ? macBuildOutputRoot : buildOutputRoot;
#else
            return buildOutputRoot;
#endif
        }

        public string GetEffectiveKeystorePath()
        {
#if UNITY_EDITOR_OSX
            return androidKeystorePathMac;
#else
            return androidKeystorePath;
#endif
        }

        public void PopulateDefaults()
        {
            if (string.IsNullOrEmpty(projectName))
            {
                var s = Application.dataPath.Split('/');
                projectName = s.Length >= 2 ? s[s.Length - 2] : Application.productName;
            }

            if (string.IsNullOrEmpty(defaultBundleIdentifier))
            {
                var company = Application.companyName.ToLower().Replace(" ", "");
                var product = Application.productName.ToLower().Replace(" ", "");
                defaultBundleIdentifier = $"com.{company}.{product}";
            }

            // Populate Languages if empty
            if (languages == null || languages.Count == 0)
            {
                languages = new List<LanguageProfile>
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

            // Populate Publishers if empty
            if (publishers == null || publishers.Count == 0)
            {
                publishers = new List<PublisherProfile>
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
    }
}
