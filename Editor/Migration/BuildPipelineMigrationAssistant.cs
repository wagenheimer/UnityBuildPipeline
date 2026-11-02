using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>One pending migration item, with the window section that fixes it.</summary>
    public sealed class MigrationChecklistItem
    {
        public string Text = "";
        public string Section = "";
        public bool Blocking;
        public List<string> Details = new List<string>();
    }

    /// <summary>What is still pending after a migration (legacy GameConfig or 1.x → 2.0). Pure apart from file reads.</summary>
    public static class MigrationChecklist
    {
        public static List<MigrationChecklistItem> Build(ProjectBuildConfig config, LegacyMigrationRecord record)
        {
            var items = new List<MigrationChecklistItem>();

            if (record != null && record.IsOpen)
            {
                var summary = new MigrationChecklistItem
                {
                    Text = $"Projeto antigo migrado pelo script em {record.migratedAt}" +
                           (record.movedFields.Count > 0 ? $": {record.movedFields.Count} campo(s) do jogo foram para {record.configClass}" : "") +
                           ". Confira que o jogo compila e roda.",
                    Section = BuildPipelineSections.Project
                };
                if (record.movedFields.Count > 0) summary.Details.Add("Campos movidos: " + string.Join(", ", record.movedFields));
                if (record.keptTypes.Count > 0) summary.Details.Add("Tipos mantidos do arquivo antigo: " + string.Join(", ", record.keptTypes));
                if (record.retypedFiles.Count > 0) summary.Details.Add("Declarações trocadas para " + record.configClass + ": " + string.Join(", ", record.retypedFiles));
                if (!string.IsNullOrEmpty(record.backup)) summary.Details.Add("Backup: " + record.backup);
                items.Add(summary);

                if (record.manualReview.Count > 0)
                    items.Add(new MigrationChecklistItem
                    {
                        Text = $"{record.manualReview.Count} ponto(s) do código para revisar à mão (apontados pelo script).",
                        Section = BuildPipelineSections.Project,
                        Blocking = true,
                        Details = new List<string>(record.manualReview)
                    });

                if (record.missingPublishers.Count > 0)
                    items.Add(new MigrationChecklistItem
                    {
                        Text = "Publishers que só existiam no jogo: " + string.Join(", ", record.missingPublishers) +
                               ". Use perfis/variantes de build no lugar deles.",
                        Section = BuildPipelineSections.Profiles
                    });
            }

            if (config == null)
            {
                items.Add(new MigrationChecklistItem
                {
                    Text = "Este projeto ainda não tem ProjectBuildConfig.",
                    Section = BuildPipelineSections.Project,
                    Blocking = true
                });
                return items;
            }

            if (config.gameConfig == null)
                items.Add(new MigrationChecklistItem { Text = "Nenhum GameConfig vinculado ao ProjectBuildConfig.", Section = BuildPipelineSections.Project, Blocking = true });

            if (config.keystoreSource == KeystoreSource.AppDeployHubVault && string.IsNullOrEmpty(config.vaultKeystoreCredentialId) &&
                config.publishers.Any(p => p != null && p.platform == PlatformType.Android))
                items.Add(new MigrationChecklistItem { Text = "Escolha o keystore Android no cofre do AppDeployHub (o cofre antigo foi aposentado).", Section = BuildPipelineSections.Signing, Blocking = true });

            var steamLike = config.publishers.Where(p => p != null && !p.isSteam && PublisherProfile.LooksLikeSteam(p)).Select(p => p.EffectiveId).ToList();
            if (steamLike.Count > 0)
                items.Add(new MigrationChecklistItem { Text = "Parecem Steam mas não estão marcados como Steam: " + string.Join(", ", steamLike), Section = BuildPipelineSections.Profiles });

            if ((config.gameDefines?.Count ?? 0) == 0 && config.publishers.Any(p => p != null && (p.scriptingDefines?.Count ?? 0) > 0))
                items.Add(new MigrationChecklistItem { Text = "Documente os defines do jogo (Defines ▸ Defines do jogo) para o validador conferir Free/Full.", Section = BuildPipelineSections.Defines });

            return items;
        }
    }

    /// <summary>
    /// Asks, once per editor session, to migrate a project that has a GameConfig but no ProjectBuildConfig (or that the
    /// legacy script just migrated), and shows what a 1.x → 2.0 upgrade changed. Nothing is created without consent.
    /// </summary>
    [InitializeOnLoad]
    public static class BuildPipelineMigrationAssistant
    {
        private const string SessionKey = "BuildPipeline.MigrationAssistant.Shown";
        private static string SkipKey => "BuildPipeline.MigrationAssistant.Skip." + Application.dataPath.GetHashCode().ToString("x");

        static BuildPipelineMigrationAssistant()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += () =>
            {
                try { Run(); }
                catch (Exception ex) { Debug.LogWarning("[BuildPipeline] Assistente de migração: " + ex.Message); }
            };
        }

        [MenuItem("Tools/Wagenheimer/Build Pipeline/Migration Assistant...", priority = 112)]
        public static void OpenFromMenu()
        {
            EditorPrefs.DeleteKey(SkipKey);
            SessionState.SetBool(SessionKey, false);
            Run(force: true);
        }

        public static void Run(bool force = false)
        {
            if (!force && SessionState.GetBool(SessionKey, false))
                return;
            SessionState.SetBool(SessionKey, true);

            var config = LegacyGameConfigMigrator.FindProjectBuildConfig();
            var record = LegacyMigrationRecord.Load();

            if (config == null)
            {
                bool legacyScriptRan = record != null && record.status == "pending-unity";
                if (!legacyScriptRan && !LegacyGameConfigMigrator.HasGameConfigAsset() && !force)
                    return;
                if (!force && !legacyScriptRan && EditorPrefs.GetBool(SkipKey, false))
                    return;

                string message = legacyScriptRan
                    ? "O script de migração já preparou este projeto. Falta criar o ProjectBuildConfig a partir do GameConfig " +
                      "(perfis padrão, nome e bundle ids de cada loja).\n\nCriar agora?"
                    : "Este projeto tem um GameConfig, mas ainda não tem ProjectBuildConfig (a configuração dos builds).\n\n" +
                      "Migrar agora? O Build Pipeline cria o ProjectBuildConfig, vincula o GameConfig e copia nome e bundle ids " +
                      "de cada loja para os perfis. Nada é apagado.";

                int choice = EditorUtility.DisplayDialogComplex("Build Pipeline — migração", message,
                    "Migrar agora", "Depois", "Não perguntar neste projeto");
                if (choice == 2) EditorPrefs.SetBool(SkipKey, true);
                if (choice != 0) return;

                LegacyGameConfigMigrator.MigrateOrCreate();
                BuildPipelineWindow.ShowSection(BuildPipelineSections.Project);
                return;
            }

            if (record != null && record.status == "pending-unity")
            {
                LegacyMigrationRecord.MarkUnityStepDone();
                BuildPipelineWindow.ShowSection(BuildPipelineSections.Project);
                return;
            }

            if (ProjectBuildConfigMigrator.Notes.Count > 0)
            {
                var notes = string.Join("\n\n• ", ProjectBuildConfigMigrator.Notes.Distinct());
                ProjectBuildConfigMigrator.Notes.Clear();
                if (EditorUtility.DisplayDialog("Build Pipeline 2.0", "• " + notes + "\n\nSalve o projeto para gravar a atualização.", "Ver o que falta", "OK"))
                    BuildPipelineWindow.ShowSection(BuildPipelineSections.Project);
            }
        }
    }
}
