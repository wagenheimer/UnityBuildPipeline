using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Post-build: reúne os artefatos de depuração que a Play Store pede junto do App Bundle e os
    /// deixa ao lado do .aab com nomes determinísticos, para o CI (AppDeployHub.Forge) enviá-los sem
    /// precisar adivinhar caminhos:
    ///  - <c>&lt;aab&gt;.symbols.zip</c>  → símbolos nativos (aviso "contém código nativo e você não
    ///    fez upload dos símbolos de depuração"). Unity gera como
    ///    <c>&lt;aabbase&gt;-&lt;version&gt;-v&lt;bundleVersionCode&gt;-IL2CPP.symbols.zip</c>.
    ///  - <c>&lt;aab&gt;_mapping.txt</c>   → arquivo de desofuscação do R8/ProGuard, quando o Minify
    ///    de release está ligado (aviso "não há um arquivo de desofuscação associado"). Sem minify
    ///    não existe mapping.txt e nada é copiado.
    /// Os caminhos ficam em <see cref="BuildContext.ExtraData"/> e saem no unity-manifest.json.
    /// </summary>
    public class ExportAndroidSymbolsStep : IPostBuildStep
    {
        public const string SymbolsZipKey = "androidSymbolsZip";
        public const string MappingFileKey = "androidMappingFile";
        public const string SymbolsEmbeddedKey = "androidSymbolsEmbedded";

        public int Order => 25;

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            if (context.Platform != PlatformType.Android)
                return true;

            var artifact = context.ResolvedOutputFilePath;
            if (string.IsNullOrEmpty(artifact) || !File.Exists(artifact))
                return true;

            var aabDir = Path.GetDirectoryName(artifact);
            if (string.IsNullOrEmpty(aabDir) || !Directory.Exists(aabDir))
                return true;

            var baseName = Path.GetFileNameWithoutExtension(artifact);
            if (string.IsNullOrEmpty(baseName))
                return true;

            var embedded = artifact.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);
            context.ExtraData[SymbolsEmbeddedKey] = embedded;

            var symbolsZip = ExportSymbolsZip(context, aabDir, baseName);
            if (!string.IsNullOrEmpty(symbolsZip))
                context.ExtraData[SymbolsZipKey] = symbolsZip;

            var mapping = ExportMappingFile(context, aabDir, baseName);
            if (!string.IsNullOrEmpty(mapping))
                context.ExtraData[MappingFileKey] = mapping;

            return true;
        }

        private static string ExportSymbolsZip(BuildContext context, string aabDir, string baseName)
        {
            try
            {
                var source = FindNewestSymbolsZip(aabDir, baseName);
                if (string.IsNullOrEmpty(source))
                {
                    context.LogWarning("Símbolos nativos: nenhum *.symbols.zip encontrado ao lado do artefato. " +
                                       "Confira Debug Symbols (Public/Debugging) no build profile do Android.");
                    return null;
                }

                var dest = Path.Combine(aabDir, baseName + ".symbols.zip");
                if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, dest, true);
                    context.Log($"Símbolos nativos exportados: {dest}");
                }
                else
                {
                    context.Log($"Símbolos nativos já no lugar: {dest}");
                }

                return dest;
            }
            catch (Exception ex)
            {
                context.LogWarning($"Falha ao exportar os símbolos nativos: {ex.Message}");
                return null;
            }
        }

        private static string FindNewestSymbolsZip(string aabDir, string baseName)
        {
            var roots = new List<string>
            {
                aabDir,
                Path.Combine(aabDir, baseName + "_BackUpThisFolder_ButDontShipItWithYourGame")
            };

            var candidates = new List<string>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    // Prioriza o zip nomeado pelo próprio artefato; depois qualquer *.symbols.zip.
                    candidates.AddRange(Directory.GetFiles(root, baseName + "*.symbols.zip", SearchOption.TopDirectoryOnly));
                    candidates.AddRange(Directory.GetFiles(root, "*.symbols.zip", SearchOption.TopDirectoryOnly));
                    candidates.AddRange(Directory.GetFiles(root, "*.symbols.zip", SearchOption.AllDirectories));
                }
                catch
                {
                    // Pastas de backup podem ter caminhos longos/permissões — segue com o que achou.
                }
            }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static string ExportMappingFile(BuildContext context, string aabDir, string baseName)
        {
            try
            {
                var source = FindNewestMappingFile(aabDir);
                if (string.IsNullOrEmpty(source))
                    return null;

                var dest = Path.Combine(aabDir, baseName + "_mapping.txt");
                if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, dest, true);
                    context.Log($"Arquivo de desofuscação (mapping.txt) exportado: {dest}");
                }

                return dest;
            }
            catch (Exception ex)
            {
                context.LogWarning($"Falha ao exportar o mapping.txt: {ex.Message}");
                return null;
            }
        }

        private static string FindNewestMappingFile(string aabDir)
        {
            var candidates = new List<string>();

            // Projeto Gradle gerado pelo Unity durante o build (R8/ProGuard escreve aqui).
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var gradleOut = Path.Combine(projectRoot, "Temp", "gradleOut");
                if (Directory.Exists(gradleOut))
                {
                    try { candidates.AddRange(Directory.GetFiles(gradleOut, "mapping.txt", SearchOption.AllDirectories)); }
                    catch { /* ignora pastas ilegíveis */ }
                }
            }

            try { candidates.AddRange(Directory.GetFiles(aabDir, "mapping.txt", SearchOption.AllDirectories)); }
            catch { }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
    }
}
