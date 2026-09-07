using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class SetupKeystoreStep : IPreBuildStep, IPostBuildStep
    {
        public int Order => 30;

        public bool ExecutePreBuild(BuildContext context)
        {
            if (context.Platform != PlatformType.Android || context.Config == null)
                return true;

            if (context.Config.keystoreSource == KeystoreSource.RemoteVault)
            {
                context.Log("Fetching Android keystore credentials from Remote Vault...");
                var token = KeystoreVaultClient.GetEffectiveToken(context.Config);
                var profileId = context.Config.vaultProfileId;
                var bundleId = context.Config.defaultBundleIdentifier;

                var result = KeystoreVaultClient.FetchCredentialsSync(context.Config.vaultUrl, profileId, bundleId, token);
                if (!result.Success || result.Credentials == null)
                {
                    context.LogError($"Failed to fetch keystore from Vault: {result.Message}");
                    return false;
                }

                var creds = result.Credentials;
                PlayerSettings.Android.keystoreName = creds.KeystorePath;
                PlayerSettings.Android.keystorePass = creds.KeystorePass;
                PlayerSettings.Android.keyaliasName = creds.KeyAliasName;
                PlayerSettings.Android.keyaliasPass = creds.KeyAliasPass;

                context.Log($"[Vault] Successfully configured Android Keystore '{creds.KeystoreFileName}' from Vault (Alias: {creds.KeyAliasName})");
                return true;
            }
            else
            {
                var keystorePath = context.Config.GetEffectiveKeystorePath();
                if (string.IsNullOrEmpty(keystorePath) || !File.Exists(keystorePath))
                {
                    context.LogWarning($"Android keystore not found at: {keystorePath}");
                    return true;
                }

                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass = context.Config.GetEffectiveKeystorePassword();
                PlayerSettings.Android.keyaliasName = context.Config.androidKeyAlias;
                PlayerSettings.Android.keyaliasPass = context.Config.GetEffectiveKeyaliasPassword();

                context.Log($"Configured Local Android Keystore: {keystorePath} (Alias: {context.Config.androidKeyAlias})");
                return true;
            }
        }

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            if (context.Platform == PlatformType.Android)
            {
                // Clear passwords from memory for security hygiene
                PlayerSettings.Android.keystorePass = string.Empty;
                PlayerSettings.Android.keyaliasPass = string.Empty;
            }
            return true;
        }
    }
}
