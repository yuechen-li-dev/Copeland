using System.Text.Json;
using Machina.Presenter.Sample.Playback;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

[Collection(PlaybackXunitCollection.Name)]
public sealed class MachinaPlaybackXunitIntegrationM16dTests
{
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

    private static readonly HashSet<string> SupportedStepTypes = new(StringComparer.Ordinal)
    {
        "click",
        "drag",
        "key",
        "wait",
        "wheel",
    };

    private static readonly HashSet<string> SupportedAssertionTypes = new(StringComparer.Ordinal)
    {
        "card-expanded",
        "region-exists",
        "scroll-offset-changed",
        "scroll-offset-equals",
        "scroll-offset-greater-than",
        "selected-card",
        "shell-mode",
        "step-scroll-delta-equals",
        "step-scroll-delta-greater-than",
    };

    [Fact]
    public void PlaybackXunit_AllCanonicalScenarios_Pass()
    {
        PlaybackScenarioXunitRunner.AssertSuitePasses(
            "canonical",
            PlaybackScenarioDiscovery.AllCanonicalScenarios(),
            $"{nameof(MachinaPlaybackXunitIntegrationM16dTests)}.{nameof(PlaybackXunit_AllCanonicalScenarios_Pass)}");
    }

    [Fact]
    public void PlaybackXunit_CanonicalScenarioIdsAreUnique()
    {
        string[] duplicateIds = PlaybackScenarioDiscovery.AllCanonicalScenarios()
            .GroupBy(file => file.ScenarioId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicateIds);
    }

    [Fact]
    public void PlaybackXunit_CanonicalScenariosParse()
    {
        Assert.All(
            PlaybackScenarioDiscovery.AllCanonicalScenarios(),
            scenarioFile => Assert.False(string.IsNullOrWhiteSpace(scenarioFile.Scenario.Name)));
    }

    [Fact]
    public void PlaybackXunit_CanonicalScenariosHaveAssertions()
    {
        Assert.All(
            PlaybackScenarioDiscovery.AllCanonicalScenarios(),
            scenarioFile => Assert.NotEmpty(scenarioFile.Scenario.Assertions));
    }

    [Fact]
    public void PlaybackXunit_CanonicalAssertionsHaveReasons()
    {
        Assert.All(
            PlaybackScenarioDiscovery.AllCanonicalScenarios(),
            scenarioFile => Assert.All(
                scenarioFile.Scenario.Assertions,
                assertion => Assert.False(
                    string.IsNullOrWhiteSpace(assertion.Reason),
                    scenarioFile.ScenarioId)));
    }

