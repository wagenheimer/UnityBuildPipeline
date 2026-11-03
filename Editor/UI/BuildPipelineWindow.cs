using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Stable ids of the window sections (also used by <see cref="BuildPipelineWindow.ShowSection"/>).</summary>
    public static class BuildPipelineSections
    {
        public const string Build = "build";
        public const string History = "history";
        public const string Profiles = "profiles";
        public const string Defines = "defines";
        public const string Languages = "languages";
        public const string Project = "project";
        public const string Signing = "signing";
        public const string Steam = "steam";
        public const string Automation = "automation";
    }

    /// <summary>
    /// The single Build Pipeline window: a sidebar grouped by intent (Build · Configure · Distribute · Automate),
    /// an always-visible health bar (validator + external define changes + catalog) and the selected section.
    /// Every setting of ProjectBuildConfig is edited here; the asset inspector only links to it.
    /// </summary>
    public class BuildPipelineWindow : EditorWindow
    {
        private static readonly Vector2 MinSize = new Vector2(900, 600);
        private static readonly Vector2 DefaultSize = new Vector2(1180, 780);

        private sealed class NavItem
        {
            public string Id;
            public string Group;
            public string Label;
            public string Tooltip;
        }

        private static readonly NavItem[] Nav =
        {
            new NavItem { Id = BuildPipelineSections.Build, Group = "BUILD", Label = "⚡ Build", Tooltip = "Um perfil com um clique, ou vários de uma vez (idiomas × variantes)." },
            new NavItem { Id = BuildPipelineSections.History, Group = "BUILD", Label = "🕘 Histórico", Tooltip = "Builds recentes, tamanho, pasta, instalar no Android." },
            new NavItem { Id = BuildPipelineSections.Profiles, Group = "CONFIGURAR", Label = "📋 Perfis", Tooltip = "Cada loja/publisher: identidade, idioma, defines, saída." },
            new NavItem { Id = BuildPipelineSections.Defines, Group = "CONFIGURAR", Label = "🧬 Defines", Tooltip = "Central de defines: origem, quem exige, onde o código usa." },
            new NavItem { Id = BuildPipelineSections.Languages, Group = "CONFIGURAR", Label = "🌐 Idiomas", Tooltip = "Idioma padrão, idiomas da matriz e perfis travados." },
            new NavItem { Id = BuildPipelineSections.Project, Group = "CONFIGURAR", Label = "⚙ Projeto", Tooltip = "GameConfig, raízes de saída, cenas e zip." },
            new NavItem { Id = BuildPipelineSections.Signing, Group = "CONFIGURAR", Label = "🔐 Assinatura", Tooltip = "Keystore Android: arquivo local ou cofre do AppDeployHub." },
            new NavItem { Id = BuildPipelineSections.Steam, Group = "DISTRIBUIR", Label = "🎮 Steam", Tooltip = "Perfis Steam, depots, upload e histórico." },
            new NavItem { Id = BuildPipelineSections.Automation, Group = "AUTOMATIZAR", Label = "💻 CLI & Forge", Tooltip = "Comandos headless e o que o Forge lê (catálogo)." },
        };

        [MenuItem("Tools/Wagenheimer/Build Pipeline/Open Build Pipeline", priority = 0)]
        [MenuItem("Window/Wagenheimer/Build Pipeline", priority = 205)]
        public static void ShowWindow() => ShowSection(null);

        /// <summary>Opens the window on a section (and optionally selects a profile in Profiles).</summary>
        public static void ShowSection(string sectionId, string profileId = null)
        {
            try
            {
                var win = GetWindow<BuildPipelineWindow>(utility: false, title: "Build Pipeline", focus: true);
                win.titleContent = new GUIContent("Build Pipeline", EditorGUIUtility.IconContent("BuildSettings.Editor").image);
                win.minSize = MinSize;
                EditorWindowPlacement.EnsureOnScreen(win, MinSize, DefaultSize);

                if (!string.IsNullOrEmpty(sectionId))
                    win.Navigate(sectionId, profileId);

                win.Show();
                win.Focus();
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
            EditorWindowPlacement.Center(win, MinSize, DefaultSize);
            win.Show();
            win.Focus();
        }

        /// <summary>Asks every open Build Pipeline window to redraw (after an external change to the config).</summary>
        public static void RefreshAll()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<BuildPipelineWindow>())
                window.RebuildUI();
        }

        private ProjectBuildConfig _config;
        private BuildPipelineSelectionState _selection = new BuildPipelineSelectionState();
        private string _section = BuildPipelineSections.Build;
        private VisualElement _content;

        private void OnEnable()
        {
            try
            {
                _config = LegacyGameConfigMigrator.FindProjectBuildConfig();
                _selection = BuildPipelineSelectionState.Load(_config);
                if (Nav.Any(item => item.Id == _selection.section))
                    _section = _selection.section;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Window OnEnable warning: {ex.Message}");
            }

            DefineUsageIndex.Changed += OnUsageIndexChanged;
        }

        private void OnDisable() => DefineUsageIndex.Changed -= OnUsageIndexChanged;

        private void OnUsageIndexChanged()
        {
            if (_section == BuildPipelineSections.Defines || _section == BuildPipelineSections.Profiles)
                RebuildContent();
        }

        private void Navigate(string sectionId, string profileId)
        {
            _section = Nav.Any(item => item.Id == sectionId) ? sectionId : BuildPipelineSections.Build;
            _selection.section = _section;
            if (!string.IsNullOrEmpty(profileId))
            {
                _selection.selectedProfileId = profileId;
                _selection.profilesTab = "";
            }

            _selection.Save(_config);
            if (rootVisualElement != null && rootVisualElement.childCount > 0)
                RebuildUI();
        }

        public void CreateGUI()
        {
            try
            {
                RebuildUI();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildPipeline] Error initializing BuildPipelineWindow GUI: {ex}");
                rootVisualElement.Clear();
                rootVisualElement.Add(new HelpBox($"Failed to initialize Build Pipeline UI:\n{ex.Message}\n\n{ex.StackTrace}", HelpBoxMessageType.Error));
                var retry = new Button(RebuildUI);
                BuildPipelineUIStyle.ApplyIconText(retry, "🔄 Retry");
                rootVisualElement.Add(retry);
            }
        }

        private void RebuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("bp-window");
            BuildPipelineUIStyle.Apply(root);

            if (_config == null)
            {
                _config = LegacyGameConfigMigrator.FindProjectBuildConfig();
                _selection = BuildPipelineSelectionState.Load(_config);
            }

            if (_config == null)
            {
                root.Add(MigrationCard(null));
                return;
            }

            _config.publishers ??= new List<PublisherProfile>();
            _config.languages ??= new List<LanguageProfile>();

            root.Add(new BuildPipelineHeaderView(_config, RebuildUI).Root);
            root.Add(BuildHealthBar());

            var body = new VisualElement();
            body.AddToClassList("bp-body");
            body.Add(BuildSidebar());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("bp-content");
            _content = new VisualElement();
            _content.AddToClassList("bp-content-inner");
            scroll.Add(_content);
            body.Add(scroll);
            root.Add(body);

            RebuildContent();
        }

        private void RebuildContent()
        {
            if (_content == null)
                return;

            _content.Clear();
            try
            {
                _content.Add(CreateSection(_section));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _content.Add(BuildPipelineUIStyle.CreateCallout($"Erro ao montar a seção '{_section}': {ex.Message}", "warn"));
            }
        }

        private VisualElement CreateSection(string id) =>
            CreateSection(new BuildPipelineSectionContext(_config, _selection, RebuildUI, RebuildContent, Navigate), id);

        /// <summary>Builds one section's content (also used by the EditMode smoke test).</summary>
        internal static VisualElement CreateSection(BuildPipelineSectionContext context, string id)
        {
            switch (id)
            {
                case BuildPipelineSections.History: return new BuildPipelineHistoryView(context.RebuildAll).Root;
                case BuildPipelineSections.Profiles: return new BuildPipelineProfilesSection(context).Root;
                case BuildPipelineSections.Defines: return new BuildPipelineDefinesSection(context).Root;
                case BuildPipelineSections.Languages: return new BuildPipelineLanguagesSection(context).Root;
                case BuildPipelineSections.Project: return new BuildPipelineProjectSection(context).Root;
                case BuildPipelineSections.Signing: return new BuildPipelineSigningSection(context).Root;
                case BuildPipelineSections.Steam: return new SteamUploadPanel(context.Config, context.RebuildSection).Root;
                case BuildPipelineSections.Automation: return new BuildPipelineAutomationSection(context).Root;
                default: return new BuildPipelineBuildSection(context).Root;
            }
        }

        /// <summary>
        /// Migration card: without a config it offers the migration (nothing is created on open); with one it lists
        /// what is still pending after a legacy or 1.x → 2.0 migration. Empty when there is nothing to do.
        /// </summary>
        internal static VisualElement MigrationCard(ProjectBuildConfig config, Action<string> navigate = null, Action rebuild = null)
        {
            var record = LegacyMigrationRecord.Load();
            var items = MigrationChecklist.Build(config, record);
            var box = new VisualElement();
            if (items.Count == 0)
                return box;

            var card = BuildPipelineUIStyle.CreateCard("🧭 Migração",
                config == null
                    ? "Este projeto ainda não usa o Build Pipeline. Migrar cria o ProjectBuildConfig a partir do GameConfig (nada é apagado). " +
                      "Projeto com GameConfig.cs/GameConfigEditor.cs próprios? Feche o Unity e rode antes Tools/migrate-legacy-project.ps1."
                    : "O que ainda falta depois da migração.");
            card.AddToClassList("bp-card--focus");

            foreach (var item in items)
            {
                var row = new VisualElement();
                row.AddToClassList("bp-usage-row");
                var text = BuildPipelineUIStyle.CreateText((item.Blocking ? "⛔ " : "⚠ ") + item.Text);
                text.style.flexGrow = 1;
                text.style.whiteSpace = WhiteSpace.Normal;
                row.Add(text);
                if (config != null && navigate != null && !string.IsNullOrEmpty(item.Section) && item.Section != BuildPipelineSections.Project)
                {
                    string section = item.Section;
                    row.Add(BuildPipelineBuildSection.SmallButton("Abrir", () => navigate(section)));
                }
                card.Add(row);

                if (item.Details.Count > 0)
                {
                    var details = new Foldout { text = "Detalhes", value = item.Blocking };
                    foreach (string detail in item.Details)
                        details.Add(new Label(detail) { selection = { isSelectable = true }, style = { fontSize = 10, whiteSpace = WhiteSpace.Normal } });
                    card.Add(details);
                }
            }

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            if (config == null)
            {
                var migrate = BuildPipelineBuildSection.SmallButton("⚡ Migrar agora", () =>
                {
                    LegacyGameConfigMigrator.MigrateOrCreate();
                    RefreshAll();
                });
                migrate.AddToClassList("bp-btn--primary");
                actions.Add(migrate);
            }
            else if (record != null && record.IsOpen)
            {
                actions.Add(BuildPipelineBuildSection.SmallButton("✔ Marcar migração do projeto antigo como concluída", () =>
                {
                    LegacyMigrationRecord.Close();
                    rebuild?.Invoke();
                    RefreshAll();
                }));
            }

            card.Add(actions);
            box.Add(card);
            return box;
        }

        private VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement();
            sidebar.AddToClassList("bp-sidebar");

            string group = null;
            foreach (var item in Nav)
            {
                if (item.Group != group)
                {
                    group = item.Group;
                    var groupLabel = new Label(group);
                    groupLabel.AddToClassList("bp-nav-group");
                    sidebar.Add(groupLabel);
                }

                var id = item.Id;
                var button = new Button(() => Navigate(id, null)) { tooltip = item.Tooltip };
                BuildPipelineUIStyle.ApplyIconText(button, item.Label);
                button.AddToClassList("bp-nav-item");
                if (id == _section)
                    button.AddToClassList("bp-nav-item--active");

                sidebar.Add(button);
            }

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            sidebar.Add(spacer);

            var counts = new Label($"{_config.publishers.Count(p => p != null && !p.hidden)} perfis · {_config.publishers.Count(p => p != null && p.hidden)} ocultos");
            counts.AddToClassList("bp-nav-footer");
            sidebar.Add(counts);
            return sidebar;
        }

        private VisualElement BuildHealthBar()
        {
            var health = BuildPipelineHealth.Compute(_config);

            var bar = new VisualElement();
            bar.AddToClassList("bp-health");
            bar.AddToClassList(health.Errors > 0 ? "bp-health--error" : health.Warnings > 0 || health.GlobalChanges > 0 ? "bp-health--warn" : "bp-health--ok");

            void AddChip(string text, string tooltip, Action onClick)
            {
                var chip = new Button(onClick) { tooltip = tooltip };
                BuildPipelineUIStyle.ApplyIconText(chip, text);
                chip.AddToClassList("bp-health-chip");
                bar.Add(chip);
            }

            if (health.Errors == 0 && health.Warnings == 0)
                AddChip("✔ Perfis válidos", "Validador sem problemas.", () => Navigate(BuildPipelineSections.Profiles, null));
            else
                AddChip($"⛔ {health.Errors} erro(s) · ⚠ {health.Warnings} aviso(s)", health.Summary,
                    () => { _selection.profilesTab = "matrix"; Navigate(BuildPipelineSections.Profiles, null); });

            if (health.GlobalChanges > 0)
                AddChip($"🌐 Defines globais mudaram ({health.GlobalChanges})", health.GlobalSummary,
                    () => { _selection.definesTab = "globals"; Navigate(BuildPipelineSections.Defines, null); });

            AddChip(health.CatalogUpToDate ? "📄 Catálogo do Forge em dia" : "📄 Catálogo desatualizado",
                health.CatalogUpToDate ? "ProjectBuildConfig.catalog.json bate com o asset." : "Salve o asset (ou clique) para regravar o catálogo que o Forge lê.",
                () =>
                {
                    string path = AssetDatabase.GetAssetPath(_config);
                    if (!string.IsNullOrEmpty(path))
                        BuildProfileCatalog.Write(_config, path);
                    RebuildUI();
                });

            var pending = MigrationChecklist.Build(_config, LegacyMigrationRecord.Load());
            if (pending.Count > 0)
                AddChip($"🧭 Migração: {pending.Count} pendência(s)", string.Join("\n", pending.Select(i => "• " + i.Text)),
                    () => Navigate(BuildPipelineSections.Project, null));

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            bar.Add(spacer);

            var refresh = new Button(RebuildUI) { tooltip = "Recalcular" };
            BuildPipelineUIStyle.ApplyIconText(refresh, "🔄");
            refresh.AddToClassList("bp-health-chip");
            bar.Add(refresh);
            return bar;
        }
    }

    /// <summary>What every section receives: the config, the persisted selection and navigation callbacks.</summary>
    internal sealed class BuildPipelineSectionContext
    {
        public readonly ProjectBuildConfig Config;
        public readonly BuildPipelineSelectionState Selection;
        /// <summary>Rebuilds the whole window (header, health bar, sidebar, section).</summary>
        public readonly Action RebuildAll;
        /// <summary>Rebuilds only the current section.</summary>
        public readonly Action RebuildSection;
        private readonly Action<string, string> _navigate;

        public BuildPipelineSectionContext(ProjectBuildConfig config, BuildPipelineSelectionState selection,
            Action rebuildAll, Action rebuildSection, Action<string, string> navigate)
        {
            Config = config;
            Selection = selection;
            RebuildAll = rebuildAll;
            RebuildSection = rebuildSection;
            _navigate = navigate;
        }

        public void Navigate(string section, string profileId = null) => _navigate(section, profileId);

        public void SaveSelection() => Selection.Save(Config);

        /// <summary>Marks the config dirty, saves it and regenerates the catalog the Forge reads.</summary>
        public void SaveConfig(bool rebuildAll = true)
        {
            EditorUtility.SetDirty(Config);
            AssetDatabase.SaveAssetIfDirty(Config);
            string path = AssetDatabase.GetAssetPath(Config);
            if (!string.IsNullOrEmpty(path))
                BuildProfileCatalog.Write(Config, path);

            if (rebuildAll) RebuildAll();
            else RebuildSection();
        }
    }

    /// <summary>Snapshot shown in the health bar.</summary>
    internal sealed class BuildPipelineHealth
    {
        public int Errors;
        public int Warnings;
        public int GlobalChanges;
        public bool CatalogUpToDate;
        public string Summary = "";
        public string GlobalSummary = "";
        public List<BuildProfileIssue> Issues = new List<BuildProfileIssue>();

        public static BuildPipelineHealth Compute(ProjectBuildConfig config)
        {
            var health = new BuildPipelineHealth();
            if (config == null)
                return health;

            try
            {
                var registry = DefineRegistry.Load(config);
                health.Issues = BuildProfileValidator.Validate(config, BuildProfileCatalog.ReadTrackingPolicy(), registry);
                health.Errors = health.Issues.Count(i => i.Severity == BuildProfileIssueSeverity.Error);
                health.Warnings = health.Issues.Count - health.Errors;
                health.Summary = string.Join("\n", health.Issues.Take(12).Select(i => i.ToString())) +
                                 (health.Issues.Count > 12 ? $"\n… +{health.Issues.Count - 12}" : "");
            }
            catch (Exception ex)
            {
                health.Summary = "Validador falhou: " + ex.Message;
            }

            var diffs = DefineGlobalSnapshot.Diff();
            health.GlobalChanges = diffs.Sum(d => d.Added.Count + d.Removed.Count);
            health.GlobalSummary = string.Join("\n", diffs.Select(d =>
                $"{d.Group}: +[{string.Join(", ", d.Added)}] -[{string.Join(", ", d.Removed)}]"));

            string path = AssetDatabase.GetAssetPath(config);
            health.CatalogUpToDate = !string.IsNullOrEmpty(path) && BuildProfileCatalog.IsUpToDate(config, path);
            return health;
        }
    }
}
