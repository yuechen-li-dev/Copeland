using Machina.Presenter.Sample.Playback;
using Xunit.Sdk;

namespace Machina.Presenter.Sample.Tests;

internal static class PlaybackScenarioXunitRunner
{
    public static PlaybackXunitRunResult RunScenario(PlaybackScenarioFile scenarioFile, string testName)
    {
        ArgumentNullException.ThrowIfNull(scenarioFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);

        string artifactDirectory = GetArtifactDirectory(scenarioFile.SuiteName, scenarioFile.ScenarioId);
        ResetArtifactDirectory(artifactDirectory);

        PresenterPlaybackScenario effectiveScenario = scenarioFile.Scenario with
        {
            Output = new PresenterPlaybackOutputOptions(
                CaptureFinalPng: true,
                CaptureTraceJson: true,
                CaptureManifest: true),
        };

        string finalPngPath = Path.Combine(artifactDirectory, "final.png");

        try
        {
            PresenterPlaybackRunResult runResult = PlaybackTestEnvironment.CreateRunner().RunScenario(
                effectiveScenario,
                finalPngPath);

            PresenterPlaybackAssertionResult? failedAssertion = runResult.Trace.Assertions
                .FirstOrDefault(assertion => !assertion.Passed);

            if (failedAssertion is null)
            {
                return new PlaybackXunitRunResult(
                    scenarioFile,
                    artifactDirectory,
                    runResult,
                    FailedAssertion: null,
                    FailureSummaryPath: null,
                    FailureMessage: null);
            }

            string failureMessage = BuildAssertionFailureMessage(scenarioFile, runResult, failedAssertion, testName);
            string failureSummaryPath = WriteFailureSummary(
                scenarioFile,
                artifactDirectory,
                runResult,
                failedAssertion,
                failureMessage,
                testName);

            return new PlaybackXunitRunResult(
                scenarioFile,
                artifactDirectory,
                runResult,
                failedAssertion,
                failureSummaryPath,
                failureMessage);
        }
        catch (Exception ex)
        {
            string failureMessage = BuildExecutionFailureMessage(scenarioFile, artifactDirectory, ex, testName);
            string failureSummaryPath = PresenterPlaybackOutputWriter.WriteFailureSummary(
                artifactDirectory,
                [
                    $"scenarioId={scenarioFile.ScenarioId}",
                    $"scenarioPath={scenarioFile.ScenarioPath}",
                    "failedAssertionType=<execution-error>",
                    "assertionReason=Scenario execution failed before assertions completed.",
                    "expected=<scenario executes>",
                    $"actual={ex.Message}",
                    $"tracePath={Path.Combine(artifactDirectory, "playback-trace.json")}",
                    $"finalPngPath={Path.Combine(artifactDirectory, "final.png")}",
                    $"rootCauseHint={ex.Message}",
                    $"testName={testName}",
                ]);

            return new PlaybackXunitRunResult(
                scenarioFile,
                artifactDirectory,
                RunResult: null,
                FailedAssertion: null,
                failureSummaryPath,
                failureMessage);
        }
    }

    public static void AssertScenarioPasses(PlaybackScenarioFile scenarioFile, string testName)
    {
        PlaybackXunitRunResult result = RunScenario(scenarioFile, testName);

        if (!string.IsNullOrWhiteSpace(result.FailureMessage))
        {
            throw new XunitException(result.FailureMessage);
        }
    }

