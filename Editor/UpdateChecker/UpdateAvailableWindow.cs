using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal class UpdateAvailableWindow : EditorWindow
    {
        const string RepoBranch = "master";
        static readonly Color AccentColor = new Color(0.24f, 0.48f, 0.95f);

        string _packageDisplayName;
        string _currentVersion;
        string _latestVersion;
        string _repoUrl;
        string _gitUrl;
        string _releaseNotes;
        string _skipPrefKey;
        Vector2 _notesScroll;

        AddRequest _addRequest;
        bool _updating;
        string _updateError;

        Texture2D _headerTex;
        Texture2D _dividerTex;
        GUIStyle _headerTitleStyle;
        GUIStyle _headerSubtitleStyle;
        GUIStyle _arrowStyle;
        GUIStyle _primaryButtonStyle;
        GUIStyle _footerStyle;
        bool _stylesBuilt;

        public static void Show(string packageDisplayName, string currentVersion, string latestVersion,
            string repoUrl, string gitUrl, string releaseNotes, string skipPrefKey)
        {
            var window = CreateInstance<UpdateAvailableWindow>();
            window.titleContent = new GUIContent($"{packageDisplayName} — Update");
            window._packageDisplayName = packageDisplayName;
            window._currentVersion = currentVersion;
            window._latestVersion = latestVersion;
            window._repoUrl = repoUrl;
            window._gitUrl = gitUrl;
            window._releaseNotes = string.IsNullOrEmpty(releaseNotes)
                ? "No release notes available — see the full changelog on GitHub."
                : releaseNotes;
            window._skipPrefKey = skipPrefKey;

            var size = new Vector2(460, 420);
            window.minSize = size;
            window.maxSize = new Vector2(640, 760);
            window.ShowUtility();
        }

        void BuildStyles()
        {
            if (_stylesBuilt)
                return;
            _stylesBuilt = true;

            var dark = EditorGUIUtility.isProSkin;
            var headerBg = dark ? new Color(0.17f, 0.17f, 0.19f) : new Color(0.90f, 0.92f, 0.97f);
            var dividerColor = dark ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.10f);

            _headerTex = MakeTex(headerBg);
            _dividerTex = MakeTex(dividerColor);

            _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                normal = { textColor = dark ? Color.white : new Color(0.12f, 0.12f, 0.14f) }
            };

            _headerSubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = dark ? new Color(1f, 1f, 1f, 0.6f) : new Color(0f, 0f, 0f, 0.55f) }
            };

            _arrowStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = AccentColor }
            };

            _primaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold
            };

            _footerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = dark ? new Color(1f, 1f, 1f, 0.4f) : new Color(0f, 0f, 0f, 0.4f) }
            };
        }

        static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        void OnGUI()
        {
            BuildStyles();

            var headerRect = GUILayoutUtility.GetRect(position.width, 56);
            GUI.DrawTexture(headerRect, _headerTex);
            var innerHeader = new Rect(headerRect.x + 16, headerRect.y + 8, headerRect.width - 32, headerRect.height - 12);
            GUI.BeginGroup(innerHeader);
            GUI.Label(new Rect(0, 0, innerHeader.width, 20), "New version available", _headerTitleStyle);
            GUI.Label(new Rect(0, 20, innerHeader.width, 16), _packageDisplayName, _headerSubtitleStyle);
            GUI.EndGroup();

            DrawDivider();
            GUILayout.Space(12);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_currentVersion, EditorStyles.label);
                GUILayout.Label("→", _arrowStyle, GUILayout.Width(22));
                GUILayout.Label(_latestVersion, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(14);

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("What's new in this version", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            _notesScroll = EditorGUILayout.BeginScrollView(_notesScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField(_releaseNotes, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndScrollView();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_updateError))
            {
                GUILayout.Space(6);
                EditorGUILayout.HelpBox(_updateError, MessageType.Error);
            }

            GUILayout.Space(10);
            DrawDivider();
            GUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                if (GUILayout.Button("View on GitHub", GUILayout.Width(110)))
                    Application.OpenURL($"{_repoUrl}/releases/tag/v{_latestVersion}");

                if (!string.IsNullOrEmpty(_skipPrefKey) && GUILayout.Button("Skip this version", GUILayout.Width(120)))
                {
                    EditorPrefs.SetString(_skipPrefKey, _latestVersion);
                    Close();
                }

                GUILayout.FlexibleSpace();

                if (_updating)
                {
                    GUI.enabled = false;
                    GUILayout.Button("Updating...", _primaryButtonStyle, GUILayout.Width(110), GUILayout.Height(24));
                    GUI.enabled = true;
                }
                else
                {
                    if (GUILayout.Button("Update Now", _primaryButtonStyle, GUILayout.Width(110), GUILayout.Height(24)))
                        StartUpdate();
                }

                if (GUILayout.Button("Later", GUILayout.Width(64), GUILayout.Height(24)))
                    Close();

                GUILayout.Space(12);
            }

            GUILayout.Space(8);
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
                Close();
            }
            else
            {
                _updateError = _addRequest.Error != null
                    ? _addRequest.Error.message
                    : "Update failed. Try re-importing manually from Package Manager.";
                Repaint();
            }
        }
    }
}
