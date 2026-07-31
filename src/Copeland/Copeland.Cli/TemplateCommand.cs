using Copeland.TS.Compiler;
using Copeland.TS.Templates;

namespace Copeland.Cli;

internal static class TemplateCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || args[1] is not ("preview" or "materialize"))
        {
            return Usage("COPE-TEMPLATE-CLI-0001", "Use 'tscl template preview <source>' or 'tscl template materialize <source> --output <path>'.");
        }
        if (args.Length < 3)
        {
            return Usage("COPE-TEMPLATE-CLI-0001", "Template commands require a source file.");
        }

        string sourcePath = args[2];
        string? entry = null;
        string? output = null;
        string? name = null;
        string format = "tree";
        for (int index = 3; index < args.Length; index++)
        {
            if (index + 1 >= args.Length)
            {
                return Usage("COPE-TEMPLATE-CLI-0002", $"Option '{args[index]}' requires a value.");
            }
            string option = args[index];
            string value = args[++index];
            switch (option)
            {
                case "--entry": entry = value; break;
                case "--output": output = value; break;
                case "--name": name = value; break;
                case "--format": format = value; break;
                default: return Usage("COPE-TEMPLATE-CLI-0003", $"Unknown template option '{option}'.");
            }
        }

        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"COPE-TEMPLATE-CLI-0004 error: Template source '{sourcePath}' was not found.");
            return 3;
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!string.IsNullOrWhiteSpace(name) && !IsValidProjectName(name!))
        {
            Console.Error.WriteLine($"COPE-TEMPLATE-CLI-0010 error: Project name '{name}' must be a non-empty C# identifier and must not contain path separators.");
            return 2;
        }

        TemplateEvaluationResult evaluation = EvaluateProjectTemplate(fullSourcePath, entry, name);
        if (!evaluation.Success)
        {
            foreach (var diagnostic in evaluation.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
            }
            return 1;
        }

        if (args[1] == "preview")
        {
            if (format == "json")
            {
                Console.Out.WriteLine(evaluation.Project!.ToPreviewJson(evaluation.TemplateName));
                return 0;
            }
            if (format != "tree") return Usage("COPE-TEMPLATE-CLI-0005", "Preview format must be 'tree' or 'json'.");
            Console.Out.WriteLine(evaluation.TemplateName);
            foreach (FileArtifact file in evaluation.Project!.Files)
            {
                Console.Out.WriteLine($"  {file.Kind,-6} {file.Path} {file.Sha256}");
            }
            return 0;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return Usage("COPE-TEMPLATE-CLI-0006", "Materialize requires '--output <path>'.");
        }
        ProjectTreeMaterializationResult result = ProjectTreeMaterializer.Materialize(evaluation.Project!, output!);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"{result.DiagnosticId} error: {result.Message}");
            return 1;
        }
        foreach (string path in result.Files) Console.Out.WriteLine($"Created {path}");
        Console.Out.WriteLine("Next steps:");
        Console.Out.WriteLine($"  cd {output}");
        Console.Out.WriteLine("  npm install");
        Console.Out.WriteLine("  dotnet build");
        Console.Out.WriteLine("  dotnet test");
        Console.Out.WriteLine("  dotnet run");
        return 0;
    }

    private static TemplateEvaluationResult EvaluateProjectTemplate(string sourcePath, string? entry, string? name)
    {
        string root = Path.GetDirectoryName(sourcePath)!;
        CopelandProjectSource[] sources = Directory.EnumerateFiles(root, "*.ts", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.tsx", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopelandProjectSource(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                Path.GetFullPath(path),
                File.ReadAllText(path)))
            .ToArray();
        return CopelandProjectCompiler.CompileTemplates(
            sources,
            entry,
            name is null ? [] : [name],
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = root,
            });
    }

    private static bool IsValidProjectName(string name)
        => name.Length > 0
            && (char.IsLetter(name[0]) || name[0] == '_')
            && name.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static int Usage(string code, string message)
    {
        Console.Error.WriteLine($"{code} error: {message}");
        return 2;
    }
}
