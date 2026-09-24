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
