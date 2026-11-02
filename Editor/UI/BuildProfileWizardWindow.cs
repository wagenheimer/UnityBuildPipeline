using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Creates a mobile profile from a template: store + Free/Full drives id, display name, platform,
    /// bundle, output folder and the ad/tracking defines, so the profile is born valid.
    /// </summary>
    public sealed class BuildProfileWizardWindow : EditorWindow
    {
        private PopupField<Publisher> publisherField;
        private Toggle fullToggle;
        private TextField idField;
        private TextField bundleField;
        private TextField outputField;
        private Label errorLabel;

        [MenuItem("Tools/Wagenheimer/Build Pipeline/New Profile...", priority = 20)]
        public static void Open()
        {
            var window = GetWindow<BuildProfileWizardWindow>();
            window.titleContent = new GUIContent("New Build Profile");
            window.minSize = new Vector2(460, 320);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 14;
            root.style.paddingRight = 14;
            root.style.paddingTop = 12;
            root.style.paddingBottom = 12;

            root.Add(new Label("Novo perfil de build")
            {
                style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 }
            });

            var publishers = PublisherCatalog.MobilePublishers;
            publisherField = new PopupField<Publisher>("Loja", publishers, publishers.Count > 0 ? publishers[0] : Publisher.iOSFree);
            publisherField.RegisterValueChangedCallback(_ => ApplySuggestions());
            root.Add(publisherField);

            fullToggle = new Toggle("Full") { value = false };
            fullToggle.RegisterValueChangedCallback(_ => ApplySuggestions());
            root.Add(fullToggle);

            idField = new TextField("Id");
            root.Add(idField);

            bundleField = new TextField("Bundle");
            root.Add(bundleField);

            outputField = new TextField("Output");
            root.Add(outputField);

            errorLabel = new Label { style = { color = new Color(0.94f, 0.33f, 0.31f), marginTop = 6 } };
            root.Add(errorLabel);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 10 } };
            var create = new Button(CreateProfile) { text = "Criar perfil" };
            create.style.height = 26;
            create.style.paddingLeft = 14;
            create.style.paddingRight = 14;
            buttons.Add(create);

            var cancel = new Button(Close) { text = "Cancelar" };
            cancel.style.height = 26;
            cancel.style.marginLeft = 6;
            buttons.Add(cancel);

            root.Add(buttons);

            ApplySuggestions();
        }

        private void ApplySuggestions()
        {
            if (publisherField == null)
                return;

            var config = BuildCLI.FindOrCreateConfig();
            bool full = fullToggle.value;
            Publisher publisher = publisherField.value;

            idField.SetValueWithoutNotify(BuildProfileTemplates.SuggestedId(publisher, full));
            bundleField.SetValueWithoutNotify(BuildProfileTemplates.SuggestedBundle(config, full));
            outputField.SetValueWithoutNotify(BuildProfileTemplates.SuggestedOutput(publisher, full));
        }

        private void CreateProfile()
        {
            var config = BuildCLI.FindOrCreateConfig();
            string id = (idField.value ?? "").Trim();

            if (string.IsNullOrEmpty(id))
            {
                errorLabel.text = "O id é obrigatório.";
                return;
            }

            if (config.publishers.Any(profile =>
                    string.Equals(profile.EffectiveId, id, StringComparison.OrdinalIgnoreCase)))
            {
                errorLabel.text = $"Já existe um perfil com o id '{id}'.";
                return;
            }

            var tracking = BuildProfileCatalog.ReadTrackingPolicy();
            var created = BuildProfileTemplates.Create(
                publisherField.value, fullToggle.value, id, bundleField.value, outputField.value, tracking);

            config.publishers.Add(created);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(assetPath))
                BuildProfileCatalog.Write(config, assetPath);

            Debug.Log($"[BuildPipeline] Perfil criado: {created.EffectiveId} ({created.displayName}).");
            Close();
            BuildPipelineWindow.ShowSection(BuildPipelineSections.Profiles, created.EffectiveId);
        }
    }
}