    public static void AssertSuitePasses(
        string suiteName,
        IReadOnlyList<PlaybackScenarioFile> scenarios,
        string testName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suiteName);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);

        List<PlaybackXunitRunResult> failures = [];

        foreach (PlaybackScenarioFile scenario in scenarios)
        {
            PlaybackXunitRunResult result = RunScenario(scenario, testName);
            if (!string.IsNullOrWhiteSpace(result.FailureMessage))
            {
                failures.Add(result);
            }
        }

        if (failures.Count == 0)
        {
            return;
        }

        List<string> lines =
        [
            $"Playback xUnit suite failed: {suiteName}",
            $"Test: {testName}",
            $"FailureCount: {failures.Count}",
        ];

        foreach (PlaybackXunitRunResult failure in failures)
        {
            lines.Add(string.Empty);
            lines.Add(failure.FailureMessage ?? $"Scenario '{failure.ScenarioFile.ScenarioId}' failed.");
        }

        throw new XunitException(string.Join(Environment.NewLine, lines));
    }

    public static string GetArtifactDirectory(string suiteName, string scenarioId)
    {
        return Path.Combine(
            PlaybackTestEnvironment.GetM16dXunitPlaybackRoot(),
            suiteName,
            scenarioId);
    }

    private static string BuildAssertionFailureMessage(
        PlaybackScenarioFile scenarioFile,
        PresenterPlaybackRunResult runResult,
        PresenterPlaybackAssertionResult failedAssertion,
        string testName)
    {
        List<string> lines =
        [
            $"Playback scenario failed: {scenarioFile.ScenarioId}",
            $"Assertion {failedAssertion.Index + 1} failed: {failedAssertion.Type}",
        ];

        int? stepIndex = TryGetStepIndex(runResult.Scenario.Assertions[failedAssertion.Index]);
        if (stepIndex is not null)
        {
            lines.Add($"Step: {stepIndex.Value}");
        }

        lines.Add($"Reason: {failedAssertion.Reason}");
        lines.Add($"Expected: {failedAssertion.Expected}");
        lines.Add($"Actual: {failedAssertion.Actual}");

        if (!string.IsNullOrWhiteSpace(failedAssertion.FailureMessage))
        {
            lines.Add($"Hint: {failedAssertion.FailureMessage}");
        }

        lines.Add($"Artifacts: {runResult.OutputDirectory}");
        lines.Add($"Test: {testName}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string WriteFailureSummary(
        PlaybackScenarioFile scenarioFile,
        string artifactDirectory,
        PresenterPlaybackRunResult runResult,
        PresenterPlaybackAssertionResult failedAssertion,
        string failureMessage,
        string testName)
    {
        string rootCauseHint = failedAssertion.FailureMessage ?? "<none>";

        return PresenterPlaybackOutputWriter.WriteFailureSummary(
            artifactDirectory,
            [
                $"scenarioId={scenarioFile.ScenarioId}",
                $"scenarioPath={scenarioFile.ScenarioPath}",
                $"failedAssertionType={failedAssertion.Type}",
                $"assertionReason={failedAssertion.Reason}",
                $"expected={failedAssertion.Expected}",
                $"actual={failedAssertion.Actual}",
                $"tracePath={runResult.TraceJsonPath ?? Path.Combine(artifactDirectory, "playback-trace.json")}",
                $"finalPngPath={runResult.FinalPngPath ?? Path.Combine(artifactDirectory, "final.png")}",
                $"rootCauseHint={rootCauseHint}",
                $"testName={testName}",
                string.Empty,
                failureMessage,
            ]);
    }

    private static string BuildExecutionFailureMessage(
        PlaybackScenarioFile scenarioFile,
        string artifactDirectory,
        Exception ex,
        string testName)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Playback scenario failed: {scenarioFile.ScenarioId}",
                "Assertion <execution-error> failed: scenario-execution",
                "Reason: Scenario execution failed before assertions completed.",
                "Expected: scenario executes and produces playback artifacts",
                $"Actual: {ex.Message}",
                $"Artifacts: {artifactDirectory}",
                $"Test: {testName}",
            ]);
    }

    private static void ResetArtifactDirectory(string artifactDirectory)
    {
        if (Directory.Exists(artifactDirectory))
        {
            Directory.Delete(artifactDirectory, recursive: true);
        }

        Directory.CreateDirectory(artifactDirectory);
    }

    private static int? TryGetStepIndex(PresenterPlaybackAssertion assertion)
    {
        return assertion switch
        {
            PresenterPlaybackStepScrollDeltaEqualsAssertion equalsAssertion => equalsAssertion.Step,
            PresenterPlaybackStepScrollDeltaGreaterThanAssertion greaterThanAssertion => greaterThanAssertion.Step,
            _ => null,
        };
    }
}

internal sealed record PlaybackXunitRunResult(
    PlaybackScenarioFile ScenarioFile,
    string ArtifactDirectory,
    PresenterPlaybackRunResult? RunResult,
    PresenterPlaybackAssertionResult? FailedAssertion,
    string? FailureSummaryPath,
    string? FailureMessage);
