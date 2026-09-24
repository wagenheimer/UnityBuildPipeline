using System;
using System.IO;
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

        private TextField _commandPreviewField;

        public BuildPipelineCliView()
        {
            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            BuildUI();
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
                UpdateCommand();
            });
            paramsRow.Add(langField);

            var cheatToggle = new Toggle("Cheat Mode") { value = _cliCheat };
            cheatToggle.style.marginRight = 10;
            cheatToggle.style.marginBottom = 6;
            cheatToggle.RegisterValueChangedCallback(e =>
            {
                _cliCheat = e.newValue;
                UpdateCommand();
            });
            paramsRow.Add(cheatToggle);

            var devToggle = new Toggle("Development Build") { value = _cliDevBuild };
            devToggle.style.marginBottom = 6;
            devToggle.RegisterValueChangedCallback(e =>
            {
                _cliDevBuild = e.newValue;
                UpdateCommand();
            });
            paramsRow.Add(devToggle);

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
            })
            { text = "📋 Copy to Clipboard" };
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
                         $"  -cheat {_cliCheat.ToString().ToLowerInvariant()} `\n" +
                         $"  -devBuild {_cliDevBuild.ToString().ToLowerInvariant()} `\n" +
                         $"  -logFile \"build_{_cliPublisher.ToString().ToLowerInvariant()}.log\"";

            _commandPreviewField.value = cmd;
        }
    }
}
