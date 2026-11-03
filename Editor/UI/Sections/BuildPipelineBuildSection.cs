using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Build section (Quick Single Build + Matrix Batch Builder).
    /// Click ⚡ Build on any profile row to compile immediately with active single overrides.
    /// Check profile boxes to assemble a multi-target batch; the Execution Plan displays
    /// the full combination (Profile × Language × Variant × Cheat) before running.
    /// </summary>
    internal sealed class BuildPipelineBuildSection
    {
        private const string AutoBumpPrefs = "BuildPipeline_AutoBumpOnBuild";
        private const string ProfileLanguageChoice = "Profile / Project Default";

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
                "⚡ Quick Build compiles a single profile instantly. Select checkboxes to build a Batch Matrix; the Plan table previews all generated builds, languages, and defines before execution.",
                "info"));

            Root.Add(BuildOptionsCard());
            Root.Add(BuildProfilesCard());

            _planHost = new VisualElement();
            Root.Add(_planHost);
            RefreshPlan();
        }

        // ── Options ──────────────────────────────────────────────────────────

        private VisualElement BuildOptionsCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("⚙️ Build Execution Settings",
                "Transient settings for upcoming builds. Profile assets are never permanently modified.");

            // ── 1. Single Build Overrides Box ────────────────────────────────
            var singleBox = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(0, 0, 0, 0.16f),
                    borderWidth = 1,
                    borderColor = new Color(1, 1, 1, 0.08f),
                    borderRadius = 6,
                    paddingTop = 8,
                    paddingBottom = 8,
                    paddingLeft = 12,
                    paddingRight = 12,
                    marginBottom = 10
                }
            };

            var singleHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 8 } };
            var singleTitle = new Label("⚡ Quick Single Build Options (applies when clicking ⚡ Build on any profile)")
            {
                style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.9f, 0.95f, 1f) }
            };
            singleHeader.Add(singleTitle);
            singleBox.Add(singleHeader);

            var singleControls = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap } };

            var languageChoices = new List<string> { ProfileLanguageChoice };
            languageChoices.AddRange(Enum.GetNames(typeof(GameLanguage)));
            string current = string.IsNullOrEmpty(_selection.buildLanguageOverride) ? ProfileLanguageChoice : _selection.buildLanguageOverride;
            if (current == "Do perfil / padrão do projeto") current = ProfileLanguageChoice;

            var languageField = new PopupField<string>("Language Override", languageChoices, languageChoices.Contains(current) ? current : ProfileLanguageChoice)
            {
                tooltip = "Override for the single build. Profiles with 🔒 lock can still be overridden here (logged in the manifest)."
            };
            languageField.style.width = 280;
            languageField.style.marginRight = 16;
            languageField.RegisterValueChangedCallback(evt =>
            {
                _selection.buildLanguageOverride = evt.newValue == ProfileLanguageChoice ? "" : evt.newValue;
                _ctx.SaveSelection();
            });
            singleControls.Add(languageField);

            var cheat = new Toggle("🪄 Cheat Mode") { value = _cheat, style = { marginRight = 16 } };
            cheat.tooltip = "Enables cheat codes / debug hotkeys in this single build";
            cheat.RegisterValueChangedCallback(evt => _cheat = evt.newValue);
            singleControls.Add(cheat);

            var apk = new Toggle("🤖 Android: Install & Launch APK") { value = _apkAutoRun, style = { marginRight = 16 } };
            apk.tooltip = "After building APK, automatically install and launch on connected Android device via adb";
            apk.RegisterValueChangedCallback(evt => _apkAutoRun = evt.newValue);
            singleControls.Add(apk);

            var bump = new Toggle("🔢 Auto-increment Build #") { value = EditorPrefs.GetBool(AutoBumpPrefs, false), style = { marginRight = 16 } };
            bump.tooltip = "Automatically increment bundleVersionCode / buildNumber on build";
            bump.RegisterValueChangedCallback(evt => EditorPrefs.SetBool(AutoBumpPrefs, evt.newValue));
            singleControls.Add(bump);

            var dev = new Toggle("🐞 Development Build") { value = _selection.matrixDevBuild };
            dev.tooltip = "Enable Unity Development Build and script debugging";
            dev.RegisterValueChangedCallback(evt =>
            {
                _selection.matrixDevBuild = evt.newValue;
                _ctx.SaveSelection();
                RefreshPlan();
            });
            singleControls.Add(dev);

            singleBox.Add(singleControls);
            card.Add(singleBox);

            // ── 2. Batch Matrix Planner Box ──────────────────────────────────
            card.Add(BuildBatchOptions());

            // ── 3. Variants (if gameConfig has them) ─────────────────────────
            if (_config.gameConfig != null && _config.gameConfig.HasBuildVariants)
                card.Add(BuildVariantOptions());

            return card;
        }

        private VisualElement BuildBatchOptions()
        {
            var box = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(0, 0, 0, 0.16f),
                    borderWidth = 1,
                    borderColor = new Color(1, 1, 1, 0.08f),
                    borderRadius = 6,
                    paddingTop = 8,
                    paddingBottom = 8,
                    paddingLeft = 12,
                    paddingRight = 12,
                    marginBottom = 4
                }
            };

            // Top Header: Title and Cheats
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };

            var leftTitle = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var batchTitle = new Label("📦 Batch Matrix Options (Profiles × Languages × Cheats)")
            {
                style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.9f, 0.95f, 1f), marginRight = 12 }
            };
            leftTitle.Add(batchTitle);
            header.Add(leftTitle);

            var rightCheats = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            rightCheats.Add(new Label("Matrix Cheats:") { style = { fontSize = 11, color = new Color(0.7f, 0.75f, 0.8f), marginRight = 8 } });
            var cheatOff = new Toggle("Standard (No Cheat)") { value = _selection.matrixCheatOff, style = { marginRight = 12 } };
            cheatOff.RegisterValueChangedCallback(evt => { _selection.matrixCheatOff = evt.newValue; _ctx.SaveSelection(); RefreshPlan(); });
            rightCheats.Add(cheatOff);

            var cheatOn = new Toggle("With Cheat") { value = _selection.matrixCheatOn };
            cheatOn.RegisterValueChangedCallback(evt => { _selection.matrixCheatOn = evt.newValue; _ctx.SaveSelection(); RefreshPlan(); });
            rightCheats.Add(cheatOn);
            header.Add(rightCheats);

            box.Add(header);

            // Sub-header for languages with action buttons
            var langBar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 6,
                    paddingTop = 6,
                    borderTopWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.05f)
                }
            };
            langBar.Add(new Label("Target Languages:") { style = { fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, width = 120, color = new Color(0.85f, 0.9f, 0.95f) } });

            langBar.Add(SmallButton("Select All", () => SetAll(_languagePicks, true, () => _selection.CaptureLanguages(_config, _languagePicks))));
            langBar.Add(SmallButton("Clear All", () => SetAll(_languagePicks, false, () => _selection.CaptureLanguages(_config, _languagePicks))));

            int activeCount = _languagePicks.Count(p => p);
            var countBadge = new Label($"{activeCount} selected")
            {
                style = { fontSize = 10, color = new Color(0.55f, 0.85f, 0.55f), marginLeft = 8 }
            };
            langBar.Add(countBadge);
            box.Add(langBar);

            // Clean flex grid of languages: aligned columns
            var languagesGrid = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    marginLeft = 4,
                    marginBottom = 4
                }
            };

            for (int i = 0; i < _config.languages.Count; i++)
            {
                var language = _config.languages[i];
                if (language == null)
                    continue;

                int index = i;
                string langName = language.language == GameLanguage.AutoDetect ? "🌐 AutoDetect" : language.language.ToString();
                var toggle = new Toggle(langName)
                {
                    value = _languagePicks[index],
                    style =
                    {
                        width = 115,
                        marginBottom = 4,
                        marginRight = 6
                    }
                };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _languagePicks[index] = evt.newValue;
                    _selection.CaptureLanguages(_config, _languagePicks);
                    _ctx.SaveSelection();
                    RefreshPlan();
                });
                languagesGrid.Add(toggle);
            }

            box.Add(languagesGrid);

            var hintLabel = new Label("ℹ Profiles with a 🔒 locked language always compile in their designated language once, ignoring this matrix selection.")
            {
                style = { fontSize = 10, color = new Color(0.6f, 0.65f, 0.7f), marginLeft = 4, marginTop = 2 }
            };
            box.Add(hintLabel);

            return box;
        }

        private VisualElement BuildVariantOptions()
        {
            var row = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(0, 0, 0, 0.16f),
                    borderWidth = 1,
                    borderColor = new Color(1, 1, 1, 0.08f),
                    borderRadius = 6,
                    paddingTop = 6,
                    paddingBottom = 6,
                    paddingLeft = 12,
                    paddingRight = 12,
                    marginTop = 8,
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    flexWrap = Wrap.Wrap
                }
            };

            var title = new Label("🎭 Build Variants:")
            {
                style = { fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, width = 120, color = new Color(0.85f, 0.9f, 0.95f) }
            };
            row.Add(title);

            var variants = _config.gameConfig.buildVariants;
            for (int i = 0; i < variants.Count; i++)
            {
                var variant = variants[i];
                if (variant == null || !variant.IsValid)
                    continue;

                int index = i;
                var toggle = new Toggle(variant.EffectiveDisplayName)
                {
                    value = _variantPicks[index],
                    style = { marginRight = 12 }
                };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _variantPicks[index] = evt.newValue;
                    _selection.CaptureVariants(_config, _variantPicks);
                    _ctx.SaveSelection();
                    RefreshPlan();
                });
                row.Add(toggle);
            }

            row.Add(new Label("(Single build ⚡ uses the first selected variant)")
            {
                style = { fontSize = 10, color = new Color(0.6f, 0.65f, 0.7f), marginLeft = 6 }
            });
            return row;
        }

        // ── Profiles ─────────────────────────────────────────────────────────

        private VisualElement BuildProfilesCard()
        {
            var showHidden = new Toggle("Show Hidden Profiles") { value = _selection.showHiddenProfiles };
            showHidden.RegisterValueChangedCallback(evt =>
            {
                _selection.showHiddenProfiles = evt.newValue;
                _ctx.SaveSelection();
                _ctx.RebuildSection();
            });

            var card = BuildPipelineUIStyle.CreateCard("📋 Build Profiles",
                "Grouped by platform. Click ⚡ Build to build immediately, or check profiles to include in the Batch Plan below.",
                showHidden);

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
            actions.Add(SmallButton("Select All Visible", () => PickVisible(true)));
            actions.Add(SmallButton("Deselect All", () => PickVisible(false)));
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
            pick.tooltip = "Include in Batch Matrix plan";
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

            var edit = SmallButton("📋 Edit", () => _ctx.Navigate(BuildPipelineSections.Profiles, profile.EffectiveId));
            edit.tooltip = "Configure this profile in the Profiles editor";
            row.Add(edit);

            var openFolder = SmallButton("📂 Open Folder", () => BuildOutputFolderActions.Open(_config, profile, profile.platform));
            openFolder.tooltip = "Open build output folder in file explorer:\n" + ProfileOutputPaths.ResolveFolder(_config, profile, profile.platform);
            openFolder.style.marginLeft = 6;
            row.Add(openFolder);

            var build = new Button(() => RunSingle(profile));
            BuildPipelineUIStyle.ApplyIconText(build, "⚡ Build");
            build.AddToClassList("bp-btn");
            build.AddToClassList("bp-btn--primary");
            build.style.marginLeft = 6;
            build.tooltip = $"Instantly compile {profile.displayName} with active single build options";
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

            var run = new Button(() => RunPlan(plan)) { tooltip = "Confirm and run batch builds in sequence" };
            BuildPipelineUIStyle.ApplyIconText(run, $"🚀 Build Batch ({plan.Count} build{(plan.Count == 1 ? "" : "s")})");
            run.AddToClassList("bp-btn");
            run.AddToClassList("bp-btn--success");
            run.SetEnabled(plan.Count > 0);

            var card = BuildPipelineUIStyle.CreateCard($"🏭 Batch Execution Plan — {plan.Count} build{(plan.Count == 1 ? "" : "s")}",
                plan.Count == 0 ? "Select profiles, target languages, and at least one Cheat option above to generate the plan." : "Review scheduled builds below. They will compile in this exact sequence.", run);

            var header = new VisualElement();
            header.AddToClassList("bp-table-header");
            header.Add(Cell("Profile", 220, true));
            header.Add(Cell("Language", 220, true));
            header.Add(Cell("Variant", 110, true));
            header.Add(Cell("Cheat", 60, true));
            header.Add(Cell("Final Scripting Defines", 0, true));
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
                row.Add(Cell(item.Cheat ? "Yes" : "—", 60));
                var defines = Cell(stack.Added.Count == 0 && stack.Removed.Count == 0 ? "(globals only)" :
                    "+" + string.Join(" +", stack.Added) + (stack.Removed.Count > 0 ? "  −" + string.Join(" −", stack.Removed) : ""), 0);
                defines.tooltip = "Final defines: " + string.Join("; ", stack.Final);
                row.Add(defines);
                card.Add(row);
            }

            if (plan.Count > 200)
                card.Add(new Label($"… and {plan.Count - 200} more.") { style = { paddingLeft = 8, paddingTop = 4, color = new Color(0.6f, 0.6f, 0.65f) } });

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

            if (!BuildConfirmation.Confirm(context, _config, "Confirm Build"))
                return;

            if (EditorPrefs.GetBool(AutoBumpPrefs, false))
                BumpBuildNumber(profile.platform);

            var result = BuildPipelineRunner.Execute(context);
            string durationText = string.Format("{0:00}:{1:00}", (int)result.Duration.TotalMinutes, result.Duration.Seconds);
            EditorUtility.DisplayDialog(result.Success ? "Build Successful" : "Build Failed",
                result.Success
                    ? profile.displayName + " completed in " + durationText + ".\n" + result.OutputPath
                    : profile.displayName + ": " + result.TotalErrors + " error(s). " + result.ErrorMessage + "\nCheck the Console.",
                "OK");
            _ctx.RebuildAll();
        }

        private void RunPlan(List<PlannedBuild> plan)
        {
            if (plan.Count == 0)
                return;

            var summaryLines = plan.Take(25).Select(item =>
            {
                var context = Create(item.Profile, item.LanguageOverride, item.Variant, item.Cheat, false);
                return "• " + item.Profile.EffectiveId + " · " + context.Language +
                       (item.Variant != null ? " · " + item.Variant.EffectiveId : "") + (item.Cheat ? " · CHEAT" : "");
            }).ToList();
            if (plan.Count > 25) summaryLines.Add("… +" + (plan.Count - 25));
            string summary = string.Join("\n", summaryLines);

            if (!BuildConfirmation.ConfirmBatch("Execute " + plan.Count + " build(s)", summary))
                return;

            var failed = new List<string>();
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    var item = plan[i];
                    var context = Create(item.Profile, item.LanguageOverride, item.Variant, item.Cheat, false);
                    string label = "[" + (i + 1) + "/" + plan.Count + "] " + item.Profile.displayName + " (" + context.Language + ")";
                    if (EditorUtility.DisplayCancelableProgressBar("Batch Building", label, (float)i / plan.Count))
                    {
                        failed.Add("(cancelled by user)");
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

            EditorUtility.DisplayDialog("Batch Execution Finished",
                failed.Count == 0 ? plan.Count + " build(s) completed successfully." : "Failed builds:\n" + string.Join("\n", failed), "OK");
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
                    ? $"{Title} ({VisibleCount} visible · {HiddenCount} hidden)"
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

            // 1. Dedicated group for Steam (Windows / macOS / Linux)
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

            // 2. Platform groups (excluding Steam profiles already grouped above)
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
                badges.Add(Badge($"Steam ({plat})", "info", $"Steam profile for {profile.platform}"));
            }
            if (profile.IsLanguageLocked) badges.Add(Badge("🔒 " + profile.lockedLanguage.AsLanguageCode().ToUpperInvariant(), "warn", $"Language locked to {profile.lockedLanguage}"));
            if (profile.hidden) badges.Add(Badge("hidden", "warn", "Hidden from list view; CI can still build by ID."));

            var resolved = BuildDefineResolver.Resolve(config, profile);
            if (resolved.Count > 0)
                badges.Add(Badge($"🧬 {resolved.Count}", "ok", "Profile defines (presets + local):\n" + string.Join("\n", resolved)));

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
