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
        /// <summary>Keystore file on this machine (also what the Forge CI uses via -keystorePath).</summary>
        LocalDisk = 0,
        /// <summary>Keystore credential read from the AppDeployHub vault (/api/v1/vault) at build time.</summary>
        AppDeployHubVault = 1
    }

    [CreateAssetMenu(fileName = "ProjectBuildConfig", menuName = "Build Pipeline/Project Build Config", order = 1)]
    public class ProjectBuildConfig : ScriptableObject
    {
        // 0 on purpose: Unity fills a field missing from an old asset with its initializer, so 0 is how
        // the migrator recognises pre-2.0 data. New assets are migrated to the current schema in OnEnable.
        [Tooltip("Data schema of this asset. Migrated automatically by ProjectBuildConfigMigrator.")]
        public int schemaVersion;

        [Header("Runtime GameConfig Reference")]
        [Tooltip("Reference to the project's GameConfig.asset instance for runtime synchronization.")]
        public GameConfig gameConfig;

        [Header("Project Identity")]
        public string projectName = "";
        [Tooltip("Clean, folder-friendly game name used in build paths (e.g. Secrets of Magic 3 or SecretsOfMagic3). Auto-derived from Project Name if empty.")]
        public string gameFolderName = "";
        public string defaultBundleIdentifier = "";
        public string gameNameJapanese = "";

        [Header("Build Output Roots")]
        public string buildOutputRoot = "E:/Games/Builds/";
        public string macBuildOutputRoot = "/Volumes/Sandisk/Projects/Builds/";
        public string splashMasterFolder = "E:/Games/Unity/games/";

        [Header("Scenes Setup")]
        public string publisherSplashScenePath = "Assets/_Game/Scenes/publishersplash.unity";
        public string defaultSplashScenePath = "Assets/_Game/Scenes/splash.unity";
        public string levelEditorScenePath = "Assets/_Game/Scenes/editor.unity";

        [Header("Android Keystore Mode")]
        public KeystoreSource keystoreSource = KeystoreSource.LocalDisk;

        [Header("Android Keystore (Local Disk)")]
        public string androidKeystorePath = "E:/Games/Unity/Android Keystore/GreenSauceKeystore (Upload Certificate).keystore";
        public string androidKeystorePathMac = "/Volumes/Dados (HD ANTIGO)/Games/Unity/Android Keystore/GreenSauceKeystore (Upload Certificate).keystore";
        public string androidKeyAlias = "upload key";
        [Tooltip("If empty or not found in environment, uses fallback value below.")]
        public string androidKeystorePassEnvVar = "ANDROID_KEYSTORE_PASS";
        public string androidKeystorePassFallback = "greengeisha";
        public string androidKeyaliasPassEnvVar = "ANDROID_KEYALIAS_PASS";
        public string androidKeyaliasPassFallback = "greengeisha";

        [Header("AppDeployHub Vault (Android keystore)")]
        [Tooltip("AppDeployHub base URL. The API key is per machine (Signing section / APPDEPLOYHUB_VAULT_KEY), never stored in this asset.")]
        public string appDeployHubUrl = "https://appdeployhub.com";
        [Tooltip("Id of the AndroidKeystore credential in the AppDeployHub vault.")]
        public string vaultKeystoreCredentialId = "";
        [Tooltip("Label of the chosen credential (display only).")]
        public string vaultKeystoreLabel = "";

        [Header("Archiving & Compression")]
        public bool enableZipArchiving = false;
        public bool useExternalSevenZip = false;
        public string sevenZipExecutable = @"C:\Program Files\7-Zip\7zg.exe";

        [Header("Publishers & Stores")]
        public List<PublisherProfile> publishers = new List<PublisherProfile>();

        [Header("Define Presets")]
        [Tooltip("Reusable define groups. A profile references them by id in 'definePresets'; the build expands presets before the profile's own list.")]
        public List<BuildDefinePreset> definePresets = new List<BuildDefinePreset>();

        [Header("Game Defines")]
        [Tooltip("Defines owned by this game (e.g. ST2_ADS): description and the rules that require/forbid them. Same schema packages use in package.json → wagenheimer.defines.")]
        public List<DefineDeclaration> gameDefines = new List<DefineDeclaration>();

        [Header("Steam Upload")]
        public SteamUploadConfig steamUpload = new SteamUploadConfig();

        [Header("Languages")]
        [Tooltip("Default build language for single and Quick Builds. AutoDetect is recommended for modern multi-language games.")]
        public GameLanguage defaultLanguage = GameLanguage.AutoDetect;
        public List<LanguageProfile> languages = new List<LanguageProfile>();

        private void OnEnable()
        {
            if (ProjectBuildConfigMigrator.Migrate(this))
                EditorApplication.delayCall += () => { if (this != null) EditorUtility.SetDirty(this); };
        }

        public string GetEffectiveGameFolderName()
        {
            if (!string.IsNullOrWhiteSpace(gameFolderName))
                return gameFolderName.Trim();
            return SanitizeFolderName(projectName);
        }

        public static string SanitizeFolderName(string name, bool removeSpaces = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Game";

            var invalidChars = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalidChars.Contains(c) && c != ':').ToArray()).Trim();

            if (removeSpaces)
            {
                var parts = clean.Split(new[] { ' ', '-', '_', ':', '.', ',', ';', '!' }, StringSplitOptions.RemoveEmptyEntries);
                return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1) : "")));
            }

            return clean;
        }

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
                languages = LanguageProfile.GetDefaultLanguages();
            }

            // Populate Publishers if empty
            if (publishers == null || publishers.Count == 0)
            {
                publishers = PublisherProfile.GetDefaultProfiles();
            }
        }
    }
}
