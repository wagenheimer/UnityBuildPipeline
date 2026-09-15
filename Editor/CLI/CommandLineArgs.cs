using System;
using System.Collections.Generic;

namespace Wagenheimer.BuildPipeline.Editor
{
    public static class CommandLineArgs
    {
        private static readonly Dictionary<string, string> Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static CommandLineArgs()
        {
            var rawArgs = Environment.GetCommandLineArgs();
            for (var i = 0; i < rawArgs.Length; i++)
            {
                var arg = rawArgs[i];
                if (arg.StartsWith("-") && i + 1 < rawArgs.Length && !rawArgs[i + 1].StartsWith("-"))
                {
                    // Some CI wrappers (e.g. the `unity build` CLI's `--args "..."` passthrough) can
                    // forward values with their shell-escaped quotes still attached (e.g. `\"15\"`
                    // surviving as a literal `"15"` argv token) instead of the bare `15` a normal
                    // shell would produce. Stripping stray wrapping quotes here means downstream
                    // int.TryParse/string comparisons don't silently fail on a well-formed CLI value.
                    var value = rawArgs[i + 1];
                    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                        value = value[1..^1];
                    Args[arg.TrimStart('-')] = value;
                    i++;
                }
                else if (arg.StartsWith("-"))
                {
                    Args[arg.TrimStart('-')] = "true";
                }
            }
        }

        public static bool Has(string key) => Args.ContainsKey(key);

        public static string Get(string key, string defaultValue = "")
        {
            return Args.TryGetValue(key, out var val) ? val : defaultValue;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            if (Args.TryGetValue(key, out var val))
            {
                if (bool.TryParse(val, out var b)) return b;
                return val.Equals("1", StringComparison.OrdinalIgnoreCase) || val.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
            return defaultValue;
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            if (Args.TryGetValue(key, out var val) && int.TryParse(val, out var i))
                return i;
            return defaultValue;
        }
    }
}
