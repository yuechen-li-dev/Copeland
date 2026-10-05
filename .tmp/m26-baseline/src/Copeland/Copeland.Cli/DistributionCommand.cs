using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Copeland.TS.LanguageServer;

namespace Copeland.Cli;

internal static class DistributionCommand
{
    private const string OwnershipRelativePath = "obj/copeland/workspace/editor-ownership.generated.json";
    private const int OwnershipSchemaVersion = 1;

    public static int RunInstallInfo(string[] args)
    {
        if (!TryParseFormat(args, out bool json))
        {
            return Usage("COPE-DIST-0002", "Usage: tscl install-info [--format text|json].");
        }

        InstallInfo info = CreateInstallInfo();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(info, JsonOptions));
        }
        else
        {
            Console.Out.WriteLine($"Copeland toolchain {info.ToolVersion}");
            Console.Out.WriteLine($"compiler: {info.CompilerVersion}");
            Console.Out.WriteLine($"language server: {info.LanguageServerVersion}");
            Console.Out.WriteLine($"tool path: {info.ToolPath}");
            Console.Out.WriteLine($"ownership schema: {info.WorkspaceOwnershipSchemaVersion}");
            Console.Out.WriteLine($"package contract schema: {info.NuGetContractSchemaVersion}");
        }

        return 0;
    }

    public static int RunDoctor(string[] args)
    {
        if (!TryParseFormat(args, out bool json))
        {
            return Usage("COPE-DIST-0003", "Usage: tscl doctor [--format text|json].");
        }

        DoctorReport report = Inspect(Environment.CurrentDirectory);
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            foreach (DoctorCheck check in report.Checks)
            {
                Console.Out.WriteLine($"{check.Status.ToUpperInvariant(),-7} {check.Name}: {check.Message}");
                if (!string.IsNullOrWhiteSpace(check.Action))
                {
                    Console.Out.WriteLine($"        Action: {check.Action}");
                }
            }
        }

        return report.Success ? 0 : 1;
    }

    private static InstallInfo CreateInstallInfo()
    {
        return new InstallInfo(
            SchemaVersion: 1,
            ToolVersion: TsclBuildContract.Version,
            CompilerVersion: TsclBuildContract.Version,
            LanguageServerVersion: LanguageServerHost.Version,
            ToolPath: Environment.ProcessPath ?? AppContext.BaseDirectory,
            WorkspaceOwnershipSchemaVersion: OwnershipSchemaVersion,
            NuGetContractSchemaVersion: 1,
            NpmContractSchemaVersion: 1,
            LanguageServerLaunch: "tscl language-server");
    }

    private static DoctorReport Inspect(string root)
    {
        var checks = new List<DoctorCheck>();
        string? dotnetVersion = TryRunDotnetVersion();
        checks.Add(dotnetVersion is null
            ? Failure("dotnet-sdk", "dotnet was not found on PATH.", "Install the supported .NET SDK, then reopen your terminal.")
            : Pass("dotnet-sdk", $"dotnet SDK {dotnetVersion} is available."));

        InstallInfo info = CreateInstallInfo();
        checks.Add(Pass("tool", $"Copeland tool {info.ToolVersion} is running from {info.ToolPath}."));
        checks.Add(info.CompilerVersion == info.LanguageServerVersion
            ? Pass("language-server", $"tscl language-server {info.LanguageServerVersion} is packaged with this tool.")
            : Failure("language-server", $"Compiler {info.CompilerVersion} and language server {info.LanguageServerVersion} differ.", "Update the Copeland toolchain."));

        string projectPath = Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;
        if (projectPath.Length == 0)
        {
            checks.Add(Warning("copeland-sdk", "No .csproj was found in the current directory.", "Run tscl doctor from a Copeland project directory."));
        }
        else
        {
            string project = File.ReadAllText(projectPath);
            Match package = Regex.Match(project, "<PackageReference\\s+[^>]*(?:Include|Update)=\\\"Copeland\\.TS\\.Sdk\\\"[^>]*Version=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            if (!package.Success)
            {
                checks.Add(Failure("copeland-sdk", $"{Path.GetFileName(projectPath)} does not reference Copeland.TS.Sdk.", "Add PackageReference Include=\"Copeland.TS.Sdk\" Version=\"0.1.0\"."));
            }
            else if (!SameMajorMinor(package.Groups[1].Value, info.ToolVersion))
            {
                checks.Add(Failure("copeland-sdk", $"Project requires Copeland TS {package.Groups[1].Value}; installed compiler is {info.ToolVersion}.", "Update the Copeland toolchain."));
            }
            else
            {
                checks.Add(Pass("copeland-sdk", $"Project SDK {package.Groups[1].Value} is compatible with tool {info.ToolVersion}."));
            }
        }

        string workspace = Path.Combine(root, "tsconfig.tsx");
        string ownership = Path.Combine(root, OwnershipRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(workspace))
        {
            checks.Add(Warning("workspace", "No tsconfig.tsx workspace manifest was found.", "This is expected for a pure CLR project."));
        }
        else if (!File.Exists(ownership))
        {
            checks.Add(Failure("workspace", "Workspace ownership metadata is missing.", "Run: tscl workspace sync"));
        }
        else
        {
            checks.Add(Pass("workspace", "Workspace manifest and generated ownership metadata are present."));
        }

        bool needsTspack = File.Exists(Path.Combine(root, "tspack.toml")) || File.Exists(Path.Combine(root, "manifest.tsx"));
        checks.Add(needsTspack
            ? (FindOnPath("tspack") ? Pass("tspack", "TSPack is available for this browser project.") : Failure("tspack", "This project declares TSPack materialization but tspack is not on PATH.", "Install the documented TSPack distribution, then rerun tscl doctor."))
            : Pass("tspack", "Not required by this pure CLR or workspace-only project."));

        return new DoctorReport(1, checks.All(check => check.Status != "fail"), checks);
    }

    private static bool TryParseFormat(string[] args, out bool json)
    {
        json = false;
        if (args.Length == 0)
        {
            return true;
        }

        if (args.Length == 2 && args[0] == "--format" && (args[1] == "text" || args[1] == "json"))
        {
            json = args[1] == "json";
            return true;
        }

        return false;
    }

    private static string? TryRunDotnetVersion()
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            string version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && version.Length > 0 ? version : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool FindOnPath(string command)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Split(Path.PathSeparator).Any(directory =>
            File.Exists(Path.Combine(directory, command)) || File.Exists(Path.Combine(directory, command + ".exe")));
    }

    private static bool SameMajorMinor(string left, string right)
    {
        string[] leftParts = left.Split('.', '-', '+');
        string[] rightParts = right.Split('.', '-', '+');
        return leftParts.Length >= 2 && rightParts.Length >= 2 && leftParts[0] == rightParts[0] && leftParts[1] == rightParts[1];
    }

    private static DoctorCheck Pass(string name, string message) => new(name, "pass", message, null);
    private static DoctorCheck Warning(string name, string message, string action) => new(name, "warn", message, action);
    private static DoctorCheck Failure(string name, string message, string action) => new(name, "fail", message, action);

    private static int Usage(string code, string message)
    {
        Console.Error.WriteLine($"{code} error: {message}");
        return 2;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record InstallInfo(int SchemaVersion, string ToolVersion, string CompilerVersion, string LanguageServerVersion, string ToolPath, int WorkspaceOwnershipSchemaVersion, int NuGetContractSchemaVersion, int NpmContractSchemaVersion, string LanguageServerLaunch);
    private sealed record DoctorReport(int SchemaVersion, bool Success, IReadOnlyList<DoctorCheck> Checks);
    private sealed record DoctorCheck(string Name, string Status, string Message, string? Action);
}
