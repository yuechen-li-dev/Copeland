using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionFunctionDiscoveryResult(
    OblivionFunctionTestDescriptor? Descriptor,
    TimeSpan BuildDuration,
    TimeSpan DiscoveryDuration,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics)
{
    public bool Succeeded => Descriptor is not null && Diagnostics.All(diagnostic =>
        diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed record OblivionFunctionRunResult(
    OblivionWorkspaceSession Session,
    OblivionFunctionTestDescriptor? Descriptor,
    OblivionFunctionExecutionResult Result,
    TimeSpan BuildDuration,
    TimeSpan DiscoveryDuration,
    TimeSpan RunnerDuration);

public interface IOblivionFunctionRunner
{
    OblivionFunctionDiscoveryResult Discover(OblivionCard card, string workspaceRoot);

    OblivionFunctionExecutionResult Run(
        OblivionCard card,
        string workspaceRoot,
        OblivionFunctionTestDescriptor descriptor);
}

public sealed class OblivionXunitFunctionRunner : IOblivionFunctionRunner
{
    public const string RunnerIdentity = "dotnet-test-trx-v1";
    private static readonly Regex SourceLocation = new(
        @" in (?<path>.*\.tsxtest):line (?<line>[0-9]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly IOblivionProcessRunner _processRunner;
    private readonly TimeSpan _timeout;

    public OblivionXunitFunctionRunner(
        IOblivionProcessRunner? processRunner = null,
        TimeSpan? timeout = null)
    {
        _processRunner = processRunner ?? new OblivionBoundedProcessRunner();
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    public OblivionFunctionDiscoveryResult Discover(OblivionCard card, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        List<OblivionCardDiagnostic> diagnostics = [];
        if (card.Kind != OblivionCardKind.Function || card.Function is null)
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-SOURCE-MISSING",
                $"Card '{card.Id.Value}' is not a Function Card with a semantic source.",
                card.Provenance.SourceReference));
            return new(null, TimeSpan.Zero, TimeSpan.Zero, diagnostics);
        }

        string root = Path.GetFullPath(workspaceRoot);
        string sourcePath = Path.GetFullPath(Path.Combine(root, card.Function.Reference));
        if (!IsInside(root, sourcePath))
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-SOURCE-UNSAFE",
                $"Function source '{card.Function.Reference}' escapes the workspace root.",
                sourcePath));
            return new(null, TimeSpan.Zero, TimeSpan.Zero, diagnostics);
        }

        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-SOURCE-NOT-FOUND",
                $"Function source '{card.Function.Reference}' was not found.",
                sourcePath));
            return new(null, TimeSpan.Zero, TimeSpan.Zero, diagnostics);
        }

        string? projectPath = FindOwningProject(sourcePath, root);
        if (projectPath is null)
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-PROJECT-NOT-FOUND",
                $"No unique owning project was found for '{card.Function.Reference}'.",
                sourcePath));
            return new(null, TimeSpan.Zero, TimeSpan.Zero, diagnostics);
        }

        Stopwatch buildClock = Stopwatch.StartNew();
        OblivionProcessResult build = _processRunner.Run(new OblivionProcessRequest(
            "dotnet",
            ["build", projectPath, "--nologo", "-m:1"],
            Path.GetDirectoryName(projectPath)!,
            _timeout));
        buildClock.Stop();
        if (!Succeeded(build))
        {
            diagnostics.Add(ProcessError(
                "OBLIVION-FUNCTION-BUILD-FAILED",
                "Copeland test project materialization failed.",
                projectPath,
                build));
            return new(null, buildClock.Elapsed, TimeSpan.Zero, diagnostics);
        }

        string[] testProjects = Directory
            .EnumerateFiles(
                Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "CopelandTests"),
                "*.CopelandTests.csproj",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (testProjects.Length != 1)
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-TEST-PROJECT-AMBIGUOUS",
                $"Expected one materialized Copeland xUnit project, found {testProjects.Length}.",
                projectPath));
            return new(null, buildClock.Elapsed, TimeSpan.Zero, diagnostics);
        }

        string testProjectPath = testProjects[0];
        buildClock.Start();
        OblivionProcessResult testBuild = _processRunner.Run(new OblivionProcessRequest(
            "dotnet",
            [
                "build",
                testProjectPath,
                "--nologo",
                "-m:1",
                "-p:CopelandAuxiliaryTestBuild=true",
            ],
            Path.GetDirectoryName(testProjectPath)!,
            _timeout));
        buildClock.Stop();
        if (!Succeeded(testBuild))
        {
            diagnostics.Add(ProcessError(
                "OBLIVION-FUNCTION-TEST-BUILD-FAILED",
                "The materialized Copeland xUnit project failed to build.",
                testProjectPath,
                testBuild));
            return new(null, buildClock.Elapsed, TimeSpan.Zero, diagnostics);
        }

        Stopwatch discoveryClock = Stopwatch.StartNew();
        OblivionProcessResult discovery = _processRunner.Run(new OblivionProcessRequest(
            "dotnet",
            [
                "test",
                testProjectPath,
                "--nologo",
                "--no-build",
                "--no-restore",
                "--list-tests",
            ],
            Path.GetDirectoryName(testProjectPath)!,
            _timeout));
        discoveryClock.Stop();
        if (!Succeeded(discovery))
        {
            diagnostics.Add(ProcessError(
                "OBLIVION-FUNCTION-DISCOVERY-FAILED",
                "xUnit/Test Platform discovery failed.",
                testProjectPath,
                discovery));
            return new(null, buildClock.Elapsed, discoveryClock.Elapsed, diagnostics);
        }

        IReadOnlyList<string> discovered = ParseDiscoveredTests(discovery.StandardOutput);
        string methodSuffix = "." + card.Function.Test;
        string[] matchingCases = discovered
            .Where(test => test.EndsWith(methodSuffix, StringComparison.Ordinal) ||
                test.Contains(methodSuffix + "(", StringComparison.Ordinal))
            .ToArray();
        string[] identities = matchingCases
            .Select(test => BaseIdentity(test, methodSuffix))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (identities.Length != 1)
        {
            string reason = identities.Length == 0
                ? "was not discovered"
                : $"resolved ambiguously to {identities.Length} discovered identities";
            diagnostics.Add(Error(
                identities.Length == 0
                    ? "OBLIVION-FUNCTION-TEST-NOT-DISCOVERED"
                    : "OBLIVION-FUNCTION-TEST-AMBIGUOUS",
                $"Test '{card.Function.Test}' {reason}.",
                sourcePath));
            return new(null, buildClock.Elapsed, discoveryClock.Elapsed, diagnostics);
        }

        string identity = identities[0];
        bool theory = matchingCases.Any(test => test.StartsWith(identity + "(", StringComparison.Ordinal));
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)));
        OblivionFunctionTestDescriptor descriptor = new(
            identity,
            card.Function.Test,
            theory ? OblivionFunctionTestKind.Theory : OblivionFunctionTestKind.Fact,
            Math.Max(1, matchingCases.Length),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            card.Function.Reference,
            sourceHash,
            projectPath,
            testProjectPath,
            RunnerIdentity,
            diagnostics);
        return new(descriptor, buildClock.Elapsed, discoveryClock.Elapsed, diagnostics);
    }

    public OblivionFunctionExecutionResult Run(
        OblivionCard card,
        string workspaceRoot,
        OblivionFunctionTestDescriptor descriptor)
    {
        string resultDirectory = Path.Combine(
            Path.GetTempPath(),
            "Oblivion",
            "function-runs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultDirectory);
        string trxPath = Path.Combine(resultDirectory, "result.trx");
        try
        {
            Stopwatch runnerClock = Stopwatch.StartNew();
            OblivionProcessResult process = _processRunner.Run(new OblivionProcessRequest(
                "dotnet",
                [
                    "test",
                    descriptor.TestProjectPath,
                    "--nologo",
                    "--no-build",
                    "--no-restore",
                    "--filter",
                    "FullyQualifiedName=" + descriptor.TestIdentity,
                    "--results-directory",
                    resultDirectory,
                    "--logger",
                    "trx;LogFileName=result.trx",
                ],
                Path.GetDirectoryName(descriptor.TestProjectPath)!,
                _timeout));
            runnerClock.Stop();
            if (process.TimedOut || !process.Started || !File.Exists(trxPath))
            {
                OblivionCardDiagnostic diagnostic = ProcessError(
                    process.TimedOut
                        ? "OBLIVION-FUNCTION-RUNNER-TIMEOUT"
                        : "OBLIVION-FUNCTION-RUNNER-FAILED",
                    "xUnit/Test Platform did not produce a structured TRX result.",
                    descriptor.TestProjectPath,
                    process);
                return ErrorResult(card, descriptor, diagnostic);
            }

            return ParseTrx(card, descriptor, trxPath, runnerClock.Elapsed);
        }
        catch (Exception exception)
        {
            return ErrorResult(card, descriptor, Error(
                "OBLIVION-FUNCTION-TRX-INVALID",
                $"Structured xUnit result could not be read: {exception.Message}",
                trxPath));
        }
        finally
        {
            if (Directory.Exists(resultDirectory))
            {
                Directory.Delete(resultDirectory, recursive: true);
            }
        }
    }

    private static OblivionFunctionExecutionResult ParseTrx(
        OblivionCard card,
        OblivionFunctionTestDescriptor descriptor,
        string trxPath,
        TimeSpan runnerDuration)
    {
        XDocument document = XDocument.Load(trxPath);
        XElement[] results = document
            .Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult")
            .Where(element =>
            {
                string name = (string?)element.Attribute("testName") ?? string.Empty;
                return name.StartsWith(descriptor.TestIdentity, StringComparison.Ordinal) ||
                    name.StartsWith(descriptor.DisplayName, StringComparison.Ordinal);
            })
            .ToArray();
        if (results.Length == 0)
        {
            return ErrorResult(card, descriptor, Error(
                "OBLIVION-FUNCTION-RESULT-MISSING",
                "The exact discovered test produced no TRX test result.",
                trxPath));
        }

        int passed = results.Count(result => Outcome(result) == "Passed");
        int failed = results.Count(result => Outcome(result) == "Failed");
        int skipped = results.Count(result => Outcome(result) is "NotExecuted" or "Skipped");
        TimeSpan duration = TimeSpan.FromTicks(results.Sum(result => ParseDuration(result).Ticks));
        XElement? failedResult = results.FirstOrDefault(result => Outcome(result) == "Failed");
        OblivionFunctionFailure? failure = failedResult is null ? null : ParseFailure(failedResult);
        OblivionFunctionExecutionOutcome outcome = failed > 0
            ? OblivionFunctionExecutionOutcome.Failed
            : passed > 0 && skipped == 0
                ? OblivionFunctionExecutionOutcome.Passed
                : OblivionFunctionExecutionOutcome.Skipped;

        return new OblivionFunctionExecutionResult(
            card.Id.Value,
            descriptor.TestIdentity,
            descriptor.DisplayName,
            outcome,
            duration == TimeSpan.Zero ? runnerDuration : duration,
            failure,
            descriptor.SourceReference,
            descriptor.SourceHash,
            descriptor.RunnerIdentity,
            results.Length,
            passed,
            failed,
            skipped,
            DateTimeOffset.UtcNow,
            descriptor.Diagnostics);
    }

    private static OblivionFunctionFailure ParseFailure(XElement result)
    {
        XElement? errorInfo = result.Descendants().FirstOrDefault(element => element.Name.LocalName == "ErrorInfo");
        string message = errorInfo?.Elements().FirstOrDefault(element => element.Name.LocalName == "Message")?.Value.Trim()
            ?? "xUnit reported a failed test.";
        string? stack = errorInfo?.Elements().FirstOrDefault(element => element.Name.LocalName == "StackTrace")?.Value.Trim();
        MatchCollection locations = SourceLocation.Matches(stack ?? string.Empty);
        Match? location = locations.Count == 0 ? null : locations[^1];
        string? exceptionType = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return new OblivionFunctionFailure(
            message,
            exceptionType,
            location?.Groups["path"].Value,
            location is not null && int.TryParse(location.Groups["line"].Value, out int line) ? line : null,
            stack);
    }

    private static TimeSpan ParseDuration(XElement result)
    {
        return TimeSpan.TryParse((string?)result.Attribute("duration"), out TimeSpan duration)
            ? duration
            : TimeSpan.Zero;
    }

    private static string Outcome(XElement result)
    {
        return (string?)result.Attribute("outcome") ?? string.Empty;
    }

    private static IReadOnlyList<string> ParseDiscoveredTests(string output)
    {
        string[] lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int marker = Array.FindIndex(lines, line =>
            line.Contains("The following Tests are available", StringComparison.Ordinal));
        if (marker < 0)
        {
            return [];
        }

        return lines[(marker + 1)..]
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !line.StartsWith("Test Run", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("Total tests", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string BaseIdentity(string displayName, string methodSuffix)
    {
        int suffix = displayName.IndexOf(methodSuffix, StringComparison.Ordinal);
        return displayName[..(suffix + methodSuffix.Length)];
    }

    private static string? FindOwningProject(string sourcePath, string workspaceRoot)
    {
        DirectoryInfo? directory = new FileInfo(sourcePath).Directory;
        while (directory is not null && IsInside(workspaceRoot, directory.FullName))
        {
            string[] projects = directory.GetFiles("*.csproj", SearchOption.TopDirectoryOnly)
                .Select(file => file.FullName)
                .ToArray();
            if (projects.Length == 1)
            {
                return projects[0];
            }
            if (projects.Length > 1)
            {
                return null;
            }
            directory = directory.Parent;
        }
        return null;
    }

    private static bool IsInside(string root, string candidate)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return !Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool Succeeded(OblivionProcessResult result)
    {
        return result.Started && !result.TimedOut && result.ExitCode == 0;
    }

    private static OblivionCardDiagnostic ProcessError(
        string code,
        string message,
        string sourcePath,
        OblivionProcessResult result)
    {
        string detail = result.TimedOut
            ? "The bounded runner timed out."
            : !string.IsNullOrWhiteSpace(result.StartError)
                ? result.StartError
                : !string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardError
                    : result.StandardOutput;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = $"Process exited with code {result.ExitCode?.ToString() ?? "unknown"}.";
        }
        return Error(code, message + " " + detail.Trim(), sourcePath);
    }

    private static OblivionCardDiagnostic Error(string code, string message, string? sourcePath)
    {
        return new OblivionCardDiagnostic(
            code,
            OblivionDiagnosticSeverity.Error,
            message,
            sourcePath);
    }

    private static OblivionFunctionExecutionResult ErrorResult(
        OblivionCard card,
        OblivionFunctionTestDescriptor descriptor,
        OblivionCardDiagnostic diagnostic)
    {
        return new OblivionFunctionExecutionResult(
            card.Id.Value,
            descriptor.TestIdentity,
            descriptor.DisplayName,
            OblivionFunctionExecutionOutcome.Error,
            null,
            null,
            descriptor.SourceReference,
            descriptor.SourceHash,
            descriptor.RunnerIdentity,
            descriptor.CaseCount,
            0,
            0,
            0,
            DateTimeOffset.UtcNow,
            [.. descriptor.Diagnostics, diagnostic]);
    }
}
