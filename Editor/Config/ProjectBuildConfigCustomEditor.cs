using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(ProjectBuildConfig))]
    public class ProjectBuildConfigCustomEditor : UnityEditor.Editor
    {
        private static bool _foldVault = true;
        private static bool _foldPublishers = true;
        private static bool _foldLanguages = true;
        private static bool _foldPaths = false;

        private static bool _showVaultToken = false;
        private static bool _showLocalPass = false;
        private string _vaultStatus = "";
        private string _localStatus = "";

        private void OnEnable()
        {
            _foldVault = EditorPrefs.GetBool("ProjectBuildConfig_FoldVault", true);
            _foldPublishers = EditorPrefs.GetBool("ProjectBuildConfig_FoldPublishers", true);
            _foldLanguages = EditorPrefs.GetBool("ProjectBuildConfig_FoldLanguages", true);
            _foldPaths = EditorPrefs.GetBool("ProjectBuildConfig_FoldPaths", false);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var config = (ProjectBuildConfig)target;

            // 1. Header Banner
            DrawHeader(config);

            // 2. Android Keystore & Remote Vault (Categorized & with Test Buttons!)
            DrawKeystoreVaultSection(config);
            EditorGUILayout.Space(6);

            // 3. Publishers & Lojas (Human-readable cards instead of Element 0..17)
            DrawPublishersSection(config);
            EditorGUILayout.Space(6);

            // 4. Languages Matrix (Clean grid of language toggles)
            DrawLanguagesSection(config);
            EditorGUILayout.Space(6);

            // 5. Output Paths & Scenes
            DrawPathsAndScenesSection(config);

            serializedObject.ApplyModifiedProperties();
        }

        #region Header
        private void DrawHeader(ProjectBuildConfig config)
        {
            var isPro = EditorGUIUtility.isProSkin;
            var bgColor = isPro ? new Color(0.12f, 0.14f, 0.18f) : new Color(0.88f, 0.90f, 0.94f);

            var rect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.DrawRect(rect, bgColor);

            GUILayout.Space(6);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isPro ? new Color(0.25f, 0.85f, 0.95f) : new Color(0.05f, 0.45f, 0.70f) }
            };
            var projTitle = !string.IsNullOrEmpty(config.projectName) ? config.projectName : "PROJECT BUILD PIPELINE";
            EditorGUILayout.LabelField(projTitle.ToUpperInvariant(), titleStyle);

            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isPro ? new Color(0.65f, 0.70f, 0.75f) : new Color(0.35f, 0.40f, 0.45f) }
            };
            var linkedName = config.gameConfig != null ? config.gameConfig.name : "Nenhum (Clique em Migrar)";
            EditorGUILayout.LabelField($"CONFIGURAÇÃO GERAL DE BUILDS  •  GameConfig: {linkedName}", subStyle);

            GUILayout.Space(6);

            // Open Build Pipeline Button
            GUI.backgroundColor = new Color(0.18f, 0.60f, 0.90f);
            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                fixedHeight = 32
            };
            if (GUILayout.Button("🚀  ABRIR JANELA DE BUILDS (BUILD PIPELINE WINDOW)", btnStyle))
            {
                Debug.Log("[BuildPipeline] 'ABRIR JANELA DE BUILDS' button clicked from ProjectBuildConfig Inspector.");
                BuildPipelineWindow.ShowWindow();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }
        #endregion

        #region Section: Keystore Vault
        private void DrawKeystoreVaultSection(ProjectBuildConfig config)
        {
            _foldVault = DrawSectionHeader("🔐 1. Android Keystore & Central de Credenciais (Vault)", _foldVault, "ProjectBuildConfig_FoldVault");
            if (!_foldVault) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.keystoreSource = (KeystoreSource)EditorGUILayout.EnumPopup("Modo de Keystore", config.keystoreSource);

            if (config.keystoreSource == KeystoreSource.RemoteVault)
            {
                EditorGUILayout.HelpBox(
                    "🌐 MODO REMOTE VAULT ATIVO:\n" +
                    "Nenhum certificado ou senha é versionado no Git.\n" +
                    "O pipeline consulta o servidor seguro via HTTPS Bearer token, baixa o .keystore em memória no cache temporário local (Library/KeystoreCache/) e apaga as senhas do PlayerSettings.Android imediatamente após o build.",
                    MessageType.Info);

                EditorGUILayout.Space(4);
                config.vaultUrl = EditorGUILayout.TextField("URL da API do Vault", config.vaultUrl);

                EditorGUILayout.BeginHorizontal();
                config.vaultProfileId = EditorGUILayout.TextField("ID do Perfil no Vault", config.vaultProfileId);
                if (GUILayout.Button("Auto-Detectar", GUILayout.Width(95)))
                {
                    config.vaultProfileId = "";
                    EditorUtility.SetDirty(config);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (_showVaultToken)
                    config.vaultTokenFallback = EditorGUILayout.TextField("Token de Acesso / API Key", config.vaultTokenFallback);
                else
                    config.vaultTokenFallback = EditorGUILayout.PasswordField("Token de Acesso / API Key", config.vaultTokenFallback);

                if (GUILayout.Button(_showVaultToken ? "Ocultar" : "Mostrar", GUILayout.Width(65)))
                    _showVaultToken = !_showVaultToken;
                EditorGUILayout.EndHorizontal();

                config.vaultTokenEnvVar = EditorGUILayout.TextField("Variável de Ambiente (CI/CD)", config.vaultTokenEnvVar);

                EditorGUILayout.Space(6);

                // Validation Button for Web Vault
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
                if (GUILayout.Button("🔌 Testar Conexão com o Vault & Baixar Keystore", GUILayout.Height(28)))
                {
                    var token = KeystoreVaultClient.GetEffectiveToken(config);
                    var bundle = config.gameConfig != null ? config.gameConfig.DefaultBundleIdentifier : config.defaultBundleIdentifier;
                    var res = KeystoreVaultClient.FetchCredentialsSync(config.vaultUrl, config.vaultProfileId, bundle, token);
                    if (res.Success && res.Credentials != null)
                    {
                        _vaultStatus = $"✓ Conexão bem-sucedida com o Vault!\nCertificado: {res.Credentials.KeystoreFileName}\nSalvo em cache: {res.Credentials.KeystorePath}\nAlias: {res.Credentials.KeyAliasName}";
                        EditorUtility.DisplayDialog("Teste do Vault: Sucesso!", _vaultStatus, "OK");
                    }
                    else
                    {
                        _vaultStatus = $"✗ Falha ao consultar o Vault:\n{res.Message}";
                        EditorUtility.DisplayDialog("Teste do Vault: Erro", _vaultStatus, "OK");
                    }
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("🌐 Abrir Painel Web (/admin/keystores)", GUILayout.Height(28), GUILayout.Width(220)))
                {
                    Application.OpenURL("https://wagenheimer.com/admin/keystores");
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_vaultStatus))
                {
                    EditorGUILayout.HelpBox(_vaultStatus, _vaultStatus.StartsWith("✓") ? MessageType.Info : MessageType.Error);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("💾 MODO LOCAL DISK ATIVO: Utiliza arquivo de keystore físico armazenado em seu computador.", MessageType.None);

                EditorGUILayout.BeginHorizontal();
                config.androidKeystorePath = EditorGUILayout.TextField("Caminho do Keystore (Win)", config.androidKeystorePath);
                if (GUILayout.Button("Procurar...", GUILayout.Width(75)))
                {
                    var file = EditorUtility.OpenFilePanel("Selecione o arquivo .keystore", Path.GetDirectoryName(config.androidKeystorePath), "keystore,jks");
                    if (!string.IsNullOrEmpty(file))
                    {
                        config.androidKeystorePath = file;
                        EditorUtility.SetDirty(config);
                    }
                }
                EditorGUILayout.EndHorizontal();

                config.androidKeyAlias = EditorGUILayout.TextField("Nome do Key Alias", config.androidKeyAlias);

                EditorGUILayout.BeginHorizontal();
                if (_showLocalPass)
                {
                    config.androidKeystorePassFallback = EditorGUILayout.TextField("Senha do Keystore", config.androidKeystorePassFallback);
                    config.androidKeyaliasPassFallback = EditorGUILayout.TextField("Senha do Alias", config.androidKeyaliasPassFallback);
                }
                else
                {
                    config.androidKeystorePassFallback = EditorGUILayout.PasswordField("Senha do Keystore", config.androidKeystorePassFallback);
                    config.androidKeyaliasPassFallback = EditorGUILayout.PasswordField("Senha do Alias", config.androidKeyaliasPassFallback);
                }
                if (GUILayout.Button(_showLocalPass ? "Ocultar" : "Mostrar", GUILayout.Width(65)))
                    _showLocalPass = !_showLocalPass;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(6);

                // Validation Button for Local Disk
                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.85f);
                if (GUILayout.Button("🔍 Validar Arquivo Keystore Local (Integridade e Senhas)", GUILayout.Height(28)))
                {
                    var val = KeystoreVaultClient.ValidateLocalKeystore(
                        config.GetEffectiveKeystorePath(),
                        config.androidKeyAlias,
                        config.GetEffectiveKeystorePassword(),
                        config.GetEffectiveKeyaliasPassword());

                    if (val.Success)
                    {
                        _localStatus = $"✓ Keystore Local Válido!\n{val.Message}";
                        EditorUtility.DisplayDialog("Validação Local: Sucesso!", _localStatus, "OK");
                    }
                    else
                    {
                        _localStatus = $"✗ Erro na validação local:\n{val.Message}";
                        EditorUtility.DisplayDialog("Validação Local: Erro", _localStatus, "OK");
                    }
                }
                GUI.backgroundColor = Color.white;

                if (!string.IsNullOrEmpty(_localStatus))
                {
                    EditorGUILayout.HelpBox(_localStatus, _localStatus.StartsWith("✓") ? MessageType.Info : MessageType.Error);
                }
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Publishers
        private void DrawPublishersSection(ProjectBuildConfig config)
        {
            var count = config.publishers != null ? config.publishers.Count : 0;
            _foldPublishers = DrawSectionHeader($"🏬 2. Publicadoras & Lojas ({count} configuradas)", _foldPublishers, "ProjectBuildConfig_FoldPublishers");
            if (!_foldPublishers) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.HelpBox(
                "💡 PARA QUE SERVE ESTA SEÇÃO?\n" +
                "Cada loja (Steam, Big Fish, Google Play, etc.) possui requisitos específicos: pasta de saída, logos splash de distribuidora e criação automática de arquivos .ZIP.\n" +
                "O pipeline usa esses perfis para gerar automaticamente os builds corretos no 'Quick Build' e no 'Matrix Batch Builder'.",
                MessageType.Info);

            if (config.publishers == null || config.publishers.Count == 0)
            {
                if (GUILayout.Button("Restaurar Lista Padrão de Publicadoras", GUILayout.Height(26)))
                {
                    config.PopulateDefaults();
                    EditorUtility.SetDirty(config);
                }
                EditorGUILayout.EndVertical();
                return;
            }

            // Desktop Windows Stores
            DrawPublisherGroup("🖥️ Lojas Desktop (Windows)", config, PlatformType.Windows64);
            EditorGUILayout.Space(4);

            // Mobile Stores
            DrawPublisherGroup("📱 Lojas Mobile (Android & iOS)", config, PlatformType.Android, PlatformType.iOS);
            EditorGUILayout.Space(4);

            // Mac Stores
            DrawPublisherGroup("🍏 Lojas Mac (macOS)", config, PlatformType.macOS);

            EditorGUILayout.EndVertical();
        }

        private void DrawPublisherGroup(string headerTitle, ProjectBuildConfig config, params PlatformType[] platforms)
        {
            var list = config.publishers.Where(p => platforms.Contains(p.platform)).ToList();
            if (list.Count == 0) return;

            var isPro = EditorGUIUtility.isProSkin;
            var headerBg = isPro ? new Color(0.16f, 0.18f, 0.22f) : new Color(0.85f, 0.87f, 0.90f);

            var rect = GUILayoutUtility.GetRect(18, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, headerBg);
            GUI.Label(new Rect(rect.x + 6, rect.y + 2, rect.width, rect.height), headerTitle, EditorStyles.boldLabel);

            foreach (var pub in list)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                var title = !string.IsNullOrEmpty(pub.displayName) ? pub.displayName : pub.publisher.ToString();
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(170));

                pub.requiresSplash = EditorGUILayout.ToggleLeft("Splash Logo", pub.requiresSplash, GUILayout.Width(95));
                pub.zipAfterBuild = EditorGUILayout.ToggleLeft("Gerar ZIP", pub.zipAfterBuild, GUILayout.Width(85));
                pub.isFullGame = EditorGUILayout.ToggleLeft("Full", pub.isFullGame, GUILayout.Width(50));

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUIUtility.labelWidth = 100;
                pub.outputSubfolder = EditorGUILayout.TextField("Pasta de Saída", pub.outputSubfolder);
                if (pub.platform == PlatformType.Android || pub.platform == PlatformType.iOS)
                {
                    EditorGUIUtility.labelWidth = 70;
                    pub.bundleIdentifier = EditorGUILayout.TextField("Bundle ID", pub.bundleIdentifier);
                }
                EditorGUIUtility.labelWidth = 0;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }
        }
        #endregion

        #region Section: Languages
        private void DrawLanguagesSection(ProjectBuildConfig config)
        {
            var enabledCount = config.languages != null ? config.languages.Count(l => l.enabled) : 0;
            var totalCount = config.languages != null ? config.languages.Count : 0;
            _foldLanguages = DrawSectionHeader($"🌐 3. Matriz de Idiomas ({enabledCount}/{totalCount} ativos)", _foldLanguages, "ProjectBuildConfig_FoldLanguages");
            if (!_foldLanguages) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.HelpBox(
                "💡 PARA QUE SERVE ESTA SEÇÃO?\n" +
                "Define quais idiomas estão habilitados para este jogo.\n" +
                "O 'Matrix Batch Builder' usará os idiomas com checkbox marcado como 'Ativo' para compilar automaticamente todos os executáveis localizados.",
                MessageType.Info);

            if (config.languages == null || config.languages.Count == 0)
            {
                if (GUILayout.Button("Restaurar Idiomas Padrão", GUILayout.Height(26)))
                {
                    config.PopulateDefaults();
                    EditorUtility.SetDirty(config);
                }
                EditorGUILayout.EndVertical();
                return;
            }

            // Quick Selection Toolbar
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Marcar Todos", EditorStyles.miniButtonLeft))
            {
                foreach (var l in config.languages) l.enabled = true;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button("Desmarcar Todos", EditorStyles.miniButtonMid))
            {
                foreach (var l in config.languages) l.enabled = false;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button("Apenas EFIGS (EN, FR, IT, DE, ES)", EditorStyles.miniButtonRight))
            {
                var efigs = new[] { GameLanguage.English, GameLanguage.French, GameLanguage.Italian, GameLanguage.German, GameLanguage.Spanish };
                foreach (var l in config.languages)
                    l.enabled = efigs.Contains(l.language);
                EditorUtility.SetDirty(config);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // 2-Column Grid of Languages
            for (var i = 0; i < config.languages.Count; i += 2)
            {
                EditorGUILayout.BeginHorizontal();
                var l1 = config.languages[i];
                l1.enabled = EditorGUILayout.ToggleLeft($"{l1.Name} ({l1.language})", l1.enabled, GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.45f));

                if (i + 1 < config.languages.Count)
                {
                    var l2 = config.languages[i + 1];
                    l2.enabled = EditorGUILayout.ToggleLeft($"{l2.Name} ({l2.language})", l2.enabled);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Paths & Scenes
        private void DrawPathsAndScenesSection(ProjectBuildConfig config)
        {
            _foldPaths = DrawSectionHeader("📁 4. Pastas de Saída, Cenas & Compactação", _foldPaths, "ProjectBuildConfig_FoldPaths");
            if (!_foldPaths) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.projectName = EditorGUILayout.TextField("Nome do Projeto", config.projectName);
            config.buildOutputRoot = EditorGUILayout.TextField("Pasta Raiz de Builds (Windows)", config.buildOutputRoot);
            config.macBuildOutputRoot = EditorGUILayout.TextField("Pasta Raiz de Builds (macOS)", config.macBuildOutputRoot);
            config.splashMasterFolder = EditorGUILayout.TextField("Pasta Mestre de Logos Splash", config.splashMasterFolder);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Cenas Utilizadas no Build:", EditorStyles.boldLabel);
            config.publisherSplashScenePath = EditorGUILayout.TextField("  Cena Publisher Splash", config.publisherSplashScenePath);
            config.defaultSplashScenePath = EditorGUILayout.TextField("  Cena Splash Padrão", config.defaultSplashScenePath);
            config.levelEditorScenePath = EditorGUILayout.TextField("  Cena Level Editor", config.levelEditorScenePath);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Compactação e Arquivamento:", EditorStyles.boldLabel);
            config.enableZipArchiving = EditorGUILayout.Toggle("Habilitar Compactação ZIP", config.enableZipArchiving);
            config.useExternalSevenZip = EditorGUILayout.Toggle("Usar 7-Zip Externo", config.useExternalSevenZip);
            if (config.useExternalSevenZip)
            {
                config.sevenZipExecutable = EditorGUILayout.TextField("Executável do 7-Zip", config.sevenZipExecutable);
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
