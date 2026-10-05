using System.Text;
using Copeland.TS.Compiler;
using Copeland.TS.Templates;

namespace Copeland.Cli;

internal static class FlowVisualizationCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3 || args[1] != "visualize")
        {
            return Usage("COPE-FLOW-VIZ-CLI-0001", "Use 'tscl flow visualize <source> --name <flow> [--output <diagram.mmd>]'.");
        }

        string sourcePath = args[2];
        string? flowName = null;
        string? outputPath = null;
        for (int index = 3; index < args.Length; index++)
        {
            if (index + 1 >= args.Length)
            {
                return Usage("COPE-FLOW-VIZ-CLI-0002", $"Option '{args[index]}' requires a value.");
            }
            string option = args[index];
            string value = args[++index];
            switch (option)
            {
                case "--name":
                    flowName = value;
                    break;
                case "--output":
                    outputPath = value;
                    break;
                default:
                    return Usage("COPE-FLOW-VIZ-CLI-0003", $"Unknown flow visualization option '{option}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(flowName))
        {
            return Usage("COPE-FLOW-VIZ-CLI-0004", "Flow visualization requires '--name <flow>'.");
        }
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"COPE-FLOW-VIZ-CLI-0005 error: Flow source '{sourcePath}' was not found.");
            return 3;
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(
            File.ReadAllText(fullSourcePath),
            new CopelandCompilationOptions
            {
                SourcePath = fullSourcePath,
                ProjectRoot = Path.GetDirectoryName(fullSourcePath),
            });
        if (!compilation.Success || compilation.BoundCompilation is null)
        {
            foreach (var diagnostic in compilation.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
            }
            return 1;
        }

        if (!StateMachineDiagramProjection.TryProject(
            compilation.BoundCompilation.Program,
            flowName!,
            out _,
            out Diagram? diagram,
            out var diagnostics))
        {
            foreach (var diagnostic in diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
            }
            return 1;
        }

        string mermaid = MermaidEmitter.Emit(diagram!);
        if (outputPath is null)
        {
            Console.Out.Write(mermaid);
            return 0;
        }

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullOutputPath, mermaid, new UTF8Encoding(false));
        Console.Out.WriteLine($"Created {fullOutputPath}");
        return 0;
    }

    private static int Usage(string code, string message)
    {
        Console.Error.WriteLine($"{code} error: {message}");
        return 2;
    }
}
