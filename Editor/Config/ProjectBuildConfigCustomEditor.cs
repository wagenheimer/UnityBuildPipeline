using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(ProjectBuildConfig))]
    public class ProjectBuildConfigCustomEditor : UnityEditor.Editor
    {
        private string testStatus = "";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var config = (ProjectBuildConfig)target;

            DrawDefaultInspector();

            if (config.keystoreSource == KeystoreSource.RemoteVault)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField("Remote Keystore Vault Actions", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Auto-Detect Profile By Bundle ID", GUILayout.Height(26)))
                {
                    config.vaultProfileId = "";
                    EditorUtility.SetDirty(config);
                }

                if (GUILayout.Button("Test Vault Connection & Cache Keystore", GUILayout.Height(26)))
                {
                    var token = KeystoreVaultClient.GetEffectiveToken(config);
                    var result = KeystoreVaultClient.FetchCredentialsSync(config.vaultUrl, config.vaultProfileId, config.defaultBundleIdentifier, token);
                    if (result.Success && result.Credentials != null)
                    {
                        testStatus = $"✓ {result.Message}\nCached at: {result.Credentials.KeystorePath}\nAlias: {result.Credentials.KeyAliasName}";
                        if (!Application.isBatchMode)
                        {
                            EditorUtility.DisplayDialog("Vault Connection Success", testStatus, "OK");
                        }
                    }
                    else
                    {
                        testStatus = $"✗ Error: {result.Message}";
                        if (!Application.isBatchMode)
                        {
                            EditorUtility.DisplayDialog("Vault Connection Failed", testStatus, "OK");
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(testStatus))
                {
                    EditorGUILayout.HelpBox(testStatus, testStatus.StartsWith("✓") ? MessageType.Info : MessageType.Error);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
