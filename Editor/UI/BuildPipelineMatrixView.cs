using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal sealed class BuildPipelineMatrixView
    {
        public VisualElement Root { get; }

        private readonly ProjectBuildConfig _config;
        private readonly BuildPipelineSelectionState _selection;
        private readonly bool[] _selectedPublishers;
        private readonly bool[] _selectedLanguages;
        private readonly bool[] _selectedVariants;
        private bool _matrixCheatOn = false;
        private bool _matrixCheatOff = true;
        private bool _matrixDevBuild = false;

        private readonly Action _onRefresh;

        // Live references so ticking a checkbox only refreshes the summary instead of rebuilding the panel.
        // Rebuilding a UI Toolkit tree from inside a Toggle's ChangeEvent leaves the freshly created controls
        // unresponsive (they look disabled until the panel hands out a new pointer capture), which made the
        // Matrix languages impossible to tick after selecting AutoDetect.
        private Label _summaryLabel;
        private Label _variantSummaryLabel;
        private Label _totalBadge;
        private Button _buildButton;
        private readonly List<Toggle> _publisherToggles = new List<Toggle>();
        private readonly List<Toggle> _languageToggles = new List<Toggle>();
        private readonly List<Toggle> _variantToggles = new List<Toggle>();

        public BuildPipelineMatrixView(ProjectBuildConfig config, BuildPipelineSelectionState selection, Action onRefresh)
        {
            _config = config;
            _selection = selection ?? new BuildPipelineSelectionState();
            _onRefresh = onRefresh;

            // Every checkbox starts from what was last selected (persisted in EditorPrefs), falling back to
            // the config defaults on a first run.
            _selectedPublishers = _selection.ResolvePublishers(config);
            _selectedLanguages = _selection.ResolveLanguages(config);
            _selectedVariants = _selection.ResolveVariants(config);
            _matrixCheatOn = _selection.matrixCheatOn;
            _matrixCheatOff = _selection.matrixCheatOff;
            _matrixDevBuild = _selection.matrixDevBuild;

            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            BuildUI();
        }

        /// <summary>Writes the current checkbox/toggle state back to EditorPrefs so it survives a reopen.</summary>
        private void PersistSelection()
        {
            try
            {
                _selection.CapturePublishers(_config, _selectedPublishers);
                _selection.CaptureLanguages(_config, _selectedLanguages);
                _selection.CaptureVariants(_config, _selectedVariants);
                _selection.matrixCheatOn = _matrixCheatOn;
                _selection.matrixCheatOff = _matrixCheatOff;
                _selection.matrixDevBuild = _matrixDevBuild;
                _selection.Save(_config);
            }
            catch (Exception ex)
            {
                // Saving the selection must never block the checkbox itself.
                Debug.LogWarning($"[BuildPipeline] Could not persist the Matrix selection: {ex.Message}");
            }
        }

        private bool HasVariants => _config?.gameConfig != null && _config.gameConfig.HasBuildVariants;

        private int SelectedPublisherCount()
            => _selectedPublishers != null ? _selectedPublishers.Count(p => p) : 0;

        private int SelectedLanguageCount()
            => _selectedLanguages != null ? _selectedLanguages.Count(l => l) : 0;

        private int SelectedCheatCount()
            => (_matrixCheatOn ? 1 : 0) + (_matrixCheatOff ? 1 : 0);

        private int TotalBuildCount()
        {
            var variants = HasVariants ? SelectedVariantCount() : 1;
            return SelectedPublisherCount() * SelectedLanguageCount() * SelectedCheatCount() * variants;
        }

        /// <summary>Updates the summary line, the badge and the launch button without touching the checkboxes.</summary>
        private void RefreshSummary()
        {
            var totalBuilds = TotalBuildCount();

            if (_summaryLabel != null)
                _summaryLabel.text = $"Selected: {SelectedPublisherCount()} Publishers  ×  {SelectedLanguageCount()} Languages  ×  {SelectedCheatCount()} Cheat modes";

            if (_variantSummaryLabel != null)
                _variantSummaryLabel.text = $"  |  {SelectedVariantCount()} Variant(s)";

            if (_totalBadge != null)
                BuildPipelineUIStyle.SetBadge(_totalBadge, $"{totalBuilds} Total Builds", totalBuilds > 0 ? "ok" : "warn");

            if (_buildButton != null)
            {
                BuildPipelineUIStyle.ApplyIconText(_buildButton, $"⚡ Generate {totalBuilds} Builds Now");
                _buildButton.SetEnabled(totalBuilds > 0);
            }
        }

        private void BuildUI()
        {
            Root.Clear();
            _publisherToggles.Clear();
            _languageToggles.Clear();
            _variantToggles.Clear();
            _summaryLabel = null;
            _variantSummaryLabel = null;
            _totalBadge = null;
            _buildButton = null;

            // Header Summary Card
            var summaryCard = BuildPipelineUIStyle.CreateCard("Matrix Batch Execution",
                "Generate a complete matrix of multiple publishers and language localizations in a single automated batch.");

            var summaryRow = new VisualElement();
            summaryRow.style.flexDirection = FlexDirection.Row;
            summaryRow.style.alignItems = Align.Center;
            summaryRow.style.justifyContent = Justify.SpaceBetween;

            _summaryLabel = new Label();
            _summaryLabel.style.fontSize = 12;
            _summaryLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            summaryRow.Add(_summaryLabel);

            if (HasVariants)
            {
                _variantSummaryLabel = new Label();
                _variantSummaryLabel.style.fontSize = 12;
                _variantSummaryLabel.style.color = new Color(0.75f, 0.85f, 0.95f);
                summaryRow.Add(_variantSummaryLabel);
            }

            _totalBadge = BuildPipelineUIStyle.CreateBadge("0 Total Builds", "warn");
            _totalBadge.style.fontSize = 12;
            summaryRow.Add(_totalBadge);

            summaryCard.Add(summaryRow);
            Root.Add(summaryCard);

            // Columns Grid: Publishers & Languages
            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.marginBottom = 10;

            // Publishers Column
            var pubCard = BuildPipelineUIStyle.CreateCard("1. Publishers (Windows Targets)");
            pubCard.style.flexGrow = 1;
            pubCard.style.marginRight = 6;
            pubCard.style.marginBottom = 0;

            var pubHeaderActions = new VisualElement();
            pubHeaderActions.style.flexDirection = FlexDirection.Row;

            var pubAllBtn = new Button(() => SetAllPublishers(true)) { text = "All" };
            pubAllBtn.AddToClassList("bp-btn");
            pubAllBtn.style.height = 20;
            pubAllBtn.style.paddingLeft = 8;
            pubAllBtn.style.paddingRight = 8;
            pubAllBtn.style.fontSize = 10;
            pubHeaderActions.Add(pubAllBtn);

            var pubNoneBtn = new Button(() => SetAllPublishers(false)) { text = "None" };
            pubNoneBtn.AddToClassList("bp-btn");
            pubNoneBtn.style.height = 20;
            pubNoneBtn.style.paddingLeft = 8;
            pubNoneBtn.style.paddingRight = 8;
            pubNoneBtn.style.fontSize = 10;
            pubHeaderActions.Add(pubNoneBtn);

            pubCard.Add(pubHeaderActions);

            if (_config?.publishers != null)
            {
                for (int i = 0; i < _config.publishers.Count; i++)
                {
                    int idx = i;
                    var pub = _config.publishers[i];
                    if (pub == null || pub.platform != PlatformType.Windows64) continue;

                    var toggle = new Toggle(pub.displayName) { value = IsPublisherSelected(idx) };
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        if (_selectedPublishers != null && idx < _selectedPublishers.Length)
                            _selectedPublishers[idx] = e.newValue;

                        PersistSelection();
                        RefreshSummary();
                    });
                    _publisherToggles.Add(toggle);
                    pubCard.Add(toggle);
                }
            }
            columns.Add(pubCard);

            // Languages Column
            var langCard = BuildPipelineUIStyle.CreateCard("2. Languages & Localizations");
            langCard.style.flexGrow = 1;
            langCard.style.marginBottom = 0;

            var langHeaderActions = new VisualElement();
            langHeaderActions.style.flexDirection = FlexDirection.Row;

            var langAllBtn = new Button(() => SetAllLanguages(true)) { text = "All" };
            langAllBtn.AddToClassList("bp-btn");
            langAllBtn.style.height = 20;
            langAllBtn.style.paddingLeft = 8;
            langAllBtn.style.paddingRight = 8;
            langAllBtn.style.fontSize = 10;
            langHeaderActions.Add(langAllBtn);

            var langNoneBtn = new Button(() => SetAllLanguages(false)) { text = "None" };
            langNoneBtn.AddToClassList("bp-btn");
            langNoneBtn.style.height = 20;
            langNoneBtn.style.paddingLeft = 8;
            langNoneBtn.style.paddingRight = 8;
            langNoneBtn.style.fontSize = 10;
            langHeaderActions.Add(langNoneBtn);

            langCard.Add(langHeaderActions);

            // AutoDetect is just "use the device language" — it is not exclusive. Spelling that out here
            // because the two used to look like they cancelled each other.
            var langHint = new Label("AutoDetect builds with the device language and can be combined with specific languages — each checked language generates its own build.");
            langHint.style.fontSize = 10;
            langHint.style.color = new Color(0.68f, 0.68f, 0.72f);
            langHint.style.whiteSpace = WhiteSpace.Normal;
            langHint.style.marginBottom = 4;
            langCard.Add(langHint);

            if (_config?.languages != null)
            {
                for (int i = 0; i < _config.languages.Count; i++)
                {
                    int idx = i;
                    var lang = _config.languages[i];
                    if (lang == null) continue;

                    var toggle = new Toggle($"{lang.Name} ({lang.language})") { value = IsLanguageSelected(idx) };
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        if (_selectedLanguages != null && idx < _selectedLanguages.Length)
                            _selectedLanguages[idx] = e.newValue;

                        PersistSelection();
                        RefreshSummary();
                    });
                    _languageToggles.Add(toggle);
                    langCard.Add(toggle);
                }
            }
            columns.Add(langCard);

            // Game-Specific Variants Column (only when the game's GameConfig declares variants)
            if (HasVariants)
            {
                var variantCard = BuildPipelineUIStyle.CreateCard("3. Game Variants");
                variantCard.style.flexGrow = 1;
                variantCard.style.marginLeft = 6;
                variantCard.style.marginBottom = 0;

                var variantHeaderActions = new VisualElement();
                variantHeaderActions.style.flexDirection = FlexDirection.Row;

                var varAllBtn = new Button(() => SetAllVariants(true)) { text = "All" };
                varAllBtn.AddToClassList("bp-btn");
                varAllBtn.style.height = 20;
                varAllBtn.style.paddingLeft = 8;
                varAllBtn.style.paddingRight = 8;
                varAllBtn.style.fontSize = 10;
                variantHeaderActions.Add(varAllBtn);

                var varNoneBtn = new Button(() => SetAllVariants(false)) { text = "None" };
                varNoneBtn.AddToClassList("bp-btn");
                varNoneBtn.style.height = 20;
                varNoneBtn.style.paddingLeft = 8;
                varNoneBtn.style.paddingRight = 8;
                varNoneBtn.style.fontSize = 10;
                variantHeaderActions.Add(varNoneBtn);

                variantCard.Add(variantHeaderActions);

                var declaredVariants = _config.gameConfig.buildVariants;
                for (int i = 0; i < declaredVariants.Count; i++)
                {
                    var variant = declaredVariants[i];
                    if (variant == null || !variant.IsValid) continue;

                    int idx = i;
                    var toggle = new Toggle(variant.EffectiveDisplayName) { value = IsVariantSelected(idx) };
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        if (_selectedVariants != null && idx < _selectedVariants.Length)
                            _selectedVariants[idx] = e.newValue;

                        PersistSelection();
                        RefreshSummary();
                    });
                    _variantToggles.Add(toggle);
                    variantCard.Add(toggle);
                }

                columns.Add(variantCard);
            }

            Root.Add(columns);

            // Options Bar
            var optionsCard = BuildPipelineUIStyle.CreateCard("4. Build Options & Launch");

            var optionsBox = new VisualElement();
            optionsBox.style.flexDirection = FlexDirection.Row;
            optionsBox.style.alignItems = Align.Center;
            optionsBox.style.marginBottom = 12;

            var togCheatOff = new Toggle("Cheat OFF (Release)") { value = _matrixCheatOff };
            togCheatOff.RegisterValueChangedCallback(e =>
            {
                _matrixCheatOff = e.newValue;
                PersistSelection();
                RefreshSummary();
            });
            optionsBox.Add(togCheatOff);

            var togCheatOn = new Toggle("Cheat ON (QA)") { value = _matrixCheatOn };
            togCheatOn.style.marginLeft = 16;
            togCheatOn.RegisterValueChangedCallback(e =>
            {
                _matrixCheatOn = e.newValue;
                PersistSelection();
                RefreshSummary();
            });
            optionsBox.Add(togCheatOn);

            var togDev = new Toggle("Development Build") { value = _matrixDevBuild };
            togDev.style.marginLeft = 16;
            togDev.RegisterValueChangedCallback(e =>
            {
                _matrixDevBuild = e.newValue;
                PersistSelection();
            });
            optionsBox.Add(togDev);

            optionsCard.Add(optionsBox);

            _buildButton = new Button(RunMatrixBuild);
            BuildPipelineUIStyle.ApplyIconText(_buildButton, "⚡ Generate 0 Builds Now");
            _buildButton.AddToClassList("bp-btn");
            _buildButton.AddToClassList("bp-btn--success");
            _buildButton.style.height = 36;
            _buildButton.style.fontSize = 13;
            optionsCard.Add(_buildButton);

            Root.Add(optionsCard);

            RefreshSummary();
        }

        // ------------------------------------------------------------------ Selection helpers

        private bool IsPublisherSelected(int index)
            => _selectedPublishers != null && index < _selectedPublishers.Length && _selectedPublishers[index];

        private bool IsLanguageSelected(int index)
            => _selectedLanguages != null && index < _selectedLanguages.Length && _selectedLanguages[index];

        private bool IsVariantSelected(int index)
            => _selectedVariants != null && index < _selectedVariants.Length && _selectedVariants[index];

        private void SetAllPublishers(bool value)
        {
            if (_selectedPublishers == null) return;

            for (int i = 0; i < _selectedPublishers.Length; i++) _selectedPublishers[i] = value;
            foreach (var toggle in _publisherToggles) toggle.SetValueWithoutNotify(value);

            PersistSelection();
            RefreshSummary();
        }

        private void SetAllLanguages(bool value)
        {
            if (_selectedLanguages == null) return;

            for (int i = 0; i < _selectedLanguages.Length; i++) _selectedLanguages[i] = value;
            foreach (var toggle in _languageToggles) toggle.SetValueWithoutNotify(value);

            PersistSelection();
            RefreshSummary();
        }

        private void SetAllVariants(bool value)
        {
            if (_selectedVariants == null) return;

            for (int i = 0; i < _selectedVariants.Length; i++) _selectedVariants[i] = value;
            foreach (var toggle in _variantToggles) toggle.SetValueWithoutNotify(value);

            PersistSelection();
            RefreshSummary();
        }

        private int SelectedVariantCount()
        {
            var declared = _config?.gameConfig?.buildVariants;
            if (declared == null || _selectedVariants == null) return 0;

            var count = 0;
            for (int i = 0; i < declared.Count && i < _selectedVariants.Length; i++)
                if (_selectedVariants[i] && declared[i] != null && declared[i].IsValid) count++;
            return count;
        }

        private List<BuildVariant> SelectedVariants()
        {
            var result = new List<BuildVariant>();
            var declared = _config?.gameConfig?.buildVariants;
            if (declared == null || _selectedVariants == null) return result;

            for (int i = 0; i < declared.Count && i < _selectedVariants.Length; i++)
                if (_selectedVariants[i] && declared[i] != null && declared[i].IsValid) result.Add(declared[i]);

            return result;
        }

        private void RunMatrixBuild()
        {
            if (_config?.publishers == null || _config?.languages == null) return;

            var pubs = _config.publishers.Where((p, idx) => _selectedPublishers != null && idx < _selectedPublishers.Length && _selectedPublishers[idx]).ToList();
            var langs = _config.languages.Where((l, idx) => _selectedLanguages != null && idx < _selectedLanguages.Length && _selectedLanguages[idx]).Select(l => l.language).ToList();
            var variants = SelectedVariants();
            var variantFactor = variants.Count > 0 ? variants.Count : 1;

            int count = 0;
            int total = pubs.Count * langs.Count * ((_matrixCheatOn ? 1 : 0) + (_matrixCheatOff ? 1 : 0)) * variantFactor;

            if (!EditorUtility.DisplayDialog("Confirm Matrix Build", $"Generate {total} builds now?\nThis may take several minutes.", "YES, START", "CANCEL"))
                return;

            try
            {
                foreach (var lang in langs)
                {
                    foreach (var pub in pubs)
                    {
                        for (int variantIndex = 0; variantIndex < variantFactor; variantIndex++)
                        {
                            var variant = variants.Count > 0 ? variants[variantIndex] : null;
                            var variantId = variant != null ? variant.EffectiveId : "";
                            var variantTag = variant != null ? $" [{variant.EffectiveDisplayName}]" : "";

                            if (_matrixCheatOff)
                            {
                                count++;
                                EditorUtility.DisplayProgressBar("Building Matrix...", $"[{count}/{total}] {pub.displayName} ({lang}){variantTag}", (float)count / total);
                                var ctx = new BuildContext
                                {
                                    Config = _config,
                                    Publisher = pub.publisher,
                                    PublisherProfile = pub,
                                    Language = lang,
                                    Platform = pub.platform,
                                    CheatMode = false,
                                    DevelopmentBuild = _matrixDevBuild,
                                    Demo = pub.isDemo,
                                    VariantId = variantId,
                                    Variant = variant
                                };
                                BuildPipelineRunner.Execute(ctx);
                            }

                            if (_matrixCheatOn)
                            {
                                count++;
                                EditorUtility.DisplayProgressBar("Building Matrix...", $"[{count}/{total}] {pub.displayName} ({lang}){variantTag} [CHEAT]", (float)count / total);
                                var ctx = new BuildContext
                                {
                                    Config = _config,
                                    Publisher = pub.publisher,
                                    PublisherProfile = pub,
                                    Language = lang,
                                    Platform = pub.platform,
                                    CheatMode = true,
                                    DevelopmentBuild = _matrixDevBuild,
                                    Demo = pub.isDemo,
                                    VariantId = variantId,
                                    Variant = variant
                                };
                                BuildPipelineRunner.Execute(ctx);
                            }
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Matrix Complete", $"Batch build finished: {count} builds processed.", "OK");
                _onRefresh?.Invoke();
            }
        }
    }
}
