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
                    Args[arg.TrimStart('-')] = rawArgs[i + 1];
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
