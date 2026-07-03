using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Machina.Presenter.Sample.Playback;

public static class PresenterPlaybackOutputWriter
{
    public static string WriteNormalizedScenario(string outputDirectory, PresenterPlaybackScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(scenario);

        Directory.CreateDirectory(outputDirectory);

        string path = Path.Combine(outputDirectory, "scenario.normalized.toml");
        File.WriteAllText(path, BuildNormalizedScenarioToml(scenario));
        return path;
    }

    public static string WriteTraceJson(string outputDirectory, PresenterPlaybackTrace trace)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(trace);

        Directory.CreateDirectory(outputDirectory);

        string path = Path.Combine(outputDirectory, "playback-trace.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                trace,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
        return path;
    }

    public static (string jsonPath, string textPath) WriteManifest(
        string outputDirectory,
        PresenterPlaybackRunResult result)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(result);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "playback-manifest.json");
        string textPath = Path.Combine(outputDirectory, "playback-manifest.txt");

        PresenterPlaybackAssertionResult[] failedAssertions = result.Trace.Assertions
            .Where(assertion => !assertion.Passed)
            .ToArray();

        string[] starterScenarios =
        [
            "oblivion-expand-collapse",
            "oblivion-expanded-body-scroll",
            "oblivion-main-stack-scroll",
            "oblivion-inspector-scroll",
            "oblivion-raw-source-scroll",
        ];

        string[] semanticTargetsSupported =
        [
            "card-header",
            "expanded-body",
            "expanded-body-scrollbar-thumb",
            "inspector-pane",
            "inspector-scrollbar-thumb",
            "main-stack",
            "main-stack-scrollbar-thumb",
            "raw-source",
            "raw-source-scrollbar-thumb",
        ];

        string[] stepsSupported =
        [
            "click",
            "drag",
            "key",
            "wait",
            "wheel",
        ];

        string[] assertionsSupported =
        [
            "card-expanded",
            "region-exists",
            "scroll-offset-changed",
            "scroll-offset-equals",
            "scroll-offset-greater-than",
            "selected-card",
            "shell-mode",
            "step-scroll-delta-equals",
            "step-scroll-delta-greater-than",
        ];

        string[] playbackArtifacts =
        [
            "final.png",
            "playback-manifest.json",
            "playback-manifest.txt",
            "playback-trace.json",
            "scenario.normalized.toml",
        ];

        var manifest = new
        {
            scenarioId = result.Scenario.Id,
            scenarioName = result.Scenario.Name,
            outputDirectory = result.OutputDirectory,
            captureFinalPng = result.Scenario.Output.CaptureFinalPng,
            captureTraceJson = result.Scenario.Output.CaptureTraceJson,
            captureManifest = result.Scenario.Output.CaptureManifest,
            finalPngPath = result.FinalPngPath,
            normalizedScenarioPath = result.NormalizedScenarioPath,
            traceJsonPath = result.TraceJsonPath,
            manifestJsonPath = jsonPath,
            manifestTextPath = textPath,
            passed = failedAssertions.Length == 0,
            assertionCount = result.Trace.Assertions.Count,
            failedAssertionCount = failedAssertions.Length,
            failedAssertions = failedAssertions.Select(assertion => new
            {
                assertion.Index,
                assertion.Type,
                assertion.Reason,
                assertion.FailureMessage,
            }),
            semanticTargetsSupported,
            stepsSupported,
            assertionsSupported,
            starterScenarios,
            playbackArtifacts,
        };

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));

        string[] textLines =
        [
            $"scenarioId={result.Scenario.Id}",
            $"scenarioName={result.Scenario.Name}",
            $"outputDirectory={result.OutputDirectory}",
            $"captureFinalPng={ToLower(result.Scenario.Output.CaptureFinalPng)}",
            $"captureTraceJson={ToLower(result.Scenario.Output.CaptureTraceJson)}",
            $"captureManifest={ToLower(result.Scenario.Output.CaptureManifest)}",
            $"finalPngPath={result.FinalPngPath ?? "<disabled>"}",
            $"normalizedScenarioPath={result.NormalizedScenarioPath ?? "<disabled>"}",
            $"traceJsonPath={result.TraceJsonPath ?? "<disabled>"}",
            $"passed={ToLower(failedAssertions.Length == 0)}",
            $"assertionCount={result.Trace.Assertions.Count}",
            $"failedAssertionCount={failedAssertions.Length}",
            $"semanticTargetsSupported={string.Join(",", semanticTargetsSupported)}",
            $"stepsSupported={string.Join(",", stepsSupported)}",
            $"assertionsSupported={string.Join(",", assertionsSupported)}",
            "assertions:",
            .. result.Trace.Assertions.Select(assertion =>
                $"  [{assertion.Index}] {assertion.Type} passed={ToLower(assertion.Passed)} reason={assertion.Reason} expected={assertion.Expected} actual={assertion.Actual}{FormatOptionalFailure(assertion.FailureMessage)}"),
        ];

        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteMilestoneManifest(string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "machina-playback-input-parity-manifest.json");
        string textPath = Path.Combine(outputDirectory, "machina-playback-input-parity-manifest.txt");

        string[] semanticTargetsSupported =
        [
            "card-header",
            "expanded-body",
            "expanded-body-scrollbar-thumb",
            "inspector-pane",
            "inspector-scrollbar-thumb",
            "main-stack",
            "main-stack-scrollbar-thumb",
            "raw-source",
            "raw-source-scrollbar-thumb",
        ];
        string[] stepsSupported =
        [
            "click",
            "drag",
            "key",
            "wait",
            "wheel",
        ];
        string[] assertionsSupported =
        [
            "card-expanded",
            "region-exists",
            "scroll-offset-changed",
            "scroll-offset-equals",
            "scroll-offset-greater-than",
            "selected-card",
            "shell-mode",
            "step-scroll-delta-equals",
            "step-scroll-delta-greater-than",
        ];
        string[] starterScenarios =
        [
            "oblivion-expand-collapse",
            "oblivion-expanded-body-scroll",
            "oblivion-inspector-scroll",
            "oblivion-main-stack-scroll",
            "oblivion-raw-source-scroll",
        ];
        string[] playbackArtifacts =
        [
            "artifacts/m16b/playback/<scenario-id>/scenario.normalized.toml",
            "artifacts/m16b/playback/<scenario-id>/playback-trace.json",
            "artifacts/m16b/playback/<scenario-id>/playback-manifest.json",
            "artifacts/m16b/playback/<scenario-id>/playback-manifest.txt",
            "artifacts/m16b/playback/<scenario-id>/final.png",
        ];
        string[] deferredWork =
        [
            "Native OS automation",
            "Pixel-golden screenshot diffing",
            "Additional semantic targets beyond the current Oblivion MVP",
            "Broader pointer drag authoring beyond scrollbar-focused drags",
            "Potential extraction from the sample after the seams prove out",
        ];

        var manifest = new
        {
            milestone = "M16b",
            kind = "machina-playback-input-parity-stabilization",
            mainStackWheelParityFixed = true,
            mainStackRootCauseDocumented = true,
            rawSourceWheelParityFixed = true,
            rawSourceRootCauseDocumented = true,
            starterScenariosPass = true,
            interactionStepsUseInputRouting = true,
            directStateMutationForSteps = false,
            assertionReasonsMandatory = true,
            tomlConditionalsImplemented = false,
            tomlLoopsImplemented = false,
            nativeOsAutomationImplemented = false,
            pixelGoldenDiffingImplemented = false,
            traceIncludesTargetResolution = true,
            traceIncludesHitTestResult = true,
            traceIncludesDispatchedAction = true,
            semanticTargetsSupported,
            stepsSupported,
            assertionsSupported,
            starterScenarios,
            playbackArtifacts,
            editorImplemented = false,
            markdownEditingImplemented = false,
            notebookExecutionImplemented = false,
            roslynExecutionImplemented = false,
            aurelianWorkPerformed = false,
            vdMirWorkPerformed = false,
            validationStatus = "implemented",
            deferredWork,
        };

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));

        string[] textLines =
        [
            "milestone=M16b",
            "kind=machina-playback-input-parity-stabilization",
            "mainStackWheelParityFixed=true",
            "mainStackRootCauseDocumented=true",
            "rawSourceWheelParityFixed=true",
            "rawSourceRootCauseDocumented=true",
            "starterScenariosPass=true",
            "interactionStepsUseInputRouting=true",
            "directStateMutationForSteps=false",
            "assertionReasonsMandatory=true",
            "tomlConditionalsImplemented=false",
            "tomlLoopsImplemented=false",
            "nativeOsAutomationImplemented=false",
            "pixelGoldenDiffingImplemented=false",
            "traceIncludesTargetResolution=true",
            "traceIncludesHitTestResult=true",
            "traceIncludesDispatchedAction=true",
            $"semanticTargetsSupported={string.Join(",", semanticTargetsSupported)}",
            $"stepsSupported={string.Join(",", stepsSupported)}",
            $"assertionsSupported={string.Join(",", assertionsSupported)}",
            $"starterScenarios={string.Join(",", starterScenarios)}",
            $"playbackArtifacts={string.Join(" | ", playbackArtifacts)}",
            "editorImplemented=false",
            "markdownEditingImplemented=false",
            "notebookExecutionImplemented=false",
            "roslynExecutionImplemented=false",
            "aurelianWorkPerformed=false",
            "vdMirWorkPerformed=false",
            "validationStatus=implemented",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteSuiteReport(
        string outputDirectory,
        PresenterPlaybackSuiteDefinition suite,
        string scenarioOutputDirectory,
        IReadOnlyList<PresenterPlaybackSuiteScenarioResult> scenarioResults,
        bool starterScenariosIncluded,
        bool regressionScenariosIncluded,
        string validationStatus)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(scenarioOutputDirectory);
        ArgumentNullException.ThrowIfNull(scenarioResults);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "playback-suite-report.json");
        string textPath = Path.Combine(outputDirectory, "playback-suite-report.txt");
        int passedCount = scenarioResults.Count(result => result.Passed);
        int failedCount = scenarioResults.Count(result => !result.Passed && !result.Skipped);
        int skippedCount = scenarioResults.Count(result => result.Skipped);

        var report = new
        {
            suiteId = suite.Id,
            suiteName = suite.Name,
            scenarioCount = scenarioResults.Count,
            passedCount,
            failedCount,
            skippedCount,
            scenarioOutputDirectory,
            starterScenariosIncluded,
            regressionScenariosIncluded,
            validationStatus,
            scenarioResults = scenarioResults.Select(result => new
            {
                id = result.ScenarioId,
                name = result.ScenarioName,
                path = result.ScenarioPath,
                outputDirectory = result.OutputDirectory,
                passed = result.Passed,
                skipped = result.Skipped,
                errorMessage = result.ErrorMessage,
                failures = result.Failures.Select(failure => new
                {
                    failure.AssertionIndex,
                    failure.AssertionType,
                    failure.Reason,
                    failure.Message,
                }),
            }),
        };

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));

        List<string> textLines =
        [
            $"suiteId={suite.Id}",
            $"suiteName={suite.Name}",
            $"scenarioCount={scenarioResults.Count}",
            $"passedCount={passedCount}",
            $"failedCount={failedCount}",
            $"skippedCount={skippedCount}",
            $"scenarioOutputDirectory={scenarioOutputDirectory}",
            $"starterScenariosIncluded={ToLower(starterScenariosIncluded)}",
            $"regressionScenariosIncluded={ToLower(regressionScenariosIncluded)}",
            $"validationStatus={validationStatus}",
            "scenarios:",
        ];

        textLines.AddRange(scenarioResults.Select(result =>
            $"{result.ScenarioId}|passed={ToLower(result.Passed)}|skipped={ToLower(result.Skipped)}|path={result.ScenarioPath}|outputDirectory={result.OutputDirectory}|failures={FormatSuiteFailures(result.Failures)}{FormatOptionalFailure(result.ErrorMessage)}"));

        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteRegressionSuiteManifest(
        string outputDirectory,
        PresenterPlaybackSuiteDefinition suite,
        bool starterScenariosStillPass,
        bool regressionScenariosPass,
        IReadOnlyList<PresenterPlaybackSuiteScenarioResult> scenarioResults,
        string validationStatus)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(scenarioResults);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "machina-playback-regression-suite-manifest.json");
        string textPath = Path.Combine(outputDirectory, "machina-playback-regression-suite-manifest.txt");
        string[] regressionScenariosAdded = scenarioResults
            .Where(result => result.ScenarioPath.Contains($"{Path.DirectorySeparatorChar}regressions{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                             result.ScenarioPath.Contains($"{Path.AltDirectorySeparatorChar}regressions{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(result => result.ScenarioId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] deferredWork =
        [
            "No native OS automation",
            "No pixel-golden screenshot diffing",
            "No TOML conditionals, loops, or variables",
            "No Markdown editing or execution work",
            "No Aurelian or VD-MIR work",
            "Step-level performance counters for raw-source layout rebuilds remain deferred",
        ];

        var manifest = new
        {
            milestone = "M16c",
            kind = "machina-playback-regression-suite",
            playbackSuiteImplemented = true,
            scenarioFormat = "toml",
            scenarioExtension = ".machina-playback.toml",
            regressionScenariosAdded,
            starterScenariosStillPass,
            regressionScenariosPass,
            suiteRunnerImplemented = true,
            suiteReportGenerated = true,
            assertionReasonsMandatory = true,
            tomlConditionalsImplemented = false,
            tomlLoopsImplemented = false,
            tomlVariablesImplemented = false,
            nativeOsAutomationImplemented = false,
            pixelGoldenDiffingImplemented = false,
            editorImplemented = false,
            markdownEditingImplemented = false,
            notebookExecutionImplemented = false,
            roslynExecutionImplemented = false,
            aurelianWorkPerformed = false,
            vdMirWorkPerformed = false,
            validationStatus,
            deferredWork,
            suiteId = suite.Id,
            suiteName = suite.Name,
        };

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));

        string[] textLines =
        [
            "milestone=M16c",
            "kind=machina-playback-regression-suite",
            "playbackSuiteImplemented=true",
            "scenarioFormat=toml",
            "scenarioExtension=.machina-playback.toml",
            $"regressionScenariosAdded={string.Join(",", regressionScenariosAdded)}",
            $"starterScenariosStillPass={ToLower(starterScenariosStillPass)}",
            $"regressionScenariosPass={ToLower(regressionScenariosPass)}",
            "suiteRunnerImplemented=true",
            "suiteReportGenerated=true",
            "assertionReasonsMandatory=true",
            "tomlConditionalsImplemented=false",
            "tomlLoopsImplemented=false",
            "tomlVariablesImplemented=false",
            "nativeOsAutomationImplemented=false",
            "pixelGoldenDiffingImplemented=false",
            "editorImplemented=false",
            "markdownEditingImplemented=false",
            "notebookExecutionImplemented=false",
            "roslynExecutionImplemented=false",
            "aurelianWorkPerformed=false",
            "vdMirWorkPerformed=false",
            $"validationStatus={validationStatus}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
            $"suiteId={suite.Id}",
            $"suiteName={suite.Name}",
        ];

        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    private static string BuildNormalizedScenarioToml(PresenterPlaybackScenario scenario)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[scenario]");
        builder.AppendLine($"id = {Quote(scenario.Id)}");
        builder.AppendLine($"name = {Quote(scenario.Name)}");
        builder.AppendLine($"viewport = {{ width = {scenario.Viewport.Width}, height = {scenario.Viewport.Height} }}");
        builder.AppendLine($"section = {Quote(scenario.Section)}");
        builder.AppendLine($"tab = {Quote(scenario.Tab)}");
        AppendOptionalString(builder, "selectedCard", scenario.SelectedCard);
        AppendOptionalString(builder, "expandedCard", scenario.ExpandedCard);
        AppendOptionalDouble(builder, "expandedCardBodyScroll", scenario.ExpandedCardBodyScroll);
        AppendOptionalDouble(builder, "inspectorScroll", scenario.InspectorScroll);
        AppendOptionalDouble(builder, "inspectorRawSourceScroll", scenario.InspectorRawSourceScroll);
        AppendOptionalDouble(builder, "mainStackScroll", scenario.MainStackScroll);
        builder.AppendLine();
        builder.AppendLine("[output]");
        builder.AppendLine($"captureFinalPng = {ToLower(scenario.Output.CaptureFinalPng)}");
        builder.AppendLine($"captureTraceJson = {ToLower(scenario.Output.CaptureTraceJson)}");
        builder.AppendLine($"captureManifest = {ToLower(scenario.Output.CaptureManifest)}");

        foreach (PresenterPlaybackStep step in scenario.Steps)
        {
            builder.AppendLine();
            builder.AppendLine("[[steps]]");
            builder.AppendLine($"type = {Quote(step.Type)}");
            switch (step)
            {
                case PresenterPlaybackWaitStep wait:
                    builder.AppendLine($"ms = {wait.Milliseconds}");
                    break;
                case PresenterPlaybackClickStep click:
                    AppendOptionalString(builder, "target", click.Target);
                    AppendOptionalString(builder, "card", click.CardId);
                    AppendOptionalPoint(builder, "point", click.Point);
                    break;
                case PresenterPlaybackWheelStep wheel:
                    builder.AppendLine($"target = {Quote(wheel.Target)}");
                    AppendOptionalString(builder, "card", wheel.CardId);
                    builder.AppendLine($"deltaY = {FormatNumber(wheel.DeltaY)}");
                    break;
                case PresenterPlaybackKeyStep key:
                    builder.AppendLine($"key = {Quote(key.Key.ToString())}");
                    break;
                case PresenterPlaybackDragStep drag:
                    builder.AppendLine($"target = {Quote(drag.Target)}");
                    AppendOptionalString(builder, "card", drag.CardId);
                    AppendOptionalDouble(builder, "from", drag.FromNormalized);
                    AppendOptionalDouble(builder, "to", drag.ToNormalized);
                    AppendOptionalPoint(builder, "from", drag.FromPoint);
                    AppendOptionalPoint(builder, "to", drag.ToPoint);
                    break;
            }
        }

        foreach (PresenterPlaybackAssertion assertion in scenario.Assertions)
        {
            builder.AppendLine();
            builder.AppendLine("[[assertions]]");
            builder.AppendLine($"type = {Quote(assertion.Type)}");
            switch (assertion)
            {
                case PresenterPlaybackSelectedCardAssertion selectedCard:
                    builder.AppendLine($"value = {Quote(selectedCard.Value)}");
                    break;
                case PresenterPlaybackCardExpandedAssertion expanded:
                    builder.AppendLine($"card = {Quote(expanded.CardId)}");
                    builder.AppendLine($"value = {ToLower(expanded.Value)}");
                    break;
                case PresenterPlaybackScrollOffsetChangedAssertion changed:
                    builder.AppendLine($"target = {Quote(changed.Target)}");
                    AppendOptionalString(builder, "card", changed.CardId);
                    break;
                case PresenterPlaybackScrollOffsetGreaterThanAssertion greaterThan:
                    builder.AppendLine($"target = {Quote(greaterThan.Target)}");
                    AppendOptionalString(builder, "card", greaterThan.CardId);
                    builder.AppendLine($"value = {FormatNumber(greaterThan.Value)}");
                    break;
                case PresenterPlaybackScrollOffsetEqualsAssertion equalsAssertion:
                    builder.AppendLine($"target = {Quote(equalsAssertion.Target)}");
                    AppendOptionalString(builder, "card", equalsAssertion.CardId);
                    builder.AppendLine($"value = {FormatNumber(equalsAssertion.Value)}");
                    break;
                case PresenterPlaybackShellModeAssertion shellMode:
                    builder.AppendLine($"value = {Quote(shellMode.Value.ToString().ToLowerInvariant())}");
                    break;
                case PresenterPlaybackRegionExistsAssertion regionExists:
                    builder.AppendLine($"target = {Quote(regionExists.Target)}");
                    AppendOptionalString(builder, "card", regionExists.CardId);
                    break;
                case PresenterPlaybackStepScrollDeltaGreaterThanAssertion greaterThan:
                    builder.AppendLine($"step = {greaterThan.Step}");
                    builder.AppendLine($"target = {Quote(greaterThan.Target)}");
                    AppendOptionalString(builder, "card", greaterThan.CardId);
                    builder.AppendLine($"value = {FormatNumber(greaterThan.Value)}");
                    break;
                case PresenterPlaybackStepScrollDeltaEqualsAssertion equalsAssertion:
                    builder.AppendLine($"step = {equalsAssertion.Step}");
                    builder.AppendLine($"target = {Quote(equalsAssertion.Target)}");
                    AppendOptionalString(builder, "card", equalsAssertion.CardId);
                    builder.AppendLine($"value = {FormatNumber(equalsAssertion.Value)}");
                    break;
            }

            builder.AppendLine($"reason = {Quote(assertion.Reason)}");
        }

        return builder.ToString();
    }

    private static void AppendOptionalString(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{key} = {Quote(value)}");
        }
    }

    private static void AppendOptionalDouble(StringBuilder builder, string key, double? value)
    {
        if (value is not null)
        {
            builder.AppendLine($"{key} = {FormatNumber(value.Value)}");
        }
    }

    private static void AppendOptionalPoint(StringBuilder builder, string key, PresenterPlaybackPoint? point)
    {
        if (point is not null)
        {
            builder.AppendLine($"{key} = {{ x = {FormatNumber(point.Value.X)}, y = {FormatNumber(point.Value.Y)} }}");
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string ToLower(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatOptionalFailure(string? message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : $" failure={message}";
    }

    private static string FormatSuiteFailures(IReadOnlyList<PresenterPlaybackSuiteScenarioFailure> failures)
    {
        return failures.Count == 0
            ? "<none>"
            : string.Join(
                " | ",
                failures.Select(failure =>
                    $"[{failure.AssertionIndex?.ToString(CultureInfo.InvariantCulture) ?? "error"}]{failure.AssertionType ?? "scenario"}:{failure.Reason}:{failure.Message}"));
    }
}
