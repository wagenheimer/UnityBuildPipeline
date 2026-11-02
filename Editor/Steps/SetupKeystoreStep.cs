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

            if (context.Config.keystoreSource == KeystoreSource.AppDeployHubVault)
            {
                context.Log("Fetching Android keystore from the AppDeployHub vault...");
                var result = AppDeployHubVaultClient.FetchKeystore(context.Config.appDeployHubUrl, context.Config.vaultKeystoreCredentialId);
                if (!result.Success || result.Keystore == null)
                {
                    context.LogError($"Failed to fetch keystore from the AppDeployHub vault: {result.Message}");
                    return false;
                }

                var keystore = result.Keystore;
                PlayerSettings.Android.keystoreName = keystore.KeystorePath;
                PlayerSettings.Android.keystorePass = keystore.KeystorePassword;
                PlayerSettings.Android.keyaliasName = keystore.Alias;
                PlayerSettings.Android.keyaliasPass = keystore.KeyPassword;

                context.Log($"[Vault] Android keystore '{keystore.Label}' configured from AppDeployHub (alias: {keystore.Alias}).");
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
