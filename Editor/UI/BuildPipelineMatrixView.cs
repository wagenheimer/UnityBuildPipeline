using System;
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
        private readonly bool[] _selectedPublishers;
        private readonly bool[] _selectedLanguages;
        private bool _matrixCheatOn = false;
        private bool _matrixCheatOff = true;
        private bool _matrixDevBuild = false;

        private readonly Action _onRefresh;

        public BuildPipelineMatrixView(ProjectBuildConfig config, bool[] selectedPublishers, bool[] selectedLanguages, Action onRefresh)
        {
            _config = config;
            _selectedPublishers = selectedPublishers;
            _selectedLanguages = selectedLanguages;
            _onRefresh = onRefresh;

            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            BuildUI();
        }

        private void BuildUI()
        {
            Root.Clear();

            var selPubs = _selectedPublishers != null ? _selectedPublishers.Count(p => p) : 0;
            var selLangs = _selectedLanguages != null ? _selectedLanguages.Count(l => l) : 0;
            var cheatsCount = (_matrixCheatOn ? 1 : 0) + (_matrixCheatOff ? 1 : 0);
            var totalBuilds = selPubs * selLangs * cheatsCount;

            // Header Summary Card
            var summaryCard = BuildPipelineUIStyle.CreateCard("Matrix Batch Execution",
                "Generate a complete matrix of multiple publishers and language localizations in a single automated batch.");

            var summaryRow = new VisualElement();
            summaryRow.style.flexDirection = FlexDirection.Row;
            summaryRow.style.alignItems = Align.Center;
            summaryRow.style.justifyContent = Justify.SpaceBetween;

            var desc = new Label($"Selected: {selPubs} Publishers  ×  {selLangs} Languages  ×  {cheatsCount} Cheat modes");
            desc.style.fontSize = 12;
            desc.style.color = new Color(0.9f, 0.9f, 0.9f);
            summaryRow.Add(desc);

            var totalBadge = BuildPipelineUIStyle.CreateBadge($"{totalBuilds} Total Builds", totalBuilds > 0 ? "ok" : "warn");
            totalBadge.style.fontSize = 12;
            summaryRow.Add(totalBadge);

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

            var pubAllBtn = new Button(() =>
            {
                if (_selectedPublishers != null)
                {
                    for (int i = 0; i < _selectedPublishers.Length; i++) _selectedPublishers[i] = true;
                }
                BuildUI();
            }) { text = "All" };
            pubAllBtn.AddToClassList("bp-btn");
            pubAllBtn.style.height = 20;
            pubAllBtn.style.paddingLeft = 8;
            pubAllBtn.style.paddingRight = 8;
            pubAllBtn.style.fontSize = 10;
            pubHeaderActions.Add(pubAllBtn);

            var pubNoneBtn = new Button(() =>
            {
                if (_selectedPublishers != null)
                {
                    for (int i = 0; i < _selectedPublishers.Length; i++) _selectedPublishers[i] = false;
                }
                BuildUI();
            }) { text = "None" };
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
                    if (pub.platform != PlatformType.Windows64) continue;

                    var toggle = new Toggle(pub.displayName) { value = _selectedPublishers != null && _selectedPublishers[idx] };
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        if (_selectedPublishers != null) _selectedPublishers[idx] = e.newValue;
                        BuildUI();
                    });
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

            var langAllBtn = new Button(() =>
            {
                if (_selectedLanguages != null)
                {
                    for (int i = 0; i < _selectedLanguages.Length; i++) _selectedLanguages[i] = true;
                }
                BuildUI();
            }) { text = "All" };
            langAllBtn.AddToClassList("bp-btn");
            langAllBtn.style.height = 20;
            langAllBtn.style.paddingLeft = 8;
            langAllBtn.style.paddingRight = 8;
            langAllBtn.style.fontSize = 10;
            langHeaderActions.Add(langAllBtn);

            var langNoneBtn = new Button(() =>
            {
                if (_selectedLanguages != null)
                {
                    for (int i = 0; i < _selectedLanguages.Length; i++) _selectedLanguages[i] = false;
                }
                BuildUI();
            }) { text = "None" };
            langNoneBtn.AddToClassList("bp-btn");
            langNoneBtn.style.height = 20;
            langNoneBtn.style.paddingLeft = 8;
            langNoneBtn.style.paddingRight = 8;
            langNoneBtn.style.fontSize = 10;
            langHeaderActions.Add(langNoneBtn);

            langCard.Add(langHeaderActions);

            if (_config?.languages != null)
            {
                for (int i = 0; i < _config.languages.Count; i++)
                {
                    int idx = i;
                    var lang = _config.languages[i];
                    var toggle = new Toggle($"{lang.Name} ({lang.language})") { value = _selectedLanguages != null && _selectedLanguages[idx] };
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        if (_selectedLanguages != null) _selectedLanguages[idx] = e.newValue;
                        BuildUI();
                    });
                    langCard.Add(toggle);
                }
            }
            columns.Add(langCard);

            Root.Add(columns);

            // Options Bar
            var optionsCard = BuildPipelineUIStyle.CreateCard("3. Build Variants & Launch");

            var optionsBox = new VisualElement();
            optionsBox.style.flexDirection = FlexDirection.Row;
            optionsBox.style.alignItems = Align.Center;
            optionsBox.style.marginBottom = 12;

            var togCheatOff = new Toggle("Cheat OFF (Release)") { value = _matrixCheatOff };
            togCheatOff.RegisterValueChangedCallback(e => { _matrixCheatOff = e.newValue; BuildUI(); });
            optionsBox.Add(togCheatOff);

            var togCheatOn = new Toggle("Cheat ON (QA)") { value = _matrixCheatOn };
            togCheatOn.style.marginLeft = 16;
            togCheatOn.RegisterValueChangedCallback(e => { _matrixCheatOn = e.newValue; BuildUI(); });
            optionsBox.Add(togCheatOn);

            var togDev = new Toggle("Development Build") { value = _matrixDevBuild };
            togDev.style.marginLeft = 16;
            togDev.RegisterValueChangedCallback(e => { _matrixDevBuild = e.newValue; });
            optionsBox.Add(togDev);

            optionsCard.Add(optionsBox);

            var buildMatrixBtn = new Button(RunMatrixBuild)
            {
                text = $"⚡ Generate {totalBuilds} Builds Now"
            };
            buildMatrixBtn.AddToClassList("bp-btn");
            buildMatrixBtn.AddToClassList("bp-btn--success");
            buildMatrixBtn.style.height = 36;
            buildMatrixBtn.style.fontSize = 13;
            buildMatrixBtn.SetEnabled(totalBuilds > 0);
            optionsCard.Add(buildMatrixBtn);

            Root.Add(optionsCard);
        }

        private void RunMatrixBuild()
        {
            if (_config?.publishers == null || _config?.languages == null) return;

            var pubs = _config.publishers.Where((p, idx) => _selectedPublishers != null && idx < _selectedPublishers.Length && _selectedPublishers[idx]).ToList();
            var langs = _config.languages.Where((l, idx) => _selectedLanguages != null && idx < _selectedLanguages.Length && _selectedLanguages[idx]).Select(l => l.language).ToList();

            int count = 0;
            int total = pubs.Count * langs.Count * ((_matrixCheatOn ? 1 : 0) + (_matrixCheatOff ? 1 : 0));

            if (!EditorUtility.DisplayDialog("Confirm Matrix Build", $"Generate {total} builds now?\nThis may take several minutes.", "YES, START", "CANCEL"))
                return;

            try
            {
                foreach (var lang in langs)
                {
                    foreach (var pub in pubs)
                    {
                        if (_matrixCheatOff)
                        {
                            count++;
                            EditorUtility.DisplayProgressBar("Building Matrix...", $"[{count}/{total}] {pub.displayName} ({lang})", (float)count / total);
                            var ctx = new BuildContext
                            {
                                Config = _config,
                                Publisher = pub.publisher,
                                PublisherProfile = pub,
                                Language = lang,
                                Platform = pub.platform,
                                CheatMode = false,
                                DevelopmentBuild = _matrixDevBuild,
                                Demo = pub.isDemo
                            };
                            BuildPipelineRunner.Execute(ctx);
                        }

                        if (_matrixCheatOn)
                        {
                            count++;
                            EditorUtility.DisplayProgressBar("Building Matrix...", $"[{count}/{total}] {pub.displayName} ({lang}) [CHEAT]", (float)count / total);
                            var ctx = new BuildContext
                            {
                                Config = _config,
                                Publisher = pub.publisher,
                                PublisherProfile = pub,
                                Language = lang,
                                Platform = pub.platform,
                                CheatMode = true,
                                DevelopmentBuild = _matrixDevBuild,
                                Demo = pub.isDemo
                            };
                            BuildPipelineRunner.Execute(ctx);
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

