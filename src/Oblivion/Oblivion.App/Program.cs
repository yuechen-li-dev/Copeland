using Oblivion.Persistence;

namespace Oblivion.App;

public static class Program
{
    public static int Main(string[] args)
    {
        string manifestPath = args.Length > 0
            ? Path.GetFullPath(args[0])
            : OblivionWorkspacePaths.ResolveWorkspaceManifestPath();
        OblivionWorkspaceLoadResult result = OblivionWorkspaceApplication.Load(manifestPath, useCache: false);

        foreach (OblivionWorkspaceDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        if (!result.Succeeded || result.Workspace is null)
        {
            return 1;
        }

        Console.WriteLine($"workspace={result.Workspace.Id.Value}");
        Console.WriteLine($"pages={result.Workspace.Pages.Count}");
        Console.WriteLine($"cards={result.Workspace.Pages.Sum(page => page.Cards.Count)}");
        return 0;
    }
}
