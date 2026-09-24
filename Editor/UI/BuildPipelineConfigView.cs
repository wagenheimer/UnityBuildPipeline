using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal sealed class BuildPipelineConfigView
    {
        public VisualElement Root { get; }

        public BuildPipelineConfigView(ProjectBuildConfig config, Action onRebuild)
        {
            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            if (config == null)
            {
                Root.Add(BuildPipelineUIStyle.CreateCallout("No ProjectBuildConfig asset found. Click 'Create ProjectBuildConfig' or locate one in your project.", "warn"));
                return;
            }

            var so = new SerializedObject(config);
            Root.Bind(so);

            // Callout
            var info = BuildPipelineUIStyle.CreateCallout(
                "Global project build settings, target paths, keystore profiles, and store publisher definitions. Edits in these fields are tracked in memory until saved.",
                "info");
            Root.Add(info);

            // 1. Runtime Reference & Project Identity
            var identityCard = BuildPipelineUIStyle.CreateCard("Runtime Reference & Project Identity", "Game configuration link and application package names");
            identityCard.Add(new PropertyField(so.FindProperty("gameConfig"), "Runtime GameConfig.asset"));
            identityCard.Add(new PropertyField(so.FindProperty("projectName"), "Project Name"));
            identityCard.Add(new PropertyField(so.FindProperty("defaultBundleIdentifier"), "Default Bundle ID"));
            identityCard.Add(new PropertyField(so.FindProperty("gameNameJapanese"), "Japanese Game Name"));
            Root.Add(identityCard);

            // 2. Output Paths & Scenes
            var pathsCard = BuildPipelineUIStyle.CreateCard("Output Directories & Scene Paths", "Destination folders and splash/level editor scene assets");
            pathsCard.Add(new PropertyField(so.FindProperty("buildOutputRoot"), "Build Output (Windows)"));
            pathsCard.Add(new PropertyField(so.FindProperty("macBuildOutputRoot"), "Build Output (macOS)"));
            pathsCard.Add(new PropertyField(so.FindProperty("splashMasterFolder"), "Splash Master Folder"));
            pathsCard.Add(new PropertyField(so.FindProperty("publisherSplashScenePath"), "Publisher Splash Scene"));
            pathsCard.Add(new PropertyField(so.FindProperty("defaultSplashScenePath"), "Default Splash Scene"));
            pathsCard.Add(new PropertyField(so.FindProperty("levelEditorScenePath"), "Level Editor Scene"));
            Root.Add(pathsCard);

            // 3. Android Keystore & Remote Vault
            var vaultCard = BuildPipelineUIStyle.CreateCard("Android Keystore & Remote Vault", "Local signing keystore paths or secure remote vault credentials");
            vaultCard.Add(new PropertyField(so.FindProperty("keystoreSource"), "Keystore Source"));

            var localBox = new VisualElement();
            localBox.Add(new PropertyField(so.FindProperty("androidKeystorePath"), "Keystore Path (Windows)"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeystorePathMac"), "Keystore Path (macOS)"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeyAlias"), "Key Alias"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeystorePassFallback"), "Keystore Password"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeyaliasPassFallback"), "Keyalias Password"));
            vaultCard.Add(localBox);

            var remoteBox = new VisualElement();
            remoteBox.Add(new PropertyField(so.FindProperty("vaultUrl"), "Vault Endpoint URL"));
            remoteBox.Add(new PropertyField(so.FindProperty("vaultProfileId"), "Vault Profile ID"));
            remoteBox.Add(new PropertyField(so.FindProperty("vaultTokenFallback"), "Vault Token (Fallback)"));
            vaultCard.Add(remoteBox);

            var testVaultBtn = new Button(async () =>
            {
                EditorUtility.DisplayProgressBar("Vault Connection", "Connecting...", 0.5f);
                try
                {
                    var result = await KeystoreVaultClient.FetchCredentialsAsync(
                        config.vaultUrl,
                        config.vaultProfileId,
                        PlayerSettings.applicationIdentifier,
                        KeystoreVaultClient.GetEffectiveToken(config));

                    EditorUtility.ClearProgressBar();
                    if (result.Success)
                    {
                        EditorUtility.DisplayDialog("Vault Connection", $"✅ Success!\nProfile: {result.Credentials.ProfileId}\nFile: {result.Credentials.KeystoreFileName}", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Vault Warning", $"Could not retrieve credentials:\n{result.Message}", "OK");
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            })
            { text = "⚡ Test Vault Connection" };
            testVaultBtn.AddToClassList("bp-btn");
            testVaultBtn.style.marginTop = 6;
            vaultCard.Add(testVaultBtn);
            Root.Add(vaultCard);

            // 4. Publishers & Stores
            var pubsCard = BuildPipelineUIStyle.CreateCard("Publishers & Store Profiles", "Custom publishers, splash overlays, and store build variants");
            var pubProp = so.FindProperty("publishers");
            pubsCard.Add(new PropertyField(pubProp, "Configured Publishers"));

            var restorePubsBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Restore Default Publishers", "Reset publisher list to default presets?", "RESTORE", "CANCEL"))
                {
                    config.publishers = PublisherProfile.GetDefaultProfiles();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    onRebuild?.Invoke();
                }
            })
            { text = "↺ Restore Default Publishers List" };
            restorePubsBtn.AddToClassList("bp-btn");
            restorePubsBtn.style.marginTop = 6;
            pubsCard.Add(restorePubsBtn);
            Root.Add(pubsCard);

            // 5. Languages Matrix
            var langsCard = BuildPipelineUIStyle.CreateCard("Languages & Localizations", "Matrix languages, display names, and code mappings");
            var langProp = so.FindProperty("languages");
            langsCard.Add(new PropertyField(langProp, "Configured Languages"));

            var restoreLangsBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Restore Default Languages", "Reset language list to default presets?", "RESTORE", "CANCEL"))
                {
                    config.languages = LanguageProfile.GetDefaultLanguages();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    onRebuild?.Invoke();
                }
            })
            { text = "↺ Restore Default Languages List" };
            restoreLangsBtn.AddToClassList("bp-btn");
            restoreLangsBtn.style.marginTop = 6;
            langsCard.Add(restoreLangsBtn);
            Root.Add(langsCard);

            // 6. Archiving & Compression
            var zipCard = BuildPipelineUIStyle.CreateCard("Archiving & 7-Zip Compression", "Automated ZIP packaging for Steam, BigFish, and standalone archives");
            zipCard.Add(new PropertyField(so.FindProperty("enableZipArchiving"), "Enable ZIP Archiving"));
            zipCard.Add(new PropertyField(so.FindProperty("useExternalSevenZip"), "Use External 7-Zip"));
            zipCard.Add(new PropertyField(so.FindProperty("sevenZipExecutable"), "7-Zip Executable Path"));
            Root.Add(zipCard);

            // 7. Bottom Save Status Bar
            Root.Add(BuildPipelineUIStyle.CreateSaveStatusBar(config, onRebuild));
        }
    }
}
