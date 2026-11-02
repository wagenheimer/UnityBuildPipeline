using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Reusable define widgets: the layered stack, the clickable usage list and the impact list.</summary>
    internal static class DefineViews
    {
        private static readonly Color Muted = new Color(0.62f, 0.65f, 0.71f);

        public static string LayerLabel(DefineLayer layer)
        {
            switch (layer)
            {
                case DefineLayer.Global: return "1 · Global";
                case DefineLayer.Preset: return "2 · Preset";
                case DefineLayer.Profile: return "3 · Perfil";
                case DefineLayer.Auto: return "4 · Automático";
                case DefineLayer.Cli: return "5 · CLI";
                default: return "Assembly";
            }
        }

        public static string KindLabel(DefineUsageKind kind)
        {
            switch (kind)
            {
                case DefineUsageKind.Condition: return "#if";
                case DefineUsageKind.LocalDefine: return "#define (arquivo)";
                case DefineUsageKind.LocalUndef: return "#undef (arquivo)";
                case DefineUsageKind.ConditionalAttribute: return "[Conditional]";
                case DefineUsageKind.AssemblyConstraint: return "asmdef: constraint (assembly inteiro!)";
                case DefineUsageKind.AssemblyVersionDefine: return "asmdef: versionDefine (só no assembly)";
                case DefineUsageKind.CompilerResponseFile: return "csc.rsp";
                default: return "grava defines globais";
            }
        }

        /// <summary>Table of every layer of the profile's stack, with registry descriptions and usage counts.</summary>
        public static VisualElement StackTable(DefineStack stack, DefineRegistry registry, Action<string> onSymbol, bool includeGlobals)
        {
            var box = new VisualElement();
            var header = new VisualElement();
            header.AddToClassList("bp-table-header");
            header.Add(BuildPipelineBuildSection.Cell("Símbolo", 260, true));
            header.Add(BuildPipelineBuildSection.Cell("Camada", 110, true));
            header.Add(BuildPipelineBuildSection.Cell("Origem", 200, true));
            header.Add(BuildPipelineBuildSection.Cell("O que faz", 0, true));
            header.Add(BuildPipelineBuildSection.Cell("Uso", 70, true));
            box.Add(header);

            var entries = stack.Entries.Where(e => includeGlobals || e.layer != DefineLayer.Global).ToList();
            if (entries.Count == 0)
            {
                box.Add(new Label("Nenhum define além dos globais.") { style = { color = Muted, marginTop = 4 } });
                return box;
            }

            foreach (var entry in entries)
            {
                var row = new VisualElement();
                row.AddToClassList("bp-table-row");

                bool survives = stack.Has(entry.symbol);
                string symbolText = (entry.removes ? "− " : "+ ") + entry.symbol;
                var symbol = BuildPipelineBuildSection.Cell(symbolText, 260);
                symbol.AddToClassList(entry.removes ? "bp-define--removed" : survives ? "bp-define--on" : "bp-define--off");
                if (!entry.removes && !survives)
                    symbol.tooltip = "Adicionado aqui, mas removido por outra camada.";
                row.Add(symbol);
                row.Add(BuildPipelineBuildSection.Cell(LayerLabel(entry.layer), 110));
                row.Add(BuildPipelineBuildSection.Cell(entry.source, 200));

                var declaration = registry?.Find(entry.symbol);
                var description = BuildPipelineBuildSection.Cell(declaration != null
                    ? $"{declaration.description}  [{declaration.source}]"
                    : DefineSymbols.IsUnityBuiltIn(entry.symbol) ? "(Unity)" : "⚠ não documentado", 0);
                if (declaration == null && !DefineSymbols.IsUnityBuiltIn(entry.symbol))
                    description.AddToClassList("bp-define--undocumented");
                row.Add(description);

                row.Add(UsageButton(entry.symbol, onSymbol));
                box.Add(row);
            }

            return box;
        }

        /// <summary>"🔍 n" button: count of places the symbol is used; click opens it in the Defines center.</summary>
        public static Button UsageButton(string symbol, Action<string> onSymbol)
        {
            string text = DefineUsageIndex.HasResult
                ? $"🔍 {DefineUsageIndex.UsagesOf(symbol).Count(u => u.kind != DefineUsageKind.GlobalWriter)}"
                : "🔍 …";
            var button = new Button(() => onSymbol?.Invoke(symbol)) { tooltip = "Ver onde o código usa " + symbol };
            BuildPipelineUIStyle.ApplyIconText(button, text);
            button.AddToClassList("bp-btn");
            button.AddToClassList("bp-btn--small");
            button.style.width = 66;
            return button;
        }

        /// <summary>Clickable list of usages grouped by package (opens the file at the line).</summary>
        public static VisualElement UsageList(IEnumerable<DefineUsage> usages, int limit = 300)
        {
            var box = new VisualElement();
            var list = usages.ToList();
            if (list.Count == 0)
            {
                box.Add(new Label(DefineUsageIndex.HasResult ? "Nenhum uso encontrado no código." : "Varredura em andamento…") { style = { color = Muted } });
                return box;
            }

            foreach (var group in list.GroupBy(u => u.package).OrderBy(g => g.Key == "Assets" ? "" : g.Key, StringComparer.Ordinal))
            {
                var title = BuildPipelineUIStyle.CreateText($"{(group.Key == "Assets" ? "📁 Assets (jogo)" : "📦 " + group.Key)} — {group.Count()}", "bp-group-title");
                box.Add(title);

                foreach (var usage in group.Take(limit))
                    box.Add(UsageRow(usage, null));
            }

            return box;
        }

        public static VisualElement UsageRow(DefineUsage usage, string prefix)
        {
            var row = new VisualElement();
            row.AddToClassList("bp-usage-row");

            if (!string.IsNullOrEmpty(prefix))
                row.Add(new Label(prefix) { style = { width = 120, flexShrink = 0 } });

            var location = new Button(() => DefineUsageIndex.Open(usage)) { tooltip = "Abrir no editor de código" };
            location.text = $"{ShortPath(usage.file)}:{usage.line}";
            location.AddToClassList("bp-link");
            row.Add(location);

            var kind = new Label(KindLabel(usage.kind));
            kind.AddToClassList("bp-usage-kind");
            if (usage.kind == DefineUsageKind.AssemblyConstraint) kind.AddToClassList("bp-usage-kind--danger");
            row.Add(kind);

            var snippet = new Label(usage.snippet) { tooltip = $"{usage.file}:{usage.line}\nassembly: {usage.assembly}" };
            snippet.AddToClassList("bp-usage-snippet");
            row.Add(snippet);
            return row;
        }

        /// <summary>Rows of code that change between two sets ("entra em A / sai em B").</summary>
        public static VisualElement ImpactList(List<DefineImpactItem> items, string labelA, string labelB, int limit = 200)
        {
            var box = new VisualElement();
            if (!DefineUsageIndex.HasResult)
            {
                box.Add(new Label("Varredura do código em andamento…") { style = { color = Muted } });
                return box;
            }

            if (items.Count == 0)
            {
                box.Add(new Label("Nenhum bloco de código muda.") { style = { color = Muted } });
                return box;
            }

            int assemblies = items.Count(i => i.Usage.kind == DefineUsageKind.AssemblyConstraint);
            box.Add(new Label($"{items.Count} ponto(s) do código mudam" + (assemblies > 0 ? $", incluindo {assemblies} ASSEMBLY(S) INTEIRO(S)" : "") + ".")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            });

            foreach (var item in items.Take(limit))
            {
                string state = item.InA ? $"só em {labelA}" : $"só em {labelB}";
                box.Add(UsageRow(item.Usage, state));
            }

            if (items.Count > limit)
                box.Add(new Label($"… +{items.Count - limit}") { style = { color = Muted } });

            return box;
        }

        public static string ShortPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                int second = path.IndexOf('/', "Packages/".Length);
                return second > 0 ? "…" + path.Substring(second) : path;
            }

            return path;
        }
    }
}
