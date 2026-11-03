using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Default language, matrix languages and which profiles are locked.</summary>
    internal sealed class BuildPipelineLanguagesSection
    {
        public VisualElement Root { get; } = new VisualElement();

        public BuildPipelineLanguagesSection(BuildPipelineSectionContext ctx)
        {
            var config = ctx.Config;
            var so = new SerializedObject(config);

            var defaults = BuildPipelineUIStyle.CreateCard("🌐 Idioma padrão",
                "Usado por perfis em modo Auto quando o build não escolhe outro. AutoDetect = o jogo segue o idioma do aparelho/sistema.");
            defaults.Add(new PropertyField(so.FindProperty("defaultLanguage"), "Padrão do projeto"));
            Root.Add(defaults);

            var matrix = BuildPipelineUIStyle.CreateCard("📦 Idiomas da matriz",
                "Os idiomas que aparecem para o build em lote (e o -matrixLanguages padrão do CLI). 'enabled' = vem marcado.");
            matrix.Add(new PropertyField(so.FindProperty("languages"), "Idiomas"));
            matrix.Add(BuildPipelineBuildSection.SmallButton("↺ Restaurar lista padrão", () =>
            {
                if (!EditorUtility.DisplayDialog("Restaurar idiomas", "Trocar a lista pelos idiomas padrão?", "Restaurar", "Cancelar"))
                    return;
                config.languages = LanguageProfile.GetDefaultLanguages();
                ctx.SaveConfig(false);
            }));
            Root.Add(matrix);

            var locked = BuildPipelineUIStyle.CreateCard("🔒 Perfis com idioma travado",
                "Travar = build sempre nesse idioma, pasta com sufixo (_DE) e o jogo esconde o seletor (GameConfig.IsLanguageLocked). " +
                "Para um pedido pontual ('manda um build em alemão') use o idioma da seção Build, sem mudar o perfil.");
            var lockedProfiles = config.publishers.Where(p => p != null && p.languageMode == ProfileLanguageMode.Locked).ToList();
            if (lockedProfiles.Count == 0)
                locked.Add(new Label("Nenhum. Todos seguem o padrão do projeto.") { style = { color = new Color(0.62f, 0.65f, 0.71f) } });
            foreach (var profile in lockedProfiles)
            {
                var row = new VisualElement();
                row.AddToClassList("bp-usage-row");
                var link = new Button(() => ctx.Navigate(BuildPipelineSections.Profiles, profile.EffectiveId)) { text = $"{profile.displayName} ({profile.EffectiveId})" };
                link.AddToClassList("bp-link");
                link.style.width = 300;
                row.Add(link);
                row.Add(BuildPipelineUIStyle.CreateText(profile.IsLanguageLocked ? $"🔒 {profile.lockedLanguage}" : "⚠ Travado em AutoDetect (sem efeito)"));
                locked.Add(row);
            }

            Root.Add(locked);
            Root.Bind(so);
        }
    }

    /// <summary>GameConfig link, game-specific fields and variants, paths, scenes and archiving.</summary>
    internal sealed class BuildPipelineProjectSection
    {
        public VisualElement Root { get; } = new VisualElement();

        public BuildPipelineProjectSection(BuildPipelineSectionContext ctx)
        {
            var config = ctx.Config;
            var so = new SerializedObject(config);

            Root.Add(BuildPipelineWindow.MigrationCard(config, section => ctx.Navigate(section), ctx.RebuildAll));

            var identity = BuildPipelineUIStyle.CreateCard("🪪 Projeto", "Vínculo com o GameConfig (verdade de runtime) e nomes.");
            identity.Add(new PropertyField(so.FindProperty("gameConfig"), "GameConfig.asset"));
            identity.Add(new PropertyField(so.FindProperty("projectName"), "Nome do projeto"));
            identity.Add(new PropertyField(so.FindProperty("defaultBundleIdentifier"), "Bundle id padrão"));
            identity.Add(new PropertyField(so.FindProperty("gameNameJapanese"), "Nome em japonês"));
            Root.Add(identity);

            var gameCard = GameSpecificCard(config);
            if (gameCard != null)
                Root.Add(gameCard);

            var paths = OutputRootsCard(ctx, so);
            Root.Add(paths);

            var scenes = BuildPipelineUIStyle.CreateCard("🎬 Splashes e cenas",
                "Cenas e pasta usadas pelo pipeline e pelo editor de níveis.");
            scenes.Add(new PropertyField(so.FindProperty("splashMasterFolder"), "Pasta mestre de splashes"));
            scenes.Add(new PropertyField(so.FindProperty("publisherSplashScenePath"), "Cena de splash do publisher"));
            scenes.Add(new PropertyField(so.FindProperty("defaultSplashScenePath"), "Cena de splash padrão"));
            scenes.Add(new PropertyField(so.FindProperty("levelEditorScenePath"), "Cena do editor de níveis"));
            Root.Add(scenes);

            var zip = BuildPipelineUIStyle.CreateCard("🗜 Compactação");
            zip.Add(new PropertyField(so.FindProperty("enableZipArchiving"), "Zipar builds"));
            zip.Add(new PropertyField(so.FindProperty("useExternalSevenZip"), "Usar 7-Zip externo"));
            zip.Add(new PropertyField(so.FindProperty("sevenZipExecutable"), "Executável do 7-Zip"));
            Root.Add(zip);

            Root.Bind(so);
        }

        private static VisualElement OutputRootsCard(BuildPipelineSectionContext ctx, SerializedObject so)
        {
            var config = ctx.Config;
            var card = BuildPipelineUIStyle.CreateCard("📁 Raízes de saída",
                "A raiz é a pasta “mãe” de todas as builds. A subpasta de cada perfil fica em Perfis → Saída do build.");
            card.Add(new PropertyField(so.FindProperty("buildOutputRoot"), "Raiz — builds feitas no Windows"));
            card.Add(new PropertyField(so.FindProperty("macBuildOutputRoot"), "Raiz — builds feitas no macOS"));

            var hint = new Label("A raiz usada depende do computador que executa o build, não da plataforma do perfil. " +
                                 "No macOS vale a raiz macOS; no Windows, a raiz Windows.")
            {
                style = { fontSize = 10, color = new Color(0.55f, 0.59f, 0.65f), whiteSpace = WhiteSpace.Normal, marginBottom = 6 }
            };
            card.Add(hint);

            var status = new VisualElement();
            void TestRoot(Func<string> root, string label)
            {
                so.ApplyModifiedProperties();
                string path = root();
                var check = BuildOutputFolderInspector.InspectRoot(path, prepareFolder: true);
                status.Clear();
                status.Add(BuildPipelineUIStyle.CreateCallout(
                    check.Summary + "\n" + string.Join("\n", check.Details),
                    BuildOutputFolderInspector.SeverityClass(check.Severity)));
                Debug.Log($"[BuildPipeline] Teste de raiz ({label}): {check.Summary}\n{string.Join("\n", check.Details)}");
            }

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
            actions.Add(BuildPipelineBuildSection.SmallButton("🧪 Testar raiz Windows", () => TestRoot(() => config.buildOutputRoot, "Windows")));
            actions.Add(BuildPipelineBuildSection.SmallButton("📂 Abrir raiz Windows", () =>
            {
                so.ApplyModifiedProperties();
                BuildOutputFolderActions.OpenPath(config.buildOutputRoot, "Abrir raiz de saída (Windows)");
            }));
            actions.Add(BuildPipelineBuildSection.SmallButton("🧪 Testar raiz macOS", () => TestRoot(() => config.macBuildOutputRoot, "macOS")));
            actions.Add(BuildPipelineBuildSection.SmallButton("📂 Abrir raiz macOS", () =>
            {
                so.ApplyModifiedProperties();
                BuildOutputFolderActions.OpenPath(config.macBuildOutputRoot, "Abrir raiz de saída (macOS)");
            }));
            card.Add(actions);
            card.Add(status);
            return card;
        }

        private static VisualElement GameSpecificCard(ProjectBuildConfig config)
        {
            var gameConfig = config.gameConfig;
            if (gameConfig == null)
                return BuildPipelineUIStyle.CreateCallout("Nenhum GameConfig vinculado: versão, idioma de runtime e variantes não são sincronizados.", "warn");

            var gameObject = new SerializedObject(gameConfig);
            var fields = GameConfigCustomEditor.BuildExtensionFields(gameObject);
            var variants = gameObject.FindProperty("buildVariants");

            var card = BuildPipelineUIStyle.CreateCard($"🎮 {gameConfig.name} — campos do jogo",
                "Campos da subclasse de GameConfig e as variantes (edições) que o build compila. Grava no GameConfig.asset.");
            if (fields != null) card.Add(fields);
            if (variants != null)
            {
                var field = new PropertyField(variants, "Variantes de build");
                field.BindProperty(variants);
                card.Add(field);
            }

            card.Add(BuildPipelineBuildSection.SmallButton("Abrir GameConfig.asset", () =>
            {
                Selection.activeObject = gameConfig;
                EditorGUIUtility.PingObject(gameConfig);
            }));
            card.Bind(gameObject);
            return card;
        }
    }

    /// <summary>Android signing: local keystore file or the AppDeployHub vault (same source as the Forge).</summary>
    internal sealed class BuildPipelineSigningSection
    {
        public VisualElement Root { get; } = new VisualElement();

        public BuildPipelineSigningSection(BuildPipelineSectionContext ctx)
        {
            var config = ctx.Config;
            var so = new SerializedObject(config);

            var mode = BuildPipelineUIStyle.CreateCard("🔐 Origem do keystore Android",
                "Vale para builds feitos NO EDITOR. O CI do Forge sempre injeta o keystore do cofre via -keystorePath (não depende disto).");
            mode.Add(new PropertyField(so.FindProperty("keystoreSource"), "Origem"));
            Root.Add(mode);

            if (config.keystoreSource == KeystoreSource.LocalDisk)
            {
                var local = BuildPipelineUIStyle.CreateCard("💾 Arquivo local");
                local.Add(new PropertyField(so.FindProperty("androidKeystorePath"), "Keystore (Windows)"));
                local.Add(new PropertyField(so.FindProperty("androidKeystorePathMac"), "Keystore (macOS)"));
                local.Add(new PropertyField(so.FindProperty("androidKeyAlias"), "Alias"));
                local.Add(new PropertyField(so.FindProperty("androidKeystorePassEnvVar"), "Variável da senha do keystore"));
                local.Add(new PropertyField(so.FindProperty("androidKeystorePassFallback"), "Senha do keystore (fallback)"));
                local.Add(new PropertyField(so.FindProperty("androidKeyaliasPassEnvVar"), "Variável da senha do alias"));
                local.Add(new PropertyField(so.FindProperty("androidKeyaliasPassFallback"), "Senha do alias (fallback)"));
                string path = config.GetEffectiveKeystorePath();
                local.Add(BuildPipelineUIStyle.CreateCallout(File.Exists(path) ? $"✔ Arquivo encontrado: {path}" : $"⚠ Arquivo não encontrado: {path}",
                    File.Exists(path) ? "ok" : "warn"));
                Root.Add(local);

                if (File.Exists(path))
                    Root.Add(MigrateToVaultCard(ctx, so, path));
            }
            else
            {
                Root.Add(VaultCard(ctx, so));
            }

            Root.Bind(so);
            Root.TrackPropertyValue(so.FindProperty("keystoreSource"), _ => ctx.RebuildSection());
        }

        /// <summary>
        /// Sends the local keystore (file + alias + passwords) to the AppDeployHub vault and switches this project to it,
        /// so the keystore and its passwords no longer have to live on each machine or in the asset.
        /// </summary>
        private static VisualElement MigrateToVaultCard(BuildPipelineSectionContext ctx, SerializedObject so, string keystorePath)
        {
            var config = ctx.Config;
            var card = BuildPipelineUIStyle.CreateCard("☁ Migrar para o cofre do AppDeployHub",
                "Envia este keystore (arquivo, alias e senhas) para o cofre — a mesma fonte que o Forge usa — e passa o projeto a ler de lá. " +
                "Se o cofre já tiver este keystore, a credencial existente é reutilizada (nada é duplicado). O arquivo local não é apagado.");

            card.Add(KeyRow());
            card.Add(new PropertyField(so.FindProperty("appDeployHubUrl"), "URL do AppDeployHub"));

            var status = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } };
            var migrate = BuildPipelineBuildSection.SmallButton("☁ Migrar para o cofre", async () =>
            {
                so.ApplyModifiedProperties();
                string alias = config.androidKeyAlias;
                string label = $"{(string.IsNullOrEmpty(config.projectName) ? Application.productName : config.projectName)} — Keystore ({alias})";
                if (!EditorUtility.DisplayDialog("Migrar keystore para o cofre",
                        $"Enviar para {config.appDeployHubUrl}:\n\n• Arquivo: {keystorePath}\n• Alias: {alias}\n• Senhas: as do keystore local\n\n" +
                        $"Nome no cofre: {label}\n\nDepois disso este projeto passa a assinar com a credencial do cofre.",
                        "Enviar", "Cancelar"))
                    return;

                status.text = "Enviando para o cofre…";
                var result = await AppDeployHubVaultClient.ImportKeystoreAsync(config.appDeployHubUrl, keystorePath, alias,
                    config.GetEffectiveKeystorePassword(), config.GetEffectiveKeyaliasPassword(), label);
                if (!result.Success)
                {
                    status.text = "✕ " + result.Message;
                    return;
                }

                // Proves the round trip before switching: the build will read exactly this.
                var check = await AppDeployHubVaultClient.FetchKeystoreAsync(config.appDeployHubUrl, result.CredentialId);
                if (!check.Success)
                {
                    status.text = $"✕ Enviado (credencial {result.CredentialId}), mas a leitura de volta falhou: {check.Message}. O projeto continua no keystore local.";
                    return;
                }

                config.keystoreSource = KeystoreSource.AppDeployHubVault;
                config.vaultKeystoreCredentialId = result.CredentialId;
                config.vaultKeystoreLabel = label;
                Debug.Log($"[BuildPipeline] Keystore migrado para o cofre do AppDeployHub: {label} ({result.CredentialId}). {result.Message}");
                ctx.SaveConfig(false);
            });
            migrate.AddToClassList("bp-btn--primary");
            card.Add(migrate);
            card.Add(status);
            return card;
        }

        /// <summary>The per-machine vault API key field (never stored in the asset).</summary>
        private static VisualElement KeyRow()
        {
            var keyRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var keyField = new TextField("API key (esta máquina)") { isPasswordField = true, value = AppDeployHubVaultClient.GetApiKey(), style = { flexGrow = 1 } };
            keyField.SetEnabled(!AppDeployHubVaultClient.KeyComesFromEnvironment);
            keyField.RegisterValueChangedCallback(e => AppDeployHubVaultClient.SaveApiKey(e.newValue));
            keyRow.Add(keyField);
            keyRow.Add(BuildPipelineBuildSection.SmallButton("Salvar", () => AppDeployHubVaultClient.SaveApiKey(keyField.value)));
            if (AppDeployHubVaultClient.KeyComesFromEnvironment)
                keyField.tooltip = $"Usando a variável {AppDeployHubVaultClient.KeyEnvVar}.";
            return keyRow;
        }

        private static VisualElement VaultCard(BuildPipelineSectionContext ctx, SerializedObject so)
        {
            var config = ctx.Config;
            var card = BuildPipelineUIStyle.CreateCard("🏦 Cofre do AppDeployHub",
                "O keystore e as senhas vêm do cofre na hora do build e são apagados do PlayerSettings depois. " +
                "A API key é do tipo 'vault' (AppDeployHub → Cofre → Conectar o Forge) e fica só nesta máquina.");

            card.Add(new PropertyField(so.FindProperty("appDeployHubUrl"), "URL do AppDeployHub"));
            card.Add(KeyRow());
            if (AppDeployHubVaultClient.KeyComesFromEnvironment)
                card.Add(new Label($"Usando a variável {AppDeployHubVaultClient.KeyEnvVar}.") { style = { fontSize = 10 } });

            string chosen = string.IsNullOrEmpty(config.vaultKeystoreCredentialId)
                ? "⚠ Nenhuma credencial escolhida — o build Android pelo editor vai falhar."
                : $"✔ {config.vaultKeystoreLabel} ({config.vaultKeystoreCredentialId})";
            card.Add(BuildPipelineUIStyle.CreateCallout(chosen, string.IsNullOrEmpty(config.vaultKeystoreCredentialId) ? "warn" : "ok"));

            var status = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } };
            var listHost = new VisualElement();

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            actions.Add(BuildPipelineBuildSection.SmallButton("📋 Listar keystores do cofre", async () =>
            {
                status.text = "Consultando o cofre…";
                listHost.Clear();
                var result = await AppDeployHubVaultClient.ListKeystoresAsync(config.appDeployHubUrl);
                status.text = result.Message;
                foreach (var credential in result.Credentials)
                {
                    var row = new VisualElement();
                    row.AddToClassList("bp-usage-row");
                    var label = BuildPipelineUIStyle.CreateText($"{(credential.Active ? "🟢" : "⚪")} {credential.Label}  · alias '{credential.KeystoreAlias}'");
                    label.style.flexGrow = 1;
                    row.Add(label);
                    var info = credential;
                    row.Add(BuildPipelineBuildSection.SmallButton("Usar esta", () =>
                    {
                        config.vaultKeystoreCredentialId = info.Id;
                        config.vaultKeystoreLabel = info.Label;
                        ctx.SaveConfig(false);
                    }));
                    listHost.Add(row);
                }
            }));
            actions.Add(BuildPipelineBuildSection.SmallButton("⚡ Testar a escolhida", async () =>
            {
                status.text = "Baixando keystore…";
                var result = await AppDeployHubVaultClient.FetchKeystoreAsync(config.appDeployHubUrl, config.vaultKeystoreCredentialId);
                status.text = result.Success ? "✔ " + result.Message + " (cache em Library/KeystoreCache)" : "✕ " + result.Message;
            }));
            card.Add(actions);
            card.Add(status);
            card.Add(listHost);
            return card;
        }
    }

    /// <summary>Headless commands and the catalog the Forge reads.</summary>
    internal sealed class BuildPipelineAutomationSection
    {
        public VisualElement Root { get; } = new VisualElement();

        public BuildPipelineAutomationSection(BuildPipelineSectionContext ctx)
        {
            var config = ctx.Config;
            string assetPath = AssetDatabase.GetAssetPath(config);
            string catalogPath = BuildProfileCatalog.GetCatalogPath(assetPath);
            bool upToDate = BuildProfileCatalog.IsUpToDate(config, assetPath);

            var regenerate = BuildPipelineBuildSection.SmallButton("↻ Regravar", () =>
            {
                BuildProfileCatalog.Write(config, assetPath);
                ctx.RebuildAll();
            });
            var forge = BuildPipelineUIStyle.CreateCard("📄 O que o Forge lê",
                "O Forge (AppDeployHub) lê ProjectBuildConfig.catalog.json — perfis, idioma, Steam, a pilha de defines e o registro — e não sobrescreve nada: " +
                "defines e idioma só mudam aqui, no Unity, e chegam ao Forge pelo commit.", regenerate);
            forge.Add(BuildPipelineUIStyle.CreateCallout(
                upToDate ? $"✔ Catálogo em dia (schema {BuildProfileCatalog.CurrentSchema}): {catalogPath}" : $"⚠ Catálogo desatualizado ou ausente: {catalogPath}. Salve o asset ou clique em Regravar.",
                upToDate ? "ok" : "warn"));

            if (File.Exists(catalogPath))
            {
                string text = File.ReadAllText(catalogPath);
                var preview = new Label(text.Length > 6000 ? text.Substring(0, 6000) + "\n…" : text);
                preview.AddToClassList("bp-code-box");
                preview.selection.isSelectable = true;
                var fold = new Foldout { text = "Prévia do JSON", value = false };
                fold.Add(preview);
                forge.Add(fold);
                forge.Add(BuildPipelineBuildSection.SmallButton("Abrir arquivo", () => EditorUtility.RevealInFinder(catalogPath)));
            }

            Root.Add(forge);
            Root.Add(new BuildPipelineCliView(config, ctx.Selection).Root);
        }
    }
}
