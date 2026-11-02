using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>A code location whose compilation differs between two define sets.</summary>
    public sealed class DefineImpactItem
    {
        public DefineUsage Usage;
        /// <summary>True when the block/assembly/call is compiled with set A.</summary>
        public bool InA;
        /// <summary>True when the block/assembly/call is compiled with set B.</summary>
        public bool InB;
    }

    /// <summary>
    /// Pure comparison of two final define sets against the usage index: which <c>#if</c> blocks,
    /// <c>[Conditional]</c> calls and asmdef constraints change state. Answers "what changes in the code
    /// between Free and Full" or "what happens if I remove this define".
    /// </summary>
    public static class DefineImpact
    {
        public static List<DefineImpactItem> Compare(ISet<string> setA, ISet<string> setB, IEnumerable<DefineUsage> usages)
        {
            var result = new List<DefineImpactItem>();
            if (setA == null || setB == null || usages == null)
                return result;

            var changed = new HashSet<string>(setA.Except(setB).Concat(setB.Except(setA)), StringComparer.Ordinal);
            if (changed.Count == 0)
                return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var usage in usages)
            {
                if (usage == null || !changed.Contains(usage.symbol))
                    continue;

                // One row per location, even when the expression mentions several changed symbols.
                string key = usage.file + ":" + usage.line + ":" + usage.kind;
                if (!seen.Add(key))
                    continue;

                bool inA, inB;
                switch (usage.kind)
                {
                    case DefineUsageKind.Condition:
                    case DefineUsageKind.AssemblyConstraint:
                        inA = DefineExpression.Evaluate(usage.expression, setA);
                        inB = DefineExpression.Evaluate(usage.expression, setB);
                        break;
                    case DefineUsageKind.ConditionalAttribute:
                        inA = setA.Contains(usage.symbol);
                        inB = setB.Contains(usage.symbol);
                        break;
                    default:
                        continue;
                }

                if (inA != inB)
                    result.Add(new DefineImpactItem { Usage = usage, InA = inA, InB = inB });
            }

            return result
                .OrderBy(item => item.Usage.kind == DefineUsageKind.AssemblyConstraint ? 0 : 1)
                .ThenBy(item => item.Usage.package, StringComparer.Ordinal)
                .ThenBy(item => item.Usage.file, StringComparer.Ordinal)
                .ThenBy(item => item.Usage.line)
                .ToList();
        }

        /// <summary>Impact of removing (or adding) one symbol from a final set.</summary>
        public static List<DefineImpactItem> Toggle(ISet<string> finalSet, string symbol, IEnumerable<DefineUsage> usages)
        {
            var other = new HashSet<string>(finalSet ?? new HashSet<string>(), StringComparer.Ordinal);
            if (!other.Remove(symbol))
                other.Add(symbol);

            return Compare(finalSet, other, usages);
        }
    }
}
