using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal class UpdateAvailableWindow : EditorWindow
    {
        const string RepoBranch = "master";
        static readonly Color AccentColor = new Color(0.18f, 0.55f, 0.95f);

        string _packageDisplayName;
        string _currentVersion;
        string _latestVersion;
        string _repoUrl;
        string _gitUrl;
        string _rawReleaseNotes;
        string _formattedNotes;
        string _skipPrefKey;
        Vector2 _notesScroll;

        AddRequest _addRequest;
        bool _updating;
        string _updateError;

        Texture2D _headerTex;
        Texture2D _dividerTex;
        GUIStyle _headerTitleStyle;
        GUIStyle _headerSubtitleStyle;
        GUIStyle _badgeStyle;
        GUIStyle _versionPillStyle;
        GUIStyle _arrowStyle;
        GUIStyle _notesStyle;
        GUIStyle _primaryButtonStyle;
        GUIStyle _secondaryButtonStyle;
        GUIStyle _footerStyle;
        bool _stylesBuilt;

        public static void Show(string packageDisplayName, string currentVersion, string latestVersion,
            string repoUrl, string gitUrl, string releaseNotes, string skipPrefKey)
        {
            var window = CreateInstance<UpdateAvailableWindow>();
            window.titleContent = new GUIContent($"{packageDisplayName} — Update");
            window._packageDisplayName = packageDisplayName;
            window._currentVersion = string.IsNullOrEmpty(currentVersion) ? "Unknown" : currentVersion;
            window._latestVersion = latestVersion;
            window._repoUrl = repoUrl;
            window._gitUrl = gitUrl;
            window._rawReleaseNotes = releaseNotes;
            window._formattedNotes = FormatReleaseNotes(releaseNotes);
            window._skipPrefKey = skipPrefKey;

            var size = new Vector2(500, 480);
            window.minSize = size;
            window.maxSize = new Vector2(700, 800);
            window.ShowUtility();
        }

        void BuildStyles()
        {
            if (_stylesBuilt)
                return;
            _stylesBuilt = true;

            var dark = EditorGUIUtility.isProSkin;
            var headerBg = dark ? new Color(0.13f, 0.14f, 0.17f) : new Color(0.88f, 0.90f, 0.95f);
            var dividerColor = dark ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.10f);

            _headerTex = MakeTex(headerBg);
            _dividerTex = MakeTex(dividerColor);

            _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                normal = { textColor = dark ? Color.white : new Color(0.10f, 0.10f, 0.12f) }
            };

            _headerSubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = dark ? new Color(0.75f, 0.75f, 0.80f) : new Color(0.35f, 0.35f, 0.40f) }
            };

            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            _versionPillStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(10, 10, 6, 6)
            };

            _arrowStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal = { textColor = AccentColor }
            };

            _notesStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 11,
                padding = new RectOffset(6, 6, 4, 4)
            };

            _primaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };

            _secondaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11
            };

            _footerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = dark ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f) }
            };
        }

        static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        static string FormatReleaseNotes(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "No release notes found in changelog.\nClick <b>View on GitHub</b> to see all release notes online.";

            var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();

            bool dark = EditorGUIUtility.isProSkin;
            string addColor = dark ? "#66BB6A" : "#2E7D32";
            string fixColor = dark ? "#42A5F5" : "#1565C0";
            string chgColor = dark ? "#FFA726" : "#E65100";
            string secColor = dark ? "#81D4FA" : "#0277BD";

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line.StartsWith("### Added", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"\n<color={addColor}><b>✦ Added</b></color>");
                }
                else if (line.StartsWith("### Fixed", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"\n<color={fixColor}><b>✔ Fixed</b></color>");
                }
                else if (line.StartsWith("### Changed", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"\n<color={chgColor}><b>⚡ Changed</b></color>");
                }
                else if (line.StartsWith("### Removed", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"\n<color=#EF5350><b>✕ Removed</b></color>");
                }
                else if (line.StartsWith("## [", StringComparison.OrdinalIgnoreCase) || line.StartsWith("## v", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"\n<size=12><color={secColor}><b>{line.TrimStart('#').Trim()}</b></color></size>");
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    var text = line.Substring(2).Trim();
                    text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "<b>$1</b>");
                    text = Regex.Replace(text, @"`([^`]+)`", "<i>$1</i>");
                    sb.AppendLine($"  <color=#888888>•</color>  {text}");
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var text = Regex.Replace(line, @"\*\*([^*]+)\*\*", "<b>$1</b>");
                    text = Regex.Replace(text, @"`([^`]+)`", "<i>$1</i>");
                    sb.AppendLine(text);
                }
            }

            return sb.ToString().Trim();
        }

        void OnGUI()
        {
            BuildStyles();

            // ── Header Banner ──────────────────────────────────────────────────
            var headerRect = GUILayoutUtility.GetRect(position.width, 60);
            GUI.DrawTexture(headerRect, _headerTex);

            // Accent bar on top
            var topAccentRect = new Rect(headerRect.x, headerRect.y, headerRect.width, 3);
            EditorGUI.DrawRect(topAccentRect, AccentColor);

            var innerHeader = new Rect(headerRect.x + 16, headerRect.y + 10, headerRect.width - 32, headerRect.height - 14);
            GUI.BeginGroup(innerHeader);

            // Pill badge
            var pillRect = new Rect(0, 4, 115, 18);
            EditorGUI.DrawRect(pillRect, new Color(0.16f, 0.65f, 0.27f));
            GUI.Label(pillRect, "UPDATE AVAILABLE", _badgeStyle);

            GUI.Label(new Rect(125, 3, innerHeader.width - 125, 20), _packageDisplayName, _headerTitleStyle);
            GUI.Label(new Rect(0, 26, innerHeader.width, 16), $"Version {_latestVersion} is ready to install from GitHub.", _headerSubtitleStyle);
            GUI.EndGroup();

            DrawDivider();
            GUILayout.Space(10);

            // ── Version Diff Card ──────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Installed:  <b>{_currentVersion}</b>", _notesStyle);
                    GUILayout.Label("➔", _arrowStyle, GUILayout.Width(24));
                    GUILayout.Label($"Latest:  <color=#2E7D32><b>{_latestVersion}</b></color>", _notesStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("GitHub Release", EditorStyles.linkLabel))
                    {
                        Application.OpenURL($"{_repoUrl}/releases");
                    }
                }
            }
            GUILayout.Space(16);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // ── What's New Header ──────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("<b>Release Notes:</b>", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // ── Release Notes Box ──────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            _notesScroll = GUILayout.BeginScrollView(_notesScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            GUILayout.Label(_formattedNotes, _notesStyle);
            GUILayout.EndScrollView();
            GUILayout.Space(16);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_updateError))
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                EditorGUILayout.HelpBox(_updateError, MessageType.Error);
                GUILayout.Space(16);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            DrawDivider();
            GUILayout.Space(8);

            // ── Action Buttons ─────────────────────────────────────────────────
            if (_updating)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUI.enabled = false;
                GUILayout.Button("Installing update via Unity Package Manager...", _primaryButtonStyle, GUILayout.Height(34));
                GUI.enabled = true;
                GUILayout.Space(16);
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16);

                GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
                if (GUILayout.Button($"Update to {_latestVersion}", _primaryButtonStyle, GUILayout.Height(32)))
                {
                    StartUpdate();
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("View on GitHub", _secondaryButtonStyle, GUILayout.Height(32), GUILayout.Width(120)))
                {
                    Application.OpenURL($"{_repoUrl}/releases");
                }

                if (!string.IsNullOrEmpty(_skipPrefKey) && GUILayout.Button("Skip this version", _secondaryButtonStyle, GUILayout.Height(32), GUILayout.Width(120)))
                {
                    EditorPrefs.SetString(_skipPrefKey, _latestVersion);
                    Close();
                }

                if (GUILayout.Button("Later", _secondaryButtonStyle, GUILayout.Height(32), GUILayout.Width(64)))
                {
                    Close();
                }

                GUILayout.Space(16);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }

            // ── Footer ─────────────────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label(_repoUrl, _footerStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        void DrawDivider()
        {
            var rect = GUILayoutUtility.GetRect(position.width, 1);
            GUI.DrawTexture(rect, _dividerTex);
        }

        void StartUpdate()
        {
            _updating = true;
            _updateError = null;
            _addRequest = Client.Add($"{_gitUrl}#{RepoBranch}");
            EditorApplication.update += PollAddRequest;
        }

        void PollAddRequest()
        {
            if (!_addRequest.IsCompleted)
                return;

            EditorApplication.update -= PollAddRequest;
            _updating = false;

            if (_addRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[{_packageDisplayName}] Successfully updated to {_latestVersion}.");
                Close();
            }
            else
            {
                _updateError = _addRequest.Error != null
                    ? _addRequest.Error.message
                    : "Update failed. Try updating manually in Window > Package Manager.";
                Debug.LogError($"[{_packageDisplayName}] Update error: {_updateError}");
                Repaint();
            }
        }

        void OnDestroy()
        {
            EditorApplication.update -= PollAddRequest;
            if (_headerTex != null) DestroyImmediate(_headerTex);
            if (_dividerTex != null) DestroyImmediate(_dividerTex);
        }
    }
}
