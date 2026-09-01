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
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    OblivionFunctionRealizationKind Realization = OblivionFunctionRealizationKind.Cold,
    string RealizationFingerprint = "",
    TimeSpan ResolutionDuration = default,
    TimeSpan FingerprintingDuration = default,
    bool MaterializationInvoked = false,
    bool DiscoveryInvoked = false)
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
    TimeSpan RunnerDuration,
    OblivionFunctionRealizationKind Realization = OblivionFunctionRealizationKind.Cold,
    string RealizationFingerprint = "",
    TimeSpan ResolutionDuration = default,
    TimeSpan FingerprintingDuration = default,
    bool MaterializationInvoked = false,
    bool DiscoveryInvoked = false,
    bool ExecutionInvoked = false);

public interface IOblivionFunctionRunner
{
    OblivionFunctionDiscoveryResult Inspect(OblivionCard card, string workspaceRoot)
    {
        return Discover(card, workspaceRoot);
    }

    OblivionFunctionDiscoveryResult Discover(OblivionCard card, string workspaceRoot);

    OblivionFunctionExecutionResult Run(
        OblivionCard card,
        string workspaceRoot,
        OblivionFunctionTestDescriptor descriptor);
}

public sealed class OblivionXunitFunctionRunner : IOblivionFunctionRunner
{
    public const string RunnerIdentity = "dotnet-test-trx-v1";
    public const string RealizationSchemaIdentity = "oblivion-function-realization-v1";
    private static readonly Regex SourceLocation = new(
        @" in (?<path>.*\.tsxtest):line (?<line>[0-9]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly IOblivionProcessRunner _processRunner;
    private readonly TimeSpan _timeout;
    private readonly object _realizationGate = new();
    private readonly Dictionary<string, ProjectRealization> _realizations =
        new(StringComparer.OrdinalIgnoreCase);

    public OblivionXunitFunctionRunner(
        IOblivionProcessRunner? processRunner = null,
        TimeSpan? timeout = null)
    {
        _processRunner = processRunner ?? new OblivionBoundedProcessRunner();
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    public OblivionFunctionDiscoveryResult Inspect(OblivionCard card, string workspaceRoot)
    {
        Resolution resolution = Resolve(card, workspaceRoot);
        if (!resolution.Succeeded || resolution.ProjectPath is null)
        {
            return new(null, TimeSpan.Zero, TimeSpan.Zero, resolution.Diagnostics);
        }

        lock (_realizationGate)
        {
            if (!_realizations.TryGetValue(resolution.ProjectPath, out ProjectRealization? realization))
            {
                return new(null, TimeSpan.Zero, TimeSpan.Zero, []);
            }

            return SelectDescriptor(
                card,
                resolution,
                realization,
                OblivionFunctionRealizationKind.Warm,
                TimeSpan.Zero,
                TimeSpan.Zero,
                materializationInvoked: false,
                discoveryInvoked: false);
        }
    }

    public OblivionFunctionDiscoveryResult Discover(OblivionCard card, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        Stopwatch resolutionClock = Stopwatch.StartNew();
        Resolution resolution = Resolve(card, workspaceRoot);
        resolutionClock.Stop();
        if (!resolution.Succeeded || resolution.ProjectPath is null || resolution.SourcePath is null)
        {
            return new(
                null,
                TimeSpan.Zero,
                TimeSpan.Zero,
                resolution.Diagnostics,
                ResolutionDuration: resolutionClock.Elapsed);
        }

        lock (_realizationGate)
        {
            Stopwatch fingerprintClock = Stopwatch.StartNew();
            string fingerprint = ComputeRealizationFingerprint(resolution.ProjectPath);
            fingerprintClock.Stop();
            if (_realizations.TryGetValue(resolution.ProjectPath, out ProjectRealization? cached) &&
                string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal) &&
                OutputsAreValid(cached))
            {
                return SelectDescriptor(
                    card,
                    resolution,
                    cached,
                    OblivionFunctionRealizationKind.Warm,
                    resolutionClock.Elapsed,
                    fingerprintClock.Elapsed,
                    materializationInvoked: false,
                    discoveryInvoked: false);
            }

            return RealizeCold(card, resolution, fingerprint, resolutionClock.Elapsed, fingerprintClock.Elapsed);
        }
    }

    private OblivionFunctionDiscoveryResult RealizeCold(
        OblivionCard card,
        Resolution resolution,
        string initialFingerprint,
        TimeSpan resolutionDuration,
        TimeSpan fingerprintDuration)
    {
        string projectPath = resolution.ProjectPath!;
        List<OblivionCardDiagnostic> diagnostics = [];
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
            return FailedRealization(
                diagnostics,
                initialFingerprint,
                buildClock.Elapsed,
                TimeSpan.Zero,
                resolutionDuration,
                fingerprintDuration,
                discoveryInvoked: false);
        }

        string testProjectDirectory = Path.Combine(
            Path.GetDirectoryName(projectPath)!,
            "obj",
            "CopelandTests");
        string[] testProjects = Directory.Exists(testProjectDirectory)
            ? Directory
                .EnumerateFiles(testProjectDirectory, "*.CopelandTests.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        if (testProjects.Length != 1)
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-TEST-PROJECT-AMBIGUOUS",
                $"Expected one materialized Copeland xUnit project, found {testProjects.Length}.",
                projectPath));
            return FailedRealization(
                diagnostics,
                initialFingerprint,
                buildClock.Elapsed,
                TimeSpan.Zero,
                resolutionDuration,
                fingerprintDuration,
                discoveryInvoked: false);
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
            return FailedRealization(
                diagnostics,
                initialFingerprint,
                buildClock.Elapsed,
                TimeSpan.Zero,
                resolutionDuration,
                fingerprintDuration,
                discoveryInvoked: false);
        }

        string? testAssemblyPath = ResolveTestAssembly(testProjectPath);
        if (testAssemblyPath is null)
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-TEST-ASSEMBLY-MISSING",
                "The materialized Copeland xUnit assembly was not produced.",
                testProjectPath));
            return FailedRealization(
                diagnostics,
                initialFingerprint,
                buildClock.Elapsed,
                TimeSpan.Zero,
                resolutionDuration,
                fingerprintDuration,
                discoveryInvoked: false);
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
            return FailedRealization(
                diagnostics,
                initialFingerprint,
                buildClock.Elapsed,
                discoveryClock.Elapsed,
                resolutionDuration,
                fingerprintDuration,
                discoveryInvoked: true);
        }

        Stopwatch finalFingerprintClock = Stopwatch.StartNew();
        string finalFingerprint = ComputeRealizationFingerprint(projectPath);
        finalFingerprintClock.Stop();
        ProjectRealization realization = new(
            finalFingerprint,
            projectPath,
            testProjectPath,
            testAssemblyPath,
            ComputeFileHash(testAssemblyPath),
            ParseDiscoveredTests(discovery.StandardOutput));
        _realizations[projectPath] = realization;
        return SelectDescriptor(
            card,
            resolution,
            realization,
            OblivionFunctionRealizationKind.Cold,
            resolutionDuration,
            fingerprintDuration + finalFingerprintClock.Elapsed,
            materializationInvoked: true,
            discoveryInvoked: true,
            buildClock.Elapsed,
            discoveryClock.Elapsed);
    }

    private static OblivionFunctionDiscoveryResult SelectDescriptor(
        OblivionCard card,
        Resolution resolution,
        ProjectRealization realization,
        OblivionFunctionRealizationKind kind,
        TimeSpan resolutionDuration,
        TimeSpan fingerprintDuration,
        bool materializationInvoked,
        bool discoveryInvoked,
        TimeSpan buildDuration = default,
        TimeSpan discoveryDuration = default)
    {
        string methodSuffix = "." + card.Function!.Test;
        string[] matchingCases = realization.DiscoveredTests
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
            OblivionCardDiagnostic diagnostic = Error(
                identities.Length == 0
                    ? "OBLIVION-FUNCTION-TEST-NOT-DISCOVERED"
                    : "OBLIVION-FUNCTION-TEST-AMBIGUOUS",
                $"Test '{card.Function.Test}' {reason}.",
                resolution.SourcePath);
            return new(
                null,
                buildDuration,
                discoveryDuration,
                [diagnostic],
                kind,
                realization.Fingerprint,
                resolutionDuration,
                fingerprintDuration,
                materializationInvoked,
                discoveryInvoked);
        }

        string identity = identities[0];
        bool theory = matchingCases.Any(test => test.StartsWith(identity + "(", StringComparison.Ordinal));
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(resolution.SourcePath!)));
        OblivionFunctionTestDescriptor descriptor = new(
            identity,
            card.Function.Test,
            theory ? OblivionFunctionTestKind.Theory : OblivionFunctionTestKind.Fact,
            Math.Max(1, matchingCases.Length),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            card.Function.Reference,
            sourceHash,
            realization.ProjectPath,
            realization.TestProjectPath,
            RunnerIdentity,
            [],
            realization.Fingerprint,
            realization.TestAssemblyPath);
        return new(
            descriptor,
            buildDuration,
            discoveryDuration,
            [],
            kind,
            realization.Fingerprint,
            resolutionDuration,
            fingerprintDuration,
            materializationInvoked,
            discoveryInvoked);
    }

    private static OblivionFunctionDiscoveryResult FailedRealization(
        IReadOnlyList<OblivionCardDiagnostic> diagnostics,
        string fingerprint,
        TimeSpan buildDuration,
        TimeSpan discoveryDuration,
        TimeSpan resolutionDuration,
        TimeSpan fingerprintDuration,
        bool discoveryInvoked)
    {
        return new(
            null,
            buildDuration,
            discoveryDuration,
            diagnostics,
            OblivionFunctionRealizationKind.Cold,
            fingerprint,
            resolutionDuration,
            fingerprintDuration,
            MaterializationInvoked: true,
            DiscoveryInvoked: discoveryInvoked);
    }

    public OblivionFunctionExecutionResult Run(
        OblivionCard card,
        string workspaceRoot,
        OblivionFunctionTestDescriptor descriptor)
    {
        string resultIdentity = Guid.NewGuid().ToString("N");
        string resultDirectory = Path.Combine(
            Path.GetTempPath(),
            "Oblivion",
            "function-runs",
            resultIdentity);
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
                return ErrorResult(card, descriptor, diagnostic) with
                {
                    ResultIdentity = resultIdentity,
                    ExecutionInvoked = true,
                };
            }

            return ParseTrx(card, descriptor, trxPath, runnerClock.Elapsed, resultIdentity);
        }
        catch (Exception exception)
        {
            return ErrorResult(card, descriptor, Error(
                "OBLIVION-FUNCTION-TRX-INVALID",
                $"Structured xUnit result could not be read: {exception.Message}",
                trxPath)) with
            {
                ResultIdentity = resultIdentity,
                ExecutionInvoked = true,
            };
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
        TimeSpan runnerDuration,
        string resultIdentity)
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
                trxPath)) with
            {
                ResultIdentity = resultIdentity,
                ExecutionInvoked = true,
            };
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
            descriptor.Diagnostics,
            RealizationFingerprint: descriptor.RealizationFingerprint,
            ResultIdentity: resultIdentity,
            ExecutionInvoked: true);
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

    private static Resolution Resolve(OblivionCard card, string workspaceRoot)
    {
        List<OblivionCardDiagnostic> diagnostics = [];
        if (card.Kind != OblivionCardKind.Function || card.Function is null)
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-SOURCE-MISSING",
                $"Card '{card.Id.Value}' is not a Function Card with a semantic source.",
                card.Provenance.SourceReference));
            return new(null, null, diagnostics);
        }

        string root = Path.GetFullPath(workspaceRoot);
        string sourcePath = Path.GetFullPath(Path.Combine(root, card.Function.Reference));
        if (!IsInside(root, sourcePath))
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-SOURCE-UNSAFE",
                $"Function source '{card.Function.Reference}' escapes the workspace root.",
                sourcePath));
            return new(sourcePath, null, diagnostics);
        }
        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-SOURCE-NOT-FOUND",
                $"Function source '{card.Function.Reference}' was not found.",
                sourcePath));
            return new(sourcePath, null, diagnostics);
        }

        string? projectPath = FindOwningProject(sourcePath, root);
        if (projectPath is null)
        {
            diagnostics.Add(Error(
                "OBLIVION-FUNCTION-PROJECT-NOT-FOUND",
                $"No unique owning project was found for '{card.Function.Reference}'.",
                sourcePath));
        }
        return new(sourcePath, projectPath, diagnostics);
    }

    private static string ComputeRealizationFingerprint(string projectPath)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddFingerprintText(hash, RealizationSchemaIdentity);
        AddFingerprintText(hash, RunnerIdentity);
        HashProjectClosure(hash, Path.GetFullPath(projectPath), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void HashProjectClosure(
        IncrementalHash hash,
        string projectPath,
        HashSet<string> visitedProjects)
    {
        if (!visitedProjects.Add(projectPath) || !File.Exists(projectPath))
        {
            return;
        }

        AddFingerprintFile(hash, projectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        foreach (string input in EnumerateProjectInputs(projectDirectory))
        {
            AddFingerprintFile(hash, input);
        }

        XDocument project = XDocument.Load(projectPath);
        foreach (XElement reference in project.Descendants().Where(element =>
            element.Name.LocalName is "ProjectReference" or "Reference"))
        {
            string? declared = reference.Name.LocalName == "ProjectReference"
                ? (string?)reference.Attribute("Include")
                : reference.Elements().FirstOrDefault(element => element.Name.LocalName == "HintPath")?.Value;
            if (string.IsNullOrWhiteSpace(declared) || declared.Contains("$(", StringComparison.Ordinal))
            {
                continue;
            }

            string resolved = Path.GetFullPath(Path.Combine(projectDirectory, declared));
            if (reference.Name.LocalName == "ProjectReference")
            {
                HashProjectClosure(hash, resolved, visitedProjects);
            }
            else if (File.Exists(resolved))
            {
                AddFingerprintFile(hash, resolved);
            }
        }

        foreach (XElement item in project.Descendants().Where(element =>
            element.Name.LocalName is "Compile" or "CopelandCompile" or "CopelandTest"))
        {
            string? declared = (string?)item.Attribute("Include");
            if (string.IsNullOrWhiteSpace(declared) ||
                declared.Contains("$(", StringComparison.Ordinal) ||
                declared.IndexOfAny(['*', '?']) >= 0)
            {
                continue;
            }

            string resolved = Path.GetFullPath(Path.Combine(projectDirectory, declared));
            if (File.Exists(resolved))
            {
                AddFingerprintFile(hash, resolved);
            }
        }

        string packageLock = Path.Combine(projectDirectory, "packages.lock.json");
        if (File.Exists(packageLock))
        {
            AddFingerprintFile(hash, packageLock);
        }

        string? current = projectDirectory;
        while (current is not null)
        {
            foreach (string name in new[]
            {
                "Directory.Build.props",
                "Directory.Build.targets",
                "Directory.Packages.props",
                "NuGet.config",
                "global.json",
            })
            {
                string candidate = Path.Combine(current, name);
                if (File.Exists(candidate))
                {
                    AddFingerprintFile(hash, candidate);
                }
            }
            current = Directory.GetParent(current)?.FullName;
        }
    }

    private static IEnumerable<string> EnumerateProjectInputs(string projectDirectory)
    {
        string[] extensions = [".cs", ".ts", ".tsx", ".tsxtest", ".props", ".targets"];
        return Directory
            .EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(projectDirectory, path))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedPath(string projectDirectory, string path)
    {
        string relative = Path.GetRelativePath(projectDirectory, path);
        string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return string.Equals(first, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(first, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(first, ".git", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(first, "artifacts", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddFingerprintFile(IncrementalHash hash, string path)
    {
        AddFingerprintText(hash, Path.GetFullPath(path).Replace('\\', '/'));
        hash.AppendData(File.ReadAllBytes(path));
        hash.AppendData([0]);
    }

    private static void AddFingerprintText(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static bool OutputsAreValid(ProjectRealization realization)
    {
        return File.Exists(realization.TestProjectPath) &&
            File.Exists(realization.TestAssemblyPath) &&
            string.Equals(
                ComputeFileHash(realization.TestAssemblyPath),
                realization.TestAssemblyHash,
                StringComparison.Ordinal);
    }

    private static string? ResolveTestAssembly(string testProjectPath)
    {
        string projectName = Path.GetFileNameWithoutExtension(testProjectPath);
        string projectDirectory = Path.GetDirectoryName(testProjectPath)!;
        return Directory
            .EnumerateFiles(projectDirectory, projectName + ".dll", SearchOption.AllDirectories)
            .Where(path =>
            {
                string[] parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return parts.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
                    !parts.Contains("ref", StringComparer.OrdinalIgnoreCase) &&
                    !parts.Contains("refint", StringComparer.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string ComputeFileHash(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
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

    private sealed record Resolution(
        string? SourcePath,
        string? ProjectPath,
        IReadOnlyList<OblivionCardDiagnostic> Diagnostics)
    {
        public bool Succeeded => SourcePath is not null &&
            ProjectPath is not null &&
            Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
    }

    private sealed record ProjectRealization(
        string Fingerprint,
        string ProjectPath,
        string TestProjectPath,
        string TestAssemblyPath,
        string TestAssemblyHash,
        IReadOnlyList<string> DiscoveredTests);
}
