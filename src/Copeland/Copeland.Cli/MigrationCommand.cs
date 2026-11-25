using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Copeland.Cli;

internal static class MigrationCommand
{
    private const string Marker = "copeland-migration.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, string> Scaffold = new Dictionary<string, string>
    {
        ["Migration.csproj"] = """
            <!-- Workspace ownership anchor for this JavaScript target. -->
            <Project Sdk="Microsoft.NET.Sdk" />
            """,
        ["tsconfig.tsx"] = """
            import { defineTypeScriptWorkspace } from "copeland/workspace";

            export default defineTypeScriptWorkspace({
                ownership: "partial",
                tscl: {
                    project: "./Migration.csproj",
                    include: ["src/**"],
                    targets: { node: { backend: "javascript", runtime: "node" } }
                }
            });
            """,
        ["manifest.tsx"] = """
            import { Package, Targets, Workspace, define } from "tspack/manifest";

            export default define(
                <Workspace name="copeland-migration" runtime="nodejs">
                    <Package name="copeland-migration" version="0.0.0" kind="app" dependencies={{ values: [] }}>
                        <Targets rows={[
                            { name: "node", entry: "src/Main.ts", runtime: "dist/main.js", deps: [], peers: [] }
                        ]} />
                    </Package>
                </Workspace>,
            );
            """,
        ["src/Main.ts"] = """
            export type CopelandProfile = "JavaScript";

            export function Main(): string {
                return "Copeland migration workspace is ready.";
            }
            """,
        [".gitignore"] = "dist/\nobj/\nbin/",
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("init" or "report"))
        {
            Console.Error.WriteLine("Usage: tscl migrate init [-p <tsconfig.json>] [--out <directory>] | tscl migrate report [-p <tsconfig.json>] [--format text|json].");
            return 2;
        }

        bool init = args[0] == "init";
        string? project = null;
        string? output = null;
        string format = "text";
        for (int index = 1; index < args.Length; index += 2)
        {
            string flag = args[index];
            string? value = index + 1 < args.Length ? args[index + 1] : null;
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-'))
            {
                Console.Error.WriteLine($"COPE-MIGRATE-0001 error: '{flag}' requires a value.");
                return 2;
            }
            switch (flag)
            {
                case "-p" or "--project": project = value; break;
                case "--out" when init: output = value; break;
                case "--format" when !init && (value is "text" or "json"): format = value; break;
                default:
                    Console.Error.WriteLine($"COPE-MIGRATE-0001 error: Unsupported option '{flag} {value}'.");
                    return 2;
            }
        }

        try
        {
            string projectPath = ResolveProject(project);
            return init ? Initialize(projectPath, output) : Report(projectPath, format);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or JsonException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"COPE-MIGRATE-0002 error: {exception.Message}");
            return 1;
        }
    }

    private static string ResolveProject(string? project)
    {
        if (project is null && File.Exists(Marker))
        {
            project = ReadMarker(Marker).LegacyProject;
        }
        string path = Path.GetFullPath(project ?? "tsconfig.json");
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, "tsconfig.json");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"TypeScript project '{path}' was not found. Pass its config with -p.", path);
        }
        return path;
    }

    private static int Initialize(string project, string? output)
    {
        string root = Path.GetDirectoryName(project)!;
        string target = Path.GetFullPath(output ?? Path.Combine(root, "copeland"));
        if (IsExistingScaffold(target, project))
        {
            return 0;
        }

        string[] included = IncludedScaffoldFiles(project, target);
        if (included.Length > 0 && output is null && Path.GetDirectoryName(root) is string parentRoot)
        {
            target = Path.Combine(parentRoot, Path.GetFileName(root) + "-copeland");
            if (IsExistingScaffold(target, project))
            {
                return 0;
            }
            included = IncludedScaffoldFiles(project, target);
        }
        if (included.Length > 0)
        {
            throw new IOException($"The legacy TypeScript config would include {string.Join(", ", included)}. Choose an --out directory outside its source globs.");
        }

        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".copeland-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            foreach ((string relativePath, string contents) in Scaffold)
            {
                string file = Path.Combine(staging, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, contents + "\n");
            }
            string legacyProject = Path.GetRelativePath(target, project).Replace('\\', '/');
            File.WriteAllText(Path.Combine(staging, Marker), JsonSerializer.Serialize(new MigrationMarker(1, "Legacy", legacyProject), JsonOptions) + "\n");
            Directory.Move(staging, target);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        Console.Out.WriteLine($"Legacy TypeScript project: {project}");
        Console.Out.WriteLine($"Copeland Node workspace: {target}");
        return 0;
    }

    private static bool IsExistingScaffold(string target, string project)
    {
        if (Directory.Exists(target))
        {
            string markerPath = Path.Combine(target, Marker);
            if (File.Exists(markerPath) && PathsEqual(Path.GetFullPath(ReadMarker(markerPath).LegacyProject, target), project))
            {
                Console.Out.WriteLine($"Migration workspace already exists at {target}");
                return true;
            }
            throw new IOException($"Destination '{target}' already exists. Choose a new directory with --out.");
        }
        if (File.Exists(target))
        {
            throw new IOException($"Destination '{target}' is already a file.");
        }
        return false;
    }

    private static string[] IncludedScaffoldFiles(string project, string target)
    {
        string[] proposed = Scaffold.Keys.Where(path => Path.GetExtension(path) is ".ts" or ".tsx")
            .Concat(["dist/main.js", "dist/src/Main.js"])
            .Select(path => Path.Combine(target, path)).ToArray();
        return Analyze(project, proposed).GetProperty("includedFiles").Deserialize<string[]>()!;
    }

    private static MigrationMarker ReadMarker(string path)
    {
        var marker = JsonSerializer.Deserialize<MigrationMarker>(File.ReadAllText(path), JsonOptions);
        if (marker is not { SchemaVersion: 1, LegacyProfile: "Legacy" } || string.IsNullOrWhiteSpace(marker.LegacyProject))
        {
            throw new IOException($"Migration marker '{Path.GetFullPath(path)}' has an unsupported schema.");
        }
        return marker;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static int Report(string project, string format)
    {
        JsonElement report = Analyze(project);
        if (format == "json")
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return 0;
        }

        JsonElement findings = report.GetProperty("findings");
        foreach (JsonElement finding in findings.EnumerateArray())
        {
            Console.Out.WriteLine($"{finding.GetProperty("file").GetString()}({finding.GetProperty("line").GetInt32()},{finding.GetProperty("column").GetInt32()}): warning {finding.GetProperty("code").GetString()}: {finding.GetProperty("message").GetString()}");
        }
        Console.Out.WriteLine($"Legacy profile: {report.GetProperty("files").GetArrayLength()} source files, {findings.GetArrayLength()} migration findings.");
        return 0;
    }

    private static JsonElement Analyze(string project, string[]? proposedFiles = null)
    {
        using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Copeland.Cli.MigrationReport.cjs")!;
        using var reader = new StreamReader(source);
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = Path.GetDirectoryName(project)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("--input-type=commonjs");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(reader.ReadToEnd());
        using Process process = Process.Start(start) ?? throw new IOException("Could not launch Node to analyze TypeScript.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(JsonSerializer.Serialize(new { project, proposedFiles }, JsonOptions));
        process.StandardInput.Close();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException(error.GetAwaiter().GetResult().Trim());
        }
        using JsonDocument result = JsonDocument.Parse(output.GetAwaiter().GetResult());
        return result.RootElement.Clone();
    }

    private sealed record MigrationMarker(int SchemaVersion, string LegacyProfile, string LegacyProject);
}
