using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(GameConfig))]
    public class GameConfigCustomEditor : UnityEditor.Editor
    {
        private static bool _foldTargets = true;
        private static bool _foldVault = true;
        private static bool _foldAssets = false;
        private static bool _foldIdentity = false;
        private static bool _foldStores = false;
        private static bool _foldExtra = false;

        private static bool _showVaultToken = false;
        private string _vaultTestStatus = "";
        private ProjectBuildConfig _linkedBuildConfig;

        private void OnEnable()
        {
            _foldTargets = EditorPrefs.GetBool("GameConfig_FoldTargets", true);
            _foldVault = EditorPrefs.GetBool("GameConfig_FoldVault", true);
            _foldAssets = EditorPrefs.GetBool("GameConfig_FoldAssets", false);
            _foldIdentity = EditorPrefs.GetBool("GameConfig_FoldIdentity", false);
            _foldStores = EditorPrefs.GetBool("GameConfig_FoldStores", false);
            _foldExtra = EditorPrefs.GetBool("GameConfig_FoldExtra", false);

            _linkedBuildConfig = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var config = (GameConfig)target;

            if (_linkedBuildConfig == null)
                _linkedBuildConfig = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();

            EnsureSubObjects(config);

            // 1. Modern Hero Branding Card
            DrawHeroBanner(config);

            // 2. Action Shortcuts Toolbar
            DrawActionShortcuts();

            EditorGUILayout.Space(6);

            // 3. Digital Version & Date Stepper
            DrawVersionManager(config);

            EditorGUILayout.Space(8);

            // 4. Grouped Collapsible Sections
            DrawCoreTargetsSection(config);
            EditorGUILayout.Space(4);

            DrawRemoteVaultSection(config);
            EditorGUILayout.Space(4);

            DrawVisualAssetsSection(config);
            EditorGUILayout.Space(4);

            DrawIdentitySection(config);
            EditorGUILayout.Space(4);

            DrawStoreIdentifiersSection(config);
            EditorGUILayout.Space(4);

            DrawExtraFlagsSection(config);
            EditorGUILayout.Space(8);

            // 5. Footer & Sync Status
            DrawFooter(config);

            serializedObject.ApplyModifiedProperties();
        }

        private void EnsureSubObjects(GameConfig config)
        {
            if (config.GameVersion == null)
            {
                config.GameVersion = new GameVersion(1, 0, 0);
                EditorUtility.SetDirty(config);
            }
            if (config.VersionDate == null)
            {
                config.VersionDate = new GameBuildDate(DateTime.Now);
                EditorUtility.SetDirty(config);
            }
        }

        #region Hero Banner
        private void DrawHeroBanner(GameConfig config)
        {
            var isPro = EditorGUIUtility.isProSkin;
            var bgColor = isPro ? new Color(0.12f, 0.14f, 0.18f) : new Color(0.88f, 0.90f, 0.94f);

            var rect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.DrawRect(rect, bgColor);

            GUILayout.Space(6);
            var title = !string.IsNullOrEmpty(config.GameName) ? config.GameName :
                        !string.IsNullOrEmpty(config.BuildFolderName) ? config.BuildFolderName : "Game Configuration";

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isPro ? new Color(0.25f, 0.85f, 0.95f) : new Color(0.05f, 0.45f, 0.70f) }
            };
            EditorGUILayout.LabelField(title.ToUpperInvariant(), titleStyle);

            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isPro ? new Color(0.65f, 0.70f, 0.75f) : new Color(0.35f, 0.40f, 0.45f) }
            };
            EditorGUILayout.LabelField("WAGENHEIMER BUILD PIPELINE — RUNTIME CONFIGURATION", subStyle);

            GUILayout.Space(6);

            // Status Chips Bar
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            DrawBadge(config.FullGame ? "FULL GAME" : "FREE / DEMO", config.FullGame ? new Color(0.15f, 0.55f, 0.35f) : new Color(0.75f, 0.55f, 0.15f));
            GUILayout.Space(4);

            if (config.CheatMode)
                DrawBadge("CHEAT ON", new Color(0.85f, 0.20f, 0.20f));
            else
                DrawBadge("CHEAT OFF", isPro ? new Color(0.28f, 0.30f, 0.35f) : new Color(0.75f, 0.75f, 0.75f));

            GUILayout.Space(4);
            DrawBadge(config.UseAchievements ? "ACHIEVEMENTS ON" : "ACHIEVEMENTS OFF", isPro ? new Color(0.25f, 0.35f, 0.55f) : new Color(0.65f, 0.75f, 0.90f));
            GUILayout.Space(4);
            DrawBadge($"STORE: {config.Publisher}", isPro ? new Color(0.35f, 0.25f, 0.55f) : new Color(0.75f, 0.65f, 0.90f));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        private void DrawBadge(string text, Color bgColor)
        {
            var style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            var content = new GUIContent(text);
            var size = style.CalcSize(content);
            var rect = GUILayoutUtility.GetRect(size.x + 10, 16);
            EditorGUI.DrawRect(rect, bgColor);
            GUI.Label(rect, text, style);
        }
        #endregion

        #region Action Shortcuts
        private void DrawActionShortcuts()
        {
            GUILayout.Space(4);
            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                fixedHeight = 32
            };

            GUI.backgroundColor = new Color(0.18f, 0.60f, 0.90f);
            if (GUILayout.Button("🚀  OPEN BUILD PIPELINE WINDOW", btnStyle))
            {
                BuildPipelineWindow.ShowWindow();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("⚙️ Ping ProjectBuildConfig", EditorStyles.miniButtonLeft, GUILayout.Height(22)))
            {
                if (_linkedBuildConfig != null)
                {
                    Selection.activeObject = _linkedBuildConfig;
                    EditorGUIUtility.PingObject(_linkedBuildConfig);
                }
                else
                {
                    LegacyGameConfigMigrator.MigrateOrCreate();
                }
            }

            if (GUILayout.Button("📖 Guide & Docs", EditorStyles.miniButtonMid, GUILayout.Height(22)))
            {
                BuildPipelineGuideWindow.Open();
            }

            if (GUILayout.Button("🔄 Check Updates", EditorStyles.miniButtonRight, GUILayout.Height(22)))
            {
                UpdateChecker.CheckForUpdate(force: true);
            }

            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Version Manager
        private void DrawVersionManager(GameConfig config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var ver = config.GameVersion;
            var date = config.VersionDate;

            EditorGUILayout.BeginHorizontal();
            var verLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.2f, 0.8f, 0.9f) }
            };
            EditorGUILayout.LabelField($"Version: v{ver.GameVersionAsTextWithBetaLabel}", verLabelStyle);
            EditorGUILayout.LabelField($"Build Date: {date.AsText}", EditorStyles.miniLabel, GUILayout.Width(130));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"+ Major ({ver.Major})", EditorStyles.miniButtonLeft))
            {
                Undo.RecordObject(config, "Increment Major Version");
                ver.Major++;
                ver.Minor = 0;
                ver.Build = 0;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button($"+ Minor ({ver.Minor})", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(config, "Increment Minor Version");
                ver.Minor++;
                ver.Build = 0;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button($"+ Build ({ver.Build})", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(config, "Increment Build Number");
                ver.Build++;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button("Today", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(config, "Set Date to Today");
                var now = DateTime.Now;
                date.Day = now.Day;
                date.Month = now.Month;
                date.Year = now.Year;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button("Reset Build", EditorStyles.miniButtonRight))
            {
                Undo.RecordObject(config, "Reset Build");
                ver.Build = 0;
                EditorUtility.SetDirty(config);
            }
            EditorGUILayout.EndHorizontal();

            // Direct inline numeric fields
            EditorGUILayout.BeginHorizontal();
            EditorGUIUtility.labelWidth = 40;
            ver.Major = EditorGUILayout.IntField("Major", ver.Major);
            ver.Minor = EditorGUILayout.IntField("Minor", ver.Minor);
            ver.Build = EditorGUILayout.IntField("Build", ver.Build);
            EditorGUIUtility.labelWidth = 35;
            date.Day = EditorGUILayout.IntField("Day", date.Day);
            date.Month = EditorGUILayout.IntField("Mon", date.Month);
            date.Year = EditorGUILayout.IntField("Year", date.Year);
            EditorGUIUtility.labelWidth = 0;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Core Targets & Mode
        private void DrawCoreTargetsSection(GameConfig config)
        {
            _foldTargets = DrawSectionHeader("🎯 1. Core Build & Target Settings", _foldTargets, "GameConfig_FoldTargets");
            if (!_foldTargets) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.BuildFolderName = EditorGUILayout.TextField("Build Folder Name", config.BuildFolderName);
            config.Publisher = (Publisher)EditorGUILayout.EnumPopup("Active Publisher", config.Publisher);
            config.GameLanguage = (GameLanguage)EditorGUILayout.EnumPopup("Active Language", config.GameLanguage);

            EditorGUILayout.Space(4);

            config.FullGame = EditorGUILayout.Toggle("Full Game", config.FullGame);
            config.Demo = EditorGUILayout.Toggle("Demo Mode", config.Demo);
            config.UseAchievements = EditorGUILayout.Toggle("Use Achievements", config.UseAchievements);

            EditorGUILayout.Space(4);

            config.CheatMode = EditorGUILayout.Toggle("Cheat Mode", config.CheatMode);
            if (config.CheatMode)
            {
                EditorGUILayout.HelpBox("⚠️ CHEAT MODE IS ENABLED! Cheat features and debug shortcuts will be included in the build.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Android Keystore & Remote Vault
        private void DrawRemoteVaultSection(GameConfig config)
        {
            _foldVault = DrawSectionHeader("🔐 2. Android Keystore & Credentials Vault", _foldVault, "GameConfig_FoldVault");
            if (!_foldVault) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_linkedBuildConfig == null)
            {
                EditorGUILayout.HelpBox("ProjectBuildConfig is not linked. Click below to initialize.", MessageType.Warning);
                if (GUILayout.Button("Create ProjectBuildConfig Now"))
                {
                    LegacyGameConfigMigrator.MigrateOrCreate();
                    _linkedBuildConfig = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();
                }
                EditorGUILayout.EndVertical();
                return;
            }

            _linkedBuildConfig.keystoreSource = (KeystoreSource)EditorGUILayout.EnumPopup("Keystore Mode", _linkedBuildConfig.keystoreSource);

            if (_linkedBuildConfig.keystoreSource == KeystoreSource.RemoteVault)
            {
                EditorGUILayout.HelpBox("🌐 Remote Vault Mode: Keeps credentials completely out of Git. Keystores and passwords are downloaded in memory during build and wiped immediately after.", MessageType.Info);

                _linkedBuildConfig.vaultUrl = EditorGUILayout.TextField("Vault API URL", _linkedBuildConfig.vaultUrl);

                EditorGUILayout.BeginHorizontal();
                _linkedBuildConfig.vaultProfileId = EditorGUILayout.TextField("Profile ID", _linkedBuildConfig.vaultProfileId);
                if (GUILayout.Button("Auto-Detect", GUILayout.Width(85)))
                {
                    _linkedBuildConfig.vaultProfileId = "";
                    EditorUtility.SetDirty(_linkedBuildConfig);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (_showVaultToken)
                    _linkedBuildConfig.vaultTokenFallback = EditorGUILayout.TextField("Secret Token", _linkedBuildConfig.vaultTokenFallback);
                else
                    _linkedBuildConfig.vaultTokenFallback = EditorGUILayout.PasswordField("Secret Token", _linkedBuildConfig.vaultTokenFallback);

                if (GUILayout.Button(_showVaultToken ? "Hide" : "Show", GUILayout.Width(50)))
                    _showVaultToken = !_showVaultToken;
                EditorGUILayout.EndHorizontal();

                _linkedBuildConfig.vaultTokenEnvVar = EditorGUILayout.TextField("Token Env Variable", _linkedBuildConfig.vaultTokenEnvVar);

                EditorGUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("🔌 Test Vault Connection & Cache", GUILayout.Height(24)))
                {
                    var token = KeystoreVaultClient.GetEffectiveToken(_linkedBuildConfig);
                    var res = KeystoreVaultClient.FetchCredentialsSync(_linkedBuildConfig.vaultUrl, _linkedBuildConfig.vaultProfileId, config.DefaultBundleIdentifier, token);
                    if (res.Success && res.Credentials != null)
                    {
                        _vaultTestStatus = $"✓ Connected! Keystore cached at: {res.Credentials.KeystorePath}\nAlias: {res.Credentials.KeyAliasName}";
                        EditorUtility.DisplayDialog("Vault Connection Succeeded", _vaultTestStatus, "OK");
                    }
                    else
                    {
                        _vaultTestStatus = $"✗ Failed: {res.Message}";
                        EditorUtility.DisplayDialog("Vault Connection Failed", _vaultTestStatus, "OK");
                    }
                }

                if (GUILayout.Button("🌐 Open Web Admin Portal", GUILayout.Height(24), GUILayout.Width(170)))
                {
                    Application.OpenURL("https://wagenheimer.com/admin/keystores");
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_vaultTestStatus))
                {
                    EditorGUILayout.HelpBox(_vaultTestStatus, _vaultTestStatus.StartsWith("✓") ? MessageType.Info : MessageType.Error);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("💾 Local Disk Mode: Keystore file loaded directly from your machine.", MessageType.None);
                _linkedBuildConfig.androidKeystorePath = EditorGUILayout.TextField("Keystore Path", _linkedBuildConfig.androidKeystorePath);
                _linkedBuildConfig.androidKeyAlias = EditorGUILayout.TextField("Key Alias", _linkedBuildConfig.androidKeyAlias);
                _linkedBuildConfig.androidKeystorePassFallback = EditorGUILayout.PasswordField("Keystore Password", _linkedBuildConfig.androidKeystorePassFallback);
                _linkedBuildConfig.androidKeyaliasPassFallback = EditorGUILayout.PasswordField("Alias Password", _linkedBuildConfig.androidKeyaliasPassFallback);
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(_linkedBuildConfig);
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Visual Assets
        private void DrawVisualAssetsSection(GameConfig config)
        {
            _foldAssets = DrawSectionHeader("🎨 3. Visual Assets & Sprite Atlases", _foldAssets, "GameConfig_FoldAssets");
            if (!_foldAssets) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.cursorTexture = (Texture2D)EditorGUILayout.ObjectField("Custom Cursor", config.cursorTexture, typeof(Texture2D), false);
            config.HudSpriteAtlas = (UnityEngine.U2D.SpriteAtlas)EditorGUILayout.ObjectField("HUD Sprite Atlas", config.HudSpriteAtlas, typeof(UnityEngine.U2D.SpriteAtlas), false);
            config.LevelHudSpriteAtlas = (UnityEngine.U2D.SpriteAtlas)EditorGUILayout.ObjectField("Level HUD Atlas", config.LevelHudSpriteAtlas, typeof(UnityEngine.U2D.SpriteAtlas), false);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("App Icons (Mobile Stores):", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            config.IconFree = (Texture2D)EditorGUILayout.ObjectField("Icon Free", config.IconFree, typeof(Texture2D), false);
            config.IconFull = (Texture2D)EditorGUILayout.ObjectField("Icon Full", config.IconFull, typeof(Texture2D), false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Game Identity
        private void DrawIdentitySection(GameConfig config)
        {
            _foldIdentity = DrawSectionHeader("🏷️ 4. Game Identity & Localized Titles", _foldIdentity, "GameConfig_FoldIdentity");
            if (!_foldIdentity) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.GameName = EditorGUILayout.TextField("Primary Game Name", config.GameName);
            config.DefaultBundleIdentifier = EditorGUILayout.TextField("Default Bundle ID", config.DefaultBundleIdentifier);

            if (string.IsNullOrEmpty(config.DefaultBundleIdentifier))
            {
                EditorGUILayout.HelpBox("⚠️ Default Bundle Identifier is empty (e.g. com.company.gamename)", MessageType.Warning);
            }

            config.iOSGameName = EditorGUILayout.TextField("iOS Game Name", config.iOSGameName);
            config.GameNameJapanese = EditorGUILayout.TextField("Japanese Title", config.GameNameJapanese);
            config.GameNameIOSAndroid = EditorGUILayout.TextField("iOS / Android Short Title", config.GameNameIOSAndroid);
            config.GameNameWSA = EditorGUILayout.TextField("Windows Store (WSA) Name", config.GameNameWSA);

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Store Identifiers
        private void DrawStoreIdentifiersSection(GameConfig config)
        {
            _foldStores = DrawSectionHeader("📱 5. Store Package Identifiers", _foldStores, "GameConfig_FoldStores");
            if (!_foldStores) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (GUILayout.Button("Sync All Store IDs from Default Bundle ID", GUILayout.Height(22)))
            {
                Undo.RecordObject(config, "Sync Store IDs");
                var baseId = config.DefaultBundleIdentifier;
                if (!string.IsNullOrEmpty(baseId))
                {
                    config.AndroidFull = $"{baseId}full";
                    config.AndroidFree = baseId;
                    config.IOSFull = $"{baseId}full";
                    config.IOSFree = baseId;
                    config.AmazonFull = $"{baseId}amazonfull";
                    config.AmazonFree = $"{baseId}amazon";
                    EditorUtility.SetDirty(config);
                }
            }

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Google Play Store", EditorStyles.boldLabel);
            config.AndroidFull = EditorGUILayout.TextField("  Android Full (.aab)", config.AndroidFull);
            config.AndroidFree = EditorGUILayout.TextField("  Android Free (.apk/.aab)", config.AndroidFree);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Apple iOS App Store", EditorStyles.boldLabel);
            config.IOSFull = EditorGUILayout.TextField("  iOS Full Bundle ID", config.IOSFull);
            config.IOSFree = EditorGUILayout.TextField("  iOS Free Bundle ID", config.IOSFree);
            config.iOSAppIDFull = EditorGUILayout.TextField("  Apple App ID (Full)", config.iOSAppIDFull);
            config.iOSAppIDFree = EditorGUILayout.TextField("  Apple App ID (Free)", config.iOSAppIDFree);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Amazon Appstore", EditorStyles.boldLabel);
            config.AmazonFull = EditorGUILayout.TextField("  Amazon Full", config.AmazonFull);
            config.AmazonFree = EditorGUILayout.TextField("  Amazon Free", config.AmazonFree);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Samsung Galaxy Store", EditorStyles.boldLabel);
            config.SamsungFull = EditorGUILayout.TextField("  Samsung Full", config.SamsungFull);
            config.SamsungFree = EditorGUILayout.TextField("  Samsung Free", config.SamsungFree);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Mac App Store", EditorStyles.boldLabel);
            config.MacAppStoreID = EditorGUILayout.TextField("  Mac App Store ID", config.MacAppStoreID);

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Extra Flags
        private void DrawExtraFlagsSection(GameConfig config)
        {
            _foldExtra = DrawSectionHeader("⚙️ 6. Extra Flags & Feature Toggles", _foldExtra, "GameConfig_FoldExtra");
            if (!_foldExtra) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.FreeToPlay = EditorGUILayout.Toggle("Free To Play (F2P)", config.FreeToPlay);
            config.NoCustomCursor = EditorGUILayout.Toggle("No Custom Cursor", config.NoCustomCursor);
            config.LogLevelsInfo = EditorGUILayout.Toggle("Log Levels Info", config.LogLevelsInfo);
            config.LevelEditor = EditorGUILayout.Toggle("Enable Level Editor", config.LevelEditor);
            config.ExternalTranslation = EditorGUILayout.Toggle("External Translation", config.ExternalTranslation);

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Footer
        private void DrawFooter(GameConfig config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var isPro = EditorGUIUtility.isProSkin;

            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isPro ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.4f, 0.4f, 0.4f) }
            };

            var assetPath = AssetDatabase.GetAssetPath(config);
            EditorGUILayout.LabelField($"Asset: {assetPath}", style);
            EditorGUILayout.LabelField("Legacy GUID Preserved: 31cdb8337559cda4c91e423a77f466e1", style);

            if (_linkedBuildConfig != null)
            {
                if (GUILayout.Button("💾 Push & Synchronize to ProjectBuildConfig.asset", GUILayout.Height(24)))
                {
                    LegacyGameConfigMigrator.MigrateOrCreate();
                }
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Helpers
        private bool DrawSectionHeader(string title, bool isExpanded, string prefKey)
        {
            var isPro = EditorGUIUtility.isProSkin;
            var headerColor = isPro ? new Color(0.18f, 0.22f, 0.28f) : new Color(0.85f, 0.88f, 0.92f);
            var rect = GUILayoutUtility.GetRect(18, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, headerColor);

            var foldStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                normal = { textColor = isPro ? Color.white : Color.black },
                onNormal = { textColor = isPro ? Color.white : Color.black }
            };

            var newExpanded = EditorGUI.Foldout(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4), isExpanded, title, true, foldStyle);
            if (newExpanded != isExpanded)
            {
                EditorPrefs.SetBool(prefKey, newExpanded);
            }
            return newExpanded;
        }
        #endregion
    }
}
