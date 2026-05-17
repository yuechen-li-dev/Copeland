using Copeland.Script.Compiler;

namespace Copeland.Cli;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int CompileFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;
    private const int FileIoErrorExitCode = 3;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("COPE-CLI-0004", "Missing command. Supported command: 'compile'.");
        }

        if (!string.Equals(args[0], "compile", StringComparison.Ordinal))
        {
            return UsageError("COPE-CLI-0004", $"Unknown command '{args[0]}'. Supported command: 'compile'.");
        }

        if (args.Length < 2)
        {
            return UsageError("COPE-CLI-0003", "Missing source file. Usage: compile <source-file>.");
        }

        var sourcePath = args[1];
        string? emitTarget = null;
        string? outPath = null;

        for (var i = 2; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--emit")
            {
                if (i + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0006", "Option '--emit' requires a value.");
                }

                emitTarget = args[++i];
                continue;
            }

            if (arg == "--out")
            {
                if (i + 1 >= args.Length)
                {
                    return UsageError("COPE-CLI-0006", "Option '--out' requires a value.");
                }

                outPath = args[++i];
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                return UsageError("COPE-CLI-0005", $"Unknown option '{arg}'.");
            }

            return UsageError("COPE-CLI-0007", $"Unexpected argument '{arg}'.");
        }

        if (emitTarget is null)
        {
            return UsageError("COPE-CLI-0001", "Missing required option '--emit'. Use '--emit mir' or '--emit csharp'.");
        }

        if (!string.Equals(emitTarget, "mir", StringComparison.Ordinal) && !string.Equals(emitTarget, "csharp", StringComparison.Ordinal))
        {
            return UsageError("COPE-CLI-0002", $"Unknown emit target '{emitTarget}'. Use 'mir' or 'csharp'.");
        }

        string sourceText;
        try
        {
            sourceText = File.ReadAllText(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"COPE-CLI-0008 error: Failed to read source file '{sourcePath}': {ex.Message}");
            return FileIoErrorExitCode;
        }

        var compilation = string.Equals(emitTarget, "mir", StringComparison.Ordinal)
            ? CopelandCompiler.CompileToMir(sourceText)
            : CopelandCompiler.CompileToCSharp(sourceText);

        if (!compilation.Success)
        {
            foreach (var diagnostic in compilation.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
            }

            return CompileFailureExitCode;
        }

        var artifactText = string.Equals(emitTarget, "mir", StringComparison.Ordinal)
            ? compilation.MirText
            : compilation.CSharpText;

        if (artifactText is null)
        {
            Console.Error.WriteLine("COPE-CLI-0010 error: Compilation succeeded but no artifact was produced.");
            return CompileFailureExitCode;
        }

        if (outPath is null)
        {
            Console.Out.Write(artifactText);
            return SuccessExitCode;
        }

        try
        {
            File.WriteAllText(outPath, artifactText);
            Console.Out.WriteLine($"wrote {outPath}");
            return SuccessExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"COPE-CLI-0009 error: Failed to write output file '{outPath}': {ex.Message}");
            return FileIoErrorExitCode;
        }
    }

    private static int UsageError(string id, string message)
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  copeland compile <source-file> --emit mir|csharp [--out <path>]");
        Console.Error.WriteLine($"{id} error: {message}");
        return UsageErrorExitCode;
    }
}
