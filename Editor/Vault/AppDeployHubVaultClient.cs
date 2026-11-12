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
    /// Outcome of a end-to-end vault probe (connection + keystore download). The signing section shows it inline and
    /// the menu item shows it in a dialog, so both explain the same thing when a build would fail.
    /// </summary>
    public sealed class VaultProbeResult
    {
        public bool Success;
        public string Message = "";
        public string Report = "";
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

        /// <summary>
        /// The vault shares AppDeployHub's API rate limit (60 requests per IP per minute on <c>/api/v1/*</c>), and a
        /// build machine usually spends that budget on itself (Forge sync, dashboard, other API calls). A single
        /// keystore fetch therefore dies with HTTP 429 even though nothing is wrong with the key. Retrying with
        /// backoff rides out the one-minute window instead of failing the build.
        /// </summary>
        internal const int MaxAttempts = 4;

        /// <summary>Delays before attempts 2, 3 and 4. A fixed one-minute window means the last one usually lands after it resets.</summary>
        internal static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(35)
        };

        /// <summary>Retry-After is honored when the server sends one, but never for absurd waits.</summary>
        internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

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

        /// <summary>
        /// Diagnóstico de ponta a ponta do cofre: valida URL + API key listando as credenciais e depois baixa o keystore
        /// escolhido para <c>Library/KeystoreCache</c>. É exatamente o caminho do <c>SetupKeystoreStep</c>, então se aqui
        /// passa o build não falha por chave, rede ou keystore. Não grava segredo nenhum no projeto.
        /// </summary>
        public static async Task<VaultProbeResult> ProbeAsync(string baseUrl, string credentialId)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var list = await ListKeystoresAsync(baseUrl);
            TimeSpan listTime = clock.Elapsed;

            if (!list.Success)
                return new VaultProbeResult { Message = "Conexão/chave falhou: " + list.Message };

            if (string.IsNullOrWhiteSpace(baseUrl))
                return new VaultProbeResult { Message = "URL do AppDeployHub vazia." };

            if (string.IsNullOrWhiteSpace(credentialId))
                return new VaultProbeResult
                {
                    Message = $"Conexão OK em {Milliseconds(listTime)} ({list.Credentials.Count} credencial(is)), mas nenhuma " +
                              "credencial está escolhida — escolha uma em 'Listar keystores do cofre'."
                };

            var fetch = await FetchKeystoreAsync(baseUrl, credentialId);
            TimeSpan downloadTime = clock.Elapsed - listTime;

            if (!fetch.Success || fetch.Keystore == null)
                return new VaultProbeResult
                {
                    Message = $"Conexão OK em {Milliseconds(listTime)}, mas o download do keystore falhou: {fetch.Message}"
                };

            long bytes = 0;
            try { bytes = new FileInfo(fetch.Keystore.KeystorePath).Length; }
            catch (IOException) { /* o cache pode ter sido limpo entre o fetch e a leitura */ }

            return new VaultProbeResult
            {
                Success = true,
                Message = $"Conexão e download OK em {Milliseconds(clock.Elapsed)}.",
                Report = BuildProbeReport(fetch.Keystore.Label, fetch.Keystore.Alias, bytes, fetch.Keystore.KeystorePath,
                    list.Credentials.Count, listTime, downloadTime)
            };
        }

        /// <summary>Relatório legível do probe. Nunca inclui senha — só rótulo, alias, tamanho e caminho do cache.</summary>
        internal static string BuildProbeReport(string label, string alias, long keystoreBytes, string cachePath,
            int credentialCount, TimeSpan listTime, TimeSpan downloadTime)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"Conexão: {credentialCount} credencial(is) listada(s) em {Milliseconds(listTime)}");
            text.AppendLine($"Download: '{label}' (alias '{alias}', {FormatBytes(keystoreBytes)}) em {Milliseconds(downloadTime)}");
            text.AppendLine($"Cache: {cachePath}");
            text.Append("Senhas: recebidas (nunca são gravadas no asset).");
            return text.ToString();
        }

        internal static string FormatBytes(long bytes) =>
            bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB" :
            bytes >= 1024 ? (bytes / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB" :
            bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";

        private static string Milliseconds(TimeSpan value) =>
            value.TotalMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms";

        private static Task<(bool Ok, string Message, string Body)> GetAsync(string baseUrl, string path) =>
            GetAsync(baseUrl, path, GetApiKey());

        private static async Task<(bool Ok, string Message, string Body)> GetAsync(string baseUrl, string path, string key)
        {
            if (string.IsNullOrEmpty(key))
                return (false, $"Sem API key do cofre. Defina {KeyEnvVar} ou cole a chave em Assinatura (fica só nesta máquina).", "");

            if (string.IsNullOrWhiteSpace(baseUrl))
                return (false, "URL do AppDeployHub vazia.", "");

            string url = baseUrl.Trim().TrimEnd('/') + path;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    var response = await client.GetAsync(url);
                    string body = await response.Content.ReadAsStringAsync();
                    int status = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                        return (true, "", body);

                    if (IsRetryableStatus(status) && attempt < MaxAttempts)
                    {
                        TimeSpan delay = RetryDelay(attempt, ReadRetryAfter(response));
                        UnityEngine.Debug.LogWarning(
                            $"[Vault] Cofre respondeu HTTP {status}; nova tentativa em {delay.TotalSeconds:0}s " +
                            $"({attempt + 1}/{MaxAttempts}).");
                        await Task.Delay(delay);
                        continue;
                    }

                    switch (status)
                    {
                        case 401:
                            return (false, "API key recusada (401). Confira a chave do cofre.", body);
                        case 403:
                            return (false, "A chave não é do tipo 'vault' (403). Crie em AppDeployHub → Cofre → Conectar o Forge.", body);
                        case 409:
                            return (false, "Credencial revogada no cofre (409).", body);
                        case 429:
                            return (false, "Cofre respondeu HTTP 429 (limite de 60 requisições por IP/min, compartilhado com a API do " +
                                           "AppDeployHub). Aguarde um minuto e rode o build de novo.", body);
                        default:
                            return (false, $"Cofre respondeu HTTP {status}.", body);
                    }
                }
                catch (Exception ex)
                {
                    if (IsRetryableException(ex) && attempt < MaxAttempts)
                    {
                        TimeSpan delay = RetryDelay(attempt, null);
                        UnityEngine.Debug.LogWarning(
                            $"[Vault] Falha de rede ao acessar o cofre ({ex.Message}); nova tentativa em " +
                            $"{delay.TotalSeconds:0}s ({attempt + 1}/{MaxAttempts}).");
                        await Task.Delay(delay);
                        continue;
                    }

                    return (false, "Falha ao acessar o cofre: " + ex.Message, "");
                }
            }
        }

        /// <summary>Transient on the server side: throttling, request timeout and any 5xx.</summary>
        internal static bool IsRetryableStatus(int statusCode) => statusCode == 408 || statusCode == 429 || statusCode >= 500;

        /// <summary>Connection resets and client timeouts are worth another attempt; auth/format errors are not.</summary>
        internal static bool IsRetryableException(Exception exception) =>
            exception is HttpRequestException || exception is TaskCanceledException || exception is AggregateException;

        /// <summary>
        /// Delay before the next attempt. <paramref name="failedAttempt"/> is 1-based: after the first failure the
        /// second attempt waits 5s, then 15s, then 35s. A server <c>Retry-After</c> wins, capped at one minute.
        /// </summary>
        internal static TimeSpan RetryDelay(int failedAttempt, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                return retryAfter.Value > MaxRetryDelay ? MaxRetryDelay : retryAfter.Value;

            int index = Math.Max(0, Math.Min(failedAttempt - 1, RetryDelays.Length - 1));
            return RetryDelays[index];
        }

        private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null) return null;

            if (retryAfter.Delta.HasValue) return retryAfter.Delta;
            if (retryAfter.Date.HasValue)
            {
                TimeSpan delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
            }

            return null;
        }

        private static string Str(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }
}
