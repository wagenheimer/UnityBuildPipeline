using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum BuildOutputCheckSeverity
    {
        Ok = 0,
        Warning = 1,
        Error = 2,
    }

    /// <summary>Result of testing an output folder: validity, existence, write access and useful diagnostics.</summary>
    public sealed class BuildOutputCheck
    {
        public string Root = "";
        public string Subfolder = "";
        public string PlatformFolder = "";
        public bool UsesPlatformFolder;
        public string BaseFolder = "";
        public bool Exists;
        public bool Created;
        public bool Writable;
        public bool InsideProject;
        public int ExistingFolders;
        public long FreeBytes = -1;
        public BuildOutputCheckSeverity Severity;
        public string Summary = "";
        public readonly List<string> Details = new List<string>();
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<string> Errors = new List<string>();

        public bool HasError => Severity == BuildOutputCheckSeverity.Error;
    }

    /// <summary>
    /// Tests a build output path without running a build: invalid characters, absolute/relative subfolder,
    /// folder inside the Unity project, long paths, existence, write permission and free disk space.
    /// The same code powers the profile card and the project root card, so both report the same diagnostics.
    /// </summary>
    public static class BuildOutputFolderInspector
    {
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars()
            .Where(c => c != '\\' && c != '/' && c != ':')
            .ToArray();

        public static BuildOutputCheck Inspect(
            ProjectBuildConfig config, PublisherProfile profile, PlatformType platform, bool prepareFolder)
        {
            string root = config != null ? config.GetEffectiveBuildOutputRoot(platform) ?? "" : "";
            string subfolder = ProfileOutputPaths.EffectiveSubfolder(profile, platform);
            bool usesPlatform = ProfileOutputPaths.UsesPlatformSubfolder(profile, platform);
            string platformFolder = usesPlatform ? ProfileOutputPaths.PlatformFolder(platform) : "";
            string folder = ProfileOutputPaths.ResolveFolder(config, profile, platform);

            var check = InspectPath(folder, subfolder, prepareFolder);
            check.Root = root;
            check.Subfolder = subfolder;
            check.UsesPlatformFolder = usesPlatform;
            check.PlatformFolder = platformFolder;

            if (string.IsNullOrWhiteSpace(root))
                check.Errors.Add("A raiz de saída do projeto está vazia.");
            else if (!Path.IsPathRooted(root))
                check.Warnings.Add("A raiz não é um caminho absoluto; o destino pode variar conforme a pasta de onde o build roda.");

            if (!string.IsNullOrWhiteSpace(root))
            {
                var used = InvalidNameChars.Where(c => root.IndexOf(c) >= 0).ToArray();
                if (used.Length > 0)
                    check.Errors.Add("A raiz contém caracteres inválidos: " + string.Join(" ", used.Select(c => $"'{c}'")));
            }

            if (!string.IsNullOrWhiteSpace(subfolder) && Path.IsPathRooted(subfolder))
                check.Warnings.Add("A subpasta é um caminho absoluto e ignora a raiz do projeto. Use um caminho relativo.");

            var details = new List<string>
            {
                "Raiz: " + (string.IsNullOrWhiteSpace(root) ? "(vazia)" : root),
                "Subpasta: " + (string.IsNullOrWhiteSpace(subfolder) ? "(raiz)" : subfolder)
            };
            if (usesPlatform)
                details.Add("Plataforma automática: " + platformFolder + " (Steam)");
            details.Add("Pasta base: " + (string.IsNullOrWhiteSpace(folder) ? "(vazia)" : folder));
            details.AddRange(check.Details);

            check.Details.Clear();
            check.Details.AddRange(details);
            Finalize(check);
            return check;
        }

        /// <summary>Tests one folder in isolation (used by the project root card).</summary>
        public static BuildOutputCheck InspectRoot(string root, bool prepareFolder)
        {
            var check = InspectPath(root, null, prepareFolder);
            check.Root = root ?? "";

            if (string.IsNullOrWhiteSpace(root))
                check.Errors.Add("A raiz de saída do projeto está vazia.");
            else if (!Path.IsPathRooted(root))
                check.Warnings.Add("A raiz não é um caminho absoluto; o destino pode variar conforme a pasta de onde o build roda.");

            if (!string.IsNullOrWhiteSpace(root))
            {
                var used = InvalidNameChars.Where(c => root.IndexOf(c) >= 0).ToArray();
                if (used.Length > 0)
                    check.Errors.Add("A raiz contém caracteres inválidos: " + string.Join(" ", used.Select(c => $"'{c}'")));
            }

            var details = new List<string>
            {
                "Raiz: " + (string.IsNullOrWhiteSpace(root) ? "(vazia)" : root)
            };
            details.AddRange(check.Details);

            check.Details.Clear();
            check.Details.AddRange(details);
            Finalize(check);
            return check;
        }

        public static string SeverityClass(BuildOutputCheckSeverity severity) => severity switch
        {
            BuildOutputCheckSeverity.Error => "fail",
            BuildOutputCheckSeverity.Warning => "warn",
            _ => "ok"
        };

        private static BuildOutputCheck InspectPath(string folder, string relativeSubfolder, bool prepareFolder)
        {
            var check = new BuildOutputCheck { BaseFolder = folder ?? "" };

            if (string.IsNullOrWhiteSpace(check.BaseFolder))
            {
                check.Errors.Add("O caminho da pasta está vazio.");
                return check;
            }

            char[] invalid = InvalidNameChars;
            if (!string.IsNullOrWhiteSpace(relativeSubfolder))
            {
                var used = invalid.Where(c => relativeSubfolder.IndexOf(c) >= 0).ToArray();
                if (used.Length > 0)
                    check.Errors.Add("A subpasta contém caracteres inválidos: " + string.Join(" ", used.Select(c => $"'{c}'")));
            }

            check.InsideProject = IsInsideProject(check.BaseFolder);
            if (check.InsideProject)
                check.Warnings.Add("A pasta está dentro do projeto Unity; isso pode causar reimportação e builds lentos. Prefira uma pasta fora do projeto.");

            if (check.BaseFolder.Length >= 240)
                check.Warnings.Add($"O caminho tem {check.BaseFolder.Length} caracteres; no Windows o limite clássico é 260.");

            try
            {
                check.Exists = Directory.Exists(check.BaseFolder);
                if (File.Exists(check.BaseFolder))
                {
                    check.Errors.Add("Existe um arquivo com o nome da pasta no destino.");
                    return check;
                }

                if (!check.Exists && prepareFolder)
                {
                    try
                    {
                        Directory.CreateDirectory(check.BaseFolder);
                        check.Created = true;
                        check.Exists = true;
                    }
                    catch (Exception ex)
                    {
                        check.Errors.Add("Não foi possível criar a pasta: " + ex.Message);
                    }
                }

                if (check.Exists)
                {
                    string writeError = null;
                    check.Writable = CanWrite(check.BaseFolder, out writeError);
                    if (!check.Writable)
                        check.Errors.Add("A pasta existe, mas não é gravável: " + writeError);

                    try { check.ExistingFolders = Directory.GetDirectories(check.BaseFolder).Length; } catch { }
                    check.FreeBytes = TryFreeBytes(check.BaseFolder);
                    check.Details.Add(check.Created ? "Criada agora pelo teste." : "Já existe.");
                    check.Details.Add($"{check.ExistingFolders} subpasta(s) dentro dela.");
                    if (check.Writable) check.Details.Add("Gravação: OK.");
                    if (check.FreeBytes >= 0) check.Details.Add("Livre no disco: " + FormatBytes(check.FreeBytes) + ".");
                }
                else
                {
                    string parent = NearestExistingParent(check.BaseFolder);
                    if (string.IsNullOrEmpty(parent))
                    {
                        check.Errors.Add("Nenhuma pasta acima do destino existe. Confira a raiz e o disco.");
                    }
                    else if (!prepareFolder)
                    {
                        check.Warnings.Add("A pasta ainda não existe; será criada no primeiro build.");
                        check.Details.Add("Pasta pai existente: " + parent);
                    }
                    else
                    {
                        string writeError = null;
                        if (!CanWrite(parent, out writeError))
                        {
                            check.Errors.Add($"A pasta não existe e a pasta pai não é gravável: {writeError}");
                        }
                        else
                        {
                            check.Warnings.Add("A pasta ainda não existe; ela será criada no início do build.");
                            check.Details.Add("Pasta pai existente e gravável: " + parent);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                check.Errors.Add("Não foi possível inspecionar o destino: " + ex.Message);
            }

            return check;
        }

        private static void Finalize(BuildOutputCheck check)
        {
            check.Severity = check.Errors.Count > 0
                ? BuildOutputCheckSeverity.Error
                : check.Warnings.Count > 0
                    ? BuildOutputCheckSeverity.Warning
                    : BuildOutputCheckSeverity.Ok;

            foreach (var warning in check.Warnings)
                check.Details.Add("Aviso: " + warning);
            foreach (var error in check.Errors)
                check.Details.Add("Erro: " + error);

            if (check.Errors.Count > 0)
            {
                check.Summary = "⛔ " + check.Errors[0] +
                                (check.Errors.Count > 1 ? $" (+{check.Errors.Count - 1} outro(s))" : "");
            }
            else if (check.Severity == BuildOutputCheckSeverity.Warning)
            {
                check.Summary = "⚠ " + check.Warnings[0] +
                                (check.Warnings.Count > 1 ? $" (+{check.Warnings.Count - 1} outro(s))" : "");
            }
            else
            {
                check.Summary = check.Created
                    ? "✔ Pasta criada e gravável."
                    : "✔ Pasta válida e gravável.";
            }
        }

        private static bool IsInsideProject(string folder)
        {
            try
            {
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string path = Path.GetFullPath(folder);
                return path.Equals(project, StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NearestExistingParent(string folder)
        {
            try
            {
                var parent = Directory.GetParent(folder);
                while (parent != null)
                {
                    if (parent.Exists)
                        return parent.FullName;
                    parent = parent.Parent;
                }
            }
            catch
            {
                // Ignore malformed paths: the caller reports the generic inspection error.
            }

            return null;
        }

        private static bool CanWrite(string folder, out string error)
        {
            string probe = Path.Combine(folder, ".buildpipeline-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
                return false;
            }
        }

        private static long TryFreeBytes(string folder)
        {
            try
            {
                string root = Path.GetPathRoot(folder);
                if (string.IsNullOrEmpty(root))
                    return -1;

                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(unit == 0 ? "0" : "0.#") + " " + units[unit];
        }
    }
}
