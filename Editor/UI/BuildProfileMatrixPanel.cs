using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// One screen with every profile and its effective defines, plus a Free/Full diff so a missing
    /// define, a duplicated bundle or a shared output folder is visible without reading the asset.
    /// </summary>
    internal sealed class BuildProfileMatrixPanel
    {
        public VisualElement Root { get; } = new VisualElement();

        public BuildProfileMatrixPanel()
        {
            CreateGUI();
        }

        private static readonly Color TextColor = new Color(0.88f, 0.89f, 0.92f);
        private static readonly Color MutedColor = new Color(0.62f, 0.65f, 0.71f);
        private static readonly Color ErrorColor = new Color(0.94f, 0.33f, 0.31f);
        private static readonly Color WarnColor = new Color(0.98f, 0.75f, 0.14f);
        private static readonly Color OkColor = new Color(0.30f, 0.85f, 0.39f);

        private VisualElement body;
        private Label summary;


        private void CreateGUI()
        {
            var root = Root;
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            header.Add(new Label("Build Profile Matrix")
            {
                style = { fontSize = 17, unityFontStyleAndWeight = FontStyle.Bold, color = TextColor }
            });

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            header.Add(spacer);

            var newProfile = new Button(() => BuildProfileWizardWindow.Open()) { text = "Novo perfil..." };
            newProfile.style.height = 24;
            newProfile.style.marginRight = 4;
            header.Add(newProfile);

            var presets = new Button(() => BuildPipelineWindow.ShowSection(BuildPipelineSections.Defines)) { text = "Presets..." };
            presets.style.height = 24;
            presets.style.marginRight = 4;
            header.Add(presets);

            var refresh = new Button(Rebuild) { text = "Refresh" };
            refresh.style.height = 24;
            refresh.style.paddingLeft = 10;
            refresh.style.paddingRight = 10;
            header.Add(refresh);

            root.Add(header);

            summary = new Label { style = { fontSize = 12, marginTop = 4, marginBottom = 8 } };
            root.Add(summary);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            root.Add(scroll);

            body = new VisualElement();
            scroll.Add(body);

            Rebuild();
        }

        private void Rebuild()
        {
            if (body == null)
                return;

            body.Clear();

            var config = BuildCLI.FindOrCreateConfig();
            if (config?.publishers == null || config.publishers.Count == 0)
            {
                summary.text = "Nenhum ProjectBuildConfig encontrado.";
                return;
            }

            var tracking = BuildProfileCatalog.ReadTrackingPolicy();
            var issues = BuildProfileValidator.Validate(config, tracking);
            int errors = issues.Count(issue => issue.Severity == BuildProfileIssueSeverity.Error);
            int warnings = issues.Count - errors;

            summary.text = $"{config.publishers.Count} perfil(is) · {errors} erro(s) · {warnings} aviso(s)";
            summary.style.color = errors > 0 ? ErrorColor : warnings > 0 ? WarnColor : OkColor;

            body.Add(BuildTable(config, issues));
            body.Add(BuildFreeFullDiff(config));
            body.Add(BuildIssues(config, issues, tracking));
            body.Add(BuildPresets(config));
        }

        private VisualElement BuildIssues(
            ProjectBuildConfig config, List<BuildProfileIssue> issues, BuildCatalogTracking tracking)
        {
            var card = BuildCard("Problemas",
                "Corrija com um clique o que é inequívoco; o restante abre no inspector.");

            if (issues.Count == 0)
            {
                card.Add(new Label("Nenhum problema encontrado.")
                {
                    style = { color = OkColor, fontSize = 11 }
                });

                return card;
            }

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            header.Add(new Label($"{issues.Count} item(ns)") { style = { color = MutedColor, fontSize = 10.5f, flexGrow = 1 } });

            var fixAll = new Button(() => FixAll(config, issues, tracking)) { text = "Corrigir tudo" };
            fixAll.style.height = 22;
            fixAll.SetEnabled(issues.Any(issue => BuildProfileFixer.CanFix(issue.Code)));
            header.Add(fixAll);
            card.Add(header);

            foreach (var issue in issues)
            {
                var row = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row, alignItems = Align.Center,
                        paddingTop = 2, paddingBottom = 2
                    }
                };

                row.Add(new Label(issue.ToString())
                {
                    style =
                    {
                        flexGrow = 1,
                        fontSize = 10.5f,
                        color = issue.Severity == BuildProfileIssueSeverity.Error ? ErrorColor : WarnColor,
                        whiteSpace = WhiteSpace.Normal
                    }
                });

                if (BuildProfileFixer.CanFix(issue.Code))
                {
                    var fix = new Button(() => Fix(config, issue, tracking)) { text = "Corrigir" };
                    fix.style.height = 20;
                    fix.style.marginLeft = 6;
                    row.Add(fix);
                }

                card.Add(row);
            }

            return card;
        }

        private void Fix(ProjectBuildConfig config, BuildProfileIssue issue, BuildCatalogTracking tracking)
        {
            var profile = config.publishers?.FirstOrDefault(candidate => candidate != null &&
                string.Equals(candidate.EffectiveId, issue.ProfileId, StringComparison.OrdinalIgnoreCase));

            if (profile == null || !BuildProfileFixer.TryFix(config, profile, issue, tracking))
            {
                Debug.LogWarning($"[BuildPipeline] Sem correção automática para '{issue.Code}' ({issue.ProfileId}).");
                return;
            }

            Save(config);
            Rebuild();
        }

        private void FixAll(ProjectBuildConfig config, List<BuildProfileIssue> issues, BuildCatalogTracking tracking)
        {
            int fixedCount = 0;

            foreach (var group in issues.GroupBy(issue => issue.ProfileId, StringComparer.OrdinalIgnoreCase))
            {
                var profile = config.publishers?.FirstOrDefault(candidate => candidate != null &&
                    string.Equals(candidate.EffectiveId, group.Key, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                    continue;

                fixedCount += BuildProfileFixer.FixAll(config, profile, group, tracking);
            }

            if (fixedCount == 0)
            {
                Debug.Log("[BuildPipeline] Nenhuma correção automática aplicável.");
                return;
            }

            Debug.Log($"[BuildPipeline] {fixedCount} correção(ões) aplicada(s).");
            Save(config);
            Rebuild();
        }

        private void Save(ProjectBuildConfig config)
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(assetPath))
                BuildProfileCatalog.Write(config, assetPath);
        }

        private static VisualElement BuildCard(string title, string description)
        {
            var card = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(1f, 1f, 1f, 0.055f),
                    paddingLeft = 12, paddingRight = 12, paddingTop = 10, paddingBottom = 10,
                    marginBottom = 10
                }
            };

            card.Add(new Label(title)
            {
                style = { fontSize = 13, unityFontStyleAndWeight = FontStyle.Bold, color = TextColor, marginBottom = 2 }
            });

            if (!string.IsNullOrEmpty(description))
            {
                card.Add(new Label(description)
                {
                    style = { fontSize = 10.5f, color = MutedColor, marginBottom = 6, whiteSpace = WhiteSpace.Normal }
                });
            }

            return card;
        }

        private VisualElement BuildTable(ProjectBuildConfig config, List<BuildProfileIssue> issues)
        {
            var card = BuildCard("Perfis", "O que cada perfil aplica de verdade (presets expandidos, remoções aplicadas).");

            var header = Row("Perfil", "Plataforma", "Free/Full", "Bundle", "Output", "Defines efetivos", "Problemas");
            header.style.marginBottom = 4;
            card.Add(header);

            foreach (var profile in config.publishers)
            {
                if (profile == null)
                    continue;

                var catalogProfile = BuildProfileCatalog.ToCatalogProfile(config, profile);
                var profileIssues = issues.Where(issue =>
                    string.Equals(issue.ProfileId, profile.EffectiveId, StringComparison.OrdinalIgnoreCase)).ToList();

                string problems = profileIssues.Count == 0 ? "ok" : $"{profileIssues.Count} item(ns)";
                var row = Row(
                    $"{profile.displayName} ({profile.EffectiveId})",
                    profile.platform.ToString(),
                    profile.isFullGame ? "Full" : "Free",
                    profile.bundleIdentifier,
                    profile.outputSubfolder,
                    catalogProfile.effectiveDefines.Count == 0 ? "(nenhum)" : string.Join(", ", catalogProfile.effectiveDefines),
                    problems);

                var labels = row.Query<Label>().ToList();
                if (labels.Count > 0)
                {
                    labels[labels.Count - 1].style.color = profileIssues.Any(issue => issue.Severity == BuildProfileIssueSeverity.Error)
                        ? ErrorColor
                        : profileIssues.Count > 0 ? WarnColor : OkColor;
                }

                row.tooltip = profileIssues.Count == 0
                    ? string.Empty
                    : string.Join("\n", profileIssues.Select(issue => issue.ToString()));

                card.Add(row);
            }

            return card;
        }

        private VisualElement BuildFreeFullDiff(ProjectBuildConfig config)
        {
            var card = BuildCard("Diff Free ↔ Full",
                "Pares da mesma loja: o que só a Free aplica, o que só a Full aplica, e bundle/output compartilhados.");

            var pairs = config.publishers
                .Where(profile => profile != null && profile.platform is PlatformType.Android or PlatformType.iOS)
                .GroupBy(profile => PublisherFamily(profile.publisher), StringComparer.OrdinalIgnoreCase);

            int rendered = 0;

            foreach (var pair in pairs)
            {
                var free = pair.FirstOrDefault(profile => !profile.isFullGame);
                var full = pair.FirstOrDefault(profile => profile.isFullGame);

                if (free == null || full == null)
                    continue;

                rendered++;

                var freeDefines = BuildProfileCatalog.ToCatalogProfile(config, free).effectiveDefines;
                var fullDefines = BuildProfileCatalog.ToCatalogProfile(config, full).effectiveDefines;

                var freeOnly = freeDefines.Where(define => !fullDefines.Contains(define, StringComparer.OrdinalIgnoreCase)).ToList();
                var fullOnly = fullDefines.Where(define => !freeDefines.Contains(define, StringComparer.OrdinalIgnoreCase)).ToList();

                var line = new Label(
                    $"{pair.Key}\n" +
                    $"  Free: {free.EffectiveId}  |  Full: {full.EffectiveId}\n" +
                    $"  Só no Free: {(freeOnly.Count == 0 ? "-" : string.Join(", ", freeOnly))}\n" +
                    $"  Só no Full: {(fullOnly.Count == 0 ? "-" : string.Join(", ", fullOnly))}\n" +
                    $"  Bundle igual: {(string.Equals(free.bundleIdentifier, full.bundleIdentifier, StringComparison.OrdinalIgnoreCase) ? "SIM (problema)" : "não")}\n" +
                    $"  Output igual: {(string.Equals(free.outputSubfolder, full.outputSubfolder, StringComparison.OrdinalIgnoreCase) ? "SIM (problema)" : "não")}")
                {
                    style = { fontSize = 11, color = TextColor, whiteSpace = WhiteSpace.Normal, marginBottom = 8 }
                };

                bool problem = freeOnly.Count > 0 ||
                               fullOnly.Count > 0 ||
                               string.Equals(free.bundleIdentifier, full.bundleIdentifier, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(free.outputSubfolder, full.outputSubfolder, StringComparison.OrdinalIgnoreCase);

                line.style.borderLeftWidth = 3;
                line.style.borderLeftColor = problem ? WarnColor : OkColor;
                line.style.paddingLeft = 8;

                card.Add(line);
            }

            if (rendered == 0)
            {
                card.Add(new Label("Nenhum par Free/Full declarado.") { style = { color = MutedColor, fontSize = 11 } });
            }

            return card;
        }

        private VisualElement BuildPresets(ProjectBuildConfig config)
        {
            var card = BuildCard("Presets de defines",
                "Grupos reutilizáveis. A build expande os presets do perfil antes da lista própria.");

            if (config.definePresets == null || config.definePresets.Count == 0)
            {
                card.Add(new Label("Nenhum preset cadastrado em ProjectBuildConfig.definePresets.")
                {
                    style = { color = MutedColor, fontSize = 11 }
                });

                return card;
            }

            foreach (var preset in config.definePresets)
            {
                if (preset == null)
                    continue;

                int uses = config.publishers.Count(profile =>
                    (profile.definePresets ?? new List<string>()).Any(id =>
                        string.Equals(id?.Trim(), preset.id, StringComparison.OrdinalIgnoreCase)));

                card.Add(new Label(
                    $"{preset.displayName} ({preset.id}) — {uses} perfil(is)\n" +
                    $"  {((preset.defines == null || preset.defines.Count == 0) ? "(nenhum define)" : string.Join(", ", preset.defines))}")
                {
                    style = { fontSize = 11, color = TextColor, whiteSpace = WhiteSpace.Normal, marginBottom = 6 }
                });
            }

            return card;
        }

        private static VisualElement Row(params string[] cells)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingTop = 2, paddingBottom = 2 } };

            float[] widths = { 230, 82, 62, 220, 220, 260, 70 };

            for (int i = 0; i < cells.Length; i++)
            {
                row.Add(new Label(cells[i] ?? "")
                {
                    style =
                    {
                        width = i < widths.Length ? widths[i] : 100,
                        fontSize = 10.5f,
                        color = TextColor,
                        whiteSpace = WhiteSpace.Normal,
                        marginRight = 6
                    }
                });
            }

            return row;
        }

        private static string PublisherFamily(Publisher publisher) =>
            publisher.ToString().Replace("Full", "").Replace("Free", "").Replace("Demo", "");
    }
}
