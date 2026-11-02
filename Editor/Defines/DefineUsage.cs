using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum DefineUsageKind
    {
        /// <summary><c>#if</c> / <c>#elif</c> condition.</summary>
        Condition,
        /// <summary><c>#define X</c> local to one file.</summary>
        LocalDefine,
        /// <summary><c>#undef X</c> local to one file.</summary>
        LocalUndef,
        /// <summary><c>[Conditional("X")]</c>: calls are stripped when X is missing.</summary>
        ConditionalAttribute,
        /// <summary>asmdef <c>defineConstraints</c>: the WHOLE assembly is excluded when the constraint fails.</summary>
        AssemblyConstraint,
        /// <summary>asmdef <c>versionDefines</c>: defined automatically, only inside that assembly.</summary>
        AssemblyVersionDefine,
        /// <summary><c>-define:</c> in a csc.rsp next to Assets/ or an asmdef.</summary>
        CompilerResponseFile,
        /// <summary>Code that writes global defines (<c>PlayerSettings.SetScriptingDefineSymbols</c>).</summary>
        GlobalWriter
    }

    /// <summary>One place where the code references (or writes) a define symbol.</summary>
    [Serializable]
    public sealed class DefineUsage
    {
        public string symbol = "";
        public DefineUsageKind kind;
        /// <summary>Path as Unity shows it (Assets/..., Packages/...) when possible, else absolute.</summary>
        public string file = "";
        public int line;
        /// <summary>Full condition for <see cref="DefineUsageKind.Condition"/>/<see cref="DefineUsageKind.AssemblyConstraint"/>.</summary>
        public string expression = "";
        public string snippet = "";
        /// <summary>asmdef name owning the file (Assembly-CSharp when none).</summary>
        public string assembly = "";
        /// <summary>UPM package name owning the file, or "Assets".</summary>
        public string package = "";
        /// <summary>versionDefines: package whose presence defines the symbol.</summary>
        public string versionDefinePackage = "";
    }

    /// <summary>Pure text parsing of the files that can reference defines. No Unity API; testable.</summary>
    public static class DefineUsageParser
    {
        private static readonly Regex ConditionalRegex =
            new Regex(@"\[\s*(?:System\.Diagnostics\.)?Conditional(?:Attribute)?\s*\(\s*""(?<symbol>[A-Za-z_][A-Za-z0-9_]*)""\s*\)",
                RegexOptions.Compiled);

        private static readonly Regex WriterRegex =
            new Regex(@"SetScriptingDefineSymbols(?:ForGroup)?\s*\(", RegexOptions.Compiled);

        private static readonly Regex RspDefineRegex =
            new Regex(@"^\s*[-/](?:define|d)\s*:\s*(?<list>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Parses a C# source file.</summary>
        public static List<DefineUsage> ParseCSharp(string file, string text)
        {
            var result = new List<DefineUsage>();
            if (string.IsNullOrEmpty(text))
                return result;

            bool mayHaveConditional = text.IndexOf("Conditional", StringComparison.Ordinal) >= 0;
            bool mayWriteGlobals = text.IndexOf("SetScriptingDefineSymbols", StringComparison.Ordinal) >= 0;

            var lines = text.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string raw = lines[index].TrimEnd('\r');
                string trimmed = raw.TrimStart();
                int lineNumber = index + 1;

                if (trimmed.Length > 1 && trimmed[0] == '#')
                {
                    string directive = trimmed.Substring(1).TrimStart();
                    if (StartsWithWord(directive, "if") || StartsWithWord(directive, "elif"))
                    {
                        string expression = directive.Substring(directive.StartsWith("elif", StringComparison.Ordinal) ? 4 : 2).Trim();
                        expression = StripComment(expression);
                        foreach (string symbol in DefineExpression.Symbols(expression))
                            result.Add(Make(symbol, DefineUsageKind.Condition, file, lineNumber, expression, trimmed));
                    }
                    else if (StartsWithWord(directive, "define") || StartsWithWord(directive, "undef"))
                    {
                        bool define = directive.StartsWith("define", StringComparison.Ordinal);
                        string symbol = StripComment(directive.Substring(define ? 6 : 5)).Trim();
                        if (IsIdentifier(symbol))
                            result.Add(Make(symbol, define ? DefineUsageKind.LocalDefine : DefineUsageKind.LocalUndef,
                                file, lineNumber, "", trimmed));
                    }

                    continue;
                }

                if (mayHaveConditional)
                {
                    foreach (Match match in ConditionalRegex.Matches(raw))
                        result.Add(Make(match.Groups["symbol"].Value, DefineUsageKind.ConditionalAttribute,
                            file, lineNumber, "", trimmed));
                }

                if (mayWriteGlobals && WriterRegex.IsMatch(raw) && !trimmed.StartsWith("//", StringComparison.Ordinal))
                    result.Add(Make("", DefineUsageKind.GlobalWriter, file, lineNumber, "", trimmed));
            }

            return result;
        }

        /// <summary>Parses an .asmdef (defineConstraints + versionDefines). Returns the assembly name too.</summary>
        public static List<DefineUsage> ParseAsmdef(string file, string json, out string assemblyName)
        {
            var result = new List<DefineUsage>();
            assemblyName = "";
            if (string.IsNullOrEmpty(json))
                return result;

            AsmdefModel model;
            try
            {
                model = JsonUtility.FromJson<AsmdefModel>(json);
            }
            catch (Exception)
            {
                return result;
            }

            if (model == null)
                return result;

            assemblyName = model.name ?? "";

            foreach (string constraint in model.defineConstraints ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(constraint))
                    continue;

                foreach (string symbol in DefineExpression.Symbols(constraint))
                {
                    var usage = Make(symbol, DefineUsageKind.AssemblyConstraint, file, FindLine(json, constraint),
                        constraint.Trim(), $"defineConstraints: \"{constraint.Trim()}\"");
                    usage.assembly = assemblyName;
                    result.Add(usage);
                }
            }

            foreach (var versionDefine in model.versionDefines ?? new List<AsmdefVersionDefine>())
            {
                if (versionDefine == null || !IsIdentifier(versionDefine.define))
                    continue;

                var usage = Make(versionDefine.define, DefineUsageKind.AssemblyVersionDefine, file,
                    FindLine(json, versionDefine.define), versionDefine.expression ?? "",
                    $"versionDefines: {versionDefine.name} {versionDefine.expression} → {versionDefine.define}");
                usage.assembly = assemblyName;
                usage.versionDefinePackage = versionDefine.name ?? "";
                result.Add(usage);
            }

            return result;
        }

        /// <summary>Parses a csc.rsp (-define:A;B / /d:A).</summary>
        public static List<DefineUsage> ParseResponseFile(string file, string text)
        {
            var result = new List<DefineUsage>();
            if (string.IsNullOrEmpty(text))
                return result;

            var lines = text.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                var match = RspDefineRegex.Match(lines[index].TrimEnd('\r'));
                if (!match.Success)
                    continue;

                foreach (string symbol in match.Groups["list"].Value.Split(';', ','))
                {
                    string trimmed = symbol.Trim();
                    if (IsIdentifier(trimmed))
                        result.Add(Make(trimmed, DefineUsageKind.CompilerResponseFile, file, index + 1, "", lines[index].Trim()));
                }
            }

            return result;
        }

        public static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || !(char.IsLetter(value[0]) || value[0] == '_'))
                return false;

            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }

            return true;
        }

        private static bool StartsWithWord(string text, string word) =>
            text.StartsWith(word, StringComparison.Ordinal) &&
            (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]));

        private static string StripComment(string text)
        {
            int comment = text.IndexOf("//", StringComparison.Ordinal);
            return comment >= 0 ? text.Substring(0, comment).Trim() : text.Trim();
        }

        private static int FindLine(string text, string needle)
        {
            int position = text.IndexOf(needle, StringComparison.Ordinal);
            if (position < 0)
                return 1;

            int line = 1;
            for (int i = 0; i < position; i++)
            {
                if (text[i] == '\n')
                    line++;
            }

            return line;
        }

        private static DefineUsage Make(string symbol, DefineUsageKind kind, string file, int line, string expression, string snippet)
        {
            return new DefineUsage
            {
                symbol = symbol,
                kind = kind,
                file = file,
                line = line,
                expression = expression,
                snippet = snippet.Length > 240 ? snippet.Substring(0, 240) + "…" : snippet
            };
        }

        [Serializable]
        private sealed class AsmdefModel
        {
            public string name = "";
            public List<string> defineConstraints = new List<string>();
            public List<AsmdefVersionDefine> versionDefines = new List<AsmdefVersionDefine>();
        }

        [Serializable]
        private sealed class AsmdefVersionDefine
        {
            public string name = "";
            public string expression = "";
            public string define = "";
        }
    }
}
