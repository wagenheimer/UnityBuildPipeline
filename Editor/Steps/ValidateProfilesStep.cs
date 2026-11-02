using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Pre-build gate for the profile matrix: empty/duplicate ids, missing bundle ids, Free/Full
    /// sharing a bundle, invalid define syntax and a Full profile applying the ad define.
    /// Warnings never block; errors abort unless the build passes <c>-allowProfileErrors</c>.
    /// </summary>
    public class ValidateProfilesStep : IPreBuildStep
    {
        public int Order => 4;

        public bool ExecutePreBuild(BuildContext context)
        {
            var issues = BuildProfileValidator.Validate(context.Config, BuildProfileCatalog.ReadTrackingPolicy());

            foreach (var issue in issues)
            {
                string line = $"[Profiles] {issue}";
                if (issue.Severity == BuildProfileIssueSeverity.Error)
                    context.LogError(line);
                else
                    context.LogWarning(line);
            }

            int errors = issues.Count(issue => issue.Severity == BuildProfileIssueSeverity.Error);
            if (errors == 0)
                return true;

            if (CommandLineArgs.GetBool("allowProfileErrors", false))
            {
                context.LogWarning($"[Profiles] {errors} erro(s) ignorado(s) por -allowProfileErrors.");
                return true;
            }

            context.LogError($"[Profiles] {errors} erro(s) no ProjectBuildConfig; build abortado. " +
                             "Corrija os perfis ou use -allowProfileErrors para uma build emergencial.");
            return false;
        }
    }
}
