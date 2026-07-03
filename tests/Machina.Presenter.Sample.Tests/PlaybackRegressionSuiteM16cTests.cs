using System.Text.Json;
using Machina.Presenter.Sample.Playback;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PlaybackRegressionSuiteM16cTests
{
    private static readonly Lazy<string> MilestoneManifestJsonPath = new(CreateMilestoneManifestJsonPath);

    private static readonly string[] ForbiddenProgrammingKeys =
    [
        "if",
        "then",
        "else",
        "loop",
        "while",
        "until",
        "for",
        "repeat",
        "script",
        "eval",
        "expr",
        "condition",
        "callback",
    ];

    [Fact]
    public void PlaybackRegressionScenarios_Exist()
    {
        Assert.NotEmpty(Directory.GetFiles(PlaybackTestEnvironment.GetStarterScenarioDirectory(), "*.machina-playback.toml", SearchOption.TopDirectoryOnly));
        Assert.NotEmpty(Directory.GetFiles(PlaybackTestEnvironment.GetRegressionScenarioDirectory(), "*.machina-playback.toml", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void PlaybackRegressionScenarios_HaveUniqueIds()
    {
        PresenterPlaybackScenario[] scenarios = LoadAllScenarios();
        string[] duplicateIds = scenarios
            .GroupBy(scenario => scenario.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicateIds);
    }

    [Fact]
    public void PlaybackRegressionScenarios_AllAssertionsHaveReasons()
    {
        Assert.All(
            LoadAllScenarios(),
            scenario => Assert.All(
                scenario.Assertions,
                assertion => Assert.False(string.IsNullOrWhiteSpace(assertion.Reason), scenario.Id)));
    }

    [Fact]
    public void PlaybackRegressionScenarios_DoNotUseProgrammingKeys()
    {
        foreach (string path in Directory.GetFiles(PlaybackTestEnvironment.GetScenariosRoot(), "*.machina-playback.toml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            Assert.All(
                ForbiddenProgrammingKeys,
                forbidden => Assert.DoesNotContain($"{forbidden} =", text, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void PlaybackSuiteRunner_RunsScenarioDirectory()
    {
        string outputDirectory = PlaybackTestEnvironment.CreateOutputDirectory("suite-directory");
        PresenterPlaybackSuiteResult result = CreateSuiteRunner().RunSuitePath(
            PlaybackTestEnvironment.GetRegressionScenarioDirectory(),
            outputDirectory);

        Assert.NotEmpty(result.ScenarioResults);
        Assert.All(result.ScenarioResults, scenario => Assert.NotNull(scenario.OutputDirectory));
    }

    [Fact]
    public void PlaybackSuiteRunner_WritesAggregateJsonReport()
    {
        string outputDirectory = PlaybackTestEnvironment.CreateOutputDirectory("suite-json-report");
        PresenterPlaybackSuiteResult result = CreateSuiteRunner().RunSuitePath(
            PlaybackTestEnvironment.GetRegressionScenarioDirectory(),
            outputDirectory);

        Assert.True(File.Exists(result.ReportJsonPath));
    }

    [Fact]
    public void PlaybackSuiteRunner_WritesAggregateTextReport()
    {
        string outputDirectory = PlaybackTestEnvironment.CreateOutputDirectory("suite-text-report");
        PresenterPlaybackSuiteResult result = CreateSuiteRunner().RunSuitePath(
            PlaybackTestEnvironment.GetRegressionScenarioDirectory(),
            outputDirectory);

        Assert.True(File.Exists(result.ReportTextPath));
    }

    [Fact]
    public void PlaybackSuiteReport_RecordsPassedAndFailedCounts()
    {
        string scenarioDirectory = CreateTemporarySuiteDirectory(
            """
            [scenario]
            id = "pass"
            name = "Pass"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [output]
            captureFinalPng = false
            captureTraceJson = false
            captureManifest = false

            [[steps]]
            type = "wait"
            ms = 0

            [[assertions]]
            type = "shell-mode"
            value = "wide"
            reason = "Wide docs should stay wide."
            """,
            """
            [scenario]
            id = "fail"
            name = "Fail"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [output]
            captureFinalPng = false
            captureTraceJson = false
            captureManifest = false

            [[steps]]
            type = "wait"
            ms = 0

            [[assertions]]
            type = "shell-mode"
            value = "compact"
            reason = "This synthetic failure proves suite counts."
            """);

        string outputDirectory = PlaybackTestEnvironment.CreateOutputDirectory("suite-counts");
        PresenterPlaybackSuiteResult result = CreateSuiteRunner().RunSuitePath(scenarioDirectory, outputDirectory);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(result.ReportJsonPath));

        Assert.Equal(2, report.RootElement.GetProperty("scenarioCount").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("passedCount").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("failedCount").GetInt32());
    }

    [Fact]
    public void PlaybackSuiteReport_RecordsAssertionReasonsOnFailure()
    {
        string scenarioDirectory = CreateTemporarySuiteDirectory(
            """
            [scenario]
            id = "fail"
            name = "Fail"
            viewport = { width = 1280, height = 720 }
            section = "oblivion"
            tab = "docs"

            [output]
            captureFinalPng = false
            captureTraceJson = false
            captureManifest = false

            [[steps]]
            type = "wait"
            ms = 0

            [[assertions]]
            type = "shell-mode"
            value = "compact"
            reason = "This synthetic failure proves assertion reasons survive suite reporting."
            """);

        string outputDirectory = PlaybackTestEnvironment.CreateOutputDirectory("suite-failure-reasons");
        PresenterPlaybackSuiteResult result = CreateSuiteRunner().RunSuitePath(scenarioDirectory, outputDirectory);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(result.ReportJsonPath));

        JsonElement failure = report.RootElement.GetProperty("scenarioResults")[0].GetProperty("failures")[0];
        Assert.Equal(
            "This synthetic failure proves assertion reasons survive suite reporting.",
            failure.GetProperty("Reason").GetString());
    }

    [Fact]
    public void PlaybackRegression_M15bResizeWideCompact_SplitCoveragePasses()
    {
        AssertScenarioPasses("m15b-wide-shell-mode.machina-playback.toml");
        AssertScenarioPasses("m15b-compact-shell-mode.machina-playback.toml");
    }

    [Fact]
    public void PlaybackRegression_M15cExpandCollapseSelection_Passes()
    {
        AssertScenarioPasses("m15c-expand-collapse-selection.machina-playback.toml");
    }

    [Fact]
    public void PlaybackRegression_M15dExpandedReadingSurface_Passes()
    {
        AssertScenarioPasses("m15d-expanded-reading-surface.machina-playback.toml");
    }

    [Fact]
    public void PlaybackRegression_M15eIndependentScrollPanes_Passes()
    {
        AssertScenarioPasses("m15e-independent-scroll-panes.machina-playback.toml");
    }

    [Fact]
    public void PlaybackRegression_M15ePartialViewportCulling_Passes()
    {
        AssertScenarioPasses("m15e-partial-viewport-culling.machina-playback.toml");
    }

    [Fact]
    public void PlaybackRegression_M15fMainStackScroll_Passes()
    {
        AssertScenarioPasses("m15f-main-stack-scroll-regression.machina-playback.toml");
    }

    [Fact]
    public void PlaybackRegression_M15fInspectorRawSourceLagGuard_Passes()
    {
        AssertScenarioPasses("m15f-inspector-raw-source-lag-guard.machina-playback.toml");
    }

    [Fact]
    public void PlaybackRegression_M16bRawSourceRouting_Passes()
    {
        AssertScenarioPasses("m16b-raw-source-routing-regression.machina-playback.toml");
    }

    [Fact]
    public void PlaybackStarterScenarios_AllPass()
    {
        string[] starterFiles = Directory.GetFiles(
            PlaybackTestEnvironment.GetStarterScenarioDirectory(),
            "*.machina-playback.toml",
            SearchOption.TopDirectoryOnly);

        Assert.All(starterFiles, path => AssertScenarioPasses(Path.GetFileName(path)));
    }

    [Fact]
    public void M16c_DoesNotImplementNativeOsAutomation()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("nativeOsAutomationImplemented").GetBoolean());
    }

    [Fact]
    public void M16c_DoesNotImplementPixelGoldenDiffing()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("pixelGoldenDiffingImplemented").GetBoolean());
    }

    [Fact]
    public void M16c_DoesNotImplementTomlLoopsOrConditionals()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("tomlConditionalsImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("tomlLoopsImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("tomlVariablesImplemented").GetBoolean());
    }

    [Fact]
    public void M16c_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M16c_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("roslynExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M16c_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M16c_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    private static PresenterPlaybackScenario[] LoadAllScenarios()
    {
        return Directory.GetFiles(PlaybackTestEnvironment.GetScenariosRoot(), "*.machina-playback.toml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(PresenterPlaybackTomlParser.LoadFile)
            .ToArray();
    }

    private static PresenterPlaybackSuiteRunner CreateSuiteRunner()
    {
        return new PresenterPlaybackSuiteRunner(PlaybackTestEnvironment.CreateRunner());
    }

    private static void AssertScenarioPasses(string fileName)
    {
        string outputPath = PlaybackTestEnvironment.CreateOutputFilePath(Path.GetFileNameWithoutExtension(fileName));
        try
        {
            PresenterPlaybackRunResult result = PlaybackTestEnvironment.CreateRunner().RunScenarioFile(
                PlaybackTestEnvironment.GetScenarioPath(fileName),
                outputPath);
            Assert.All(result.Trace.Assertions, assertion => Assert.True(assertion.Passed, assertion.FailureMessage));
        }
        finally
        {
            PlaybackTestEnvironment.DeleteOutputPath(outputPath);
        }
    }

    private static string CreateTemporarySuiteDirectory(params string[] scenarioTomls)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "machina-playback-m16c-suite-temp",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        for (int index = 0; index < scenarioTomls.Length; index++)
        {
            string path = Path.Combine(directory, $"scenario-{index:00}.machina-playback.toml");
            File.WriteAllText(path, scenarioTomls[index]);
        }

        return directory;
    }

    private static JsonDocument LoadMilestoneManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(MilestoneManifestJsonPath.Value));
    }

    private static string CreateMilestoneManifestJsonPath()
    {
        string outputDirectory = PlaybackTestEnvironment.CreateOutputDirectory("suite-manifest");
        PresenterPlaybackSuiteResult suiteResult = CreateSuiteRunner().RunSuitePath(
            PlaybackTestEnvironment.GetSuiteManifestPath("m16c-oblivion-playback-suite.machina-playback-suite.toml"),
            outputDirectory);
        return suiteResult.ManifestJsonPath;
    }
}
