using System.Diagnostics;
using System.Text.Json;
using Copeland.TS.Workspace;

namespace Copeland.Cli;

internal static class FileProfileCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("COPE-PROFILE-0001 error: Usage: tscl profile <source-file> [--stdin] [--owner tsc|tscl] [--format json].");
            return 2;
        }
        try
        {
            string? owner = null;
            bool stdin = false;
            for (int index = 2; index < args.Length; index += 1)
            {
                if (args[index] == "--stdin")
                {
                    stdin = true;
                }
                else if (args[index] == "--owner" && index + 1 < args.Length && args[index + 1] is "tsc" or "tscl")
                {
                    owner = args[++index];
                }
                else if (args[index] == "--format" && index + 1 < args.Length && args[index + 1] == "json")
                {
                    index += 1;
                }
                else
                {
                    throw new ArgumentException($"Unknown or incomplete profile option '{args[index]}'.");
                }
            }
            string text = stdin ? Console.In.ReadToEnd() : File.ReadAllText(args[1]);
            SourceFileProfile profile = SourceFileProfiles.Read(text);
            var diagnostics = profile.Diagnostics.ToList();
            if (profile.CheckOwner(owner) is SourceProfileDiagnostic conflict)
            {
                diagnostics.Add(conflict);
            }
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                success = diagnostics.Count == 0,
                profile = profile.Name,
                owner = profile.Owner,
                diagnostics = diagnostics.Select(diagnostic => new
                {
                    code = diagnostic.Code,
                    message = diagnostic.Message,
                    position = diagnostic.Position,
                    length = diagnostic.Length,
                }),
            }));
            return diagnostics.Count == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"COPE-PROFILE-0001 error: {exception.Message}");
            return 2;
        }
    }

    public static int Compile(string[] args, Func<string[], int> compileCopeland)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            return compileCopeland(args);
        }
        try
        {
            SourceFileProfile profile = SourceFileProfiles.Read(File.ReadAllText(args[1]));
            if (profile.Diagnostics.Count > 0)
            {
                foreach (SourceProfileDiagnostic diagnostic in profile.Diagnostics)
                {
                    Console.Error.WriteLine($"{diagnostic.Code} error: {diagnostic.Message}");
                }
                return 1;
            }
            if (profile.Name is not null && !ValidateWorkspace(Path.GetFullPath(args[1])))
            {
                return 1;
            }
            if (profile.Owner != "tsc")
            {
                return compileCopeland(args);
            }
            return CompileLegacy(Path.GetFullPath(args[1]), args[2..]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            Console.Error.WriteLine($"COPE-PROFILE-0005 error: {exception.Message}");
            return 1;
        }
    }

    private static bool ValidateWorkspace(string sourcePath)
    {
        for (DirectoryInfo? directory = new(Path.GetDirectoryName(sourcePath)!); directory is not null; directory = directory.Parent)
        {
            string manifest = Path.Combine(directory.FullName, "tsconfig.tsx");
            if (!File.Exists(manifest))
            {
                continue;
            }
            CopelandWorkspaceOwnershipResult ownership = CopelandWorkspaceOwnership.Resolve(manifest);
            foreach (CopelandWorkspaceOwnershipDiagnostic diagnostic in ownership.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Code} error: {diagnostic.Message}");
            }
            return ownership.Success;
        }
        return true;
    }

    private static int CompileLegacy(string sourcePath, string[] arguments)
    {
        bool typeScriptArguments = arguments.FirstOrDefault() == "--";
        if (!typeScriptArguments && (arguments.Contains("--emit") || arguments.Contains("--out") || arguments.Contains("--javascript-profile")))
        {
            Console.Error.WriteLine("COPE-PROFILE-0004 error: Legacy delegates to tsc. Pass TypeScript arguments after '--', for example tscl compile Legacy.ts -- --noEmit.");
            return 1;
        }
        if (typeScriptArguments)
        {
            arguments = arguments[1..];
        }
        string? configPath = null;
        for (int index = 0; index < arguments.Length; index += 1)
        {
            if (arguments[index] is "--project" or "-p")
            {
                if (index + 1 == arguments.Length)
                {
                    throw new ArgumentException("TypeScript --project requires a path.");
                }
                configPath = Path.GetFullPath(arguments[index + 1]);
                break;
            }
        }
        string directory = Path.GetDirectoryName(sourcePath)!;
        if (configPath is null)
        {
            for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            {
                string workspace = Path.Combine(current.FullName, "tsconfig.tsx");
                string candidate = Path.Combine(current.FullName, "tsconfig.json");
                if (File.Exists(workspace))
                {
                    CopelandWorkspaceOwnershipResult ownership = CopelandWorkspaceOwnership.Resolve(workspace);
                    if (!ownership.Success)
                    {
                        foreach (CopelandWorkspaceOwnershipDiagnostic diagnostic in ownership.Diagnostics)
                        {
                            Console.Error.WriteLine($"{diagnostic.Code} error: {diagnostic.Message}");
                        }
                        return 1;
                    }
                    candidate = Path.Combine(current.FullName, "obj/copeland/workspace/tsconfig.generated.json");
                    if (!File.Exists(candidate))
                    {
                        throw new ArgumentException("Run tscl workspace sync before compiling a Legacy source in a mixed workspace.");
                    }
                }
                if (File.Exists(candidate))
                {
                    configPath = candidate;
                    arguments = ["--project", candidate, .. arguments];
                    break;
                }
            }
        }
        string compilerDirectory = directory;
        if (configPath is not null)
        {
            compilerDirectory = Directory.Exists(configPath) ? configPath : Path.GetDirectoryName(configPath)!;
        }
        string? compiler = null;
        for (DirectoryInfo? current = new(compilerDirectory); current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, "node_modules/typescript/bin/tsc");
            if (File.Exists(candidate))
            {
                compiler = candidate;
                break;
            }
        }
        if (compiler is null)
        {
            throw new ArgumentException("The project has no local TypeScript compiler. Install its typescript dependency; tscl does not download or substitute a compiler.");
        }
        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        start.ArgumentList.Add(compiler);
        if (configPath is null)
        {
            start.ArgumentList.Add(sourcePath);
        }
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ?? throw new IOException("Could not launch Node for the project's TypeScript compiler.");
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        Task error = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        process.WaitForExit();
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        return process.ExitCode;
    }
}
