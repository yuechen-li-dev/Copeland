using Copeland.Cli;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Copeland.TS.MSBuild;

/// <summary>
/// Resolves tscl-owned source items directly from tsconfig.tsx during normal
/// MSBuild evaluation. No generated props file is required for compilation.
/// </summary>
public sealed class CopelandResolveWorkspace : Microsoft.Build.Utilities.Task
{
    [Required]
    public string WorkspacePath { get; set; } = string.Empty;

    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] Sources { get; private set; } = [];

    public override bool Execute()
    {
        CopelandWorkspaceOwnershipResult result = CopelandWorkspaceOwnership.Resolve(WorkspacePath);
        if (!result.Success)
        {
            foreach (CopelandWorkspaceOwnershipDiagnostic diagnostic in result.Diagnostics)
            {
                Log.LogError(
                    diagnostic.Code,
                    "",
                    "",
                    diagnostic.File,
                    0,
                    0,
                    0,
                    0,
                    diagnostic.Message);
            }

            return false;
        }

        string fullProjectPath = Path.GetFullPath(ProjectPath);
        CopelandWorkspaceOwnedSource[] projectSources = result.Sources
            .Where(source => string.Equals(
                Path.GetFullPath(source.Project, Path.GetDirectoryName(Path.GetFullPath(WorkspacePath))!),
                fullProjectPath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Sources = projectSources
            .Select(source =>
            {
                var item = new TaskItem(source.Path);
                item.SetMetadata("MatchedRule", source.MatchedRule);
                item.SetMetadata("OwnershipSource", Path.GetFullPath(WorkspacePath));
                return (ITaskItem)item;
            })
            .ToArray();
        return true;
    }
}
