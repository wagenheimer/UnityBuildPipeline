using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class KeystoreCredentials
    {
        public string ProfileId { get; set; } = string.Empty;
        public string KeystorePath { get; set; } = string.Empty;
        public string KeystorePass { get; set; } = string.Empty;
        public string KeyAliasName { get; set; } = string.Empty;
        public string KeyAliasPass { get; set; } = string.Empty;
        public string KeystoreFileName { get; set; } = string.Empty;
    }

    [Serializable]
    internal class VaultApiResponse
    {
        public string profileId { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string company { get; set; } = string.Empty;
        public string keystoreFileName { get; set; } = string.Empty;
        public string keystoreBase64 { get; set; } = string.Empty;
        public string keystorePass { get; set; } = string.Empty;
        public string keyAliasName { get; set; } = string.Empty;
        public string keyAliasPass { get; set; } = string.Empty;
    }

    public static class KeystoreVaultClient
    {
        private const string PREFS_TOKEN_KEY = "Wagenheimer.BuildPipeline.VaultToken";

        public static string GetEffectiveToken(ProjectBuildConfig? config = null)
        {
            // 1. Environment variable
            var envVar = config != null && !string.IsNullOrEmpty(config.vaultTokenEnvVar)
                ? config.vaultTokenEnvVar
                : "VAULT_SECRET_TOKEN";

            var envVal = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(envVal))
                return envVal.Trim();

            // 2. EditorPrefs
            var prefsVal = EditorPrefs.GetString(PREFS_TOKEN_KEY, "");
            if (!string.IsNullOrEmpty(prefsVal))
                return prefsVal.Trim();

            // 3. Fallback in config
            if (config != null && !string.IsNullOrEmpty(config.vaultTokenFallback))
                return config.vaultTokenFallback.Trim();

            return string.Empty;
        }

        public static void SaveTokenToPrefs(string token)
        {
            EditorPrefs.SetString(PREFS_TOKEN_KEY, token ?? string.Empty);
        }

        public static async Task<(bool Success, string Message, KeystoreCredentials? Credentials)> FetchCredentialsAsync(
            string vaultUrl,
            string profileId,
            string bundleId,
            string secretToken)
        {
            if (string.IsNullOrWhiteSpace(vaultUrl))
            {
                return (false, "Vault URL is not configured.", null);
            }

            try
            {
                var cleanUrl = vaultUrl.Trim().TrimEnd('/');
                string requestUrl;

                if (!string.IsNullOrWhiteSpace(profileId))
                {
                    // If cleanUrl already ends with /keystore, append profileId
                    if (cleanUrl.EndsWith("/keystore", StringComparison.OrdinalIgnoreCase))
                    {
                        requestUrl = $"{cleanUrl}/{profileId.Trim()}";
                    }
                    else
                    {
                        requestUrl = $"{cleanUrl}/keystore/{profileId.Trim()}";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(bundleId))
                {
                    var baseApi = cleanUrl.EndsWith("/keystore", StringComparison.OrdinalIgnoreCase)
                        ? cleanUrl.Substring(0, cleanUrl.Length - "/keystore".Length)
                        : cleanUrl;
                    requestUrl = $"{baseApi}/by-bundle/{bundleId.Trim()}";
                }
                else
                {
                    return (false, "Neither Profile ID nor Bundle ID was provided.", null);
                }

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(20);

                if (!string.IsNullOrWhiteSpace(secretToken))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secretToken.Trim());
                    client.DefaultRequestHeaders.Add("X-Vault-Token", secretToken.Trim());
                }

                var response = await client.GetAsync(requestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return (false, $"Vault HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}", null);
                }

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<VaultApiResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (data == null || string.IsNullOrEmpty(data.keystoreBase64))
                {
                    return (false, "Invalid response payload from Vault: keystoreBase64 is empty.", null);
                }

                // Cache file in Library/KeystoreCache/
                var cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "Library", "KeystoreCache");
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                var fileName = !string.IsNullOrEmpty(data.keystoreFileName)
                    ? data.keystoreFileName
                    : $"{data.profileId}.keystore";

                var cachedFilePath = Path.Combine(cacheDir, fileName);
                var bytes = Convert.FromBase64String(data.keystoreBase64);
                await File.WriteAllBytesAsync(cachedFilePath, bytes);

                var creds = new KeystoreCredentials
                {
                    ProfileId = data.profileId,
                    KeystorePath = cachedFilePath,
                    KeystorePass = data.keystorePass,
                    KeyAliasName = data.keyAliasName,
                    KeyAliasPass = data.keyAliasPass,
                    KeystoreFileName = fileName
                };

                return (true, $"Successfully fetched and cached '{fileName}' ({bytes.Length} bytes) from Vault.", creds);
            }
            catch (Exception ex)
            {
                return (false, $"Vault connection error: {ex.Message}", null);
            }
        }

        public static (bool Success, string Message, KeystoreCredentials? Credentials) FetchCredentialsSync(
            string vaultUrl,
            string profileId,
            string bundleId,
            string secretToken)
        {
            try
            {
                return Task.Run(() => FetchCredentialsAsync(vaultUrl, profileId, bundleId, secretToken)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return (false, $"Synchronous Vault query failed: {ex.Message}", null);
            }
        }
    }
}
