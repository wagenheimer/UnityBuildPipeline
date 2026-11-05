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
    /// UI Toolkit confirmation shown before any editor-triggered build. Replaces the OS message box
    /// (<see cref="EditorUtility.DisplayDialog"/>) that truncated long define lists and could not show
    /// chips/colors. It is displayed with <see cref="EditorWindow.ShowModalUtility"/>, which blocks the
    /// editor and returns when the window closes, so callers keep the synchronous bool contract.
    /// </summary>
    internal sealed class BuildConfirmationWindow : EditorWindow
    {
        private static readonly Vector2 DefaultSize = new Vector2(660, 740);

        private BuildContext _context;
        private ProjectBuildConfig _config;
        private List<string> _batchLines;
        private bool _confirmed;
        private Button _confirmButton;
        private bool _focusedOnce;

        public bool Confirmed => _confirmed;

        public static BuildConfirmationWindow Create(
            BuildContext context, ProjectBuildConfig config, string title, string batchTitle = null, List<string> batchLines = null)
        {
            var window = CreateInstance<BuildConfirmationWindow>();
            window._context = context;
            window._config = config;
            window._batchLines = batchLines;
            window.titleContent = new GUIContent(
                string.IsNullOrEmpty(batchTitle) ? (string.IsNullOrEmpty(title) ? "Confirm Build" : title) : batchTitle,
                EditorGUIUtility.IconContent("BuildSettings.Editor").image);
            window.minSize = new Vector2(560, 520);
            window.maxSize = new Vector2(1200, 1600);
            window.CenterOnEditor();
            return window;
        }

        private void CenterOnEditor()
        {
            try
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                float width = Mathf.Min(DefaultSize.x, main.width - 40);
                float height = Mathf.Min(DefaultSize.y, main.height - 60);
                position = new Rect(
                    main.x + (main.width - width) * 0.5f,
                    main.y + (main.height - height) * 0.5f,
                    width,
                    height);
            }
            catch (Exception)
            {
                position = new Rect(200, 120, DefaultSize.x, DefaultSize.y);
            }
        }

        private void CreateGUI()
        {
            // Defensive: CreateGUI can be invoked by a domain reload. A build confirmation always has
            // either a context (single build) or the batch lines.
            if (_context == null && _batchLines == null)
                return;

            var root = rootVisualElement;
            root.Clear();
            BuildPipelineUIStyle.Apply(root);
            root.AddToClassList("bp-confirm");
            root.focusable = true;

            bool batch = _batchLines != null;

            // Platform accent strip + hero header.
            var accentColor = batch ? new Color(0.20f, 0.60f, 0.86f) : PlatformColor(_context != null ? _context.Platform : PlatformType.Windows64);

            var accent = new VisualElement();
            accent.AddToClassList("bp-confirm-accent");
            accent.style.backgroundColor = accentColor;
            root.Add(accent);

            var hero = new VisualElement();
            hero.AddToClassList("bp-confirm-hero");
            hero.style.borderLeftColor = accentColor;

            var eyebrow = new Label(batch ? "BUILD PIPELINE  ·  LOTE" : "BUILD PIPELINE  ·  CONFIRMAÇÃO");
            eyebrow.AddToClassList("bp-confirm-eyebrow");
            hero.Add(eyebrow);

            var titleRow = new VisualElement();
            titleRow.AddToClassList("bp-confirm-title-row");

            var title = new Label(batch ? "Confirmar builds" : "Confirmar build");
            title.AddToClassList("bp-confirm-title");
            titleRow.Add(title);
            titleRow.Add(batch ? Chip($"{_batchLines.Count} na fila", "info") : PlatformChip(_context.Platform));
            hero.Add(titleRow);

            var subtitle = new Label(batch
                ? "Nada foi compilado ainda. Os builds abaixo rodam em sequência."
                : "Nada foi compilado ainda. Confira os detalhes antes de iniciar.");
            subtitle.AddToClassList("bp-confirm-subtitle");
            hero.Add(subtitle);
            root.Add(hero);

            // Scrollable body.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("bp-confirm-scroll");
            root.Add(scroll);

            var content = new VisualElement();
            content.AddToClassList("bp-confirm-body");
            scroll.Add(content);

            if (batch)
                BuildBatchContent(content);
            else
                BuildSingleContent(content);

            BuildFooter(root, batch);

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    _confirmed = false;
                    evt.StopPropagation();
                    Close();
                }
            }, TrickleDown.TrickleDown);

            root.RegisterCallback<GeometryChangedEvent>(_ => FocusConfirmOnce());
            FocusConfirmOnce();
        }

        private void FocusConfirmOnce()
        {
            if (_focusedOnce || _confirmButton == null)
                return;

            _confirmButton.Focus();

            // Only stop retrying once focus actually landed (layout may not be ready on the first pass).
            if (_confirmButton.focusController?.focusedElement == _confirmButton)
                _focusedOnce = true;
        }

        // ── Single build ─────────────────────────────────────────────────────

        private void BuildSingleContent(VisualElement content)
        {
            var profile = _context.PublisherProfile;

            var summary = BuildPipelineUIStyle.CreateCard("📋 Resumo do build");
            content.Add(summary);

            if (profile == null)
            {
                summary.Add(KvRow("Perfil", Chip("sem perfil correspondente", "warn"),
                    Muted("A build usará os defaults do pipeline.")));
            }
            else
            {
                summary.Add(KvRow("Perfil",
                    Strong(profile.displayName),
                    Chip("#" + profile.EffectiveId, "muted"),
                    Chip(profile.isFullGame ? "Full" : "Free", profile.isFullGame ? "ok" : "info"),
                    profile.isDemo ? Chip("Demo", "accent") : null,
                    profile.isSteam ? Chip("🎮 Steam", "accent") : null));
            }

            summary.Add(KvRow("Plataforma",
                PlatformChip(_context.Platform),
                _context.Platform == PlatformType.Windows32
                    ? Chip("x86 · 32-bit", "warn")
                    : _context.Platform.IsWindows() ? Chip("x64 · 64-bit", "info") : null));

            summary.Add(KvRow("Publisher", Chip(_context.Publisher.ToString(), "muted")));

            if (profile != null)
            {
                var backend = profile.scriptingBackend;
                summary.Add(KvRow("Backend",
                    Chip(backend == ScriptingImplementation.IL2CPP ? "IL2CPP" : "Mono2x",
                        backend == ScriptingImplementation.IL2CPP ? "info" : "ok",
                        backend == ScriptingImplementation.IL2CPP
                            ? "IL2CPP consome mais tempo de build; Mono2x compila mais rápido."
                            : "Mono2x: build mais rápido. No Windows x86 é o backend que evita o crash conhecido do IL2CPP.")));
            }

            summary.Add(KvRow("Idioma",
                Strong(_context.Language.AsLanguageName()),
                Chip(LanguageSourceLabel(_context.LanguageSource), LanguageSourceType(_context.LanguageSource))));

            summary.Add(KvRow("Variante",
                string.IsNullOrEmpty(_context.VariantId)
                    ? Muted("nenhuma (build padrão)")
                    : Chip(_context.VariantId, "accent", _context.VariantLabel),
                !string.IsNullOrEmpty(_context.VariantId) && _context.VariantLabel != _context.VariantId
                    ? Muted(_context.VariantLabel)
                    : null));

            var flags = FlagChips(_context);
            var flagValues = new List<VisualElement>();
            if (flags.Count == 0)
                flagValues.Add(Muted("nenhuma"));
            else
                flagValues.AddRange(flags);
            summary.Add(KvRow("Flags", flagValues.ToArray()));

            summary.Add(KvRow("Versão", Chip("v" + VersionText(), "ok")));

            if (profile != null && !string.IsNullOrWhiteSpace(profile.notes))
            {
                var notes = BuildPipelineUIStyle.CreateCallout("📝 " + profile.notes.Trim(), "info");
                summary.Add(notes);
            }

            content.Add(OutputCard());
            content.Add(DefinesCard(profile));
        }

        private VisualElement OutputCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("📁 Saída");

            string folder = "";
            string artifact = "";
            try
            {
                _context.ResolveOutputPaths();
                folder = _context.ResolvedOutputDirectory ?? "";
                artifact = _context.ResolvedOutputFilePath ?? "";
            }
            catch (Exception ex)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout("Não foi possível resolver a pasta de saída: " + ex.Message, "warn"));
                return card;
            }

            bool showsFile = !string.IsNullOrEmpty(artifact) &&
                             !string.Equals(folder, artifact, StringComparison.OrdinalIgnoreCase);

            var pathBox = new Label(string.IsNullOrEmpty(folder) ? "(não definida)" : folder);
            pathBox.AddToClassList("bp-confirm-path");
            pathBox.selection.isSelectable = true;
            card.Add(pathBox);

            var actions = new VisualElement();
            actions.AddToClassList("bp-confirm-inline-actions");

            if (showsFile)
            {
                var fileName = new Label("Executável: " + Path.GetFileName(artifact));
                fileName.AddToClassList("bp-confirm-hint-inline");
                actions.Add(fileName);
            }

            Button copy = null;
            copy = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = showsFile ? artifact : folder;
                BuildPipelineUIStyle.ApplyIconText(copy, "✅ Copiado");
            });
            BuildPipelineUIStyle.ApplyIconText(copy, "📋 Copiar caminho");
            copy.AddToClassList("bp-btn");
            copy.AddToClassList("bp-btn--small");
            actions.Add(copy);
            card.Add(actions);

            return card;
        }

        private VisualElement DefinesCard(PublisherProfile profile)
        {
            var card = BuildPipelineUIStyle.CreateCard("🧬 Defines");

            DefineStack stack = null;
            List<DefineRuleViolation> violations = new List<DefineRuleViolation>();
            string error = null;

            try
            {
                stack = DefineStackResolver.Resolve(_config, profile,
                    DefineEnvironment.GlobalDefines(_context.Platform), DefineEnvironment.AutoDefines(profile),
                    _context.ScriptingDefines, DefineEnvironment.TargetGroupName(_context.Platform));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (stack != null)
            {
                try
                {
                    violations = DefineRules.Check(DefineRegistry.Load(_config), profile, stack);
                }
                catch (Exception ex)
                {
                    error = "não foi possível validar o registro de defines: " + ex.Message;
                }
            }

            // Header counter badge lives in the card header area.
            if (stack != null)
            {
                var badge = Chip($"{stack.Final.Count} ativos", "ok",
                    $"{stack.Added.Count} adicionado(s) · {stack.Removed.Count} removido(s)");
                badge.style.marginLeft = 6;
                card.Q(className: "bp-card-header")?.Add(badge);
            }

            if (stack == null)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    "Não foi possível calcular os defines: " + (error ?? "erro desconhecido"), "warn"));
                return card;
            }

            if (!string.IsNullOrEmpty(error))
                card.Add(BuildPipelineUIStyle.CreateCallout("⚠ " + error, "warn"));

            card.Add(DefineSection("Adicionados", stack.Added.Select(symbol => Chip(symbol, "ok")).ToList(), "nenhum"));
            card.Add(DefineSection("Removidos", stack.Removed.Select(symbol => Chip(symbol, "fail")).ToList(), "nenhum"));
            card.Add(DefineSection("Final (" + (string.IsNullOrEmpty(stack.TargetGroup) ? "target" : stack.TargetGroup) + ")",
                stack.Final.Select(symbol => FinalChip(stack, symbol)).ToList(), "(nenhum)"));

            if (stack.CliOverrides.Count > 0)
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    "⚠ O CLI reativou define(s) removidos pelo perfil: " + string.Join(", ", stack.CliOverrides), "warn"));

            if (violations.Count > 0)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    "⛔ Regras de defines quebradas:\n" + string.Join("\n", violations.Select(v => "• " + v.Message)), "fail"));
            }

            return card;
        }

        private static VisualElement DefineSection(string label, IEnumerable<VisualElement> chips, string emptyText)
        {
            var section = new VisualElement();
            section.AddToClassList("bp-confirm-define-section");

            var caption = new Label(label);
            caption.AddToClassList("bp-confirm-define-caption");
            section.Add(caption);

            var row = new VisualElement();
            row.AddToClassList("bp-chip-row");

            var list = chips?.ToList() ?? new List<VisualElement>();
            if (list.Count == 0)
                row.Add(Chip(emptyText, "muted"));
            else
                foreach (var chip in list)
                    row.Add(chip);

            section.Add(row);
            return section;
        }

        private static Label FinalChip(DefineStack stack, string symbol)
        {
            var entry = stack.Entries.LastOrDefault(item =>
                !item.removes && string.Equals(item.symbol, symbol, StringComparison.OrdinalIgnoreCase));

            string type = entry == null ? "muted" : entry.layer switch
            {
                DefineLayer.Global => "info",
                DefineLayer.Preset => "ok",
                DefineLayer.Profile => "ok",
                DefineLayer.Auto => "accent",
                DefineLayer.Cli => "warn",
                _ => "muted"
            };

            string source = entry != null ? $"{entry.layer} · {entry.source}" : "origem desconhecida";
            return Chip(symbol, type, source);
        }

        // ── Batch ────────────────────────────────────────────────────────────

        private void BuildBatchContent(VisualElement content)
        {
            var card = BuildPipelineUIStyle.CreateCard("🗂️ Builds na fila",
                $"{_batchLines.Count} build(s) serão executados em sequência. O Unity compila cada um e restaura as configurações ao final.");

            var list = new VisualElement();
            list.AddToClassList("bp-confirm-batch-list");

            for (int i = 0; i < _batchLines.Count; i++)
            {
                string line = (_batchLines[i] ?? "").Trim();
                bool isMore = line.StartsWith("…", StringComparison.Ordinal);
                string text = line.StartsWith("•", StringComparison.Ordinal) ? line.Substring(1).Trim() : line;

                var row = new VisualElement();
                row.AddToClassList("bp-confirm-batch-row");

                if (isMore)
                {
                    row.Add(new Label(text).WithClass("bp-confirm-batch-more"));
                }
                else
                {
                    var index = new Label((i + 1).ToString("00"));
                    index.AddToClassList("bp-confirm-batch-index");
                    row.Add(index);

                    var parts = text.Split('·').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
                    if (parts.Count == 0)
                    {
                        row.Add(Muted(text));
                    }
                    else
                    {
                        row.Add(Strong(parts[0]));
                        for (int p = 1; p < parts.Count; p++)
                        {
                            string part = parts[p];
                            string type = part.Equals("CHEAT", StringComparison.OrdinalIgnoreCase) ? "warn" : "muted";
                            row.Add(Chip(part, type));
                        }
                    }
                }

                list.Add(row);
            }

            card.Add(list);
            content.Add(card);

            content.Add(BuildPipelineUIStyle.CreateCallout(
                "O lote pode levar bastante tempo. O progresso aparece na barra do Editor e cada build entra no histórico.", "info"));
        }

        // ── Footer ───────────────────────────────────────────────────────────

        private void BuildFooter(VisualElement root, bool batch)
        {
            var footer = new VisualElement();
            footer.AddToClassList("bp-confirm-footer");

            var hint = new Label(batch
                ? "Executa todos os builds da matriz. Pode levar muitos minutos.\nEnter confirma · Esc cancela"
                : "Executa o pipeline completo (pre/post steps) e pode levar vários minutos.\nEnter confirma · Esc cancela");
            hint.AddToClassList("bp-confirm-footer-hint");
            footer.Add(hint);

            var cancel = new Button(() =>
            {
                _confirmed = false;
                Close();
            });
            BuildPipelineUIStyle.ApplyIconText(cancel, "Cancelar");
            cancel.AddToClassList("bp-btn");
            cancel.AddToClassList("bp-confirm-btn");
            footer.Add(cancel);

            _confirmButton = new Button(() =>
            {
                _confirmed = true;
                Close();
            });
            BuildPipelineUIStyle.ApplyIconText(_confirmButton, batch ? "🚀 Build All" : "🚀 Build Now");
            _confirmButton.AddToClassList("bp-btn");
            _confirmButton.AddToClassList("bp-btn--success");
            _confirmButton.AddToClassList("bp-confirm-btn");
            footer.Add(_confirmButton);

            root.Add(footer);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static VisualElement KvRow(string label, params VisualElement[] values)
        {
            var row = new VisualElement();
            row.AddToClassList("bp-confirm-kv");

            var caption = new Label(label);
            caption.AddToClassList("bp-confirm-kv-label");
            row.Add(caption);

            var value = new VisualElement();
            value.AddToClassList("bp-confirm-kv-value");
            var any = false;
            foreach (var element in values)
            {
                if (element == null)
                    continue;
                value.Add(element);
                any = true;
            }

            if (!any)
                value.Add(Muted("—"));

            row.Add(value);
            return row;
        }

        private static Label Chip(string text, string type = "info", string tooltip = null)
        {
            var chip = BuildPipelineUIStyle.CreateBadge(text, type);
            chip.AddToClassList("bp-chip");
            if (!string.IsNullOrEmpty(tooltip))
                chip.tooltip = tooltip;
            return chip;
        }

        private static Label Strong(string text) => new Label(text).WithClass("bp-confirm-value-strong");

        private static Label Muted(string text) => new Label(text).WithClass("bp-confirm-muted");

        private static VisualElement PlatformChip(PlatformType platform) =>
            Chip(PlatformName(platform), PlatformBadgeType(platform), "Plataforma de destino do build");

        private static string PlatformName(PlatformType platform) => platform switch
        {
            PlatformType.Windows64 => "🪟 Windows 64-bit",
            PlatformType.Windows32 => "🪟 Windows 32-bit",
            PlatformType.macOS => "🍎 macOS",
            PlatformType.Android => "🤖 Android",
            PlatformType.iOS => "📱 iOS",
            PlatformType.Linux64 => "🐧 Linux 64-bit",
            PlatformType.WebGL => "🌐 WebGL",
            _ => platform.ToString()
        };

        private static string PlatformBadgeType(PlatformType platform) => platform switch
        {
            PlatformType.Windows64 or PlatformType.Windows32 => "info",
            PlatformType.Android => "ok",
            PlatformType.macOS => "accent",
            PlatformType.Linux64 => "warn",
            PlatformType.iOS => "muted",
            PlatformType.WebGL => "warn",
            _ => "muted"
        };

        private static Color PlatformColor(PlatformType platform) => platform switch
        {
            PlatformType.Windows64 or PlatformType.Windows32 => new Color(0.20f, 0.60f, 0.86f),
            PlatformType.Android => new Color(0.18f, 0.80f, 0.44f),
            PlatformType.macOS => new Color(0.65f, 0.55f, 0.95f),
            PlatformType.iOS => new Color(0.70f, 0.74f, 0.80f),
            PlatformType.Linux64 => new Color(0.95f, 0.77f, 0.06f),
            PlatformType.WebGL => new Color(0.90f, 0.49f, 0.13f),
            _ => new Color(0.20f, 0.60f, 0.86f)
        };

        private List<VisualElement> FlagChips(BuildContext context)
        {
            var chips = new List<VisualElement>();
            if (context.CheatMode) chips.Add(Chip("Cheat", "warn"));
            if (context.DevelopmentBuild) chips.Add(Chip("Development", "warn"));
            if (context.Demo) chips.Add(Chip("Demo", "accent"));
            if (context.Platform == PlatformType.Android) chips.Add(Chip(context.AppBundle ? ".aab" : ".apk", "ok"));
            if (context.AutoRun) chips.Add(Chip("Auto Run", "info"));
            return chips;
        }

        private string VersionText()
        {
            if (_config?.gameConfig == null)
                return "?";

            try
            {
                return _config.gameConfig.GameVersion.GameVersionAsText;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string LanguageSourceLabel(BuildLanguageSource source) => source switch
        {
            BuildLanguageSource.ProfileLock => "🔒 travado no perfil",
            BuildLanguageSource.Override => "override deste build",
            _ => "padrão do projeto"
        };

        private static string LanguageSourceType(BuildLanguageSource source) => source switch
        {
            BuildLanguageSource.ProfileLock => "warn",
            BuildLanguageSource.Override => "accent",
            _ => "muted"
        };
    }

    internal static class VisualElementClassExtensions
    {
        public static T WithClass<T>(this T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }
    }
}
