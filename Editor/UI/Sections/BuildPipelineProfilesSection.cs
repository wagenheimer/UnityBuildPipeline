using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Profiles: master list grouped by platform on the left, the selected profile on the right in focused
    /// blocks (Identity · Language · Platform · Defines · Output · Notes). The Defines block shows the full
    /// layered stack, the registry rules and what changes in the code against another profile.
    /// The second tab is the matrix/validator with one-click fixes.
    /// </summary>
    internal sealed class BuildPipelineProfilesSection
    {
        private readonly BuildPipelineSectionContext _ctx;
        private readonly ProjectBuildConfig _config;
        private readonly BuildPipelineSelectionState _selection;
        private SerializedObject _so;
        private VisualElement _definesHost;

        public VisualElement Root { get; } = new VisualElement();

        public BuildPipelineProfilesSection(BuildPipelineSectionContext ctx)
        {
            _ctx = ctx;
            _config = ctx.Config;
            _selection = ctx.Selection;

            Root.Add(Tabs());
            if (_selection.profilesTab == "matrix")
            {
                Root.Add(new BuildProfileMatrixPanel().Root);
                return;
            }

            DefineUsageIndex.EnsureScanned();
            _so = new SerializedObject(_config);

            var split = new VisualElement();
            split.AddToClassList("bp-split");
            split.Add(BuildList());

            var detail = new VisualElement();
            detail.AddToClassList("bp-split-detail");
            int index = SelectedIndex();
            if (index >= 0)
                BuildDetail(detail, index);
            else
                detail.Add(BuildPipelineUIStyle.CreateCallout("Nenhum perfil. Clique em '＋ Novo perfil'.", "info"));

            split.Add(detail);
            Root.Add(split);
            Root.Bind(_so);
        }

        private VisualElement Tabs()
        {
            var row = new VisualElement();
            row.AddToClassList("bp-subtabs");
            void Add(string id, string text)
            {
                var button = new Button(() =>
                {
                    _selection.profilesTab = id;
                    _ctx.SaveSelection();
                    _ctx.RebuildSection();
                });
                BuildPipelineUIStyle.ApplyIconText(button, text);
                button.AddToClassList("bp-subtab");
                if ((_selection.profilesTab ?? "") == id) button.AddToClassList("bp-subtab--active");
                row.Add(button);
            }

            Add("", "📋 Editor de perfis");
            Add("matrix", "🧮 Matriz & validação");
            return row;
        }

        private int SelectedIndex()
        {
            var list = _config.publishers;
            int index = list.FindIndex(p => p != null && string.Equals(p.EffectiveId, _selection.selectedProfileId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = list.FindIndex(p => p != null && !p.hidden);
            if (index < 0 && list.Count > 0)
                index = 0;
            return index;
        }

        // ── Master list ──────────────────────────────────────────────────────

        private VisualElement BuildList()
        {
            var list = new VisualElement();
            list.AddToClassList("bp-split-list");

            int total = _config.publishers?.Count ?? 0;
            int visible = _config.publishers?.Count(p => p != null && !p.hidden) ?? 0;
            int hiddenCount = total - visible;

            var tools = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 4, alignItems = Align.Center } };
            tools.Add(BuildPipelineBuildSection.SmallButton("＋ Novo perfil", () => BuildProfileWizardWindow.Open()));
            tools.Add(BuildPipelineBuildSection.SmallButton("👁 Exibir todos", () =>
            {
                if (_config.publishers == null) return;
                foreach (var p in _config.publishers) if (p != null) p.hidden = false;
                _ctx.SaveConfig();
            }));
            tools.Add(BuildPipelineBuildSection.SmallButton("🙈 Ocultar todos", () =>
            {
                if (_config.publishers == null) return;
                foreach (var p in _config.publishers) if (p != null) p.hidden = true;
                _ctx.SaveConfig();
            }));

            var hidden = new Toggle("ocultos") { value = _selection.showHiddenProfiles, style = { marginLeft = 6 } };
            hidden.tooltip = "Exibe os perfis marcados como ocultos nesta lista";
            hidden.RegisterValueChangedCallback(evt =>
            {
                _selection.showHiddenProfiles = evt.newValue;
                _ctx.SaveSelection();
                _ctx.RebuildSection();
            });
            tools.Add(hidden);
            list.Add(tools);

            var summary = new Label($"Total: {total} perfil{(total == 1 ? "" : "s")} ({visible} visíve{(visible == 1 ? "l" : "is")} · {hiddenCount} oculto{(hiddenCount == 1 ? "" : "s")})")
            {
                style = { fontSize = 10, color = new Color(0.65f, 0.7f, 0.75f), marginBottom = 8 }
            };
            list.Add(summary);

            var issues = BuildPipelineHealth.Compute(_config).Issues;
            int selected = SelectedIndex();
            foreach (var group in ProfileGroups.Build(_config, _selection.showHiddenProfiles || IsHiddenSelected(selected)))
            {
                var title = BuildPipelineUIStyle.CreateText(group.SummaryText, "bp-group-title");
                list.Add(title);

                foreach (int index in group.Indices)
                {
                    var profile = _config.publishers[index];
                    var item = new Button(() =>
                    {
                        _selection.selectedProfileId = profile.EffectiveId;
                        _ctx.SaveSelection();
                        _ctx.RebuildSection();
                    });
                    item.AddToClassList("bp-list-item");
                    if (index == selected) item.AddToClassList("bp-list-item--active");
                    if (profile.hidden) item.AddToClassList("bp-profile-row--hidden");

                    var mine = issues.Where(i => string.Equals(i.ProfileId, profile.EffectiveId, StringComparison.OrdinalIgnoreCase)).ToList();
                    string mark = mine.Any(i => i.Severity == BuildProfileIssueSeverity.Error) ? "⛔ " : mine.Count > 0 ? "⚠ " : "";
                    string lockMark = profile.IsLanguageLocked ? $" 🔒{profile.lockedLanguage.AsLanguageCode().ToUpperInvariant()}" : "";
                    string platIcon = group.Title.Contains("Steam")
                        ? (profile.platform == PlatformType.macOS ? "🍎 " : profile.platform == PlatformType.Linux64 ? "🐧 " : "🪟 ")
                        : "";
                    BuildPipelineUIStyle.ApplyIconText(item, $"{mark}{platIcon}{profile.displayName}{lockMark}");
                    item.style.justifyContent = Justify.FlexStart;
                    item.tooltip = $"{profile.EffectiveId} ({profile.platform})\n" + string.Join("\n", mine.Select(i => i.Message));
                    list.Add(item);
                }
            }

            return list;
        }

        private bool IsHiddenSelected(int index) =>
            index >= 0 && index < _config.publishers.Count && _config.publishers[index] != null && _config.publishers[index].hidden;

        // ── Detail ───────────────────────────────────────────────────────────

        private void BuildDetail(VisualElement detail, int index)
        {
            var profile = _config.publishers[index];
            var element = _so.FindProperty("publishers").GetArrayElementAtIndex(index);
            PropertyField Field(string name, string label) => new PropertyField(element.FindPropertyRelative(name), label);

            detail.Add(DetailHeader(profile, index));

            var identity = BuildPipelineUIStyle.CreateCard("🪪 Identity", "The profile id is used by Forge and CLI (-buildProfile). Change with care.");
            identity.Add(Field("id", "Id (CI / Forge)"));
            identity.Add(Field("displayName", "Nome"));
            identity.Add(Field("publisher", "Publisher"));
            identity.Add(Field("platform", "Plataforma"));
            identity.Add(Field("bundleIdentifier", "Bundle id"));
            identity.Add(Field("isFullGame", "Jogo completo (Full)"));
            identity.Add(Field("isDemo", "Demo"));
            identity.Add(Field("isSteam", "Steam"));
            identity.Add(Field("requiresSplash", "Splash do publisher"));
            identity.Add(Field("hidden", "Oculto no editor"));

            if (profile.isSteam)
            {
                var steamProfiles = _config.publishers.Where(p => p != null && p.isSteam).ToList();
                var plats = steamProfiles.Select(s => s.platform == PlatformType.macOS ? "🍎 Mac" : s.platform == PlatformType.Linux64 ? "🐧 Linux" : "🪟 Windows").Distinct();
                identity.Add(BuildPipelineUIStyle.CreateCallout(
                    $"🎮 Perfil do ecossistema Steam. Plataformas Steam configuradas: {string.Join(", ", plats)}. " +
                    "Use os botões no cabeçalho (+ Steam Mac / + Steam Linux) para gerar os perfis irmãos das outras plataformas caso queira compilá-los separadamente no Forge/CI.", "info"));
            }

            detail.Add(identity);

            detail.Add(LanguageCard(profile, element));

            var platform = BuildPipelineUIStyle.CreateCard("🧩 Platform");
            platform.Add(Field("scriptingBackend", "Scripting backend"));
            if (profile.platform == PlatformType.macOS)
            {
                platform.Add(Field("macArchitecture", "Arquitetura Mac"));
                platform.Add(Field("macAppStoreValidation", "Validação de recibo Mac App Store"));
            }
            detail.Add(platform);

            var defines = BuildPipelineUIStyle.CreateCard("🧬 Defines",
                "Presets primeiro, depois a lista do perfil. '!X' remove X só neste build. A tabela mostra TODAS as camadas que o build aplica.");
            defines.Add(Field("definePresets", "Presets"));
            defines.Add(Field("scriptingDefines", "Defines do perfil"));
            _definesHost = new VisualElement();
            defines.Add(_definesHost);
            detail.Add(defines);
            RefreshDefines(profile);
            defines.TrackSerializedObjectValue(_so, _ => defines.schedule.Execute(() => RefreshDefines(profile)).ExecuteLater(300));

            var output = BuildPipelineUIStyle.CreateCard("📁 Build Output",
                "A pasta final é montada em camadas: raiz do projeto → subpasta do perfil → plataforma (Steam) → versão/data/idioma. " +
                "Preencha apenas a subpasta; o resto é automático.");
            BuildOutputCard(output, profile, element);
            detail.Add(output);

            var zip = BuildPipelineUIStyle.CreateCard("🗜 Zip Archive (Optional)",
                "Independente da pasta do build: se marcar, o .zip é criado na pasta de destino abaixo.");
            BuildZipCard(zip, profile, element);
            detail.Add(zip);

            var notes = BuildPipelineUIStyle.CreateCard("📝 Notes");
            notes.Add(Field("notes", ""));
            detail.Add(notes);
        }

        private VisualElement DetailHeader(PublisherProfile profile, int index)
        {
            var header = new VisualElement();
            header.AddToClassList("bp-detail-header");

            var title = new VisualElement { style = { flexGrow = 1 } };
            title.Add(new Label(profile.displayName) { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold } });
            title.Add(new Label($"{profile.EffectiveId} · {profile.platform} · {profile.publisher}") { style = { color = new Color(0.62f, 0.65f, 0.71f) } });
            header.Add(title);

            var issues = BuildPipelineHealth.Compute(_config).Issues;
            foreach (var badge in ProfileBadges.For(_config, profile, issues))
                header.Add(badge);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 8 } };
            if (profile.isSteam)
            {
                bool hasWindows = _config.publishers.Any(p => p != null && p.isSteam && p.platform == PlatformType.Windows64);
                bool hasMac = _config.publishers.Any(p => p != null && p.isSteam && p.platform == PlatformType.macOS);
                bool hasLinux = _config.publishers.Any(p => p != null && p.isSteam && p.platform == PlatformType.Linux64);

                if (!hasWindows) actions.Add(BuildPipelineBuildSection.SmallButton("🪟 + Steam Win", () => CreateSteamSibling(index, PlatformType.Windows64)));
                if (!hasMac) actions.Add(BuildPipelineBuildSection.SmallButton("🍎 + Steam Mac", () => CreateSteamSibling(index, PlatformType.macOS)));
                if (!hasLinux) actions.Add(BuildPipelineBuildSection.SmallButton("🐧 + Steam Linux", () => CreateSteamSibling(index, PlatformType.Linux64)));
            }
            actions.Add(BuildPipelineBuildSection.SmallButton("⧉ Duplicate", () => Duplicate(index)));
            if (PublisherProfileDuplicator.TryGetCounterpart(profile.publisher, out _))
                actions.Add(BuildPipelineBuildSection.SmallButton(profile.isFullGame ? "→ Free" : "→ Full", () => Counterpart(index)));
            actions.Add(BuildPipelineBuildSection.SmallButton(profile.hidden ? "👁 Show" : "🙈 Hide", () =>
            {
                profile.hidden = !profile.hidden;
                _ctx.SaveConfig();
            }));
            var remove = BuildPipelineBuildSection.SmallButton("✕", () => Remove(index));
            remove.AddToClassList("bp-btn--danger");
            remove.tooltip = "Excluir perfil";
            actions.Add(remove);
            header.Add(actions);
            return header;
        }

        private VisualElement LanguageCard(PublisherProfile profile, SerializedProperty element)
        {
            var card = BuildPipelineUIStyle.CreateCard("🌐 Language",
                "Auto = padrão do projeto (normalmente AutoDetect) ou o idioma escolhido na hora do build. " +
                "Travado = o build SEMPRE sai nesse idioma e o jogo esconde o seletor (GameConfig.IsLanguageLocked).");

            var mode = new PropertyField(element.FindPropertyRelative("languageMode"), "Modo");
            var locked = new PropertyField(element.FindPropertyRelative("lockedLanguage"), "Idioma travado");
            card.Add(mode);
            card.Add(locked);

            var info = new Label();
            info.style.whiteSpace = WhiteSpace.Normal;
            info.style.marginTop = 4;
            card.Add(info);

            void Refresh()
            {
                locked.style.display = profile.languageMode == ProfileLanguageMode.Locked ? DisplayStyle.Flex : DisplayStyle.None;
                var language = BuildLanguageResolver.Resolve(_config, profile, null, out var source);
                info.text = "Build sem override sai em: " + BuildLanguageResolver.Describe(language, source) +
                            (profile.IsLanguageLocked ? $" · pasta com sufixo {profile.lockedLanguage.FolderNameLanguage()}" : "");
            }

            Refresh();
            card.TrackSerializedObjectValue(_so, _ => Refresh());
            return card;
        }

        /// <summary>
        /// Output card: one editable subfolder, a live breakdown (root → subfolder → Steam platform → base folder),
        /// an example of the next build and a Test button that validates/creates the folder and checks write access.
        /// </summary>
        private void BuildOutputCard(VisualElement card, PublisherProfile profile, SerializedProperty element)
        {
            var subfolderField = new PropertyField(element.FindPropertyRelative("outputSubfolder"), "Profile subfolder (relative to root)");
            card.Add(subfolderField);
            card.Add(Hint("Ex.: Builds/Publishers/{Publisher}/  ·  {Publisher} vira o nome do publisher  ·  não inclua versão/data."));

            var steamCallout = BuildPipelineUIStyle.CreateCallout(
                $"🎮 Perfil Steam: a pasta {ProfileOutputPaths.PlatformFolder(profile.platform)} é adicionada automaticamente. " +
                "Não escreva Windows/Linux/Mac na subpasta.", "info");
            card.Add(steamCallout);

            var pathList = new VisualElement();
            pathList.AddToClassList("bp-path-list");

            var rootPath = PathValue();
            var rootRow = PathRow("Project Output Root", rootPath);
            var editRoot = BuildPipelineBuildSection.SmallButton("✏ Alterar", () => _ctx.Navigate(BuildPipelineSections.Project));
            editRoot.tooltip = "As raízes de Windows e macOS ficam em Projeto → Raízes de saída.";
            rootRow.Add(editRoot);
            pathList.Add(rootRow);

            var subPath = PathValue();
            pathList.Add(PathRow("+ Subpasta", subPath));

            var platformPath = PathValue();
            var platformRow = PathRow("+ Plataforma", platformPath);
            pathList.Add(platformRow);

            var basePath = PathValue();
            basePath.AddToClassList("bp-path-value--final");
            pathList.Add(PathRow("= Pasta base", basePath, final: true));

            var nextPath = PathValue();
            var nextRow = PathRow("Próximo build", nextPath);
            pathList.Add(nextRow);

            var artifactPath = PathValue();
            var artifactRow = PathRow("Arquivo", artifactPath);
            pathList.Add(artifactRow);

            card.Add(pathList);
            card.Add(Hint("“Próximo build” é um exemplo calculado neste computador com a versão/data/idioma atuais; o nome exato só é definido quando o build começa."));

            var statusHost = new VisualElement();
            bool testStatusActive = false;

            void ShowCheck(BuildOutputCheck check)
            {
                statusHost.Clear();
                statusHost.Add(BuildPipelineUIStyle.CreateCallout(
                    check.Summary + "\n" + string.Join("\n", check.Details),
                    BuildOutputFolderInspector.SeverityClass(check.Severity)));
            }

            void ShowInitialStatus()
            {
                var check = BuildOutputFolderInspector.Inspect(_config, profile, profile.platform, prepareFolder: false);
                statusHost.Clear();
                if (check.HasError)
                {
                    ShowCheck(check);
                    return;
                }

                if (check.Exists && check.Severity == BuildOutputCheckSeverity.Warning)
                {
                    ShowCheck(check);
                    return;
                }

                string text = check.Exists
                    ? $"✔ Pasta base existe ({check.ExistingFolders} subpasta(s)). Clique em Testar para confirmar a gravação."
                    : "• Pasta base ainda não existe; será criada no primeiro build. Clique em Testar para criar e validar agora.";
                statusHost.Add(new Label(text)
                {
                    style =
                    {
                        fontSize = 10,
                        color = new Color(0.65f, 0.68f, 0.72f),
                        whiteSpace = WhiteSpace.Normal,
                        marginTop = 4
                    }
                });
            }

            bool refreshing = false;
            void Refresh()
            {
                if (refreshing)
                    return;

                refreshing = true;
                try
                {
                    string root = _config.GetEffectiveBuildOutputRoot(profile.platform);
                    string subfolder = (profile.outputSubfolder ?? "").Replace("{Publisher}", profile.publisher.ToString());
                    string platformFolder = ProfileOutputPaths.UsesPlatformSubfolder(profile, profile.platform)
                        ? ProfileOutputPaths.PlatformFolder(profile.platform)
                        : "";
                    string baseFolder = ProfileOutputPaths.ResolveFolder(_config, profile, profile.platform);

                    rootPath.text = string.IsNullOrWhiteSpace(root) ? "(vazia)" : root;
                    subPath.text = string.IsNullOrWhiteSpace(subfolder) ? "(vazia — a pasta base será a raiz)" : subfolder;
                    platformRow.style.display = string.IsNullOrEmpty(platformFolder) ? DisplayStyle.None : DisplayStyle.Flex;
                    platformPath.text = platformFolder + "  (automática)";
                    basePath.text = baseFolder;
                    steamCallout.style.display = string.IsNullOrEmpty(platformFolder) ? DisplayStyle.None : DisplayStyle.Flex;

                    var preview = ProfileOutputPaths.ResolveNextBuild(_config, profile, profile.platform);
                    nextRow.style.display = string.IsNullOrEmpty(preview.Folder) ? DisplayStyle.None : DisplayStyle.Flex;
                    nextPath.text = preview.Folder;
                    artifactRow.style.display = preview.HasArtifact ? DisplayStyle.Flex : DisplayStyle.None;
                    artifactPath.text = preview.Artifact;
                }
                finally
                {
                    refreshing = false;
                }

                if (!testStatusActive)
                    ShowInitialStatus();
            }

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
            var test = BuildPipelineBuildSection.SmallButton("🧪 Test Folder", () =>
            {
                _so.ApplyModifiedProperties();
                testStatusActive = true;
                Refresh();
                var check = BuildOutputFolderInspector.Inspect(_config, profile, profile.platform, prepareFolder: true);
                ShowCheck(check);
                Debug.Log($"[BuildPipeline] Teste de pasta ({profile.EffectiveId}): {check.Summary}\n{string.Join("\n", check.Details)}");
            });
            test.AddToClassList("bp-btn--primary");
            test.tooltip = "Cria a pasta se necessário, grava um arquivo temporário e confere permissão, disco e caminho.";
            actions.Add(test);

            actions.Add(BuildPipelineBuildSection.SmallButton("📂 Open Folder", () =>
            {
                _so.ApplyModifiedProperties();
                BuildOutputFolderActions.Open(_config, profile, profile.platform);
            }));

            actions.Add(BuildPipelineBuildSection.SmallButton("📋 Copy Path", () =>
            {
                _so.ApplyModifiedProperties();
                BuildOutputFolderActions.CopyPath(ProfileOutputPaths.ResolveFolder(_config, profile, profile.platform));
            }));

            actions.Add(BuildPipelineBuildSection.SmallButton("↺ Reset to Default", () =>
            {
                string standard = profile.isSteam ? $"Publishers/Steam/{{GameFolder}}/" : $"Publishers/{profile.publisher}/{{GameFolder}}/";
                if (!EditorUtility.DisplayDialog("Reset Subfolder",
                        $"Voltar a subpasta deste perfil para:\n\n{standard}", "Reset", "Cancel"))
                    return;

                element.FindPropertyRelative("outputSubfolder").stringValue = standard;
                _so.ApplyModifiedProperties();
                _ctx.SaveConfig(false);
            }));

            card.Add(actions);
            card.Add(statusHost);

            card.TrackPropertyValue(element.FindPropertyRelative("outputSubfolder"), _ =>
            {
                testStatusActive = false;
                card.schedule.Execute(Refresh).ExecuteLater(120);
            });
            card.TrackPropertyValue(element.FindPropertyRelative("platform"), _ => _ctx.RebuildSection());
            card.TrackPropertyValue(element.FindPropertyRelative("isSteam"), _ => _ctx.RebuildSection());

            Refresh();
        }

        /// <summary>Zip destination is separate from the build folder; show it only when the toggle is on.</summary>
        private void BuildZipCard(VisualElement card, PublisherProfile profile, SerializedProperty element)
        {
            card.Add(new PropertyField(element.FindPropertyRelative("zipAfterBuild"), "Zipar depois do build"));

            var destination = new PropertyField(element.FindPropertyRelative("zipDestinationTemplate"), "Pasta de destino do zip");
            card.Add(destination);
            var hint = Hint("Tokens: {ProjectName} e {Publisher}. Ex.: E:\\Dropbox\\Builds\\{ProjectName}\\{Publisher}\\");
            card.Add(hint);

            var resolved = PathValue();
            resolved.AddToClassList("bp-path-value--final");
            var resolvedRow = PathRow("Destino resolvido", resolved, final: true);
            card.Add(resolvedRow);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
            actions.Add(BuildPipelineBuildSection.SmallButton("📋 Copiar", () => BuildOutputFolderActions.CopyPath(resolved.text)));
            actions.Add(BuildPipelineBuildSection.SmallButton("📂 Abrir destino", () =>
            {
                _so.ApplyModifiedProperties();
                BuildOutputFolderActions.OpenPath(resolved.text, "Abrir destino do zip");
            }));
            card.Add(actions);

            void Refresh()
            {
                bool on = profile.zipAfterBuild;
                destination.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                hint.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                resolvedRow.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                actions.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

                string template = !string.IsNullOrWhiteSpace(profile.zipDestinationTemplate)
                    ? profile.zipDestinationTemplate
                    : @"E:\Documentos\Dropbox\Builds\{ProjectName}\{Publisher}\";
                string projectName = !string.IsNullOrWhiteSpace(_config.projectName) ? _config.projectName : Application.productName;
                resolved.text = template
                    .Replace("{ProjectName}", projectName)
                    .Replace("{Publisher}", profile.publisher.ToString());
            }

            card.TrackPropertyValue(element.FindPropertyRelative("zipAfterBuild"), _ => Refresh());
            card.TrackPropertyValue(element.FindPropertyRelative("zipDestinationTemplate"), _ =>
                card.schedule.Execute(Refresh).ExecuteLater(120));
            Refresh();
        }

        private static Label PathValue(string text = "")
        {
            var value = new Label(text);
            value.AddToClassList("bp-path-value");
            value.selection.isSelectable = true;
            return value;
        }

        private static VisualElement PathRow(string label, VisualElement value, bool final = false)
        {
            var row = new VisualElement();
            row.AddToClassList("bp-path-row");
            if (final)
                row.AddToClassList("bp-path-row--final");

            var caption = new Label(label);
            caption.AddToClassList("bp-path-label");
            row.Add(caption);
            value.style.flexGrow = 1;
            row.Add(value);
            return row;
        }

        private static Label Hint(string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("bp-hint");
            return hint;
        }

        private void RefreshDefines(PublisherProfile profile)
        {
            if (_definesHost == null)
                return;

            _definesHost.Clear();
            var registry = DefineRegistry.Load(_config);
            var stack = DefineEnvironment.StackFor(_config, profile);
            DefineStackResolver.AddAssemblyScoped(stack, DefineUsageIndex.VersionDefines());

            var showGlobals = new Toggle("Mostrar também os globais (PlayerSettings)") { value = _selection.showUnityDefines };
            showGlobals.RegisterValueChangedCallback(evt =>
            {
                _selection.showUnityDefines = evt.newValue;
                _ctx.SaveSelection();
                RefreshDefines(profile);
            });
            _definesHost.Add(showGlobals);
            _definesHost.Add(DefineViews.StackTable(stack, registry, OpenSymbol, _selection.showUnityDefines));

            foreach (var violation in DefineRules.Check(registry, profile, stack))
                _definesHost.Add(BuildPipelineUIStyle.CreateCallout("⛔ " + violation.Message, "warn"));

            var final = new Label($"Resultado final ({stack.TargetGroup}): " + (stack.Final.Count == 0 ? "(nenhum)" : string.Join("; ", stack.Final)));
            final.AddToClassList("bp-code-box");
            final.selection.isSelectable = true;
            _definesHost.Add(final);

            _definesHost.Add(CompareBlock(profile, stack));
        }

        private VisualElement CompareBlock(PublisherProfile profile, DefineStack stack)
        {
            var box = new VisualElement { style = { marginTop = 8 } };
            var others = _config.publishers.Where(p => p != null && p != profile).ToList();
            if (others.Count == 0)
                return box;

            var counterpart = others.FirstOrDefault(p => p.platform == profile.platform && p.isFullGame != profile.isFullGame) ?? others[0];
            var compare = others.FirstOrDefault(p => string.Equals(p.EffectiveId, _selection.compareProfileId, StringComparison.OrdinalIgnoreCase)) ?? counterpart;

            var names = others.Select(p => $"{p.displayName} ({p.EffectiveId})").ToList();
            var picker = new PopupField<string>("Comparar com", names, others.IndexOf(compare));
            picker.RegisterValueChangedCallback(evt =>
            {
                _selection.compareProfileId = others[names.IndexOf(evt.newValue)].EffectiveId;
                _ctx.SaveSelection();
                RefreshDefines(profile);
            });
            box.Add(picker);

            var other = DefineEnvironment.StackFor(_config, compare);
            var onlyHere = stack.Final.Except(other.Final, StringComparer.Ordinal).ToList();
            var onlyThere = other.Final.Except(stack.Final, StringComparer.Ordinal).ToList();
            box.Add(new Label($"Só neste: {(onlyHere.Count == 0 ? "—" : string.Join(", ", onlyHere))}   ·   Só em {compare.EffectiveId}: {(onlyThere.Count == 0 ? "—" : string.Join(", ", onlyThere))}")
            {
                style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 }
            });

            if (stack.TargetGroup != other.TargetGroup)
                box.Add(new Label($"Grupos diferentes ({stack.TargetGroup} × {other.TargetGroup}): os globais também diferem.") { style = { fontSize = 10, color = new Color(0.9f, 0.7f, 0.4f) } });

            var impact = DefineImpact.Compare(stack.FinalSet(), other.FinalSet(), DefineUsageIndex.Usages);
            var foldout = new Foldout { text = $"O que muda no código ({(DefineUsageIndex.HasResult ? impact.Count.ToString() : "…")})", value = impact.Count > 0 && impact.Count <= 40 };
            foldout.Add(DefineViews.ImpactList(impact, profile.EffectiveId, compare.EffectiveId));
            box.Add(foldout);
            return box;
        }

        private void OpenSymbol(string symbol)
        {
            _selection.selectedDefine = symbol;
            _selection.definesTab = "";
            _ctx.SaveSelection();
            _ctx.Navigate(BuildPipelineSections.Defines);
        }

        // ── Actions ──────────────────────────────────────────────────────────

        private void CreateSteamSibling(int sourceIndex, PlatformType targetPlatform)
        {
            var source = _config.publishers[sourceIndex];
            var copy = JsonUtility.FromJson<PublisherProfile>(JsonUtility.ToJson(source));
            copy.platform = targetPlatform;
            copy.isSteam = true;
            string platSuffix = targetPlatform == PlatformType.macOS ? "Mac" : targetPlatform == PlatformType.Linux64 ? "Linux" : "Windows";
            copy.displayName = $"Steam ({platSuffix})";

            if (source.displayName == "Steam" && source.platform == PlatformType.Windows64)
            {
                source.displayName = "Steam (Windows)";
                if (string.IsNullOrEmpty(source.id) || source.id.Equals("steam", StringComparison.OrdinalIgnoreCase))
                    source.id = "steam-windows";
            }

            copy.id = $"steam-{platSuffix.ToLowerInvariant()}";
            _config.publishers.Add(copy);
            _selection.selectedProfileId = copy.EffectiveId;
            _ctx.SaveSelection();
            _ctx.SaveConfig();
            _ctx.RebuildSection();
        }

        private void Duplicate(int index)
        {
            var source = _config.publishers[index];
            var copy = JsonUtility.FromJson<PublisherProfile>(JsonUtility.ToJson(source));
            copy.displayName += " (cópia)";
            copy.id = string.IsNullOrEmpty(source.id) ? "" : source.id + "-copy";
            _config.publishers.Insert(index + 1, copy);
            _selection.selectedProfileId = copy.EffectiveId;
            _ctx.SaveSelection();
            _ctx.SaveConfig();
        }

        private void Counterpart(int index)
        {
            var source = _config.publishers[index];
            var tracking = BuildProfileCatalog.ReadTrackingPolicy();
            var clone = PublisherProfileDuplicator.Create(source, tracking?.adsDefine ?? "", tracking?.scriptingDefine ?? "");
            if (clone == null)
                return;

            if (_config.publishers.Any(p => string.Equals(p.EffectiveId, clone.EffectiveId, StringComparison.OrdinalIgnoreCase)) &&
                !EditorUtility.DisplayDialog("Duplicar perfil", $"Já existe um perfil com o id '{clone.EffectiveId}'. Duplicar mesmo assim?", "Duplicar", "Cancel"))
                return;

            clone.isSteam = source.isSteam;
            clone.languageMode = source.languageMode;
            clone.lockedLanguage = source.lockedLanguage;
            _config.publishers.Insert(index + 1, clone);
            _selection.selectedProfileId = clone.EffectiveId;
            _ctx.SaveSelection();
            _ctx.SaveConfig();
        }

        private void Remove(int index)
        {
            var profile = _config.publishers[index];
            if (!EditorUtility.DisplayDialog("Excluir perfil",
                    $"Excluir '{profile.displayName}' ({profile.EffectiveId})?\n\nO Forge e o CI deixam de encontrar este id. Para só tirar da lista, use Ocultar.",
                    "Excluir", "Cancel"))
                return;

            _config.publishers.RemoveAt(index);
            _selection.selectedProfileId = "";
            _ctx.SaveSelection();
            _ctx.SaveConfig();
        }
    }
}
