using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal sealed class BuildPipelineVaultView
    {
        public VisualElement Root { get; }

        private readonly ProjectBuildConfig _config;
        private readonly Action _onRefresh;

        public BuildPipelineVaultView(ProjectBuildConfig config, Action onRefresh)
        {
            _config = config;
            _onRefresh = onRefresh;

            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            BuildUI();
        }

        private void BuildUI()
        {
            Root.Clear();

            // Header Banner Callout
            var headerCallout = BuildPipelineUIStyle.CreateCallout(
                "Manage and validate signing credentials for Android builds (.apk and .aab). " +
                "The recommended mode is the Centralized Keystore Vault (passwords and keystores remain in a secure remote cloud and never touch Git).",
                "info");
            Root.Add(headerCallout);

            // Card 1: Remote Keystore Vault
            var webPortalBtn = new Button(() => Application.OpenURL("https://wagenheimer.com/admin/keystores"))
            {
                text = "Web Portal ↗"
            };
            webPortalBtn.AddToClassList("bp-btn");
            webPortalBtn.style.height = 22;
            webPortalBtn.style.fontSize = 10;

            var vaultCard = BuildPipelineUIStyle.CreateCard("🌐 Central Keystore Vault", "Secure, zero-leak credential storage for CI/CD and multi-machine setups", webPortalBtn);

            var token = KeystoreVaultClient.GetEffectiveToken(_config);
            bool hasToken = !string.IsNullOrEmpty(token);

            var vaultInfoRow = new VisualElement();
            vaultInfoRow.style.flexDirection = FlexDirection.Row;
            vaultInfoRow.style.alignItems = Align.Center;
            vaultInfoRow.style.marginBottom = 10;

            var tokenBadge = BuildPipelineUIStyle.CreateBadge(hasToken ? "● Token Configured" : "○ No Token", hasToken ? "ok" : "warn");
            tokenBadge.style.marginRight = 8;
            vaultInfoRow.Add(tokenBadge);

            string vaultUrlText = _config != null && !string.IsNullOrEmpty(_config.vaultUrl) ? _config.vaultUrl : "https://wagenheimer.com/api/vault/keystore";
            var urlLbl = new Label($"Endpoint: {vaultUrlText}");
            urlLbl.style.fontSize = 11;
            urlLbl.style.color = new Color(0.7f, 0.7f, 0.7f);
            urlLbl.style.marginRight = 8;
            vaultInfoRow.Add(urlLbl);

            string profileId = _config != null && !string.IsNullOrEmpty(_config.vaultProfileId) ? _config.vaultProfileId : "(auto by bundle id)";
            var profLbl = new Label($"Profile: {profileId}");
            profLbl.style.fontSize = 11;
            profLbl.style.color = new Color(0.7f, 0.7f, 0.7f);
            vaultInfoRow.Add(profLbl);

            vaultCard.Add(vaultInfoRow);

            var testBtn = new Button(async () =>
            {
                EditorUtility.DisplayProgressBar("Vault Connection", "Querying remote credentials server...", 0.5f);
                try
                {
                    string effectiveUrl = _config != null && !string.IsNullOrEmpty(_config.vaultUrl) ? _config.vaultUrl : "https://wagenheimer.com/api/vault/keystore";
                    string effToken = KeystoreVaultClient.GetEffectiveToken(_config);
                    string profId = _config != null ? _config.vaultProfileId : "";

                    var result = await KeystoreVaultClient.FetchCredentialsAsync(
                        effectiveUrl,
                        profId,
                        PlayerSettings.applicationIdentifier,
                        effToken);

                    EditorUtility.ClearProgressBar();
                    if (result.Success && result.Credentials != null)
                    {
                        EditorUtility.DisplayDialog("Vault Connection Succeeded",
                            $"✅ Vault credentials retrieved successfully!\n\n" +
                            $"Profile: {result.Credentials.ProfileId}\n" +
                            $"Keystore: {result.Credentials.KeystoreFileName}\n" +
                            $"Key Alias: {result.Credentials.KeyAliasName}\n\n" +
                            $"Ready for automated headless builds!", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Vault Warning",
                            $"Could not retrieve credentials from Vault:\n\n{result.Message}\n\n" +
                            $"Please check that the profile is registered at wagenheimer.com and that the token is valid.", "OK");
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            })
            { text = "⚡ Test Remote Vault Connection" };
            testBtn.AddToClassList("bp-btn");
            testBtn.AddToClassList("bp-btn--primary");
            testBtn.style.height = 30;
            testBtn.style.fontSize = 12;
            vaultCard.Add(testBtn);

            Root.Add(vaultCard);

            // Card 2: Local Keystore File
            var localCard = BuildPipelineUIStyle.CreateCard("📁 Local Keystore File (Fallback / Offline)", "Standard on-disk Android signing keystore");

            string keystorePath = _config != null ? _config.GetEffectiveKeystorePath() : "";
            bool fileExists = !string.IsNullOrEmpty(keystorePath) && File.Exists(keystorePath);

            var fileStatusRow = new VisualElement();
            fileStatusRow.style.flexDirection = FlexDirection.Row;
            fileStatusRow.style.alignItems = Align.Center;
            fileStatusRow.style.marginBottom = 8;

            var fileBadge = BuildPipelineUIStyle.CreateBadge(fileExists ? "Keystore Found" : "File Missing", fileExists ? "ok" : "fail");
            fileBadge.style.marginRight = 8;
            fileStatusRow.Add(fileBadge);

            var pathLbl = new Label(string.IsNullOrEmpty(keystorePath) ? "(No path configured)" : keystorePath);
            pathLbl.style.fontSize = 11;
            pathLbl.style.color = new Color(0.7f, 0.7f, 0.7f);
            pathLbl.style.flexGrow = 1;
            pathLbl.style.whiteSpace = WhiteSpace.Normal;
            fileStatusRow.Add(pathLbl);

            localCard.Add(fileStatusRow);

            var localActions = new VisualElement();
            localActions.style.flexDirection = FlexDirection.Row;

            var browseBtn = new Button(() =>
            {
                string path = EditorUtility.OpenFilePanel("Select Android Keystore", "", "keystore,jks");
                if (!string.IsNullOrEmpty(path) && _config != null)
                {
#if UNITY_EDITOR_OSX
                    _config.androidKeystorePathMac = path;
#else
                    _config.androidKeystorePath = path;
#endif
                    EditorUtility.SetDirty(_config);
                    AssetDatabase.SaveAssetIfDirty(_config);
                    BuildUI();
                    _onRefresh?.Invoke();
                }
            })
            { text = "📂 Browse Keystore..." };
            browseBtn.AddToClassList("bp-btn");
            browseBtn.style.height = 24;
            localActions.Add(browseBtn);

            var applyBtn = new Button(() =>
            {
                if (_config != null)
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = _config.GetEffectiveKeystorePath();
                    PlayerSettings.Android.keyaliasName = _config.androidKeyAlias;
                    PlayerSettings.Android.keystorePass = _config.GetEffectiveKeystorePassword();
                    PlayerSettings.Android.keyaliasPass = _config.GetEffectiveKeyaliasPassword();
                    EditorUtility.DisplayDialog("Keystore Applied", "Keystore credentials applied to Unity PlayerSettings.Android.", "OK");
                }
            })
            { text = "Apply to Unity PlayerSettings" };
            applyBtn.AddToClassList("bp-btn");
            applyBtn.style.height = 24;
            applyBtn.SetEnabled(fileExists);
            localActions.Add(applyBtn);

            localCard.Add(localActions);
            Root.Add(localCard);
        }
    }
}

