using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class BuildPipelineWindow : EditorWindow
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Open Build Window", priority = 110)]
        [MenuItem("Window/Wagenheimer/Build Pipeline", priority = 205)]
        public static void ShowWindow()
        {
            Debug.Log("[BuildPipeline] 🚀 Opening Build Pipeline Window...");
            try
            {
                var win = GetWindow<BuildPipelineWindow>(utility: false, title: "Build Pipeline", focus: true);
                win.titleContent = new GUIContent("Build Pipeline", EditorGUIUtility.IconContent("BuildSettings.Editor").image);
                win.minSize = new Vector2(760, 560);

                // Check if the window is off-screen or positioned on a disconnected monitor, and center it
                var mainPos = EditorGUIUtility.GetMainWindowPosition();
                bool isOffscreen = !win.docked && (
                    win.position.x >= mainPos.x + mainPos.width - 80 ||
                    win.position.x + win.position.width <= mainPos.x + 80 ||
                    win.position.y >= mainPos.y + mainPos.height - 80 ||
                    win.position.y < mainPos.y - 100 ||
                    win.position.width < 300 || win.position.height < 300
                );

                if (isOffscreen)
                {
                    float w = Mathf.Min(880f, mainPos.width * 0.85f);
                    float h = Mathf.Min(660f, mainPos.height * 0.85f);
                    float x = mainPos.x + (mainPos.width - w) * 0.5f;
                    float y = mainPos.y + (mainPos.height - h) * 0.5f;
                    win.position = new Rect(x, y, w, h);
                    Debug.Log($"[BuildPipeline] Window was off-screen; repositioned to center: {win.position}");
                }

                win.Show();
                win.Focus();
                win.Repaint();
                Debug.Log($"[BuildPipeline] ✓ Build Pipeline Window is now active and focused at: {win.position} (docked: {win.docked})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildPipeline] ❌ Failed to open BuildPipelineWindow: {ex}");
            }
        }

        [MenuItem("Tools/Wagenheimer/Build Pipeline/Reset Window Position (Center Screen)", priority = 114)]
        public static void ResetPosition()
        {
            var win = GetWindow<BuildPipelineWindow>(utility: false, title: "Build Pipeline", focus: true);
            var mainPos = EditorGUIUtility.GetMainWindowPosition();
            float w = Mathf.Min(880f, mainPos.width * 0.85f);
            float h = Mathf.Min(660f, mainPos.height * 0.85f);
            float x = mainPos.x + (mainPos.width - w) * 0.5f;
            float y = mainPos.y + (mainPos.height - h) * 0.5f;
            win.position = new Rect(x, y, w, h);
            win.Show();
            win.Focus();
            win.Repaint();
            Debug.Log($"[BuildPipeline] ✓ Build Pipeline Window position reset to center: {win.position}");
        }

        private ProjectBuildConfig _config;
        private VisualElement _root;
        private ScrollView _contentContainer;
        private int _selectedTab = 0; // 0=Quick Build, 1=Matrix, 2=History, 3=Vault, 4=CLI, 5=Config

        private bool[] _selectedPublishers;
        private bool[] _selectedLanguages;

        private void OnEnable()
        {
            try
            {
                _config = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();
                InitMatrixArrays();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Window OnEnable warning: {ex.Message}");
            }
        }

        private void InitMatrixArrays()
        {
            if (_config == null) return;
            if (_config.publishers == null) _config.publishers = new System.Collections.Generic.List<PublisherProfile>();
            if (_config.languages == null) _config.languages = new System.Collections.Generic.List<LanguageProfile>();

            if (_selectedPublishers == null || _selectedPublishers.Length != _config.publishers.Count)
                _selectedPublishers = new bool[_config.publishers.Count];

            if (_selectedLanguages == null || _selectedLanguages.Length != _config.languages.Count)
            {
                _selectedLanguages = new bool[_config.languages.Count];
                for (var i = 0; i < _config.languages.Count; i++)
                {
                    if (_config.languages[i] != null)
                        _selectedLanguages[i] = _config.languages[i].enabled;
                }
            }
        }

        public void CreateGUI()
        {
            try
            {
                _root = rootVisualElement;
                _root.style.flexGrow = 1;
                _root.style.height = Length.Percent(100);
                _root.style.paddingTop = 10;
                _root.style.paddingBottom = 10;
                _root.style.paddingLeft = 12;
                _root.style.paddingRight = 12;

                BuildPipelineUIStyle.Apply(_root);
                RebuildUI();
                Debug.Log("[BuildPipeline] BuildPipelineWindow UI visual elements loaded successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildPipeline] Error initializing BuildPipelineWindow GUI: {ex}");
                rootVisualElement.Clear();
                var errBox = new HelpBox($"Failed to initialize Build Pipeline UI:\n{ex.Message}\n\n{ex.StackTrace}", HelpBoxMessageType.Error);
                rootVisualElement.Add(errBox);
                var retryBtn = new Button(RebuildUI) { text = "🔄 Retry" };
                retryBtn.style.height = 30;
                rootVisualElement.Add(retryBtn);
            }
        }

        private void RebuildUI()
        {
            _root.Clear();

            try
            {
                if (_config == null)
                    _config = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] FindOrCreateProjectBuildConfig: {ex.Message}");
            }

            InitMatrixArrays();

            // 1. Unified Header (Project title, linked config, live version summary & steppers, save status)
            var headerView = new BuildPipelineHeaderView(_config, RebuildUI);
            _root.Add(headerView.Root);

            // 2. Tab Bar
            _root.Add(CreateTabBar());

            // 3. Tab Content Container
            _contentContainer = new ScrollView(ScrollViewMode.Vertical);
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.flexShrink = 1;
            _root.Add(_contentContainer);

            RebuildContent();
        }

        private VisualElement CreateTabBar()
        {
            var tabRow = new VisualElement();
            tabRow.AddToClassList("bp-tab-bar");
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.flexShrink = 0;
            tabRow.style.marginBottom = 10;

            (string icon, string label)[] tabs =
            {
                ("⚡", "Quick Build"),
                ("🏭", "Matrix Batch"),
                ("🕘", "Recent Builds"),
                ("🔐", "Keystore & Vault"),
                ("💻", "CLI & Automation"),
                ("⚙️", "Project Config"),
            };

            for (var i = 0; i < tabs.Length; i++)
            {
                var tabIndex = i;
                var isSelected = _selectedTab == tabIndex;

                var tabBtn = new Button(() =>
                {
                    _selectedTab = tabIndex;
                    RebuildUI();
                })
                { text = $"{tabs[i].icon} {tabs[i].label}" };

                tabBtn.AddToClassList("bp-tab-button");
                tabBtn.style.flexGrow = 1;
                tabBtn.style.flexShrink = 0;
                tabBtn.style.height = 32;

                if (isSelected)
                {
                    tabBtn.AddToClassList("bp-tab-button--active");
                }

                tabRow.Add(tabBtn);
            }

            return tabRow;
        }

        private void RebuildContent()
        {
            _contentContainer.Clear();
            switch (_selectedTab)
            {
                case 0:
                    _contentContainer.Add(new BuildPipelineQuickBuildView(_config, RebuildUI).Root);
                    break;
                case 1:
                    _contentContainer.Add(new BuildPipelineMatrixView(_config, _selectedPublishers, _selectedLanguages, RebuildUI).Root);
                    break;
                case 2:
                    _contentContainer.Add(new BuildPipelineHistoryView(RebuildUI).Root);
                    break;
                case 3:
                    _contentContainer.Add(new BuildPipelineVaultView(_config, RebuildUI).Root);
                    break;
                case 4:
                    _contentContainer.Add(new BuildPipelineCliView().Root);
                    break;
                case 5:
                    _contentContainer.Add(new BuildPipelineConfigView(_config, RebuildUI).Root);
                    break;
            }
        }
    }
}
