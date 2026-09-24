using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline
{
    /// <summary>
    /// In-game runtime UI Toolkit debug overlay for inspecting and verifying build configurations.
    /// Displays the active Publisher, FullGame status, Package / Bundle Identifier verification,
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

        [Tooltip("Optional custom PanelSettings. If null, a high-priority runtime PanelSettings is created automatically.")]
        public PanelSettings customPanelSettings;

        #endregion

        #region Private Fields

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _floatingBtn;
        private VisualElement _window;
        private ScrollView _scrollView;

        private Label _publisherLabel;
        private Label _fullGameLabel;
        private Label _cheatModeLabel;
        private Label _matchBanner;
        private Label _runningPkgLabel;
        private Label _expectedPkgLabel;
        private Label _statusLogLabel;

        private bool _isOpen;
        private float _lastRefreshTime;
        private const float RefreshInterval = 0.5f;

        private GameConfig _cachedConfig;

        // Window drag state
        private bool _isDragging;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartWindowPos;

        // Floating button drag state
        private bool _isFloatingDragging;
        private Vector2 _floatingDragStartPointer;
        private Vector2 _floatingDragStartPos;
        private bool _hasDraggedFloating;

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
            InitializeUI();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                SetOpen(!_isOpen);
            }

            if (_isOpen && Time.unscaledTime - _lastRefreshTime >= RefreshInterval)
            {
                _lastRefreshTime = Time.unscaledTime;
                RefreshData();
            }
        }

        #endregion

        #region UI Toolkit Initialization

        private void InitializeUI()
        {
            _uiDocument = gameObject.GetComponent<UIDocument>();
            if (_uiDocument == null)
            {
                _uiDocument = gameObject.AddComponent<UIDocument>();
            }

            EnsurePanelSettings();

            _root = _uiDocument.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;

            BuildFloatingButton();
            BuildWindow();

            SetOpen(false);
            RefreshData();
        }

        private void EnsurePanelSettings()
        {
            if (_uiDocument.panelSettings != null) return;

            if (customPanelSettings != null)
            {
                _uiDocument.panelSettings = customPanelSettings;
                return;
            }

            var loaded = Resources.Load<PanelSettings>("Wagenheimer/DebugPanelSettings");
            if (loaded != null)
            {
                _uiDocument.panelSettings = loaded;
                return;
            }

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "BuildDebugPanelSettings";
            ps.sortingOrder = 9997;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match = 0.5f;

            var themes = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>();
            if (themes != null && themes.Length > 0)
            {
                ps.themeStyleSheet = themes[0];
            }

            _uiDocument.panelSettings = ps;
        }

        #endregion

        #region Floating Button

        private void BuildFloatingButton()
        {
            _floatingBtn = new VisualElement();
            _floatingBtn.name = "BuildDebugFloatingButton";
            _floatingBtn.pickingMode = PickingMode.Position;
            _floatingBtn.style.position = Position.Absolute;
            _floatingBtn.style.bottom = 58;
            _floatingBtn.style.left = 18;
            _floatingBtn.style.height = 34;
            _floatingBtn.style.paddingLeft = 12;
            _floatingBtn.style.paddingRight = 12;
            _floatingBtn.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f, 0.94f));
            _floatingBtn.style.borderTopWidth = 1;
            _floatingBtn.style.borderBottomWidth = 1;
            _floatingBtn.style.borderLeftWidth = 1;
            _floatingBtn.style.borderRightWidth = 1;
            _floatingBtn.style.borderTopColor = new StyleColor(new Color(0.95f, 0.65f, 0.20f, 0.8f));
            _floatingBtn.style.borderBottomColor = new StyleColor(new Color(0.95f, 0.65f, 0.20f, 0.8f));
            _floatingBtn.style.borderLeftColor = new StyleColor(new Color(0.95f, 0.65f, 0.20f, 0.8f));
            _floatingBtn.style.borderRightColor = new StyleColor(new Color(0.95f, 0.65f, 0.20f, 0.8f));
            _floatingBtn.style.borderTopLeftRadius = 17;
            _floatingBtn.style.borderTopRightRadius = 17;
            _floatingBtn.style.borderBottomLeftRadius = 17;
            _floatingBtn.style.borderBottomRightRadius = 17;
            _floatingBtn.style.flexDirection = FlexDirection.Row;
            _floatingBtn.style.alignItems = Align.Center;
            _floatingBtn.style.justifyContent = Justify.Center;

            var dot = new VisualElement();
            dot.style.width = 8;
            dot.style.height = 8;
            dot.style.borderTopLeftRadius = 4;
            dot.style.borderTopRightRadius = 4;
            dot.style.borderBottomLeftRadius = 4;
            dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = new StyleColor(new Color(0.95f, 0.65f, 0.20f));
            dot.style.marginRight = 6;
            _floatingBtn.Add(dot);

            var label = new Label("🔨 BUILD DBG");
            label.style.color = new StyleColor(Color.white);
            label.style.fontSize = 11.5f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _floatingBtn.Add(label);

            // Drag / Click handling
            _floatingBtn.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _isFloatingDragging = true;
                _hasDraggedFloating = false;
                _floatingDragStartPointer = evt.position;
                _floatingDragStartPos = new Vector2(_floatingBtn.resolvedStyle.left, _floatingBtn.resolvedStyle.top);
                _floatingBtn.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            _floatingBtn.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isFloatingDragging) return;
                Vector2 delta = (Vector2)evt.position - _floatingDragStartPointer;
                if (delta.sqrMagnitude > 16f) _hasDraggedFloating = true;

                if (_hasDraggedFloating)
                {
                    _floatingBtn.style.bottom = StyleKeyword.Auto;
                    _floatingBtn.style.left = Mathf.Max(0, _floatingDragStartPos.x + delta.x);
                    _floatingBtn.style.top = Mathf.Max(0, _floatingDragStartPos.y + delta.y);
                }
                evt.StopPropagation();
            });

            _floatingBtn.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isFloatingDragging) return;
                _isFloatingDragging = false;
                _floatingBtn.ReleasePointer(evt.pointerId);
                evt.StopPropagation();

                if (!_hasDraggedFloating)
                {
                    SetOpen(true);
                }
            });

            _root.Add(_floatingBtn);
        }

        #endregion

        #region Main Window

        private void BuildWindow()
        {
            _window = new VisualElement();
            _window.name = "BuildDebugWindow";
            _window.pickingMode = PickingMode.Position;
            _window.style.position = Position.Absolute;
            _window.style.left = 24;
            _window.style.top = 30;
            _window.style.width = 470;
            _window.style.maxHeight = new StyleLength(new Length(86, LengthUnit.Percent));
            _window.style.backgroundColor = new StyleColor(new Color(0.09f, 0.09f, 0.11f, 0.96f));
            _window.style.borderTopWidth = 1;
            _window.style.borderBottomWidth = 1;
            _window.style.borderLeftWidth = 1;
            _window.style.borderRightWidth = 1;
            _window.style.borderTopColor = new StyleColor(new Color(0.25f, 0.26f, 0.32f));
            _window.style.borderBottomColor = new StyleColor(new Color(0.25f, 0.26f, 0.32f));
            _window.style.borderLeftColor = new StyleColor(new Color(0.25f, 0.26f, 0.32f));
            _window.style.borderRightColor = new StyleColor(new Color(0.25f, 0.26f, 0.32f));
            _window.style.borderTopLeftRadius = 10;
            _window.style.borderTopRightRadius = 10;
            _window.style.borderBottomLeftRadius = 10;
            _window.style.borderBottomRightRadius = 10;
            _window.style.overflow = Overflow.Hidden;

            // Header (Draggable)
            var header = BuildHeader();
            _window.Add(header);

            // Scrollable Content
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.paddingLeft = 12;
            _scrollView.style.paddingRight = 12;
            _scrollView.style.paddingTop = 10;
            _scrollView.style.paddingBottom = 12;

            _scrollView.Add(BuildPublisherSection());
            _scrollView.Add(BuildPackageVerificationSection());
            _scrollView.Add(BuildVersionSection());
            _scrollView.Add(BuildStoresSection());
            _scrollView.Add(BuildSystemSection());
            _scrollView.Add(BuildActionsSection());

            _window.Add(_scrollView);
            _root.Add(_window);
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.height = 38;
            header.style.paddingLeft = 12;
            header.style.paddingRight = 8;
            header.style.backgroundColor = new StyleColor(new Color(0.13f, 0.14f, 0.18f));
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new StyleColor(new Color(0.22f, 0.23f, 0.28f));

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var titleLbl = new Label("🔨 Build Pipeline Debug");
            titleLbl.style.fontSize = 13;
            titleLbl.style.color = new StyleColor(Color.white);
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(titleLbl);

            var liveBadge = CreatePill("LIVE", new Color(0.15f, 0.5f, 0.25f), Color.white);
            liveBadge.style.marginLeft = 8;
            titleRow.Add(liveBadge);

            header.Add(titleRow);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.alignItems = Align.Center;

            var minBtn = CreateSmallButton("—", () => SetOpen(false));
            minBtn.style.marginRight = 4;
            actions.Add(minBtn);

            var closeBtn = CreateSmallButton("✕", () => SetOpen(false));
            actions.Add(closeBtn);

            header.Add(actions);

            // Drag handling
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _isDragging = true;
                _dragStartPointer = evt.position;
                _dragStartWindowPos = new Vector2(_window.resolvedStyle.left, _window.resolvedStyle.top);
                header.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isDragging) return;
                Vector2 delta = (Vector2)evt.position - _dragStartPointer;
                _window.style.left = Mathf.Max(0, _dragStartWindowPos.x + delta.x);
                _window.style.top = Mathf.Max(0, _dragStartWindowPos.y + delta.y);
                evt.StopPropagation();
            });

            header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isDragging) return;
                _isDragging = false;
                header.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            return header;
        }

        #endregion

        #region Content Sections

        private VisualElement BuildPublisherSection()
        {
            var card = CreateCard("1. Publisher & SKU Configuration");

            _publisherLabel = CreateRow(card, "Active Publisher", "-");
            _fullGameLabel = CreateRow(card, "FullGame Status", "-");
            _cheatModeLabel = CreateRow(card, "Cheat Mode", "-");

            var cfg = Config;
            if (cfg != null)
            {
                CreateRow(card, "Demo Mode", cfg.Demo ? "YES" : "NO");
                CreateRow(card, "Free to Play", cfg.FreeToPlay ? "YES" : "NO");
                CreateRow(card, "Language", cfg.GameLanguage.ToString());
                CreateRow(card, "Achievements Enabled", cfg.UseAchievements ? "YES" : "NO");
                CreateRow(card, "Level Editor", cfg.LevelEditor ? "YES" : "NO");
            }
            else
            {
                var warn = new Label("GameConfig asset not found in memory!");
                warn.style.fontSize = 11;
                warn.style.color = new StyleColor(new Color(0.95f, 0.4f, 0.4f));
                card.Add(warn);
            }

            return card;
        }

        private VisualElement BuildPackageVerificationSection()
        {
            var card = CreateCard("2. Platform & Package Verification");

            _matchBanner = new Label("VERIFYING...");
            _matchBanner.style.fontSize = 12;
            _matchBanner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _matchBanner.style.paddingTop = 4;
            _matchBanner.style.paddingBottom = 4;
            _matchBanner.style.paddingLeft = 8;
            _matchBanner.style.paddingRight = 8;
            _matchBanner.style.borderTopLeftRadius = 4;
            _matchBanner.style.borderTopRightRadius = 4;
            _matchBanner.style.borderBottomLeftRadius = 4;
            _matchBanner.style.borderBottomRightRadius = 4;
            _matchBanner.style.marginBottom = 6;
            card.Add(_matchBanner);

            CreateRow(card, "Running Platform", Application.platform.ToString());
            _runningPkgLabel = CreateRow(card, "Running Package ID", Application.identifier);
            _expectedPkgLabel = CreateRow(card, "Expected Package ID", "-");

            return card;
        }

        private VisualElement BuildVersionSection()
        {
            var card = CreateCard("3. Version & Build Numbers");

            var cfg = Config;
            CreateRow(card, "Application.version", Application.version);
            CreateRow(card, "Game Version", cfg?.GameVersion != null ? cfg.GameVersion.GameVersionAsText : "N/A");
            CreateRow(card, "Version Date", cfg?.VersionDate != null ? cfg.VersionDate.AsText : "N/A");
            CreateRow(card, "Android BundleVersionCode", cfg != null ? cfg.AndroidBundleVersionCode.ToString() : "-");
            CreateRow(card, "iOS / macOS Build Number", cfg != null ? cfg.iOSBuildNumber.ToString() : "-");
            CreateRow(card, "Unity Version", Application.unityVersion);
            CreateRow(card, "Debug Build", Debug.isDebugBuild ? "YES" : "NO");

            return card;
        }

        private VisualElement BuildStoresSection()
        {
            var card = CreateCard("4. Store Catalog & Identifiers");

            var cfg = Config;
            if (cfg != null)
            {
                CreateStoreRow(card, "Google Android Free", cfg.AndroidFree);
                CreateStoreRow(card, "Google Android Full", cfg.AndroidFull);
                CreateStoreRow(card, "Apple iOS Free", $"{cfg.IOSFree} (ID: {cfg.iOSAppIDFree})");
                CreateStoreRow(card, "Apple iOS Full", $"{cfg.IOSFull} (ID: {cfg.iOSAppIDFull})");
                CreateStoreRow(card, "Amazon Free", cfg.AmazonFree);
                CreateStoreRow(card, "Amazon Full", cfg.AmazonFull);
                CreateStoreRow(card, "Default Bundle ID", cfg.DefaultBundleIdentifier);
            }

            return card;
        }

        private VisualElement BuildSystemSection()
        {
            var card = CreateCard("5. Runtime Environment");

            CreateRow(card, "Active Scene", SceneManager.GetActiveScene().name);
            CreateRow(card, "Screen Resolution", $"{Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0.##}Hz");
            CreateRow(card, "Device Model", SystemInfo.deviceModel);
            CreateRow(card, "Operating System", SystemInfo.operatingSystem);
            CreateRow(card, "Graphics Device", $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)");

            return card;
        }

        private VisualElement BuildActionsSection()
        {
            var card = CreateCard("6. Diagnostics & Quick Actions");

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom = 6;

            var copyReportBtn = CreateButton("📋 Copy Report", new Color(0.22f, 0.23f, 0.28f), Color.white, () =>
            {
                var cfg = Config;
                if (cfg != null)
                {
                    string rep = GenerateReport(cfg);
                    GUIUtility.systemCopyBuffer = rep;
                    SetStatus("Full report copied to clipboard!");
                    Debug.Log($"[BuildDebugOverlay] Report copied:\n{rep}");
                }
            });
            copyReportBtn.style.flexGrow = 1;
            copyReportBtn.style.marginRight = 4;
            btnRow.Add(copyReportBtn);

            var logConsoleBtn = CreateButton("📜 Log to Console", new Color(0.22f, 0.23f, 0.28f), Color.white, () =>
            {
                var cfg = Config;
                if (cfg != null)
                {
                    string rep = GenerateReport(cfg);
                    Debug.Log($"[BuildDebugOverlay]\n{rep}");
                    SetStatus("Report logged to Console.");
                }
            });
            logConsoleBtn.style.flexGrow = 1;
            logConsoleBtn.style.marginRight = 4;
            btnRow.Add(logConsoleBtn);

            var cheatBtn = CreateButton("Toggle Cheats", new Color(0.55f, 0.35f, 0.15f), Color.white, () =>
            {
                var cfg = Config;
                if (cfg != null)
                {
                    cfg.CheatMode = !cfg.CheatMode;
                    SetStatus($"CheatMode set to: {cfg.CheatMode}");
                    RefreshData();
                }
            });
            cheatBtn.style.flexGrow = 1;
            btnRow.Add(cheatBtn);

            card.Add(btnRow);

            _statusLogLabel = new Label("Ready.");
            _statusLogLabel.style.fontSize = 10.5f;
            _statusLogLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.75f));
            card.Add(_statusLogLabel);

            return card;
        }

        #endregion

        #region Data Refresh

        private void RefreshData()
        {
            var cfg = Config;
            if (cfg == null)
            {
                if (_publisherLabel != null) _publisherLabel.text = "(Config Not Found)";
                return;
            }

            if (_publisherLabel != null)
            {
                _publisherLabel.text = $"{cfg.Publisher} ({(int)cfg.Publisher})";
            }

            if (_fullGameLabel != null)
            {
                _fullGameLabel.text = cfg.FullGame ? "YES (Full Game / Paid)" : "NO (Free / IAP Unlock)";
                _fullGameLabel.style.color = cfg.FullGame ? new StyleColor(new Color(0.24f, 0.82f, 0.45f)) : new StyleColor(new Color(0.95f, 0.65f, 0.20f));
            }

            if (_cheatModeLabel != null)
            {
                _cheatModeLabel.text = cfg.CheatMode ? "ENABLED" : "Disabled";
                _cheatModeLabel.style.color = cfg.CheatMode ? new StyleColor(new Color(0.95f, 0.35f, 0.35f)) : new StyleColor(new Color(0.7f, 0.7f, 0.75f));
            }

            string expected = GetExpectedBundleIdentifier(cfg);
            if (_expectedPkgLabel != null) _expectedPkgLabel.text = expected;
            if (_runningPkgLabel != null) _runningPkgLabel.text = Application.identifier;

            bool isMatch = string.Equals(Application.identifier, expected, StringComparison.OrdinalIgnoreCase);
            if (_matchBanner != null)
            {
                if (isMatch)
                {
                    _matchBanner.text = "✓ PACKAGE MATCH: OK";
                    _matchBanner.style.backgroundColor = new StyleColor(new Color(0.15f, 0.55f, 0.28f));
                    _matchBanner.style.color = new StyleColor(Color.white);
                }
                else
                {
                    _matchBanner.text = "⚠️ PACKAGE MISMATCH: Current != Expected";
                    _matchBanner.style.backgroundColor = new StyleColor(new Color(0.65f, 0.22f, 0.20f));
                    _matchBanner.style.color = new StyleColor(Color.white);
                }
            }
        }

        private void SetStatus(string msg)
        {
            if (_statusLogLabel != null)
            {
                _statusLogLabel.text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            }
        }

        #endregion

        #region Helpers & UI Factories

        public void SetOpen(bool open)
        {
            _isOpen = open;
            if (_window != null)
            {
                _window.style.display = _isOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_floatingBtn != null)
            {
                _floatingBtn.style.display = (showFloatingButton && !_isOpen) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_isOpen)
            {
                RefreshData();
            }
        }

        private VisualElement CreateCard(string title)
        {
            var card = new VisualElement();
            card.style.backgroundColor = new StyleColor(new Color(0.12f, 0.13f, 0.16f, 0.9f));
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = new StyleColor(new Color(0.20f, 0.21f, 0.26f));
            card.style.borderBottomColor = new StyleColor(new Color(0.20f, 0.21f, 0.26f));
            card.style.borderLeftColor = new StyleColor(new Color(0.20f, 0.21f, 0.26f));
            card.style.borderRightColor = new StyleColor(new Color(0.20f, 0.21f, 0.26f));
            card.style.borderTopLeftRadius = 8;
            card.style.borderTopRightRadius = 8;
            card.style.borderBottomLeftRadius = 8;
            card.style.borderBottomRightRadius = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.marginBottom = 8;

            var titleLbl = new Label(title);
            titleLbl.style.fontSize = 11.5f;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = new StyleColor(new Color(0.95f, 0.75f, 0.35f));
            titleLbl.style.marginBottom = 6;
            card.Add(titleLbl);

            return card;
        }

        private Label CreateRow(VisualElement parent, string key, object value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;

            var keyLbl = new Label(key);
            keyLbl.style.fontSize = 11;
            keyLbl.style.color = new StyleColor(new Color(0.68f, 0.70f, 0.76f));
            row.Add(keyLbl);

            var valLbl = new Label(value != null ? value.ToString() : "");
            valLbl.style.fontSize = 11;
            valLbl.style.color = new StyleColor(Color.white);
            valLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(valLbl);

            parent.Add(row);
            return valLbl;
        }

        private void CreateStoreRow(VisualElement parent, string label, string id)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;

            var keyLbl = new Label(label);
            keyLbl.style.fontSize = 10.5f;
            keyLbl.style.color = new StyleColor(new Color(0.68f, 0.70f, 0.76f));
            row.Add(keyLbl);

            var rightBox = new VisualElement();
            rightBox.style.flexDirection = FlexDirection.Row;
            rightBox.style.alignItems = Align.Center;

            var valLbl = new Label(string.IsNullOrEmpty(id) ? "(empty)" : id);
            valLbl.style.fontSize = 10.5f;
            valLbl.style.color = new StyleColor(string.IsNullOrEmpty(id) ? new Color(0.5f, 0.5f, 0.55f) : Color.white);
            valLbl.style.marginRight = 6;
            rightBox.Add(valLbl);

            if (!string.IsNullOrEmpty(id))
            {
                var copyBtn = CreateSmallButton("📋", () =>
                {
                    GUIUtility.systemCopyBuffer = id;
                    SetStatus($"Copied '{id}' to clipboard.");
                });
                rightBox.Add(copyBtn);
            }

            row.Add(rightBox);
            parent.Add(row);
        }

        private Button CreateButton(string text, Color bg, Color textCol, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.backgroundColor = new StyleColor(bg);
            btn.style.color = new StyleColor(textCol);
            btn.style.fontSize = 11;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.borderTopLeftRadius = 5;
            btn.style.borderTopRightRadius = 5;
            btn.style.borderBottomLeftRadius = 5;
            btn.style.borderBottomRightRadius = 5;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.paddingTop = 4;
            btn.style.paddingBottom = 4;
            btn.style.paddingLeft = 8;
            btn.style.paddingRight = 8;
            return btn;
        }

        private Button CreateSmallButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            btn.style.width = 22;
            btn.style.height = 22;
            btn.style.fontSize = 11;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = new StyleColor(new Color(0.22f, 0.23f, 0.28f));
            btn.style.color = new StyleColor(Color.white);
            btn.style.borderTopLeftRadius = 4;
            btn.style.borderTopRightRadius = 4;
            btn.style.borderBottomLeftRadius = 4;
            btn.style.borderBottomRightRadius = 4;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            return btn;
        }

        private VisualElement CreatePill(string text, Color bg, Color textCol)
        {
            var pill = new VisualElement();
            pill.style.backgroundColor = new StyleColor(bg);
            pill.style.borderTopLeftRadius = 4;
            pill.style.borderTopRightRadius = 4;
            pill.style.borderBottomLeftRadius = 4;
            pill.style.borderBottomRightRadius = 4;
            pill.style.paddingTop = 1;
            pill.style.paddingBottom = 1;
            pill.style.paddingLeft = 5;
            pill.style.paddingRight = 5;

            var lbl = new Label(text);
            lbl.style.fontSize = 9.5f;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.color = new StyleColor(textCol);
            pill.Add(lbl);

            return pill;
        }

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
