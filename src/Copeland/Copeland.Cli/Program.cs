using Copeland.Markdown;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.LanguageServer;

namespace Copeland.Cli;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int CompileFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;
    private const int FileIoErrorExitCode = 3;

    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
        {
            Console.Out.WriteLine(TsclBuildContract.Version);
            return SuccessExitCode;
        }

        if (args.Length == 0)
        {
            return UsageError("COPE-CLI-0004", "Missing command. Supported commands: 'compile', 'build', 'workspace', 'database', 'markdown', 'template', 'language-server'.");
        }

        return args[0] switch
        {
            "compile" => RunCompile(args),
            "build" => TsclBuildContract.Run(args),
            "workspace" => WorkspaceCommand.Run(args),
            "database" => DatabaseCommand.Run(args),
            "markdown" => RunMarkdown(args),
            "table" => TableToolCommand.Run(args),
            "layout" => LayoutInspectionCommand.Run(args),
            "template" => TemplateCommand.Run(args),
            "language-server" or "lsp" => RunLanguageServer(args),
            "doctor" => DistributionCommand.RunDoctor(args[1..]),
            "install-info" => DistributionCommand.RunInstallInfo(args[1..]),
            _ => UsageError("COPE-CLI-0004", $"Unknown command '{args[0]}'. Supported commands: 'compile', 'build', 'workspace', 'database', 'markdown', 'table', 'template', 'language-server'."),
        };
    }

    private static int RunLanguageServer(string[] args)
    {
        return Copeland.TS.LanguageServer.Program.Main(args.Skip(1).ToArray());
    }

    private static int RunCompile(string[] args)
    {
        if (args.Length < 2)
        {
            return UsageError("COPE-CLI-0003", "Missing source file. Usage: compile <source-file>.");
        }

        string sourcePath = args[1];
        string? emitTarget = null;
        string? outPath = null;
        string? javaScriptProfile = null;

        for (int index = 2; index < args.Length; index += 1)
        {
            string argument = args[index];
            if (argument == "--emit")
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0006", "Option '--emit' requires a value.");
                }

                emitTarget = args[index + 1];
                index += 1;
                continue;
            }

            if (argument == "--out")
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0006", "Option '--out' requires a value.");
                }

                outPath = args[index + 1];
                index += 1;
                continue;
            }

            if (argument == "--javascript-profile")
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0006", "Option '--javascript-profile' requires a value.");
                }

                javaScriptProfile = args[index + 1];
                index += 1;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                return UsageError("COPE-CLI-0005", $"Unknown option '{argument}'.");
            }

            return UsageError("COPE-CLI-0007", $"Unexpected argument '{argument}'.");
        }

        if (emitTarget is null)
        {
            return UsageError("COPE-CLI-0001", "Missing required option '--emit'. Use '--emit mir', '--emit csharp', or '--emit javascript'.");
        }

        if (!string.Equals(emitTarget, "mir", StringComparison.Ordinal) &&
            !string.Equals(emitTarget, "csharp", StringComparison.Ordinal) &&
            !string.Equals(emitTarget, "javascript", StringComparison.Ordinal))
        {
            return UsageError("COPE-CLI-0002", $"Unknown emit target '{emitTarget}'. Use 'mir', 'csharp', or 'javascript'.");
        }

        if (javaScriptProfile is not null && !string.Equals(emitTarget, "javascript", StringComparison.Ordinal))
        {
            return UsageError("COPE-CLI-0022", "Option '--javascript-profile' is valid only with '--emit javascript'.");
        }

        JavaScriptEmissionProfile profile = JavaScriptEmissionProfile.Diagnostic;
        if (javaScriptProfile is not null)
        {
            profile = javaScriptProfile switch
            {
                "diagnostic" => JavaScriptEmissionProfile.Diagnostic,
                "symbolic" => JavaScriptEmissionProfile.Symbolic,
                "production" => JavaScriptEmissionProfile.Production,
                "release" => JavaScriptEmissionProfile.Production,
                _ => (JavaScriptEmissionProfile)(-1),
            };
            if (!Enum.IsDefined(profile))
            {
                return UsageError("COPE-CLI-0023", $"Unsupported JavaScript profile '{javaScriptProfile}'. Use 'diagnostic', 'symbolic', or 'production'.");
            }
        }

        if (!TryReadAllText(sourcePath, out string? sourceText, out int readFailureExitCode))
        {
            return readFailureExitCode;
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            sourceText!,
            new CopelandCompilationOptions
            {
                SourcePath = fullSourcePath,
                ProjectRoot = Path.GetDirectoryName(fullSourcePath),
                AssetSource = FileSystemAssetSource.Instance,
            });

        if (!compilation.Success)
        {
            foreach (var diagnostic in compilation.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
            }

            return CompileFailureExitCode;
        }

        string? artifactText = compilation.MirText;

        if (string.Equals(emitTarget, "csharp", StringComparison.Ordinal))
        {
            var csharpCompilation = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
            if (csharpCompilation.Diagnostics.Count > 0)
            {
                foreach (var diagnostic in csharpCompilation.Diagnostics)
                {
                    Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
                }

                return CompileFailureExitCode;
            }

            artifactText = csharpCompilation.SourceText;
        }

        if (string.Equals(emitTarget, "javascript", StringComparison.Ordinal))
        {
            var javaScriptCompilation = JavaScriptBackend.Emit(
                compilation.MirCompilation!.Program!,
                new JavaScriptEmissionOptions { Profile = profile });
            if (!javaScriptCompilation.Success)
            {
                foreach (var diagnostic in javaScriptCompilation.Diagnostics)
                {
                    Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
                }

                return CompileFailureExitCode;
            }

            artifactText = javaScriptCompilation.SourceText;
        }

        if (artifactText is null)
        {
            Console.Error.WriteLine("COPE-CLI-0010 error: Compilation succeeded but no artifact was produced.");
            return CompileFailureExitCode;
        }

        return WriteArtifact(artifactText, outPath);
    }

    private static int RunMarkdown(string[] args)
    {
        if (args.Length < 2)
        {
            return UsageError(
                "COPE-CLI-0011",
                "Missing markdown subcommand. Use 'markdown parse <file>' or 'markdown export-corpus --output-dir <path>'.");
        }

        return args[1] switch
        {
            "parse" => RunMarkdownParse(args),
            "export-corpus" => RunMarkdownExportCorpus(args),
            _ => UsageError(
                "COPE-CLI-0011",
                $"Unknown markdown subcommand '{args[1]}'. Use 'parse' or 'export-corpus'."),
        };
    }

    private static int RunMarkdownParse(string[] args)
    {
        if (args.Length < 3)
        {
            return UsageError(
                "COPE-CLI-0012",
                "Missing Markdown source file. Usage: markdown parse <source-file> --emit ast|mir|tokens|diagnostics [--format text|json] [--out <path>].");
        }

        string sourcePath = args[2];
        string? emitTarget = null;
        string format = "text";
        string? outPath = null;

        for (int index = 3; index < args.Length; index += 1)
        {
            string argument = args[index];
            if (argument == "--emit")
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0013", "Option '--emit' requires a value.");
                }

                emitTarget = args[index + 1];
                index += 1;
                continue;
            }

            if (argument == "--format")
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0013", "Option '--format' requires a value.");
                }

                format = args[index + 1];
                index += 1;
                continue;
            }

            if (argument == "--out")
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0013", "Option '--out' requires a value.");
                }

                outPath = args[index + 1];
                index += 1;
                continue;
            }

            return UsageError("COPE-CLI-0014", $"Unexpected markdown parse argument '{argument}'.");
        }

        if (emitTarget is null)
        {
            return UsageError("COPE-CLI-0015", "Markdown parse requires '--emit ast|mir|tokens|diagnostics'.");
        }

        if (!TryReadAllText(sourcePath, out string? sourceText, out int readFailureExitCode))
        {
            return readFailureExitCode;
        }

        MarkdownCompilation compilation = MarkdownCompiler.Compile(sourceText!);
        string artifactText;

        switch (emitTarget)
        {
            case "ast":
                artifactText = string.Equals(format, "json", StringComparison.Ordinal)
                    ? MarkdownDumpWriter.SerializeSyntaxAsJson(compilation.Syntax)
                    : MarkdownDumpWriter.DumpSyntax(compilation.Syntax);
                break;
            case "mir":
                artifactText = string.Equals(format, "json", StringComparison.Ordinal)
                    ? MarkdownDumpWriter.SerializeMirAsJson(compilation.Mir)
                    : MarkdownDumpWriter.DumpMir(compilation.Mir);
                break;
            case "tokens":
                if (string.Equals(format, "json", StringComparison.Ordinal))
                {
                    return UsageError("COPE-CLI-0016", "Markdown token JSON output is not implemented. Use '--format text'.");
                }

                artifactText = MarkdownDumpWriter.DumpTokens(compilation.TokenizedSource);
                break;
            case "diagnostics":
                if (string.Equals(format, "json", StringComparison.Ordinal))
                {
                    artifactText = MarkdownDumpWriter.SerializeSyntaxAsJson(
                        new MarkdownDocument([], compilation.Syntax.Diagnostics, compilation.Syntax.Span));
                }
                else
                {
                    artifactText = MarkdownDumpWriter.DumpDiagnostics(compilation.Syntax.Diagnostics);
                }

                break;
            default:
                return UsageError("COPE-CLI-0017", $"Unknown markdown emit target '{emitTarget}'.");
        }

        return WriteArtifact(artifactText, outPath);
    }

    private static int RunMarkdownExportCorpus(string[] args)
    {
        string? outputDirectory = null;

        for (int index = 2; index < args.Length; index += 1)
        {
            string argument = args[index];
            if (argument == "--output-dir")
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0018", "Option '--output-dir' requires a value.");
                }

                outputDirectory = args[index + 1];
                index += 1;
                continue;
            }

            return UsageError("COPE-CLI-0019", $"Unexpected markdown export-corpus argument '{argument}'.");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return UsageError("COPE-CLI-0020", "Markdown export-corpus requires '--output-dir <path>'.");
        }

        try
        {
            string repoRoot = GetRepoRoot();
            MarkdownCorpusExporter.ExportSelectedDocs(repoRoot, outputDirectory);
            Console.Out.WriteLine($"wrote {Path.GetFullPath(outputDirectory)}");
            return SuccessExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"COPE-CLI-0021 error: Failed to export Markdown corpus: {exception.Message}");
            return FileIoErrorExitCode;
        }
    }

    private static int WriteArtifact(string artifactText, string? outPath)
    {
        if (outPath is null)
        {
            Console.Out.Write(artifactText);
            return SuccessExitCode;
        }

        try
        {
            string fullOutputPath = Path.GetFullPath(outPath);
            string? directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullOutputPath, artifactText);
            Console.Out.WriteLine($"wrote {fullOutputPath}");
            return SuccessExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"COPE-CLI-0009 error: Failed to write output file '{outPath}': {ex.Message}");
            return FileIoErrorExitCode;
        }
    }

    private static bool TryReadAllText(string path, out string? text, out int exitCode)
    {
        try
        {
            text = File.ReadAllText(path);
            exitCode = SuccessExitCode;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"COPE-CLI-0008 error: Failed to read source file '{path}': {ex.Message}");
            text = null;
            exitCode = FileIoErrorExitCode;
            return false;
        }
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static int UsageError(string id, string message)
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  copeland compile <source-file> --emit mir|csharp|javascript [--javascript-profile diagnostic|symbolic|production] [--out <path>]");
        Console.Error.WriteLine("  tscl build (--project <descriptor.json> | --standalone <project-root> [--target <name>]) --result <result.json>");
        Console.Error.WriteLine("  tscl workspace sync|validate|status|owner ...");
        Console.Error.WriteLine("  tscl table list|schema|rows|query|set|add-row|delete-row|validate|export|import ...");
        Console.Error.WriteLine("  tscl table list (--project <manifest.tsx> | --source <entry.ts>)  (compiler-projected layout tables)");
        Console.Error.WriteLine("  tscl layout inspect <layout|module::layout> (--project <manifest.tsx> | --source <entry.ts>) [--json]");
        Console.Error.WriteLine("  tscl template preview <source> [--entry <template>] [--format tree|json]");
        Console.Error.WriteLine("  tscl template materialize <source> --output <path> [--entry <template>] [--name <project-name>] [--target <framework>]");
        Console.Error.WriteLine("  tscl language-server [--version]");
        Console.Error.WriteLine("  tscl doctor [--format text|json]");
        Console.Error.WriteLine("  tscl install-info [--format text|json]");
        Console.Error.WriteLine("  copeland markdown parse <source-file> --emit ast|mir|tokens|diagnostics [--format text|json] [--out <path>]");
        Console.Error.WriteLine("  copeland markdown export-corpus --output-dir <path>");
        Console.Error.WriteLine($"{id} error: {message}");
        return UsageErrorExitCode;
    }

    private sealed class FileSystemAssetSource : ICopelandAssetSource
    {
        public static FileSystemAssetSource Instance { get; } = new();

        public bool TryRead(string normalizedPath, out string? sourceText)
        {
            try
            {
                sourceText = File.ReadAllText(normalizedPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sourceText = null;
                return false;
            }
        }
    }
}
