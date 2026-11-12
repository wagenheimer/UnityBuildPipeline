using UnityEditor;

using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// "Testar conexão + baixar keystore" fora da janela: valida URL + API key listando as credenciais e baixa o keystore
    /// escolhido — exatamente o caminho do <c>SetupKeystoreStep</c>. Serve para diagnosticar 401/403/429 e credencial
    /// errada sem rodar um build inteiro, e para conferir a chave de uma máquina nova.
    /// </summary>
    internal static class VaultProbeMenu
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Testar cofre do AppDeployHub...", priority = 62)]
        private static void ProbeFromMenu()
        {
            var config = LegacyGameConfigMigrator.FindProjectBuildConfig();
            if (config == null)
            {
                EditorUtility.DisplayDialog("Cofre do AppDeployHub",
                    "Nenhum ProjectBuildConfig no projeto. Crie ou migre um perfil de build primeiro.", "OK");
                return;
            }

            Probe(config);
        }

        private static async void Probe(ProjectBuildConfig config)
        {
            string url = config.appDeployHubUrl;
            string credentialId = config.vaultKeystoreCredentialId;
            Debug.Log($"[BuildPipeline] Testando o cofre do AppDeployHub em '{url}' (credencial '{credentialId}')...");

            var probe = await AppDeployHubVaultClient.ProbeAsync(url, credentialId);

            string summary = probe.Message + (string.IsNullOrEmpty(probe.Report) ? "" : "\n\n" + probe.Report);

            if (probe.Success)
                Debug.Log("[BuildPipeline] " + summary);
            else
                Debug.LogWarning("[BuildPipeline] " + summary);

            EditorUtility.DisplayDialog("Cofre do AppDeployHub", summary, "OK");
        }
    }
}
