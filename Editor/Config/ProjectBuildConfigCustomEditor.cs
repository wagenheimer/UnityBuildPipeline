using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// The asset inspector is a summary only: everything is edited in the Build Pipeline window, so there is one
    /// place to change a profile (before 2.0 the inspector, the window and the matrix window each had an editor).
    /// </summary>
    [CustomEditor(typeof(ProjectBuildConfig))]
    public class ProjectBuildConfigCustomEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var config = (ProjectBuildConfig)target;
            var root = new VisualElement();
            BuildPipelineUIStyle.Apply(root);

            var card = BuildPipelineUIStyle.CreateCard("🚀 Project Build Config",
                "Fonte única dos builds Unity (o Forge lê o catálogo gerado deste asset). Edite tudo na janela Build Pipeline.");

            var open = new Button(() => BuildPipelineWindow.ShowSection(BuildPipelineSections.Profiles));
            BuildPipelineUIStyle.ApplyIconText(open, "📋 Abrir no Build Pipeline");
            open.AddToClassList("bp-btn");
            open.AddToClassList("bp-btn--primary");
            open.style.height = 30;
            card.Add(open);

            var visible = config.publishers?.Count(p => p != null && !p.hidden) ?? 0;
            var locked = config.publishers?.Count(p => p != null && p.IsLanguageLocked) ?? 0;
            var steam = config.publishers?.Count(p => p != null && p.isSteam) ?? 0;
            card.Add(new Label($"{config.publishers?.Count ?? 0} perfis ({visible} visíveis) · {steam} Steam · {locked} com idioma travado · " +
                               $"{config.gameDefines?.Count ?? 0} defines do jogo documentados · schema {config.schemaVersion}")
            {
                style = { marginTop = 6, whiteSpace = WhiteSpace.Normal }
            });

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
            void Shortcut(string text, string section)
            {
                var button = new Button(() => BuildPipelineWindow.ShowSection(section));
                BuildPipelineUIStyle.ApplyIconText(button, text);
                button.AddToClassList("bp-btn");
                button.style.marginRight = 4;
                row.Add(button);
            }

            Shortcut("⚡ Build", BuildPipelineSections.Build);
            Shortcut("🧬 Defines", BuildPipelineSections.Defines);
            Shortcut("🎮 Steam", BuildPipelineSections.Steam);
            Shortcut("🔐 Assinatura", BuildPipelineSections.Signing);
            card.Add(row);
            root.Add(card);

            var raw = new Foldout { text = "Dados brutos (somente leitura)", value = false };
            var imgui = new IMGUIContainer(() =>
            {
                using (new EditorGUI.DisabledScope(true))
                    DrawDefaultInspector();
            });
            raw.Add(imgui);
            root.Add(raw);
            return root;
        }
    }
}
