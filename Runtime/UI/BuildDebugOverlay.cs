using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Wagenheimer.BuildPipeline
{
    /// <summary>
    /// In-game runtime debug overlay for inspecting and verifying build configurations.
    /// Displays the active Publisher, FullGame status, Package / Bundle Identifier,
    /// Store IDs, and Build Numbers in Unity Editor and Development Builds.
    /// </summary>
    [AddComponentMenu("Wagenheimer/Build Pipeline/Build Debug Overlay")]
    [DisallowMultipleComponent]
    public class BuildDebugOverlay : MonoBehaviour
    {
        #region Settings

        [Header("Runtime Access")]
        [Tooltip("Hot key to toggle debug panel visibility in game.")]
        public KeyCode toggleKey = KeyCode.F7;

        [Tooltip("Whether to draw a small floating 'BUILD DBG' button on screen.")]
        public bool showFloatingButton = true;

        [Tooltip("Allow overlay to run even in non-development / release builds. Strongly recommended FALSE for production.")]
        public bool enableInReleaseBuilds = false;

        [Tooltip("Optional direct reference to GameConfig. If null, automatically discovered at runtime.")]
        public GameConfig customGameConfig;

        #endregion

        #region Private Fields

        private bool _isOpen;
        private Rect _windowRect = new Rect(15, 15, 520, 580);
        private Vector2 _scrollPos;
        private string _statusLog = "Ready.";
        private GameConfig _cachedConfig;

        #endregion

        #region Config Discovery

        public GameConfig Config
        {
            get
            {
                if (customGameConfig != null)
                    return customGameConfig;

                if (_cachedConfig != null)
                    return _cachedConfig;

                // 1. Search for any loaded GameConfig in memory
                var configs = Resources.FindObjectsOfTypeAll<GameConfig>();
                if (configs != null && configs.Length > 0)
                {
                    _cachedConfig = configs[0];
                    return _cachedConfig;
                }

                // 2. Search for any MonoBehaviour with a 'Config' field of type GameConfig (e.g. Main / MainBase)
                var allMonos = FindObjectsOfType<MonoBehaviour>();
                foreach (var mono in allMonos)
                {
                    if (mono == null) continue;
                    var field = mono.GetType().GetField("Config", BindingFlags.Public | BindingFlags.Instance);
                    if (field != null && field.FieldType == typeof(GameConfig))
                    {
                        var val = field.GetValue(mono) as GameConfig;
                        if (val != null)
                        {
                            _cachedConfig = val;
                            return _cachedConfig;
                        }
                    }
                }

                return null;
            }
        }

        #endregion

        #region Unity Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (!Debug.isDebugBuild && !Application.isEditor)
                return;

            if (FindObjectOfType<BuildDebugOverlay>() == null)
            {
                var go = new GameObject("BuildDebugOverlay");
                go.AddComponent<BuildDebugOverlay>();
            }
        }

        private void Awake()
        {
            if (!Debug.isDebugBuild && !Application.isEditor && !enableInReleaseBuilds)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                _isOpen = !_isOpen;
            }
        }

        private void OnGUI()
        {
            if (!Debug.isDebugBuild && !Application.isEditor && !enableInReleaseBuilds)
                return;

            GUI.depth = -9998;

            if (showFloatingButton && !_isOpen)
            {
                // Placed at (5, Screen.height - 72) so it stacks neatly above IAP DBG (Screen.height - 40)
                if (GUI.Button(new Rect(5, Screen.height - 72, 90, 30), "BUILD DBG"))
                {
                    _isOpen = true;
                }
            }

            if (_isOpen)
            {
                _windowRect = GUI.Window(888456, _windowRect, DrawDebugWindow, "Build Pipeline — Runtime Verification");
            }
        }

        #endregion

        #region GUI Layout

        private void DrawDebugWindow(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width - 60, 24));

            if (GUI.Button(new Rect(_windowRect.width - 55, 3, 50, 20), "Close"))
            {
                _isOpen = false;
                return;
            }

            GUILayout.Space(22);

            var config = Config;
            if (config == null)
            {
                GUILayout.Label("<color=red><b>GameConfig asset not found in memory!</b></color>");
                GUILayout.Label("Ensure your scene or bootstrap prefab has a reference to GameConfig.");
                return;
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            DrawPublisherSection(config);
            GUILayout.Space(4);
            DrawPackageVerificationSection(config);
            GUILayout.Space(4);
            DrawVersionSection(config);
            GUILayout.Space(4);
            DrawStoreCatalogSection(config);
            GUILayout.Space(4);
            DrawActionsSection(config);

            GUILayout.EndScrollView();
        }

        private void DrawPublisherSection(GameConfig config)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>1. Active Publisher & Game Configuration</b>");

            // Publisher badge
            string pubColor = "cyan";
            if (config.Publisher == Publisher.Default) pubColor = "yellow";
            else if (config.PublisherIsGoogleAndroid) pubColor = "lime";
            else if (config.PublisherIsiOS) pubColor = "#4DA6FF";
            else if (config.PublisherIsAmazonAndroid) pubColor = "#FFA500";

            GUILayout.Label($"Publisher: <color={pubColor}><b>{config.Publisher}</b></color> ({(int)config.Publisher})");

            // Full Game vs Free
            string fullColor = config.FullGame ? "lime" : "#FF9933";
            string fullLabel = config.FullGame ? "YES (Full Game / Paid)" : "NO (Free / IAP Unlock)";
            GUILayout.Label($"Full Game: <color={fullColor}><b>{fullLabel}</b></color>");

            GUILayout.Label($"Demo: {(config.Demo ? "<color=yellow>YES</color>" : "NO")} | " +
                            $"Cheat Mode: {(config.CheatMode ? "<color=red>ENABLED</color>" : "Disabled")} | " +
                            $"FreeToPlay: {config.FreeToPlay}");

            GUILayout.Label($"Language: <b>{config.GameLanguage}</b> | " +
                            $"Achievements: {config.UseAchievements} | " +
                            $"Level Editor: {config.LevelEditor}");

            GUILayout.EndVertical();
        }

        private void DrawPackageVerificationSection(GameConfig config)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>2. Platform & Package Verification</b>");

            string currentPkg = Application.identifier;
            string expectedPkg = GetExpectedBundleIdentifier(config);

            bool isMatch = string.Equals(currentPkg, expectedPkg, StringComparison.OrdinalIgnoreCase);
            string matchBadge = isMatch
                ? "<color=lime><b>[MATCH: OK]</b></color>"
                : "<color=red><b>[MISMATCH: WARNING]</b></color>";

            GUILayout.Label($"Running Platform: <b>{Application.platform}</b>");
            GUILayout.Label($"Running Package ID: <b>{currentPkg}</b>");
            GUILayout.Label($"Expected for {config.Publisher}: <b>{(!string.IsNullOrEmpty(expectedPkg) ? expectedPkg : "(not configured)")}</b>");
            GUILayout.Label($"Verification: {matchBadge}");

            // Verification details
            if (config.Publisher == Publisher.GoogleAndroidFree && config.FullGame)
            {
                GUILayout.Label("<color=red><b>⚠️ WARNING: Publisher is GoogleAndroidFree but FullGame is TRUE!</b></color>");
            }
            else if (config.Publisher == Publisher.GoogleAndroidFull && !config.FullGame)
            {
                GUILayout.Label("<color=red><b>⚠️ WARNING: Publisher is GoogleAndroidFull but FullGame is FALSE!</b></color>");
            }

            GUILayout.EndVertical();
        }

        private void DrawVersionSection(GameConfig config)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>3. Version & Build Numbers</b>");

            var ver = config.GameVersion != null ? config.GameVersion.GameVersionAsText : "N/A";
            var date = config.VersionDate != null ? config.VersionDate.AsText : "N/A";

            GUILayout.Label($"Game Version: <b>{ver}</b> (Date: {date})");
            GUILayout.Label($"Application.version: <b>{Application.version}</b>");
            GUILayout.Label($"Android BundleVersionCode: <b>{config.AndroidBundleVersionCode}</b>");
            GUILayout.Label($"iOS / macOS Build Number: <b>{config.iOSBuildNumber}</b>");
            GUILayout.Label($"Unity Runtime: {Application.unityVersion} | Debug Build: {Debug.isDebugBuild}");

            GUILayout.EndVertical();
        }

        private void DrawStoreCatalogSection(GameConfig config)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>4. Configured Store Packages (GameConfig)</b>");

            GUILayout.Label($"Google Android Free: <b>{config.AndroidFree}</b>");
            GUILayout.Label($"Google Android Full: <b>{config.AndroidFull}</b>");
            GUILayout.Label($"iOS Free: <b>{config.IOSFree}</b> (App ID: {config.iOSAppIDFree})");
            GUILayout.Label($"iOS Full: <b>{config.IOSFull}</b> (App ID: {config.iOSAppIDFull})");
            GUILayout.Label($"Amazon Free: <b>{config.AmazonFree}</b>");
            GUILayout.Label($"Amazon Full: <b>{config.AmazonFull}</b>");
            GUILayout.Label($"Default Bundle ID: {config.DefaultBundleIdentifier}");

            GUILayout.EndVertical();
        }

        private void DrawActionsSection(GameConfig config)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>5. Diagnostics & Quick Actions</b>");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy Report"))
            {
                var report = GenerateReport(config);
                GUIUtility.systemCopyBuffer = report;
                _statusLog = "Full configuration report copied to clipboard!";
                Debug.Log("[BuildDebugOverlay] " + _statusLog);
            }

            if (GUILayout.Button("Log to Console"))
            {
                var report = GenerateReport(config);
                Debug.Log($"[BuildDebugOverlay]\n{report}");
                _statusLog = "Logged to Console / Logcat.";
            }

            if (GUILayout.Button(config.CheatMode ? "Disable Cheats" : "Enable Cheats"))
            {
                config.CheatMode = !config.CheatMode;
                _statusLog = $"CheatMode toggled to: {config.CheatMode}";
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusLog))
            {
                GUILayout.Space(2);
                GUILayout.Label($"<color=grey>Status: {_statusLog}</color>");
            }

            GUILayout.EndVertical();
        }

        #endregion

        #region Helpers

        public string GetExpectedBundleIdentifier(GameConfig config)
        {
            if (config == null) return "";

            return config.Publisher switch
            {
                Publisher.GoogleAndroidFree => !string.IsNullOrEmpty(config.AndroidFree) ? config.AndroidFree : config.DefaultBundleIdentifier,
                Publisher.GoogleAndroidFull => !string.IsNullOrEmpty(config.AndroidFull) ? config.AndroidFull : config.DefaultBundleIdentifier,
                Publisher.AmazonAndroidFree => !string.IsNullOrEmpty(config.AmazonFree) ? config.AmazonFree : config.DefaultBundleIdentifier,
                Publisher.AmazonAndroidFull => !string.IsNullOrEmpty(config.AmazonFull) ? config.AmazonFull : config.DefaultBundleIdentifier,
                Publisher.SamsungFree => !string.IsNullOrEmpty(config.SamsungFree) ? config.SamsungFree : config.DefaultBundleIdentifier,
                Publisher.SamsungFull => !string.IsNullOrEmpty(config.SamsungFull) ? config.SamsungFull : config.DefaultBundleIdentifier,
                Publisher.iOSFree => !string.IsNullOrEmpty(config.IOSFree) ? config.IOSFree : config.DefaultBundleIdentifier,
                Publisher.iOSFull => !string.IsNullOrEmpty(config.IOSFull) ? config.IOSFull : config.DefaultBundleIdentifier,
                Publisher.MacAppStore or Publisher.MacAppStoreFull => !string.IsNullOrEmpty(config.IOSFree) ? config.IOSFree : config.DefaultBundleIdentifier,
                _ => config.DefaultBundleIdentifier
            };
        }

        public string GenerateReport(GameConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Unity Build Pipeline Runtime Verification Report ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Publisher: {config.Publisher} ({(int)config.Publisher})");
            sb.AppendLine($"FullGame: {config.FullGame}");
            sb.AppendLine($"Demo: {config.Demo}");
            sb.AppendLine($"CheatMode: {config.CheatMode}");
            sb.AppendLine($"Language: {config.GameLanguage}");
            sb.AppendLine($"Running Platform: {Application.platform}");
            sb.AppendLine($"Running Package: {Application.identifier}");
            sb.AppendLine($"Expected Package: {GetExpectedBundleIdentifier(config)}");
            sb.AppendLine($"Package Match: {string.Equals(Application.identifier, GetExpectedBundleIdentifier(config), StringComparison.OrdinalIgnoreCase)}");
            sb.AppendLine($"Game Version: {config.GameVersion?.GameVersionAsText}");
            sb.AppendLine($"Version Date: {config.VersionDate?.AsText}");
            sb.AppendLine($"Android BundleVersionCode: {config.AndroidBundleVersionCode}");
            sb.AppendLine($"iOS Build Number: {config.iOSBuildNumber}");
            sb.AppendLine($"Unity Version: {Application.unityVersion}");
            sb.AppendLine($"Is Debug Build: {Debug.isDebugBuild}");
            sb.AppendLine("=======================================================");
            return sb.ToString();
        }

        /// <summary>
        /// Programmatically instantiates a BuildDebugOverlay in the current scene if one does not exist.
        /// </summary>
        public static BuildDebugOverlay CreateOverlay()
        {
            var existing = FindObjectOfType<BuildDebugOverlay>();
            if (existing != null) return existing;

            var go = new GameObject("BuildDebugOverlay", typeof(BuildDebugOverlay));
            return go.GetComponent<BuildDebugOverlay>();
        }

        #endregion
    }
}

/// <summary>
/// Global namespace alias for convenience in inspector and game scripts.
/// </summary>
public class BuildDebugOverlay : Wagenheimer.BuildPipeline.BuildDebugOverlay
{
}
