namespace Machina.Presenter.Sample.Playback;

public sealed class PresenterPlaybackSuiteRunner
{
    private readonly PresenterPlaybackRunner _scenarioRunner;

    public PresenterPlaybackSuiteRunner(PresenterPlaybackRunner? scenarioRunner = null)
    {
        _scenarioRunner = scenarioRunner ?? new PresenterPlaybackRunner();
    }

    public PresenterPlaybackSuiteResult RunSuitePath(string suitePath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suitePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        PresenterPlaybackSuiteDefinition suite = LoadSuiteDefinition(suitePath);
        return RunSuite(suite, outputDirectory);
    }

    public PresenterPlaybackSuiteResult RunSuite(PresenterPlaybackSuiteDefinition suite, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        List<PresenterPlaybackSuiteScenarioResult> scenarioResults = [];

        foreach (string scenarioPath in suite.ScenarioPaths)
        {
            scenarioResults.Add(RunScenario(scenarioPath, fullOutputDirectory));
        }

        bool starterIncluded = scenarioResults.Any(result => IsStarterScenarioPath(result.ScenarioPath));
        bool regressionIncluded = scenarioResults.Any(result => IsRegressionScenarioPath(result.ScenarioPath));
        bool starterPass = !starterIncluded || scenarioResults.Where(result => IsStarterScenarioPath(result.ScenarioPath)).All(result => result.Passed);
        bool regressionPass = !regressionIncluded || scenarioResults.Where(result => IsRegressionScenarioPath(result.ScenarioPath)).All(result => result.Passed);
        string validationStatus = scenarioResults.All(result => result.Passed)
            ? "passed"
            : "failed";

        (string reportJsonPath, string reportTextPath) = PresenterPlaybackOutputWriter.WriteSuiteReport(
            fullOutputDirectory,
            suite,
            fullOutputDirectory,
            scenarioResults,
            starterIncluded,
            regressionIncluded,
            validationStatus);
        (string manifestJsonPath, string manifestTextPath) = PresenterPlaybackOutputWriter.WriteRegressionSuiteManifest(
            fullOutputDirectory,
            suite,
            starterPass,
            regressionPass,
            scenarioResults,
            validationStatus);

        return new PresenterPlaybackSuiteResult(
            suite,
            fullOutputDirectory,
            reportJsonPath,
            reportTextPath,
            manifestJsonPath,
            manifestTextPath,
            scenarioResults,
            starterIncluded,
            regressionIncluded,
            starterPass,
            regressionPass,
            validationStatus);
    }

    private PresenterPlaybackSuiteScenarioResult RunScenario(string scenarioPath, string outputDirectory)
    {
        try
        {
            PresenterPlaybackScenario scenario = PresenterPlaybackTomlParser.LoadFile(scenarioPath);
            string finalPngPath = Path.Combine(outputDirectory, scenario.Id, "final.png");
            PresenterPlaybackRunResult runResult = _scenarioRunner.RunScenario(scenario, finalPngPath);
            PresenterPlaybackSuiteScenarioFailure[] failures = runResult.Trace.Assertions
                .Where(assertion => !assertion.Passed)
                .Select(assertion => new PresenterPlaybackSuiteScenarioFailure(
                    assertion.Index,
                    assertion.Type,
                    assertion.Reason,
                    assertion.FailureMessage ?? "Assertion failed."))
                .ToArray();

            return new PresenterPlaybackSuiteScenarioResult(
                scenario.Id,
                scenario.Name,
                scenarioPath,
                runResult.OutputDirectory,
                Passed: failures.Length == 0,
                Skipped: false,
                Failures: failures,
                RunResult: runResult,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            string scenarioId = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(scenarioPath));
            string outputPath = Path.Combine(outputDirectory, scenarioId);
            return new PresenterPlaybackSuiteScenarioResult(
                scenarioId,
                scenarioId,
                scenarioPath,
                outputPath,
                Passed: false,
                Skipped: false,
                Failures:
                [
                    new PresenterPlaybackSuiteScenarioFailure(
                        AssertionIndex: null,
                        AssertionType: null,
                        Reason: "Scenario execution failed before assertions could complete.",
                        Message: ex.Message),
                ],
                RunResult: null,
                ErrorMessage: ex.Message);
        }
    }

    private static PresenterPlaybackSuiteDefinition LoadSuiteDefinition(string suitePath)
    {
        string fullPath = Path.GetFullPath(suitePath);
        if (Directory.Exists(fullPath))
        {
            string[] scenarioPaths = Directory.GetFiles(fullPath, "*.machina-playback.toml", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return new PresenterPlaybackSuiteDefinition(
                SourcePath: fullPath,
                Id: BuildDirectorySuiteId(fullPath),
                Name: BuildDirectorySuiteName(fullPath),
                ScenarioPaths: scenarioPaths);
        }

        if (File.Exists(fullPath))
        {
            return PresenterPlaybackSuiteManifestTomlParser.LoadFile(fullPath);
        }

        throw new FileNotFoundException($"Playback suite path '{suitePath}' does not exist.", fullPath);
    }

    private static bool IsStarterScenarioPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}starter{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.AltDirectorySeparatorChar}starter{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRegressionScenarioPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}regressions{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.AltDirectorySeparatorChar}regressions{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDirectorySuiteId(string path)
    {
        string leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(leaf)
            ? "playback-suite"
            : leaf.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string BuildDirectorySuiteName(string path)
    {
        string leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(leaf)
            ? "Playback suite"
            : $"Playback suite: {leaf}";
    }
}
