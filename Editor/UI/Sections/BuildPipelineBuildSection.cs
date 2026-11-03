using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Build section (Quick Build + Matrix in one place). Every profile is a row: ⚡ builds it now, the checkbox
    /// adds it to the batch. The plan table shows exactly what the batch will produce (profile × language ×
    /// variant × cheat, with the language source and the final defines) before anything runs.
    /// </summary>
    internal sealed class BuildPipelineBuildSection
    {
        private const string AutoBumpPrefs = "BuildPipeline_AutoBumpOnBuild";
        private const string ProfileLanguageChoice = "Do perfil / padrão do projeto";

        private readonly BuildPipelineSectionContext _ctx;
        private readonly ProjectBuildConfig _config;
        private readonly BuildPipelineSelectionState _selection;
        private bool[] _profilePicks;
        private bool[] _languagePicks;
        private bool[] _variantPicks;
        private bool _cheat;
        private bool _apkAutoRun;
        private VisualElement _planHost;

        public VisualElement Root { get; } = new VisualElement();

        public BuildPipelineBuildSection(BuildPipelineSectionContext ctx)
        {
            _ctx = ctx;
            _config = ctx.Config;
            _selection = ctx.Selection;
            _profilePicks = _selection.ResolvePublishers(_config);
            _languagePicks = _selection.ResolveLanguages(_config);
            _variantPicks = _selection.ResolveVariants(_config);

            Root.Add(BuildPipelineUIStyle.CreateCallout(
                "⚡ compila um perfil agora. Marque vários perfis para montar um lote: a tabela 'Plano' mostra cada build, " +
                "o idioma (e de onde ele veio) e os defines finais antes de executar.", "info"));

            Root.Add(BuildOptionsCard());
            Root.Add(BuildProfilesCard());

            _planHost = new VisualElement();
            Root.Add(_planHost);
            RefreshPlan();
        }

        // ── Options ──────────────────────────────────────────────────────────

        private VisualElement BuildOptionsCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("Opções", "Valem para o próximo build; nada aqui é gravado nos perfis.");

            var single = Row();
            single.Add(RowTitle("⚡ Build individual"));

            var languageChoices = new List<string> { ProfileLanguageChoice };
            languageChoices.AddRange(Enum.GetNames(typeof(GameLanguage)));
            string current = string.IsNullOrEmpty(_selection.buildLanguageOverride) ? ProfileLanguageChoice : _selection.buildLanguageOverride;
            var languageField = new PopupField<string>("Idioma", languageChoices, languageChoices.Contains(current) ? current : ProfileLanguageChoice)
            {
                tooltip = "Override só para este build. Perfis travados mostram 🔒; escolher outro idioma aqui ainda vence o lock (fica registrado no manifest)."
            };
            languageField.style.minWidth = 300;
            languageField.RegisterValueChangedCallback(evt =>
            {
                _selection.buildLanguageOverride = evt.newValue == ProfileLanguageChoice ? "" : evt.newValue;
                _ctx.SaveSelection();
            });
            single.Add(languageField);

            var cheat = new Toggle("Cheat") { value = _cheat, style = { marginLeft = 12 } };
            cheat.RegisterValueChangedCallback(evt => _cheat = evt.newValue);
            single.Add(cheat);

            var apk = new Toggle("Android: APK + instalar/rodar") { value = _apkAutoRun, style = { marginLeft = 12 } };
            apk.RegisterValueChangedCallback(evt => _apkAutoRun = evt.newValue);
            single.Add(apk);

            var bump = new Toggle("Auto-incrementar build number") { value = EditorPrefs.GetBool(AutoBumpPrefs, false), style = { marginLeft = 12 } };
            bump.RegisterValueChangedCallback(evt => EditorPrefs.SetBool(AutoBumpPrefs, evt.newValue));
            single.Add(bump);
            card.Add(single);

            card.Add(BuildBatchOptions());

            if (_config.gameConfig != null && _config.gameConfig.HasBuildVariants)
                card.Add(BuildVariantOptions());

            var dev = new Toggle("Development Build (individual e lote)") { value = _selection.matrixDevBuild };
            dev.RegisterValueChangedCallback(evt =>
            {
                _selection.matrixDevBuild = evt.newValue;
                _ctx.SaveSelection();
                RefreshPlan();
            });
            card.Add(dev);
            return card;
        }

        private VisualElement BuildBatchOptions()
        {
            var box = new VisualElement { style = { marginTop = 6 } };
            var header = Row();
            header.Add(RowTitle("📦 Lote: idiomas"));
            header.Add(SmallButton("Todos", () => SetAll(_languagePicks, true, () => _selection.CaptureLanguages(_config, _languagePicks))));
            header.Add(SmallButton("Nenhum", () => SetAll(_languagePicks, false, () => _selection.CaptureLanguages(_config, _languagePicks))));

            var cheatOff = new Toggle("sem Cheat") { value = _selection.matrixCheatOff, style = { marginLeft = 12 } };
            cheatOff.RegisterValueChangedCallback(evt => { _selection.matrixCheatOff = evt.newValue; _ctx.SaveSelection(); RefreshPlan(); });
            header.Add(cheatOff);
            var cheatOn = new Toggle("com Cheat") { value = _selection.matrixCheatOn, style = { marginLeft = 6 } };
            cheatOn.RegisterValueChangedCallback(evt => { _selection.matrixCheatOn = evt.newValue; _ctx.SaveSelection(); RefreshPlan(); });
            header.Add(cheatOn);
            box.Add(header);

            var languages = Row();
            languages.style.flexWrap = Wrap.Wrap;
            languages.style.marginLeft = 130;
            for (int i = 0; i < _config.languages.Count; i++)
            {
                var language = _config.languages[i];
                if (language == null)
                    continue;

                int index = i;
                var toggle = new Toggle(language.language == GameLanguage.AutoDetect ? "AutoDetect" : language.language.ToString())
                {
                    value = _languagePicks[index],
                    style = { marginRight = 10 }
                };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _languagePicks[index] = evt.newValue;
                    _selection.CaptureLanguages(_config, _languagePicks);
                    _ctx.SaveSelection();
                    RefreshPlan();
                });
                languages.Add(toggle);
            }

            box.Add(languages);
            box.Add(new Label("Perfis 🔒 travados ignoram esta lista e compilam só o idioma deles, uma vez.")
            {
                style = { marginLeft = 130, fontSize = 10, color = new Color(0.65f, 0.68f, 0.72f) }
            });
            return box;
        }

        private VisualElement BuildVariantOptions()
        {
            var row = Row();
            row.style.marginTop = 6;
            row.Add(RowTitle("🎭 Variantes"));

            var variants = _config.gameConfig.buildVariants;
            for (int i = 0; i < variants.Count; i++)
            {
                var variant = variants[i];
                if (variant == null || !variant.IsValid)
                    continue;

                int index = i;
                var toggle = new Toggle(variant.EffectiveDisplayName) { value = _variantPicks[index], style = { marginRight = 10 } };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _variantPicks[index] = evt.newValue;
                    _selection.CaptureVariants(_config, _variantPicks);
                    _ctx.SaveSelection();
                    RefreshPlan();
                });
                row.Add(toggle);
            }

            row.Add(new Label("(o ⚡ usa a primeira marcada)") { style = { fontSize = 10, color = new Color(0.65f, 0.68f, 0.72f) } });
            return row;
        }

        // ── Profiles ─────────────────────────────────────────────────────────

        private VisualElement BuildProfilesCard()
        {
            var showHidden = new Toggle("Mostrar ocultos") { value = _selection.showHiddenProfiles };
            showHidden.RegisterValueChangedCallback(evt =>
            {
                _selection.showHiddenProfiles = evt.newValue;
                _ctx.SaveSelection();
                _ctx.RebuildSection();
            });

            var card = BuildPipelineUIStyle.CreateCard("Perfis", "Agrupados por plataforma. O lote compila os marcados.", showHidden);

            var issues = BuildPipelineHealth.Compute(_config).Issues;
            foreach (var group in ProfileGroups.Build(_config, _selection.showHiddenProfiles))
            {
                var header = BuildPipelineUIStyle.CreateText(group.SummaryText, "bp-group-title");
                card.Add(header);

                foreach (int index in group.Indices)
                    card.Add(BuildProfileRow(index, issues));
            }

            var actions = Row();
            actions.style.marginTop = 8;
            actions.Add(SmallButton("Marcar visíveis", () => PickVisible(true)));
            actions.Add(SmallButton("Desmarcar todos", () => PickVisible(false)));
            card.Add(actions);
            return card;
        }

        private VisualElement BuildProfileRow(int index, List<BuildProfileIssue> issues)
        {
            var profile = _config.publishers[index];
            var row = new VisualElement();
            row.AddToClassList("bp-profile-row");
            if (profile.hidden) row.AddToClassList("bp-profile-row--hidden");

            var pick = new Toggle { value = index < _profilePicks.Length && _profilePicks[index] };
            pick.RegisterValueChangedCallback(evt =>
            {
                _profilePicks[index] = evt.newValue;
                _selection.CapturePublishers(_config, _profilePicks);
                _ctx.SaveSelection();
                RefreshPlan();
            });
            row.Add(pick);

            var name = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
            name.Add(new Label(profile.displayName) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            name.Add(new Label(profile.EffectiveId) { style = { fontSize = 10, color = new Color(0.6f, 0.63f, 0.68f) } });
            row.Add(name);

            foreach (var badge in ProfileBadges.For(_config, profile, issues))
                row.Add(badge);

            var edit = SmallButton("📋", () => _ctx.Navigate(BuildPipelineSections.Profiles, profile.EffectiveId));
            edit.tooltip = "Abrir o perfil";
            row.Add(edit);

            var openFolder = SmallButton("📂 Open Folder", () => OpenProfileFolder(profile));
            openFolder.tooltip = "Abrir a pasta de saída:\n" + ProfileOutputPaths.ResolveFolder(_config, profile, profile.platform);
            openFolder.style.marginLeft = 6;
            row.Add(openFolder);

            var build = new Button(() => RunSingle(profile));
            BuildPipelineUIStyle.ApplyIconText(build, "⚡ Build");
            build.AddToClassList("bp-btn");
            build.AddToClassList("bp-btn--primary");
            build.style.marginLeft = 6;
            row.Add(build);
            return row;
        }

        private void PickVisible(bool value)
        {
            for (int i = 0; i < _config.publishers.Count && i < _profilePicks.Length; i++)
            {
                var profile = _config.publishers[i];
                if (profile != null && (_selection.showHiddenProfiles || !profile.hidden))
                    _profilePicks[i] = value;
            }

            _selection.CapturePublishers(_config, _profilePicks);
            _ctx.SaveSelection();
            _ctx.RebuildSection();
        }

        /// <summary>Opens the profile's output folder (creating it on demand when it does not exist yet).</summary>
        private void OpenProfileFolder(PublisherProfile profile)
        {
            string folder = ProfileOutputPaths.ResolveFolder(_config, profile, profile.platform);
            try
            {
                if (!Directory.Exists(folder))
                {
                    if (!EditorUtility.DisplayDialog("Open Folder",
                            $"A pasta de saída ainda não existe:\n\n{folder}\n\nCriar a pasta agora?",
                            "Criar e abrir", "Cancelar"))
                        return;

                    Directory.CreateDirectory(folder);
                }

                EditorUtility.RevealInFinder(folder);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Open Folder",
                    $"Não foi possível abrir:\n\n{folder}\n\n{ex.Message}", "OK");
            }
        }

        // ── Plan ─────────────────────────────────────────────────────────────

        private sealed class PlannedBuild
        {
            public PublisherProfile Profile;
            public GameLanguage? LanguageOverride;
            public BuildVariant Variant;
            public bool Cheat;
        }

        private List<PlannedBuild> Plan()
        {
            var plan = new List<PlannedBuild>();
            var languages = _config.languages.Where((l, i) => l != null && i < _languagePicks.Length && _languagePicks[i]).Select(l => l.language).ToList();
            var variants = SelectedVariants();
            var cheats = new List<bool>();
            if (_selection.matrixCheatOff) cheats.Add(false);
            if (_selection.matrixCheatOn) cheats.Add(true);

            for (int i = 0; i < _config.publishers.Count && i < _profilePicks.Length; i++)
            {
                var profile = _config.publishers[i];
                if (profile == null || !_profilePicks[i] || (profile.hidden && !_selection.showHiddenProfiles))
                    continue;

                var profileLanguages = profile.IsLanguageLocked
                    ? new List<GameLanguage?> { null }
                    : languages.Select(l => (GameLanguage?)l).ToList();

                foreach (var language in profileLanguages)
                    foreach (var variant in variants)
                        foreach (bool cheat in cheats)
                            plan.Add(new PlannedBuild { Profile = profile, LanguageOverride = language, Variant = variant, Cheat = cheat });
            }

            return plan;
        }

        private List<BuildVariant> SelectedVariants()
        {
            var variants = new List<BuildVariant>();
            var declared = _config.gameConfig?.buildVariants;
            if (declared != null)
            {
                for (int i = 0; i < declared.Count && i < _variantPicks.Length; i++)
                    if (declared[i] != null && declared[i].IsValid && _variantPicks[i])
                        variants.Add(declared[i]);
            }

            if (variants.Count == 0)
                variants.Add(null);

            return variants;
        }

        private void RefreshPlan()
        {
            if (_planHost == null)
                return;

            _planHost.Clear();
            var plan = Plan();

            var run = new Button(() => RunPlan(plan)) { tooltip = "Confirma e executa em sequência" };
            BuildPipelineUIStyle.ApplyIconText(run, $"🏭 Executar {plan.Count} build(s)");
            run.AddToClassList("bp-btn");
            run.AddToClassList("bp-btn--success");
            run.SetEnabled(plan.Count > 0);

            var card = BuildPipelineUIStyle.CreateCard($"Plano do lote — {plan.Count} build(s)",
                plan.Count == 0 ? "Marque perfis, idiomas e ao menos uma opção de Cheat." : "Exatamente o que será compilado, nesta ordem.", run);

            var header = new VisualElement();
            header.AddToClassList("bp-table-header");
            header.Add(Cell("Perfil", 220, true));
            header.Add(Cell("Idioma", 220, true));
            header.Add(Cell("Variante", 110, true));
            header.Add(Cell("Cheat", 50, true));
            header.Add(Cell("Defines finais", 0, true));
            card.Add(header);

            foreach (var item in plan.Take(200))
            {
                var context = Create(item.Profile, item.LanguageOverride, item.Variant, item.Cheat, false);
                var stack = DefineEnvironment.StackFor(_config, item.Profile);

                var row = new VisualElement();
                row.AddToClassList("bp-table-row");
                row.Add(Cell(item.Profile.displayName, 220));
                row.Add(Cell(BuildLanguageResolver.Describe(context.Language, context.LanguageSource), 220));
                row.Add(Cell(item.Variant != null ? item.Variant.EffectiveDisplayName : "—", 110));
                row.Add(Cell(item.Cheat ? "sim" : "—", 50));
                var defines = Cell(stack.Added.Count == 0 && stack.Removed.Count == 0 ? "(só globais)" :
                    "+" + string.Join(" +", stack.Added) + (stack.Removed.Count > 0 ? "  −" + string.Join(" −", stack.Removed) : ""), 0);
                defines.tooltip = "Final: " + string.Join("; ", stack.Final);
                row.Add(defines);
                card.Add(row);
            }

            if (plan.Count > 200)
                card.Add(new Label($"… e mais {plan.Count - 200}."));

            _planHost.Add(card);
        }

        // ── Running ──────────────────────────────────────────────────────────

        private BuildContext Create(PublisherProfile profile, GameLanguage? languageOverride, BuildVariant variant, bool cheat, bool single)
        {
            return BuildContextFactory.Create(_config, profile, new BuildContextFactory.Options
            {
                LanguageOverride = languageOverride,
                Variant = variant,
                VariantId = variant != null ? variant.EffectiveId : "",
                CheatMode = cheat,
                DevelopmentBuild = _selection.matrixDevBuild,
                AutoRun = single && _apkAutoRun && profile.platform == PlatformType.Android,
                AppBundle = !(single && _apkAutoRun)
            });
        }

        private void RunSingle(PublisherProfile profile)
        {
            var languageOverride = BuildLanguageResolver.Parse(_selection.buildLanguageOverride);
            var variant = SelectedVariants().FirstOrDefault();
            var context = Create(profile, languageOverride, variant, _cheat, true);

            if (!BuildConfirmation.Confirm(context, _config, "Confirmar build"))
                return;

            if (EditorPrefs.GetBool(AutoBumpPrefs, false))
                BumpBuildNumber(profile.platform);

            var result = BuildPipelineRunner.Execute(context);
            EditorUtility.DisplayDialog(result.Success ? "Build concluído" : "Build falhou",
                result.Success
                    ? $"{profile.displayName} em {result.Duration:mm\\:ss}.\n{result.OutputPath}"
                    : $"{profile.displayName}: {result.TotalErrors} erro(s). {result.ErrorMessage}\nVeja o Console.",
                "OK");
            _ctx.RebuildAll();
        }

        private void RunPlan(List<PlannedBuild> plan)
        {
            if (plan.Count == 0)
                return;

            string summary = string.Join("\n", plan.Take(25).Select(item =>
            {
                var context = Create(item.Profile, item.LanguageOverride, item.Variant, item.Cheat, false);
                return $"• {item.Profile.EffectiveId} · {context.Language}" +
                       (item.Variant != null ? $" · {item.Variant.EffectiveId}" : "") + (item.Cheat ? " · CHEAT" : "");
            })) + (plan.Count > 25 ? $"\n… +{plan.Count - 25}" : "");

            if (!BuildConfirmation.ConfirmBatch($"Executar {plan.Count} build(s)", summary))
                return;

            var failed = new List<string>();
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    var item = plan[i];
                    var context = Create(item.Profile, item.LanguageOverride, item.Variant, item.Cheat, false);
                    string label = $"[{i + 1}/{plan.Count}] {item.Profile.displayName} ({context.Language})";
                    if (EditorUtility.DisplayCancelableProgressBar("Build em lote", label, (float)i / plan.Count))
                    {
                        failed.Add("(cancelado pelo usuário)");
                        break;
                    }

                    if (!BuildPipelineRunner.Execute(context).Success)
                        failed.Add(label);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("Lote finalizado",
                failed.Count == 0 ? $"{plan.Count} build(s) concluído(s)." : "Falharam:\n" + string.Join("\n", failed), "OK");
            _ctx.RebuildAll();
        }

        private void BumpBuildNumber(PlatformType platform)
        {
            if (platform == PlatformType.Android)
            {
                int next = Math.Max(0, PlayerSettings.Android.bundleVersionCode) + 1;
                PlayerSettings.Android.bundleVersionCode = next;
                if (_config.gameConfig != null)
                {
                    _config.gameConfig.AndroidBundleVersionCode = next;
                    EditorUtility.SetDirty(_config.gameConfig);
                }
            }
            else if (platform == PlatformType.iOS || platform == PlatformType.macOS)
            {
                int.TryParse(PlayerSettings.iOS.buildNumber, out int current);
                string next = (current + 1).ToString();
                PlayerSettings.iOS.buildNumber = next;
                PlayerSettings.macOS.buildNumber = next;
                if (_config.gameConfig != null)
                {
                    _config.gameConfig.iOSBuildNumber = next;
                    EditorUtility.SetDirty(_config.gameConfig);
                }
            }

            AssetDatabase.SaveAssets();
        }

        // ── Small helpers ────────────────────────────────────────────────────

        private void SetAll(bool[] picks, bool value, Action capture)
        {
            for (int i = 0; i < picks.Length; i++) picks[i] = value;
            capture();
            _ctx.SaveSelection();
            _ctx.RebuildSection();
        }

        private static VisualElement RowTitle(string text)
        {
            var title = BuildPipelineUIStyle.CreateText(text);
            title.style.width = 130;
            title.style.flexShrink = 0;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            return title;
        }

        private static VisualElement Row() =>
            new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap, marginBottom = 2 } };

        internal static Button SmallButton(string text, Action action)
        {
            var button = new Button(action);
            BuildPipelineUIStyle.ApplyIconText(button, text);
            button.AddToClassList("bp-btn");
            button.AddToClassList("bp-btn--small");
            return button;
        }

        internal static VisualElement Cell(string text, float width, bool header = false)
        {
            var label = BuildPipelineUIStyle.CreateText(text);
            label.AddToClassList(header ? "bp-table-header-cell" : "bp-table-cell");
            if (width > 0) label.style.width = width;
            else label.style.flexGrow = 1;
            label.style.flexShrink = width > 0 ? 0 : 1;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }

    /// <summary>Profiles grouped by platform (and dedicated Steam group), in a fixed, predictable order.</summary>
    internal static class ProfileGroups
    {
        public sealed class Group
        {
            public string Title;
            public List<int> Indices = new List<int>();
            public int VisibleCount;
            public int HiddenCount;
            public int TotalCount => VisibleCount + HiddenCount;

            public string SummaryText =>
                HiddenCount > 0
                    ? $"{Title} ({VisibleCount} visíve{(VisibleCount == 1 ? "l" : "is")} · {HiddenCount} oculto{(HiddenCount == 1 ? "" : "s")})"
                    : $"{Title} ({VisibleCount})";
        }

        private static readonly (PlatformType Platform, string Title)[] Order =
        {
            (PlatformType.Android, "🤖 Android"),
            (PlatformType.iOS, "📱 iOS"),
            (PlatformType.Windows64, "🪟 Windows"),
            (PlatformType.macOS, "🍎 macOS"),
            (PlatformType.Linux64, "🐧 Linux"),
            (PlatformType.WebGL, "🌐 WebGL"),
        };

        public static List<Group> Build(ProjectBuildConfig config, bool includeHidden)
        {
            var groups = new List<Group>();
            int total = config.publishers?.Count ?? 0;

            // 1. Grupo dedicado para Steam (Windows / macOS / Linux)
            var steamGroup = new Group { Title = "🎮 Steam" };
            for (int i = 0; i < total; i++)
            {
                var profile = config.publishers[i];
                if (profile != null && profile.IsSteamProfile)
                {
                    if (profile.hidden) steamGroup.HiddenCount++;
                    else steamGroup.VisibleCount++;

                    if (includeHidden || !profile.hidden)
                        steamGroup.Indices.Add(i);
                }
            }
            if (steamGroup.Indices.Count > 0)
                groups.Add(steamGroup);

            // 2. Grupos por plataforma (exclui perfis que já estão no grupo Steam)
            foreach (var (platform, title) in Order)
            {
                var group = new Group { Title = title };
                for (int i = 0; i < total; i++)
                {
                    var profile = config.publishers[i];
                    if (profile != null && !profile.IsSteamProfile && profile.platform == platform)
                    {
                        if (profile.hidden) group.HiddenCount++;
                        else group.VisibleCount++;

                        if (includeHidden || !profile.hidden)
                            group.Indices.Add(i);
                    }
                }

                if (group.Indices.Count > 0)
                    groups.Add(group);
            }

            return groups;
        }
    }

    /// <summary>The badges every profile row shows (Free/Full, 🔒 language, Steam, defines, issues).</summary>
    internal static class ProfileBadges
    {
        public static List<VisualElement> For(ProjectBuildConfig config, PublisherProfile profile, List<BuildProfileIssue> issues)
        {
            var badges = new List<VisualElement>();
            badges.Add(Badge(profile.isFullGame ? "Full" : "Free", profile.isFullGame ? "ok" : "info", null));
            if (profile.isDemo) badges.Add(Badge("Demo", "info", null));
            if (profile.isSteam)
            {
                string plat = profile.platform == PlatformType.macOS ? "macOS" : profile.platform == PlatformType.Linux64 ? "Linux" : "Win";
                badges.Add(Badge($"Steam ({plat})", "info", $"Perfil Steam para {profile.platform}"));
            }
            if (profile.IsLanguageLocked) badges.Add(Badge("🔒 " + profile.lockedLanguage.AsLanguageCode().ToUpperInvariant(), "warn", $"Idioma travado em {profile.lockedLanguage}"));
            if (profile.hidden) badges.Add(Badge("oculto", "warn", "Escondido das listas; o CI ainda builda pelo id."));

            var resolved = BuildDefineResolver.Resolve(config, profile);
            if (resolved.Count > 0)
                badges.Add(Badge($"🧬 {resolved.Count}", "ok", "Defines do perfil (presets + próprios):\n" + string.Join("\n", resolved)));

            var mine = issues?.Where(i => string.Equals(i.ProfileId, profile.EffectiveId, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<BuildProfileIssue>();
            int errors = mine.Count(i => i.Severity == BuildProfileIssueSeverity.Error);
            if (errors > 0)
                badges.Add(Badge($"⛔ {errors}", "fail", string.Join("\n", mine.Select(i => i.Message))));
            else if (mine.Count > 0)
                badges.Add(Badge($"⚠ {mine.Count}", "warn", string.Join("\n", mine.Select(i => i.Message))));

            return badges;
        }

        private static VisualElement Badge(string text, string type, string tooltip)
        {
            var badge = BuildPipelineUIStyle.CreateIconBadge(text, type);
            badge.style.marginLeft = 4;
            if (!string.IsNullOrEmpty(tooltip)) badge.tooltip = tooltip;
            return badge;
        }
    }
}
