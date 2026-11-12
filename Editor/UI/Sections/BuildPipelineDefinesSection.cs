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
    /// Defines center. Central: every relevant symbol with its documentation, the profiles that get it, where the
    /// code uses it and a status (ok / undocumented / dead / orphan / required-missing); selecting one shows its
    /// declaration, a per-profile matrix, the clickable usage list and the impact of removing it.
    /// Other tabs: the game's own declarations, define presets and the PlayerSettings globals (with the
    /// external-change detector and the list of code that writes global defines).
    /// </summary>
    internal sealed class BuildPipelineDefinesSection
    {
        private static readonly Color Muted = new Color(0.62f, 0.65f, 0.71f);

        private readonly BuildPipelineSectionContext _ctx;
        private readonly ProjectBuildConfig _config;
        private readonly BuildPipelineSelectionState _selection;
        private DefineRegistry _registry;
        private Dictionary<PublisherProfile, DefineStack> _stacks;
        private Dictionary<string, List<DefineUsage>> _usagesBySymbol;
        private List<DefineRuleViolation> _violations = new List<DefineRuleViolation>();
        private string _filter = "";
        private bool _showAll;

        public VisualElement Root { get; } = new VisualElement();

        private enum Status { Ok, RequiredMissing, Forbidden, Undocumented, Dead, Orphan }

        private sealed class Row
        {
            public string Symbol;
            public DefineDeclaration Declaration;
            public List<PublisherProfile> AppliedIn = new List<PublisherProfile>();
            public int Usages;
            public bool UsedInRelevantCode;
            public bool InProfileLayer;
            public Status Status;
            public string StatusText = "";
        }

        public BuildPipelineDefinesSection(BuildPipelineSectionContext ctx)
        {
            _ctx = ctx;
            _config = ctx.Config;
            _selection = ctx.Selection;
            DefineUsageIndex.EnsureScanned();

            Root.Add(Tabs());
            switch (_selection.definesTab)
            {
                case "registry": Root.Add(RegistryTab()); break;
                case "presets": Root.Add(new BuildDefinePresetPanel().Root); break;
                case "globals": Root.Add(GlobalsTab()); break;
                default: Root.Add(CentralTab()); break;
            }
        }

        private VisualElement Tabs()
        {
            var row = new VisualElement();
            row.AddToClassList("bp-subtabs");
            void Add(string id, string text)
            {
                var button = new Button(() =>
                {
                    _selection.definesTab = id;
                    _ctx.SaveSelection();
                    _ctx.RebuildSection();
                });
                BuildPipelineUIStyle.ApplyIconText(button, text);
                button.AddToClassList("bp-subtab");
                if ((_selection.definesTab ?? "") == id) button.AddToClassList("bp-subtab--active");
                row.Add(button);
            }

            Add("", "🧬 Central");
            Add("registry", "📚 Defines do jogo");
            Add("presets", "🧱 Presets");
            Add("globals", "🌐 Globais (PlayerSettings)");

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            row.Add(spacer);
            string scanText = DefineUsageIndex.IsScanning ? "⏳ varrendo código…"
                : DefineUsageIndex.HasResult ? $"🔍 {DefineUsageIndex.FileCount} arquivos · {DefineUsageIndex.LastScanUtc.ToLocalTime():HH:mm}"
                : "🔍 não varrido";
            var rescan = BuildPipelineBuildSection.SmallButton(scanText + "  ↻", () => DefineUsageIndex.Scan());
            rescan.tooltip = "Revarrer Assets/, Packages/ e Library/PackageCache (só relê arquivos alterados).";
            row.Add(rescan);
            return row;
        }

        // ── Central ──────────────────────────────────────────────────────────

        private void Prepare()
        {
            _registry = DefineRegistry.Load(_config);
            _stacks = _config.publishers.Where(p => p != null).ToDictionary(p => p, p => DefineEnvironment.StackFor(_config, p));
            _usagesBySymbol = DefineUsageIndex.Usages
                .Where(u => u.kind != DefineUsageKind.GlobalWriter && !string.IsNullOrEmpty(u.symbol))
                .GroupBy(u => u.symbol, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            _violations = _stacks.SelectMany(pair => DefineRules.Check(_registry, pair.Key, pair.Value)).ToList();
        }

        private List<Row> BuildRows()
        {
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            foreach (var declaration in _registry.Declarations) symbols.Add(declaration.symbol);
            foreach (var stack in _stacks.Values)
                foreach (var entry in stack.Entries)
                    symbols.Add(entry.symbol);
            foreach (string symbol in _usagesBySymbol.Keys) symbols.Add(symbol);

            var versionDefined = new HashSet<string>(DefineUsageIndex.VersionDefines().Select(u => u.symbol), StringComparer.Ordinal);
            var rows = new List<Row>();

            foreach (string symbol in symbols)
            {
                var row = new Row { Symbol = symbol, Declaration = _registry.Find(symbol) };
                _usagesBySymbol.TryGetValue(symbol, out var usages);
                usages ??= new List<DefineUsage>();
                row.Usages = usages.Count(u => u.kind == DefineUsageKind.Condition || u.kind == DefineUsageKind.ConditionalAttribute || u.kind == DefineUsageKind.AssemblyConstraint);
                row.UsedInRelevantCode = usages.Any(u => u.package == "Assets" || u.package.StartsWith("com.wagenheimer", StringComparison.Ordinal));

                foreach (var pair in _stacks)
                {
                    if (pair.Value.Has(symbol)) row.AppliedIn.Add(pair.Key);
                    if (pair.Value.Entries.Any(e => e.symbol == symbol && e.layer != DefineLayer.Global)) row.InProfileLayer = true;
                }

                bool builtIn = DefineSymbols.IsUnityBuiltIn(symbol);
                bool definedSomewhere = row.AppliedIn.Count > 0 || versionDefined.Contains(symbol) ||
                                        usages.Any(u => u.kind == DefineUsageKind.LocalDefine || u.kind == DefineUsageKind.CompilerResponseFile) ||
                                        (row.Declaration != null && row.Declaration.IsAutomatic);

                var violation = _violations.FirstOrDefault(v => v.Declaration.symbol == symbol);

                if (violation != null)
                {
                    row.Status = violation.Required ? Status.RequiredMissing : Status.Forbidden;
                    row.StatusText = violation.Required ? $"⛔ falta em {violation.Profile.EffectiveId}" : $"⛔ proibido em {violation.Profile.EffectiveId}";
                }
                else if (DefineUsageIndex.HasResult && row.Usages > 0 && !definedSomewhere && !builtIn)
                {
                    row.Status = Status.Orphan;
                    string near = DefineAdvisor.NearestSymbol(symbol, KnownDefinedSymbols(versionDefined));
                    row.StatusText = near == null
                        ? "❓ órfão: usado, nunca definido"
                        : $"❓ órfão: usado, nunca definido — talvez você quis dizer {near}?";
                }
                else if (DefineUsageIndex.HasResult && row.Usages == 0 && (row.InProfileLayer || row.Declaration != null) && !builtIn)
                {
                    row.Status = Status.Dead;
                    row.StatusText = "💤 sem uso no código";
                }
                else if (row.Declaration == null && row.InProfileLayer && !builtIn)
                {
                    row.Status = Status.Undocumented;
                    row.StatusText = "⚠ não documentado";
                }
                else
                {
                    row.Status = Status.Ok;
                    row.StatusText = "✔";
                }

                bool relevant = row.Declaration != null || row.InProfileLayer || row.UsedInRelevantCode;
                if (!_showAll && (!relevant || (builtIn && !_selection.showUnityDefines)))
                    continue;

                rows.Add(row);
            }

            return rows
                .OrderBy(r => r.Status == Status.Ok ? 1 : 0)
                .ThenBy(r => r.Symbol, StringComparer.Ordinal)
                .ToList();
        }

        private VisualElement CentralTab()
        {
            var box = new VisualElement();
            Prepare();

            if (!string.IsNullOrEmpty(_selection.selectedDefine))
                box.Add(SymbolDetail(_selection.selectedDefine));

            box.Add(HealthCard());

            var tools = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            var search = new ToolbarSearchField { value = _filter, style = { width = 260 } };
            tools.Add(search);
            var all = new Toggle("incluir pacotes de terceiros") { value = _showAll, style = { marginLeft = 10 } };
            tools.Add(all);
            var unity = new Toggle("incluir símbolos do Unity") { value = _selection.showUnityDefines, style = { marginLeft = 10 } };
            tools.Add(unity);

            var tableHost = new VisualElement();
            void Refresh()
            {
                tableHost.Clear();
                tableHost.Add(Table(BuildRows().Where(r => string.IsNullOrEmpty(_filter) ||
                    r.Symbol.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList()));
            }

            search.RegisterValueChangedCallback(evt => { _filter = evt.newValue ?? ""; Refresh(); });
            all.RegisterValueChangedCallback(evt => { _showAll = evt.newValue; Refresh(); });
            unity.RegisterValueChangedCallback(evt => { _selection.showUnityDefines = evt.newValue; _ctx.SaveSelection(); Refresh(); });

            var card = BuildPipelineUIStyle.CreateCard("Todos os defines",
                "Relevantes = documentados, aplicados por algum perfil, ou usados no jogo (Assets) e nos pacotes com.wagenheimer.*. " +
                "Clique num símbolo para ver regras, perfis e cada linha de código que o usa.");
            card.Add(tools);
            card.Add(tableHost);
            Refresh();
            box.Add(card);
            return box;
        }

        private VisualElement Table(List<Row> rows)
        {
            var box = new VisualElement();
            var header = new VisualElement();
            header.AddToClassList("bp-table-header");
            header.Add(BuildPipelineBuildSection.Cell("Símbolo", 280, true));
            header.Add(BuildPipelineBuildSection.Cell("Documentado por", 190, true));
            header.Add(BuildPipelineBuildSection.Cell("Perfis", 80, true));
            header.Add(BuildPipelineBuildSection.Cell("Usos", 60, true));
            header.Add(BuildPipelineBuildSection.Cell("Status", 0, true));
            box.Add(header);

            int total = _stacks.Count;
            foreach (var row in rows)
            {
                var line = new VisualElement();
                line.AddToClassList("bp-table-row");
                line.AddToClassList("bp-table-row--clickable");
                if (row.Symbol == _selection.selectedDefine) line.AddToClassList("bp-table-row--selected");
                string symbol = row.Symbol;
                line.RegisterCallback<ClickEvent>(_ => Select(symbol));

                line.Add(BuildPipelineBuildSection.Cell(row.Symbol, 280));
                line.Add(BuildPipelineBuildSection.Cell(row.Declaration != null ? row.Declaration.source : "—", 190));
                var profiles = BuildPipelineBuildSection.Cell($"{row.AppliedIn.Count}/{total}", 80);
                profiles.tooltip = row.AppliedIn.Count == 0 ? "Nenhum perfil" : string.Join("\n", row.AppliedIn.Select(p => p.EffectiveId));
                line.Add(profiles);
                line.Add(BuildPipelineBuildSection.Cell(DefineUsageIndex.HasResult ? row.Usages.ToString() : "…", 60));
                var status = BuildPipelineBuildSection.Cell(row.StatusText, 0);
                if (row.Status != Status.Ok) status.AddToClassList(row.Status == Status.RequiredMissing || row.Status == Status.Forbidden ? "bp-define--removed" : "bp-define--undocumented");
                line.Add(status);
                box.Add(line);
            }

            if (rows.Count == 0)
                box.Add(new Label("Nada encontrado.") { style = { color = Muted } });

            return box;
        }

        private void Select(string symbol)
        {
            _selection.selectedDefine = _selection.selectedDefine == symbol ? "" : symbol;
            _ctx.SaveSelection();
            _ctx.RebuildSection();
        }

        private VisualElement SymbolDetail(string symbol)
        {
            var close = BuildPipelineBuildSection.SmallButton("✕", () => Select(symbol));
            var card = BuildPipelineUIStyle.CreateCard("🔎 " + symbol, null, close);
            card.AddToClassList("bp-card--focus");

            var declaration = _registry.Find(symbol);
            if (declaration != null)
            {
                card.Add(new Label(string.IsNullOrEmpty(declaration.description) ? "(sem descrição)" : declaration.description) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });
                var facts = new List<string>
                {
                    "Declarado por: " + declaration.source,
                    "Escopo: " + (declaration.IsAssemblyScoped ? "assembly (versionDefine — só dentro do asmdef)" : "global (PlayerSettings / perfil)")
                };
                if (declaration.IsAutomatic) facts.Add("Automático com o pacote: " + declaration.autoFromPackage);
                if (declaration.requiredWhen.Count > 0) facts.Add("Obrigatório quando: " + string.Join(" | ", declaration.requiredWhen));
                if (declaration.forbiddenWhen.Count > 0) facts.Add("Proibido quando: " + string.Join(" | ", declaration.forbiddenWhen));
                if (declaration.platforms.Count > 0) facts.Add("Plataformas: " + string.Join(", ", declaration.platforms));
                card.Add(new Label(string.Join("\n", facts)) { style = { fontSize = 11, color = Muted, whiteSpace = WhiteSpace.Normal } });
                if (!string.IsNullOrEmpty(declaration.docsUrl))
                    card.Add(BuildPipelineBuildSection.SmallButton("📖 Documentação", () => Application.OpenURL(declaration.docsUrl)));
            }
            else
            {
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    DefineSymbols.IsUnityBuiltIn(symbol) ? "Símbolo do Unity." : "⚠ Ninguém documentou este define. Se é do jogo, documente aqui; se é de um pacote seu, declare no package.json dele (wagenheimer.defines).",
                    DefineSymbols.IsUnityBuiltIn(symbol) ? "info" : "warn"));
                if (!DefineSymbols.IsUnityBuiltIn(symbol))
                {
                    var suggestion = SuggestionFor(symbol);
                    foreach (string reason in suggestion.Reasons)
                        card.Add(new Label("• " + reason) { style = { fontSize = 11, color = Muted, whiteSpace = WhiteSpace.Normal } });
                    card.Add(new Label("Descrição sugerida: " + suggestion.Declaration.description) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } });
                    foreach (string evidence in suggestion.Evidence)
                        card.Add(new Label("→ " + evidence) { style = { fontSize = 11, color = Muted, whiteSpace = WhiteSpace.Normal } });
                    card.Add(BuildPipelineBuildSection.SmallButton("＋ Documentar com a sugestão", () => AddGameDefine(symbol, suggestion.Declaration)));
                }
            }

            card.Add(ProfileMatrix(symbol));

            _usagesBySymbol.TryGetValue(symbol, out var usages);
            var usageFold = new Foldout { text = $"Onde é usado ({(DefineUsageIndex.HasResult ? (usages?.Count ?? 0).ToString() : "…")})", value = true };
            usageFold.Add(DefineViews.UsageList(usages ?? new List<DefineUsage>()));
            card.Add(usageFold);

            var writers = DefineUsageIndex.Writers();
            if (writers.Count > 0)
            {
                var writerFold = new Foldout { text = $"Código que grava defines globais ({writers.Count}) — pode ter mexido neste símbolo", value = false };
                foreach (var writer in writers) writerFold.Add(DefineViews.UsageRow(writer, null));
                card.Add(writerFold);
            }

            return card;
        }

        private VisualElement ProfileMatrix(string symbol)
        {
            var fold = new Foldout { text = "Por perfil (✔ entra no build · ✖ não entra) — e o que muda no código se sair", value = true };
            foreach (var pair in _stacks.OrderBy(p => p.Key.platform).ThenBy(p => p.Key.displayName))
            {
                var profile = pair.Key;
                var stack = pair.Value;
                bool has = stack.Has(symbol);
                var origin = stack.For(symbol).Select(e => (e.removes ? "−" : "+") + DefineViews.LayerLabel(e.layer) + " " + e.source).ToList();

                var row = new VisualElement();
                row.AddToClassList("bp-usage-row");
                row.Add(new Label(has ? "✔" : "✖") { style = { width = 18, color = has ? new Color(0.4f, 0.85f, 0.45f) : new Color(0.6f, 0.6f, 0.6f) } });
                var name = new Button(() => _ctx.Navigate(BuildPipelineSections.Profiles, profile.EffectiveId)) { text = $"{profile.displayName} ({profile.EffectiveId})" };
                name.AddToClassList("bp-link");
                name.style.width = 260;
                row.Add(name);
                row.Add(new Label(origin.Count == 0 ? "—" : string.Join("  ·  ", origin)) { style = { flexGrow = 1, fontSize = 10, color = Muted, whiteSpace = WhiteSpace.Normal } });

                if (has && DefineUsageIndex.HasResult)
                {
                    int changes = DefineImpact.Toggle(stack.FinalSet(), symbol, DefineUsageIndex.Usages).Count;
                    row.Add(new Label($"sem ele: {changes} ponto(s) mudam") { style = { fontSize = 10, color = changes > 0 ? new Color(0.95f, 0.75f, 0.35f) : Muted } });
                }

                fold.Add(row);
            }

            return fold;
        }

        private void AddGameDefine(string symbol, DefineDeclaration suggested = null)
        {
            _config.gameDefines ??= new List<DefineDeclaration>();
            if (_config.gameDefines.Any(d => d != null && d.symbol == symbol))
                return;

            _config.gameDefines.Add(suggested ?? new DefineDeclaration { symbol = symbol, description = "" });
            _selection.definesTab = "registry";
            _ctx.SaveSelection();
            _ctx.SaveConfig(false);
        }

        // ── Documentation advisor ────────────────────────────────────────────

        private static string GroupOf(PlatformType platform) => DefineEnvironment.TargetGroupName(platform);

        private List<DefineProfileFootprint> Footprints(string symbol) =>
            _stacks.Select(pair => new DefineProfileFootprint
            {
                ProfileId = pair.Key.EffectiveId,
                DisplayName = pair.Key.displayName,
                PlatformGroup = GroupOf(pair.Key.platform),
                Tags = ProfileTags.For(pair.Key),
                Has = pair.Value.Has(symbol)
            }).ToList();

        private DefineSuggestion SuggestionFor(string symbol) =>
            DefineAdvisor.Suggest(symbol, Footprints(symbol), DefineUsageIndex.Usages, DefineUsageIndex.VersionDefines());

        private IEnumerable<string> KnownDefinedSymbols(HashSet<string> versionDefined)
        {
            foreach (var declaration in _registry.Declarations) yield return declaration.symbol;
            foreach (var stack in _stacks.Values)
                foreach (var entry in stack.Entries)
                    if (!entry.removes) yield return entry.symbol;
            foreach (string symbol in versionDefined) yield return symbol;
        }

        private List<string> UndocumentedSymbols()
        {
            return _config.publishers.Where(p => p != null)
                .SelectMany(p => BuildDefineResolver.Resolve(_config, p))
                .Select(d => d.TrimStart('!', '-').Trim())
                .Where(d => d.Length > 0 && !DefineSymbols.IsUnityBuiltIn(d) && !_registry.IsDeclared(d))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();
        }

        private VisualElement SuggestionsCard()
        {
            var undocumented = UndocumentedSymbols();
            var card = BuildPipelineUIStyle.CreateCard(
                $"🧾 Defines usados e sem documentação ({undocumented.Count})",
                "Documentar um define diz ao pipeline PARA QUE ele serve e QUANDO é obrigatório ou proibido. Com isso a validação avisa " +
                "se um perfil novo esquece um define (ex.: build Free sem os anúncios) ou deixa vazar um que não deve (ex.: anúncios no Full). " +
                "As descrições e regras abaixo são inferidas dos seus perfis e do código; revise e edite antes de adicionar.");

            if (undocumented.Count == 0)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout("Todos os defines usados pelos perfis estão documentados.", "ok"));
                return card;
            }

            if (!DefineUsageIndex.HasResult)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    "A varredura do código ainda não terminou: as sugestões ficam mais pobres (sem contagem de usos). Aguarde ou rescaneie.", "warn"));
                card.Add(BuildPipelineBuildSection.SmallButton("🔄 Escanear código agora", () => DefineUsageIndex.Scan(() => _ctx.RebuildSection())));
            }

            var drafts = new Dictionary<string, Func<DefineDeclaration>>();

            foreach (string symbol in undocumented)
            {
                var suggestion = SuggestionFor(symbol);
                var fold = new Foldout { text = $"{symbol}  —  {suggestion.UsageCount} uso(s) no código · {suggestion.Declaration.description}", value = false };
                fold.style.marginBottom = 4;

                foreach (string reason in suggestion.Reasons)
                    fold.Add(new Label("• " + reason) { style = { fontSize = 11, color = Muted, whiteSpace = WhiteSpace.Normal } });

                var description = new TextField("Descrição") { value = suggestion.Declaration.description, multiline = true };
                description.style.whiteSpace = WhiteSpace.Normal;
                fold.Add(description);

                var required = new TextField("Obrigatório quando") { value = string.Join(", ", suggestion.Declaration.requiredWhen) };
                required.tooltip = "Tags separadas por vírgula; '+' = E. Ex.: free, ios+free";
                fold.Add(required);

                var forbidden = new TextField("Proibido quando") { value = string.Join(", ", suggestion.Declaration.forbiddenWhen) };
                fold.Add(forbidden);

                foreach (string evidence in suggestion.Evidence)
                    fold.Add(new Label("→ " + evidence) { style = { fontSize = 11, color = Muted, whiteSpace = WhiteSpace.Normal } });

                Func<DefineDeclaration> draft = () =>
                {
                    var declaration = JsonUtility.FromJson<DefineDeclaration>(JsonUtility.ToJson(suggestion.Declaration));
                    declaration.description = description.value?.Trim() ?? "";
                    declaration.requiredWhen = SplitRules(required.value);
                    declaration.forbiddenWhen = SplitRules(forbidden.value);
                    return declaration;
                };
                drafts[symbol] = draft;

                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
                buttons.Add(BuildPipelineBuildSection.SmallButton("＋ Documentar este", () =>
                {
                    AddGameDefine(symbol, draft());
                    _selection.definesTab = "registry";
                    _ctx.RebuildSection();
                }));

                var usageHost = new VisualElement();
                var usageButton = BuildPipelineBuildSection.SmallButton($"🔍 Ver usos no código ({suggestion.UsageCount})", () =>
                {
                    if (usageHost.childCount > 0) { usageHost.Clear(); return; }
                    var usages = DefineUsageIndex.UsagesOf(symbol);
                    usageHost.Add(usages.Count == 0
                        ? new Label("Nenhum uso encontrado no código (só nos perfis).") { style = { color = Muted } }
                        : DefineViews.UsageList(usages));
                });
                buttons.Add(usageButton);
                buttons.Add(BuildPipelineBuildSection.SmallButton("🧬 Ver na central", () => { _selection.definesTab = ""; _selection.selectedDefine = symbol; _ctx.SaveSelection(); _ctx.RebuildSection(); }));
                fold.Add(buttons);
                fold.Add(usageHost);
                card.Add(fold);
            }

            card.Add(BuildPipelineBuildSection.SmallButton($"＋ Adicionar todos ({undocumented.Count}) com as descrições acima", () =>
            {
                foreach (var pair in drafts)
                    AddGameDefine(pair.Key, pair.Value());
                _selection.definesTab = "registry";
                _ctx.RebuildSection();
            }));

            return card;
        }

        private static List<string> SplitRules(string text) =>
            (text ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim()).Where(r => r.Length > 0).ToList();

        // ── Health ───────────────────────────────────────────────────────────

        private VisualElement HealthCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("🩺 Saúde dos defines",
                "O que costuma dar erro ou ser esquecido: perfil sem um define obrigatório, define proibido que vazou, símbolo usado no código " +
                "mas nunca definido (possível erro de digitação), define sem documentação ou sem nenhum uso.");

            var problems = 0;

            foreach (var violation in _violations)
            {
                problems++;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.Add(new Label("⛔ " + violation.Message) { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal, fontSize = 11 } });
                var captured = violation;
                row.Add(BuildPipelineBuildSection.SmallButton(violation.Required ? "＋ Adicionar ao perfil" : "− Remover do perfil", () =>
                {
                    var list = captured.Profile.scriptingDefines ??= new List<string>();
                    string symbol = captured.Declaration.symbol;
                    list.RemoveAll(d => string.Equals(d.TrimStart('!', '-').Trim(), symbol, StringComparison.Ordinal));
                    list.Add(captured.Required ? symbol : "!" + symbol);
                    _ctx.SaveConfig(false);
                    _ctx.RebuildSection();
                }));
                card.Add(row);
            }

            var rows = BuildRows();
            foreach (var row in rows.Where(r => r.Status == Status.Orphan))
            {
                problems++;
                var line = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                line.Add(new Label($"❓ {row.Symbol}: {row.StatusText.Replace("❓ ", "")} ({row.Usages} uso(s))") { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal, fontSize = 11 } });
                string symbol = row.Symbol;
                line.Add(BuildPipelineBuildSection.SmallButton("🔍 Ver usos", () => Select(symbol)));
                card.Add(line);
            }

            int undocumented = UndocumentedSymbols().Count;
            if (undocumented > 0)
            {
                problems++;
                var line = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                line.Add(new Label($"⚠ {undocumented} define(s) usados pelos perfis sem documentação (sem descrição nem regras).") { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal, fontSize = 11 } });
                line.Add(BuildPipelineBuildSection.SmallButton("🧾 Documentar", () =>
                {
                    _selection.definesTab = "registry";
                    _ctx.SaveSelection();
                    _ctx.RebuildSection();
                }));
                card.Add(line);
            }

            var dead = rows.Where(r => r.Status == Status.Dead).Select(r => r.Symbol).ToList();
            if (dead.Count > 0)
            {
                problems++;
                card.Add(new Label($"💤 {dead.Count} define(s) sem nenhum uso no código (remova do perfil/registro se não são mais necessários): {string.Join(", ", dead.Take(12))}{(dead.Count > 12 ? "…" : "")}")
                { style = { fontSize = 11, whiteSpace = WhiteSpace.Normal, color = Muted } });
            }

            if (!DefineUsageIndex.HasResult)
                card.Add(new Label("ℹ A varredura do código ainda está rodando: órfãos e defines sem uso aparecem quando terminar.") { style = { fontSize = 11, color = Muted } });

            if (problems == 0)
                card.Add(BuildPipelineUIStyle.CreateCallout("Tudo consistente: nada obrigatório faltando, nada órfão, nada sem documentação.", "ok"));

            return card;
        }

        // ── Game registry ────────────────────────────────────────────────────

        private VisualElement RegistryTab()
        {
            Prepare();
            var box = new VisualElement();
            var card = BuildPipelineUIStyle.CreateCard("📚 Defines do jogo",
                "Documente aqui os defines que pertencem a ESTE jogo (ex.: ST2_ADS). Regras: requiredWhen/forbiddenWhen usam tags " +
                "(steam, free, full, demo, android, ios, mac, windows, linux, webgl, mobile, desktop, publisher:<Nome>, id:<perfil>); " +
                "'+' = E, '!' = NÃO. Ex.: ST2_ADS → requiredWhen: free · forbiddenWhen: full.");

            var so = new SerializedObject(_config);
            var list = new PropertyField(so.FindProperty("gameDefines"), "Declarações");
            card.Add(list);
            card.Bind(so);

            box.Add(card);
            box.Add(SuggestionsCard());

            var packages = BuildPipelineUIStyle.CreateCard("📦 Declarados pelos pacotes",
                "Lidos do package.json de cada pacote instalado (bloco \"wagenheimer\": { \"defines\": [...] }). Para mudar, edite o pacote.");
            var registry = DefineRegistry.Load(_config);
            foreach (var group in registry.Declarations.Where(d => d.source != DefineDeclaration.GameSource).GroupBy(d => d.source))
            {
                var title = BuildPipelineUIStyle.CreateText("📦 " + group.Key, "bp-group-title");
                packages.Add(title);
                foreach (var declaration in group)
                {
                    var row = new VisualElement();
                    row.AddToClassList("bp-usage-row");
                    var link = new Button(() => { _selection.selectedDefine = declaration.symbol; _selection.definesTab = ""; _ctx.SaveSelection(); _ctx.RebuildSection(); }) { text = declaration.symbol };
                    link.AddToClassList("bp-link");
                    link.style.width = 300;
                    row.Add(link);
                    row.Add(new Label(declaration.description) { style = { flexGrow = 1, whiteSpace = WhiteSpace.Normal, fontSize = 11 } });
                    packages.Add(row);
                }
            }

            foreach (var error in registry.Errors)
                packages.Add(BuildPipelineUIStyle.CreateCallout($"package.json de {error.Key} inválido: {error.Value}", "warn"));
            if (registry.Declarations.All(d => d.source == DefineDeclaration.GameSource))
                packages.Add(new Label("Nenhum pacote instalado declara defines ainda.") { style = { color = Muted } });

            box.Add(packages);
            return box;
        }

        // ── Globals ──────────────────────────────────────────────────────────

        private VisualElement GlobalsTab()
        {
            var box = new VisualElement();
            var diffs = DefineGlobalSnapshot.Diff();

            var accept = BuildPipelineBuildSection.SmallButton("✔ Marcar como revisado", () =>
            {
                DefineGlobalSnapshot.Accept();
                _ctx.RebuildAll();
            });
            var changes = BuildPipelineUIStyle.CreateCard("🌐 Mudanças desde a última revisão",
                $"Snapshot: {DefineGlobalSnapshot.SavedAt()}. Os globais valem para TODOS os perfis do grupo. O pipeline restaura o que aplica; " +
                "mudança aqui veio de edição manual, merge ou de um pacote que grava defines.", accept);
            if (diffs.Count == 0)
            {
                changes.Add(BuildPipelineUIStyle.CreateCallout("Nenhuma mudança desde a última revisão.", "ok"));
            }
            else
            {
                foreach (var diff in diffs)
                    changes.Add(BuildPipelineUIStyle.CreateCallout($"{diff.Group}: adicionados [{string.Join(", ", diff.Added)}] · removidos [{string.Join(", ", diff.Removed)}]", "warn"));
            }

            box.Add(changes);

            var current = BuildPipelineUIStyle.CreateCard("Globais atuais por grupo",
                "Windows, Mac e Linux compartilham o grupo Standalone: não existe define separado por SO desktop.");
            foreach (var group in DefineGlobalSnapshot.Groups)
            {
                var defines = DefineGlobalSnapshot.Current(group);
                current.Add(new Label(group.ToString()) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } });
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
                foreach (string symbol in defines)
                {
                    string s = symbol;
                    var chip = BuildPipelineBuildSection.SmallButton(symbol, () => { _selection.selectedDefine = s; _selection.definesTab = ""; _ctx.SaveSelection(); _ctx.RebuildSection(); });
                    row.Add(chip);
                }
                if (defines.Count == 0) row.Add(new Label("(vazio)") { style = { color = Muted } });
                current.Add(row);
            }

            current.Add(BuildPipelineBuildSection.SmallButton("Abrir Player Settings", () => SettingsService.OpenProjectSettings("Project/Player")));
            box.Add(current);

            var writers = DefineUsageIndex.Writers();
            var writerCard = BuildPipelineUIStyle.CreateCard($"✍ Código que grava defines globais ({writers.Count})",
                "PlayerSettings.SetScriptingDefineSymbols em scripts de editor. Se um global mudou sozinho, o culpado provável está aqui.");
            if (writers.Count == 0)
                writerCard.Add(new Label(DefineUsageIndex.HasResult ? "Nenhum." : "Varredura em andamento…") { style = { color = Muted } });
            foreach (var writer in writers)
                writerCard.Add(DefineViews.UsageRow(writer, writer.package));
            box.Add(writerCard);
            return box;
        }
    }
}