    [Fact]
    public void PlaybackXunit_CanonicalScenariosDoNotUseProgrammingKeys()
    {
        foreach (PlaybackScenarioFile scenarioFile in PlaybackScenarioDiscovery.AllCanonicalScenarios())
        {
            string text = File.ReadAllText(scenarioFile.ScenarioPath);

            Assert.All(
                ForbiddenProgrammingKeys,
                forbiddenKey => Assert.DoesNotContain(
                    $"{forbiddenKey} =",
                    text,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void PlaybackXunit_CanonicalScenariosUseSupportedStepsAndAssertions()
    {
        Assert.All(
            PlaybackScenarioDiscovery.AllCanonicalScenarios(),
            scenarioFile =>
            {
                Assert.All(
                    scenarioFile.Scenario.Steps,
                    step => Assert.Contains(step.Type, SupportedStepTypes));
                Assert.All(
                    scenarioFile.Scenario.Assertions,
                    assertion => Assert.Contains(assertion.Type, SupportedAssertionTypes));
            });
    }

    [Fact]
    public void PlaybackXunit_ScenarioDiscoveryIsDeterministic()
    {
        string[] first = PlaybackScenarioDiscovery.AllCanonicalScenarios()
            .Select(file => file.ScenarioPath)
            .ToArray();
        string[] second = PlaybackScenarioDiscovery.AllCanonicalScenarios()
            .Select(file => file.ScenarioPath)
            .ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void PlaybackXunit_FailureMessageIncludesScenarioId()
    {
        PlaybackXunitRunResult result = RunFailingFixtureScenario();

        Assert.NotNull(result.FailureMessage);
        Assert.Contains("m16d-failing-fixture-shell-mode", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackXunit_FailureMessageIncludesAssertionReason()
    {
        PlaybackXunitRunResult result = RunFailingFixtureScenario();

        Assert.NotNull(result.FailureMessage);
        Assert.Contains(
            "This synthetic failure proves xUnit playback assertions carry scenario reasons.",
            result.FailureMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackXunit_FailureWritesFailureTxt()
    {
        PlaybackXunitRunResult result = RunFailingFixtureScenario();

        Assert.NotNull(result.FailureSummaryPath);
        Assert.True(File.Exists(result.FailureSummaryPath));

        string text = File.ReadAllText(result.FailureSummaryPath);
        Assert.Contains("scenarioId=m16d-failing-fixture-shell-mode", text, StringComparison.Ordinal);
        Assert.Contains(
            "assertionReason=This synthetic failure proves xUnit playback assertions carry scenario reasons.",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackXunit_FailureWritesTraceAndFinalPng()
    {
        PlaybackXunitRunResult result = RunFailingFixtureScenario();

        Assert.NotNull(result.RunResult);
        Assert.True(File.Exists(result.RunResult.TraceJsonPath ?? string.Empty));
        Assert.True(File.Exists(result.RunResult.FinalPngPath ?? string.Empty));
        Assert.True(File.Exists(result.RunResult.ManifestJsonPath ?? string.Empty));
        Assert.True(File.Exists(result.RunResult.ManifestTextPath ?? string.Empty));
        Assert.True(File.Exists(result.RunResult.NormalizedScenarioPath ?? string.Empty));
    }

    [Fact]
    public void M16d_DoesNotAddTomlLoopsConditionalsOrVariables()
    {
        using JsonDocument manifest = LoadM16dManifest();

        Assert.False(manifest.RootElement.GetProperty("tomlConditionalsImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("tomlLoopsImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("tomlVariablesImplemented").GetBoolean());
    }

    [Fact]
    public void M16d_DoesNotImplementNativeOsAutomation()
    {
        using JsonDocument manifest = LoadM16dManifest();
        Assert.False(manifest.RootElement.GetProperty("nativeOsAutomationImplemented").GetBoolean());
    }

    [Fact]
    public void M16d_DoesNotImplementPixelGoldenDiffing()
    {
        using JsonDocument manifest = LoadM16dManifest();
        Assert.False(manifest.RootElement.GetProperty("pixelGoldenDiffingImplemented").GetBoolean());
    }

    [Fact]
    public void M16d_DoesNotChangeProductUiBehavior()
    {
        using JsonDocument manifest = LoadM16dManifest();
        Assert.False(manifest.RootElement.GetProperty("productUiBehaviorChanged").GetBoolean());
    }

    [Fact]
    public void M16d_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = LoadM16dManifest();
        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M16d_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadM16dManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("roslynExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M16d_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadM16dManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M16d_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadM16dManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    private static PlaybackXunitRunResult RunFailingFixtureScenario()
    {
        string scenarioPath = CreateTemporaryScenarioFile(
            "m16d-failing-fixture-shell-mode.machina-playback.toml",
            """
            [scenario]
            id = "m16d-failing-fixture-shell-mode"
            name = "M16d failing fixture shell mode"
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
            reason = "This synthetic failure proves xUnit playback assertions carry scenario reasons."
            """);

        PlaybackScenarioFile scenarioFile = new(
            ScenarioId: "m16d-failing-fixture-shell-mode",
            ScenarioPath: scenarioPath,
            SuiteName: "fixtures",
            IsCanonical: false,
            Scenario: PresenterPlaybackTomlParser.LoadFile(scenarioPath));

        return PlaybackScenarioXunitRunner.RunScenario(
            scenarioFile,
            $"{nameof(MachinaPlaybackXunitIntegrationM16dTests)}.FixtureFailure");
    }

    private static string CreateTemporaryScenarioFile(string fileName, string toml)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "machina-playback-m16d-fixtures",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, toml);
        return path;
    }

    private static JsonDocument LoadM16dManifest()
    {
        string path = Path.Combine(
            PlaybackTestEnvironment.GetArtifactsRoot(),
            "m16d",
            "machina-playback-xunit-integration-manifest.json");

        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
