using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal sealed class BuildPipelineCliView
    {
        public VisualElement Root { get; }

        private Publisher _cliPublisher = Publisher.BigFish;
        private PlatformType _cliPlatform = PlatformType.Windows64;
        private GameLanguage _cliLanguage = GameLanguage.English;
        private bool _cliCheat = false;
        private bool _cliDevBuild = false;
        private string _cliVariantId = "";

        private readonly ProjectBuildConfig _config;
        private readonly BuildPipelineSelectionState _selection;

        private TextField _commandPreviewField;

        public BuildPipelineCliView(ProjectBuildConfig config = null, BuildPipelineSelectionState selection = null)
        {
            _config = config;
            _selection = selection ?? new BuildPipelineSelectionState();

            // Restore the last command generator picks.
            if (Enum.TryParse(_selection.cliPublisher, out Publisher savedPublisher)) _cliPublisher = savedPublisher;
            if (Enum.TryParse(_selection.cliPlatform, out PlatformType savedPlatform)) _cliPlatform = savedPlatform;
            if (Enum.TryParse(_selection.cliLanguage, out GameLanguage savedLanguage)) _cliLanguage = savedLanguage;
            _cliCheat = _selection.cliCheat;
            _cliDevBuild = _selection.cliDevBuild;
            _cliVariantId = _selection.cliVariantId;

            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            BuildUI();
        }

        /// <summary>Writes the generator selections back to EditorPrefs so the tab reopens as it was left.</summary>
        private void PersistSelection()
        {
            _selection.cliPublisher = _cliPublisher.ToString();
            _selection.cliPlatform = _cliPlatform.ToString();
            _selection.cliLanguage = _cliLanguage.ToString();
            _selection.cliCheat = _cliCheat;
            _selection.cliDevBuild = _cliDevBuild;
            _selection.cliVariantId = _cliVariantId ?? "";
            _selection.Save(_config);
        }

        private void BuildUI()
        {
            Root.Clear();

            var callout = BuildPipelineUIStyle.CreateCallout(
                "Use the Unity CLI commands below to execute builds headlessly outside Unity (via PowerShell, Bash, or CI/CD pipelines such as GitHub Actions and Jenkins).",
                "info");
            Root.Add(callout);

            // Generator Card
            var genCard = BuildPipelineUIStyle.CreateCard("Interactive Command Generator", "Select build parameters to generate a production-ready headless execution command");

            var paramsRow = new VisualElement();
            paramsRow.style.flexDirection = FlexDirection.Row;
            paramsRow.style.flexWrap = Wrap.Wrap;
            paramsRow.style.marginBottom = 10;

            var pubField = new EnumField("Publisher", _cliPublisher);
            pubField.style.width = 200;
            pubField.style.marginRight = 10;
            pubField.style.marginBottom = 6;
            pubField.RegisterValueChangedCallback(e =>
            {
                _cliPublisher = (Publisher)e.newValue;
                PersistSelection();
                UpdateCommand();
            });
            paramsRow.Add(pubField);

            var platField = new EnumField("Platform", _cliPlatform);
            platField.style.width = 200;
            platField.style.marginRight = 10;
            platField.style.marginBottom = 6;
            platField.RegisterValueChangedCallback(e =>
            {
                _cliPlatform = (PlatformType)e.newValue;
                PersistSelection();
                UpdateCommand();
            });
            paramsRow.Add(platField);

            var langField = new EnumField("Language", _cliLanguage);
            langField.style.width = 200;
            langField.style.marginRight = 10;
            langField.style.marginBottom = 6;
            langField.RegisterValueChangedCallback(e =>
            {
                _cliLanguage = (GameLanguage)e.newValue;
                PersistSelection();
                UpdateCommand();
            });
            paramsRow.Add(langField);

            var cheatToggle = new Toggle("Cheat Mode") { value = _cliCheat };
            cheatToggle.style.marginRight = 10;
            cheatToggle.style.marginBottom = 6;
            cheatToggle.RegisterValueChangedCallback(e =>
            {
                _cliCheat = e.newValue;
                PersistSelection();
                UpdateCommand();
            });
            paramsRow.Add(cheatToggle);

            var devToggle = new Toggle("Development Build") { value = _cliDevBuild };
            devToggle.style.marginBottom = 6;
            devToggle.RegisterValueChangedCallback(e =>
            {
                _cliDevBuild = e.newValue;
                PersistSelection();
                UpdateCommand();
            });
            paramsRow.Add(devToggle);

            var variants = _config?.gameConfig != null && _config.gameConfig.HasBuildVariants
                ? _config.gameConfig.buildVariants.Where(v => v != null && v.IsValid).ToList()
                : new List<BuildVariant>();

            if (variants.Count > 0)
            {
                var variantNames = new List<string> { "(no variant)" };
                variantNames.AddRange(variants.Select(v => v.EffectiveDisplayName));

                var savedVariantIndex = string.IsNullOrEmpty(_cliVariantId)
                    ? 0
                    : Math.Max(0, variantNames.IndexOf(variants.FirstOrDefault(v => v.EffectiveId == _cliVariantId)?.EffectiveDisplayName ?? ""));

                var variantField = new PopupField<string>("Variant", variantNames, savedVariantIndex);
                variantField.style.width = 200;
                variantField.style.marginRight = 10;
                variantField.style.marginBottom = 6;
                variantField.RegisterValueChangedCallback(e =>
                {
                    var picked = variants.FirstOrDefault(v => v.EffectiveDisplayName == e.newValue);
                    _cliVariantId = picked != null ? picked.EffectiveId : "";
                    PersistSelection();
                    UpdateCommand();
                });
                paramsRow.Add(variantField);
            }

            genCard.Add(paramsRow);

            // Command Box
            var commandHeader = new VisualElement();
            commandHeader.style.flexDirection = FlexDirection.Row;
            commandHeader.style.justifyContent = Justify.SpaceBetween;
            commandHeader.style.alignItems = Align.Center;
            commandHeader.style.marginBottom = 4;

            var cmdTitle = new Label("Generated CLI Command (PowerShell):");
            cmdTitle.style.fontSize = 11;
            cmdTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            commandHeader.Add(cmdTitle);

            var copyBtn = new Button(() =>
            {
                if (_commandPreviewField != null)
                {
                    EditorGUIUtility.systemCopyBuffer = _commandPreviewField.value;
                    EditorUtility.DisplayDialog("Copied", "Command copied to clipboard!", "OK");
                }
            });
            BuildPipelineUIStyle.ApplyIconText(copyBtn, "📋 Copy to Clipboard");
            copyBtn.AddToClassList("bp-btn");
            copyBtn.AddToClassList("bp-btn--primary");
            copyBtn.style.height = 22;
            copyBtn.style.fontSize = 10;
            commandHeader.Add(copyBtn);

            genCard.Add(commandHeader);

            _commandPreviewField = new TextField { multiline = true };
            _commandPreviewField.isReadOnly = true;
            _commandPreviewField.AddToClassList("bp-code-box");
            _commandPreviewField.style.minHeight = 120;
            genCard.Add(_commandPreviewField);

            UpdateCommand();
            Root.Add(genCard);

            // Preset Scripts Card
            var scriptsCard = BuildPipelineUIStyle.CreateCard("Included Build Runner Scripts", "Portable scripts ready to run from terminal or CI runners");

            var scriptInfo = new Label(
                "• PowerShell Script: Packages/com.wagenheimer.buildpipeline/Tools/build.ps1\n" +
                "   Example: ./build.ps1 -publisher BigFish -language English -cheat false\n\n" +
                "• Bash Script: Packages/com.wagenheimer.buildpipeline/Tools/build.sh\n" +
                "   Example: ./build.sh -publisher Steam -language de");
            scriptInfo.style.fontSize = 11;
            scriptInfo.style.color = new Color(0.85f, 0.85f, 0.85f);
            scriptInfo.style.whiteSpace = WhiteSpace.Normal;
            scriptsCard.Add(scriptInfo);

            Root.Add(scriptsCard);
        }

        private void UpdateCommand()
        {
            if (_commandPreviewField == null) return;

            string unityPath = EditorApplication.applicationPath;
            string projPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string cmd = $"& \"{unityPath}\" -batchmode -nographics -quit `\n" +
                         $"  -projectPath \"{projPath}\" `\n" +
                         $"  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.Build `\n" +
                         $"  -publisher {_cliPublisher} `\n" +
                         $"  -language {_cliLanguage} `\n" +
                         $"  -platform {_cliPlatform} `\n" +
                         (string.IsNullOrEmpty(_cliVariantId) ? "" : $"  -variant {_cliVariantId} `\n") +
                         $"  -cheat {_cliCheat.ToString().ToLowerInvariant()} `\n" +
                         $"  -development {_cliDevBuild.ToString().ToLowerInvariant()} `\n" +
                         $"  -logFile \"build_{_cliPublisher.ToString().ToLowerInvariant()}.log\"";

            _commandPreviewField.value = cmd;
        }
    }
}

