using System.Diagnostics;
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
        string? tspackPath = null;
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
                case "--tspack": tspackPath = value; break;
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
        TemplateEvaluationResult evaluation = EvaluateProjectTemplate(fullSourcePath, entry);
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
        return MaterializeWithTspack(evaluation, output!, tspackPath);
    }

    private static TemplateEvaluationResult EvaluateProjectTemplate(string sourcePath, string? entry)
    {
        string root = Path.GetDirectoryName(sourcePath)!;
        CopelandProjectSource[] sources = Directory.EnumerateFiles(root, "*.ts", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopelandProjectSource(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                Path.GetFullPath(path),
                File.ReadAllText(path)))
            .ToArray();
        return CopelandProjectCompiler.CompileTemplates(
            sources,
            entry,
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = root,
            });
    }

    private static int MaterializeWithTspack(TemplateEvaluationResult evaluation, string output, string? configuredTspackPath)
    {
        string manifestPath = Path.Combine(Path.GetTempPath(), $"copeland-template-{Guid.NewGuid():N}.json");
        string executable = configuredTspackPath ?? Environment.GetEnvironmentVariable("COPELAND_TSPACK_PATH") ?? "tspack";
        try
        {
            if (!VerifyTspackMaterializationCapability(executable))
            {
                Console.Error.WriteLine("COPE-TEMPLATE-CLI-0008 error: The configured TSPack executable does not support the required 'materialize-tree' capability. Build or select a compatible TSPack artifact.");
                return 1;
            }
            File.WriteAllText(manifestPath, evaluation.Project!.ToPreviewJson(evaluation.TemplateName, includeContents: true));
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
            };
            start.ArgumentList.Add("materialize-tree");
            start.ArgumentList.Add("--manifest");
            start.ArgumentList.Add(manifestPath);
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(Path.GetFullPath(output));
            using Process? process = Process.Start(start);
            if (process is null)
            {
                Console.Error.WriteLine("COPE-TEMPLATE-CLI-0007 error: Could not start TSPack materialization.");
                return 1;
            }
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"COPE-TEMPLATE-CLI-0007 error: TSPack materialization failed: {exception.Message}");
            return 1;
        }
        finally
        {
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
        }
    }

    private static bool VerifyTspackMaterializationCapability(string executable)
    {
        try
        {
            var probe = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            probe.ArgumentList.Add("materialize-tree");
            using Process? process = Process.Start(probe);
            if (process is null)
            {
                return false;
            }
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            string output = standardOutput + standardError;
            return output.Contains("TSPACK_TEMPLATE_ARGUMENTS_REQUIRED", StringComparison.Ordinal);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static int Usage(string code, string message)
    {
        Console.Error.WriteLine($"{code} error: {message}");
        return 2;
    }
}
