using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;

namespace Copeland.Cli;

internal static class CheckCommand
{
    private const string Usage = "Usage: tscl check --standalone <project-root>";

    internal static int Run(string[] args)
    {
        if (args is ["check", "--help"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (args is not ["check", "--standalone", string projectRoot]
            || string.IsNullOrWhiteSpace(projectRoot))
        {
            Console.Error.WriteLine($"COPE-CHECK-0001 error: {Usage}");
            return 2;
        }

        try
        {
            var target = CopelandBackendTarget.Create("javascript", "node");
            CopelandProjectContext context = CopelandProjectContext.LoadStandalone(projectRoot, target);
            IReadOnlyList<Diagnostic> diagnostics = context.CreateSnapshot().Check();
            foreach (Diagnostic diagnostic in diagnostics)
            {
                string location = diagnostic.SourcePath is null
                    ? string.Empty
                    : $"{diagnostic.SourcePath}({diagnostic.Position},{diagnostic.Length}): ";
                Console.Error.WriteLine($"{location}{diagnostic.Id} error: {diagnostic.Message}");
            }
            return diagnostics.Count == 0 ? 0 : 1;
        }
        catch (CopelandProjectContextException exception)
        {
            Console.Error.WriteLine($"{exception.Code} error: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"COPE-CHECK-0002 error: {exception.Message}");
            return 3;
        }
    }
}
