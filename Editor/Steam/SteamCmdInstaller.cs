using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Automatic downloader and installer for Valve's official SteamCmd utility on Windows and macOS.
    /// Can be triggered via editor button or automatically in headless CI/Forge builds when missing.
    /// </summary>
    public static class SteamCmdInstaller
    {
        public const string WindowsZipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
        public const string MacTarUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";

        public static string GetDefaultInstallDirectory()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "steamcmd");
            }
            else
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "Programs", "SteamCmd");
            }
        }

        public static string GetExpectedExecutablePath(string installDir = null)
        {
            installDir = installDir ?? GetDefaultInstallDirectory();
            return Application.platform == RuntimePlatform.OSXEditor
                ? Path.Combine(installDir, "steamcmd.sh")
                : Path.Combine(installDir, "steamcmd.exe");
        }

        public static bool IsInstalled(SteamUploadConfig cfg)
        {
            if (cfg == null) return false;
            return cfg.IsSteamCmdInstalled();
        }

        /// <summary>
        /// Synchronously ensures SteamCmd is installed (ideal for headless CLI / batch mode).
        /// </summary>
        public static bool EnsureInstalledSync(SteamUploadConfig cfg)
        {
            if (IsInstalled(cfg))
                return true;

            Debug.Log("[BuildPipeline] SteamCmd not found. Auto-downloading from Valve CDN...");
            var task = InstallAsync(cfg, (progress, msg) => Debug.Log($"[BuildPipeline] SteamCmd: {msg}"));
            var result = task.GetAwaiter().GetResult();
            if (result.success)
            {
                Debug.Log($"[BuildPipeline] SteamCmd successfully installed at: {result.installedPath}");
                return true;
            }

            Debug.LogError($"[BuildPipeline] Failed to auto-install SteamCmd: {result.message}");
            return false;
        }

        /// <summary>
        /// Asynchronously downloads and installs SteamCmd from Valve's official CDN.
        /// </summary>
        public static async Task<(bool success, string message, string installedPath)> InstallAsync(SteamUploadConfig cfg, Action<float, string> onProgress = null)
        {
            try
            {
                var installDir = GetDefaultInstallDirectory();
                Directory.CreateDirectory(installDir);

                bool isMac = Application.platform == RuntimePlatform.OSXEditor;
                string downloadUrl = isMac ? MacTarUrl : WindowsZipUrl;
                string tempArchive = Path.Combine(Path.GetTempPath(), isMac ? "steamcmd_osx.tar.gz" : "steamcmd.zip");

                onProgress?.Invoke(0.15f, "Downloading SteamCmd from official Valve CDN...");
                using (var httpClient = new HttpClient())
                {
                    var data = await httpClient.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(tempArchive, data);
                }

                onProgress?.Invoke(0.6f, "Extracting SteamCmd...");
                if (isMac)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"-xzf \"{tempArchive}\" -C \"{installDir}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        p?.WaitForExit();
                    }

                    // Ensure execution permissions on macOS
                    var scriptPath = Path.Combine(installDir, "steamcmd.sh");
                    if (File.Exists(scriptPath))
                    {
                        var chmodPsi = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (var p = Process.Start(chmodPsi))
                        {
                            p?.WaitForExit();
                        }
                    }
                }
                else
                {
                    ZipFile.ExtractToDirectory(tempArchive, installDir, true);
                }

                if (File.Exists(tempArchive))
                {
                    try { File.Delete(tempArchive); } catch { }
                }

                var exePath = GetExpectedExecutablePath(installDir);
                if (File.Exists(exePath))
                {
                    if (isMac) cfg.steamCmdPathMac = exePath;
                    else cfg.steamCmdPath = exePath;

                    onProgress?.Invoke(1.0f, "SteamCmd ready!");
                    return (true, $"SteamCmd installed successfully at: {exePath}", exePath);
                }

                return (false, $"Archive extracted but executable was not found at {exePath}", null);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to install SteamCmd: {ex.Message}", null);
            }
        }
    }
}
