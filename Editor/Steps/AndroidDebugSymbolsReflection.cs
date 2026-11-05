using System;
using System.Linq;
using System.Reflection;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Sets <c>UnityEditor.Android.UserBuildSettings.DebugSymbols.level/format</c> (Unity 6) without a compile-time
    /// reference to the Android build support module. With a direct reference the whole package failed to compile on
    /// editors without Android support installed, even for Windows/macOS-only builds.
    /// </summary>
    internal static class AndroidDebugSymbolsReflection
    {
        public static bool TryApply(bool create, bool embedInBundle, out string problem)
        {
            problem = null;
            try
            {
                var settingsType = FindType("UnityEditor.Android.UserBuildSettings");
                if (settingsType == null)
                {
                    problem = "Android build support is not installed in this Editor.";
                    return false;
                }

                // DebugSymbols is a static nested class in Unity 6 (UserBuildSettings.DebugSymbols.level); fall back to a
                // static property holding an instance in case the API shape changes.
                const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.Static;
                const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.Instance;
                var nested = settingsType.GetNestedType("DebugSymbols", BindingFlags.Public);
                object target = null;
                PropertyInfo level, format;

                if (nested != null)
                {
                    level = nested.GetProperty("level", staticFlags);
                    format = nested.GetProperty("format", staticFlags);
                }
                else
                {
                    target = settingsType.GetProperty("DebugSymbols", staticFlags)?.GetValue(null);
                    level = target?.GetType().GetProperty("level", instanceFlags);
                    format = target?.GetType().GetProperty("format", instanceFlags);
                }

                if (level == null || format == null)
                {
                    problem = "UserBuildSettings.DebugSymbols API not found in this Unity version.";
                    return false;
                }

                level.SetValue(target, Enum.Parse(level.PropertyType, create ? "SymbolTable" : "None"));

                if (create)
                {
                    var names = embedInBundle ? "Zip, LegacyExtensions, IncludeInBundle" : "Zip, LegacyExtensions";
                    format.SetValue(target, Enum.Parse(format.PropertyType, names));
                }

                return true;
            }
            catch (Exception exception)
            {
                problem = exception.GetBaseException().Message;
                return false;
            }
        }

        private static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly =>
                {
                    try { return assembly.GetType(fullName, false); }
                    catch { return null; }
                })
                .FirstOrDefault(type => type != null);
    }
}
