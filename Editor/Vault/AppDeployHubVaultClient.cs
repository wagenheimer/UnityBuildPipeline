using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>One credential as listed by the AppDeployHub vault (no secrets).</summary>
    public sealed class VaultCredentialInfo
    {
        public string Id = "";
        public string Type = "";
        public string Label = "";
        public bool Active;
        public string KeystoreAlias = "";
        public string Fingerprint = "";
    }

    /// <summary>Android signing material written to Library/KeystoreCache.</summary>
    public sealed class VaultKeystore
    {
        public string KeystorePath = "";
        public string Alias = "";
        public string KeystorePassword = "";
        public string KeyPassword = "";
        public string Label = "";
    }

    /// <summary>
    /// Reads Android keystores from the AppDeployHub vault (<c>/api/v1/vault</c>), the same single source the
    /// Forge uses. The API key (Purpose = vault, created in AppDeployHub → Cofre → Conectar o Forge) is per
    /// machine: <c>APPDEPLOYHUB_VAULT_KEY</c> or EditorPrefs, never the asset. Replaces the retired
    /// wagenheimer.com keystore vault.
    /// </summary>
    public static class AppDeployHubVaultClient
    {
        public const string KeyEnvVar = "APPDEPLOYHUB_VAULT_KEY";
        private const string KeyPrefs = "Wagenheimer.BuildPipeline.AppDeployHubVaultKey";
        private const string KeystoreType = "AndroidKeystore";
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static string GetApiKey()
        {
            string env = Environment.GetEnvironmentVariable(KeyEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            try
            {
                return EditorPrefs.GetString(KeyPrefs, "").Trim();
            }
            catch (UnityEngine.UnityException)
            {
                // EditorPrefs is main-thread only. A background caller must resolve the key before
                // hopping threads (see FetchKeystore); returning empty here keeps the failure a
                // clear "sem API key do cofre" instead of a thread-affinity crash.
                return "";
            }
        }

        public static bool KeyComesFromEnvironment => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyEnvVar));

        public static void SaveApiKey(string key) => EditorPrefs.SetString(KeyPrefs, key?.Trim() ?? "");

        /// <summary>Lists the active AndroidKeystore credentials the key can see.</summary>
        public static Task<(bool Success, string Message, List<VaultCredentialInfo> Credentials)> ListKeystoresAsync(string baseUrl) =>
            // Resolve the key on the caller (main) thread: EditorPrefs is main-thread only.
            ListKeystoresAsync(baseUrl, GetApiKey());

        private static async Task<(bool Success, string Message, List<VaultCredentialInfo> Credentials)> ListKeystoresAsync(string baseUrl, string apiKey)
        {
            var (ok, message, body) = await GetAsync(baseUrl, "/api/v1/vault/credentials", apiKey);
            if (!ok)
                return (false, message, new List<VaultCredentialInfo>());

            try
            {
                var list = new List<VaultCredentialInfo>();
                using var document = JsonDocument.Parse(body);
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var info = new VaultCredentialInfo
                    {
                        Id = Str(item, "id"),
                        Type = Str(item, "type"),
                        Label = Str(item, "label"),
                        Active = item.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True,
                        KeystoreAlias = Str(item, "keystoreAlias"),
                        Fingerprint = Str(item, "fingerprint")
                    };

                    if (string.Equals(info.Type, KeystoreType, StringComparison.OrdinalIgnoreCase))
                        list.Add(info);
                }

                return (true, $"{list.Count} keystore(s) no cofre.", list.OrderBy(c => c.Label).ToList());
            }
            catch (Exception ex)
            {
                return (false, "Resposta inesperada do cofre: " + ex.Message, new List<VaultCredentialInfo>());
            }
        }

        /// <summary>Downloads the keystore + passwords of one credential into Library/KeystoreCache.</summary>
        public static Task<(bool Success, string Message, VaultKeystore Keystore)> FetchKeystoreAsync(string baseUrl, string credentialId) =>
            // Resolve the key on the caller (main) thread: EditorPrefs is main-thread only.
            FetchKeystoreAsync(baseUrl, credentialId, GetApiKey());

        private static async Task<(bool Success, string Message, VaultKeystore Keystore)> FetchKeystoreAsync(string baseUrl, string credentialId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(credentialId))
                return (false, "Nenhuma credencial de keystore escolhida (Assinatura → Cofre do AppDeployHub).", null);

            var (ok, message, body) = await GetAsync(baseUrl, $"/api/v1/vault/credentials/{credentialId.Trim()}/secrets", apiKey);
            if (!ok)
                return (false, message, null);

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                string base64 = Str(root, "keystoreBase64");
                if (string.IsNullOrEmpty(base64))
                    return (false, "A credencial não tem keystore (tipo errado?).", null);

                var credential = root.TryGetProperty("credential", out var c) ? c : default;
                string alias = credential.ValueKind == JsonValueKind.Object ? Str(credential, "keystoreAlias") : "";
                string label = credential.ValueKind == JsonValueKind.Object ? Str(credential, "label") : credentialId;

                string directory = Path.Combine("Library", "KeystoreCache");
                Directory.CreateDirectory(directory);
                string path = Path.GetFullPath(Path.Combine(directory, credentialId.Trim() + ".keystore"));
                File.WriteAllBytes(path, Convert.FromBase64String(base64));

                string storePassword = Str(root, "keystorePassword");
                string keyPassword = Str(root, "keyPassword");

                return (true, $"Keystore '{label}' (alias '{alias}').", new VaultKeystore
                {
                    KeystorePath = path,
                    Alias = alias,
                    KeystorePassword = storePassword,
                    KeyPassword = string.IsNullOrEmpty(keyPassword) ? storePassword : keyPassword,
                    Label = label
                });
            }
            catch (Exception ex)
            {
                return (false, "Não foi possível ler o keystore do cofre: " + ex.Message, null);
            }
        }

        /// <summary>
        /// Uploads a local keystore to the vault (<c>POST /api/v1/vault/credentials/import</c>). The vault upserts by
        /// fingerprint, so importing the same keystore twice returns the existing credential instead of a duplicate.
        /// </summary>
        public static async Task<(bool Success, string Message, string CredentialId, bool Created)> ImportKeystoreAsync(
            string baseUrl, string keystorePath, string alias, string keystorePassword, string keyPassword, string label)
        {
            string key = GetApiKey();
            if (string.IsNullOrEmpty(key))
                return (false, $"Sem API key do cofre. Cole a chave (tipo 'vault') no campo acima ou defina {KeyEnvVar}.", "", false);
            if (string.IsNullOrWhiteSpace(baseUrl))
                return (false, "URL do AppDeployHub vazia.", "", false);
            if (string.IsNullOrWhiteSpace(keystorePath) || !File.Exists(keystorePath))
                return (false, "Arquivo de keystore não encontrado: " + keystorePath, "", false);
            if (string.IsNullOrWhiteSpace(alias))
                return (false, "Informe o alias do keystore.", "", false);
            if (string.IsNullOrEmpty(keystorePassword))
                return (false, "Senha do keystore vazia (variável de ambiente e fallback).", "", false);

            try
            {
                var payload = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["type"] = KeystoreType,
                    ["label"] = label,
                    ["secret"] = Convert.ToBase64String(File.ReadAllBytes(keystorePath)),
                    ["keystoreAlias"] = alias.Trim(),
                    ["keystorePassword"] = keystorePassword,
                    ["keyPassword"] = string.IsNullOrEmpty(keyPassword) ? keystorePassword : keyPassword
                });

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
                var response = await client.PostAsync(baseUrl.Trim().TrimEnd('/') + "/api/v1/vault/credentials/import",
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
                string body = await response.Content.ReadAsStringAsync();

                if ((int)response.StatusCode == 401)
                    return (false, "API key recusada (401). Confira a chave do cofre.", "", false);
                if ((int)response.StatusCode == 403)
                    return (false, "A chave não é do tipo 'vault' (403). Crie em AppDeployHub → Cofre → Conectar o Forge.", "", false);
                if (!response.IsSuccessStatusCode)
                {
                    string error = body;
                    try { using var doc = JsonDocument.Parse(body); error = Str(doc.RootElement, "error"); } catch (Exception) { }
                    return (false, $"O cofre recusou o keystore (HTTP {(int)response.StatusCode}): {error}", "", false);
                }

                using var document = JsonDocument.Parse(body);
                string id = document.RootElement.TryGetProperty("id", out var idElement) ? idElement.ToString() : "";
                bool created = document.RootElement.TryGetProperty("created", out var createdElement) && createdElement.ValueKind == JsonValueKind.True;
                return (!string.IsNullOrEmpty(id), created ? "Keystore enviado para o cofre." : "O cofre já tinha este keystore; reutilizando a credencial.", id, created);
            }
            catch (Exception ex)
            {
                return (false, "Falha ao enviar o keystore: " + ex.Message, "", false);
            }
        }

        public static (bool Success, string Message, VaultKeystore Keystore) FetchKeystore(string baseUrl, string credentialId)
        {
            // EditorPrefs (the key fallback) is main-thread only, and Task.Run moves the whole call
            // to the thread pool. Resolve the key HERE, before hopping threads — otherwise a CI
            // batchmode build died with "GetString can only be called from the main thread" in
            // SetupKeystoreStep whenever APPDEPLOYHUB_VAULT_KEY was not in the environment.
            string apiKey = GetApiKey();
            return Task.Run(() => FetchKeystoreAsync(baseUrl, credentialId, apiKey)).GetAwaiter().GetResult();
        }

        private static Task<(bool Ok, string Message, string Body)> GetAsync(string baseUrl, string path) =>
            GetAsync(baseUrl, path, GetApiKey());

        private static async Task<(bool Ok, string Message, string Body)> GetAsync(string baseUrl, string path, string key)
        {
            if (string.IsNullOrEmpty(key))
                return (false, $"Sem API key do cofre. Defina {KeyEnvVar} ou cole a chave em Assinatura (fica só nesta máquina).", "");

            if (string.IsNullOrWhiteSpace(baseUrl))
                return (false, "URL do AppDeployHub vazia.", "");

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
                var response = await client.GetAsync(baseUrl.Trim().TrimEnd('/') + path);
                string body = await response.Content.ReadAsStringAsync();

                if ((int)response.StatusCode == 401)
                    return (false, "API key recusada (401). Confira a chave do cofre.", body);
                if ((int)response.StatusCode == 403)
                    return (false, "A chave não é do tipo 'vault' (403). Crie em AppDeployHub → Cofre → Conectar o Forge.", body);
                if ((int)response.StatusCode == 409)
                    return (false, "Credencial revogada no cofre (409).", body);
                if (!response.IsSuccessStatusCode)
                    return (false, $"Cofre respondeu HTTP {(int)response.StatusCode}.", body);

                return (true, "", body);
            }
            catch (Exception ex)
            {
                return (false, "Falha ao acessar o cofre: " + ex.Message, "");
            }
        }

        private static string Str(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }
}
