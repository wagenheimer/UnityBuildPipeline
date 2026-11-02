using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Small editor for <see cref="ProjectBuildConfig.definePresets"/>: id, label and one define per
    /// line, with usage count. The profile card only references the preset id.
    /// </summary>
    internal sealed class BuildDefinePresetPanel
    {
        public VisualElement Root { get; } = new VisualElement();

        public BuildDefinePresetPanel()
        {
            CreateGUI();
        }

        private static readonly Color MutedColor = new Color(0.62f, 0.65f, 0.71f);

        private ScrollView list;


        private void CreateGUI()
        {
            var root = Root;
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 8 } };
            header.Add(new Label("Define Presets")
            {
                style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold }
            });

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            header.Add(spacer);

            var add = new Button(AddPreset) { text = "Adicionar preset" };
            add.style.height = 24;
            add.style.paddingLeft = 10;
            add.style.paddingRight = 10;
            header.Add(add);

            root.Add(header);

            root.Add(new Label("O perfil referencia o id em 'definePresets'. Renomear o id quebra as referências (o validador acusa).")
            {
                style = { fontSize = 10.5f, color = MutedColor, marginBottom = 8, whiteSpace = WhiteSpace.Normal }
            });

            list = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            root.Add(list);

            Rebuild();
        }

        private void Rebuild()
        {
            if (list == null)
                return;

            list.Clear();

            var config = BuildCLI.FindOrCreateConfig();
            config.definePresets ??= new List<BuildDefinePreset>();

            if (config.definePresets.Count == 0)
            {
                list.Add(new Label("Nenhum preset cadastrado. Clique em 'Adicionar preset'.")
                {
                    style = { color = MutedColor, fontSize = 11 }
                });
                return;
            }

            foreach (var preset in config.definePresets.ToList())
            {
                if (preset != null)
                    list.Add(BuildCard(config, preset));
            }
        }

        private VisualElement BuildCard(ProjectBuildConfig config, BuildDefinePreset preset)
        {
            var card = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(1f, 1f, 1f, 0.05f),
                    paddingLeft = 10, paddingRight = 10, paddingTop = 8, paddingBottom = 8,
                    marginBottom = 8
                }
            };

            var id = new TextField("Id") { value = preset.id ?? "" };
            id.RegisterValueChangedCallback(evt =>
            {
                preset.id = (evt.newValue ?? "").Trim();
                Save(config, false);
            });
            card.Add(id);

            var name = new TextField("Nome") { value = preset.displayName ?? "" };
            name.RegisterValueChangedCallback(evt =>
            {
                preset.displayName = evt.newValue ?? "";
                Save(config, false);
            });
            card.Add(name);

            var defines = new TextField("Defines (um por linha)")
            {
                value = string.Join("\n", preset.defines ?? new List<string>()),
                multiline = true
            };
            defines.style.minHeight = 64;
            defines.RegisterValueChangedCallback(evt =>
            {
                preset.defines = (evt.newValue ?? "")
                    .Split('\n')
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToList();

                Save(config, false);
            });
            card.Add(defines);

            int uses = config.publishers?.Count(profile =>
                (profile.definePresets ?? new List<string>()).Any(presetId =>
                    string.Equals(presetId?.Trim(), preset.id, StringComparison.OrdinalIgnoreCase))) ?? 0;

            var footer = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
            footer.Add(new Label($"Usado por {uses} perfil(is)")
            {
                style = { fontSize = 10.5f, color = MutedColor, flexGrow = 1 }
            });

            var remove = new Button(() =>
            {
                config.definePresets.Remove(preset);
                Save(config, true);
            })
            {
                text = "Remover"
            };
            remove.style.height = 22;
            footer.Add(remove);

            card.Add(footer);
            return card;
        }

        private void AddPreset()
        {
            var config = BuildCLI.FindOrCreateConfig();
            config.definePresets ??= new List<BuildDefinePreset>();

            config.definePresets.Add(new BuildDefinePreset
            {
                id = $"preset{config.definePresets.Count + 1}",
                displayName = "Novo preset",
                defines = new List<string>()
            });

            Save(config, true);
        }

        private void Save(ProjectBuildConfig config, bool rebuild)
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(assetPath))
                BuildProfileCatalog.Write(config, assetPath);

            if (rebuild)
                Rebuild();
        }
    }
}
