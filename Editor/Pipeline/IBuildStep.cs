using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    public interface IPreBuildStep
    {
        int Order { get; }
        bool ExecutePreBuild(BuildContext context);
    }

    public interface IPostBuildStep
    {
        int Order { get; }
        bool ExecutePostBuild(BuildContext context, BuildReport report);
    }
}
