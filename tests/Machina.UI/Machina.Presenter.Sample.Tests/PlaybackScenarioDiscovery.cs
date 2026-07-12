using Machina.Presenter.Sample.Playback;

namespace Machina.Presenter.Sample.Tests;

public static class PlaybackScenarioDiscovery
{
    private const string ScenarioExtension = "*.machina-playback.toml";
    private const string CanonicalSuiteFileName = "m16c-oblivion-playback-suite.machina-playback-suite.toml";

    private static readonly Lazy<IReadOnlyList<PlaybackScenarioFile>> StarterScenarioCache =
        new(LoadStarterScenarios);

    private static readonly Lazy<IReadOnlyList<PlaybackScenarioFile>> RegressionScenarioCache =
        new(LoadRegressionScenarios);

    private static readonly Lazy<IReadOnlyList<PlaybackScenarioFile>> CanonicalScenarioCache =
        new(LoadCanonicalScenarios);

    public static IReadOnlyList<PlaybackScenarioFile> StarterScenarios()
    {
        return StarterScenarioCache.Value;
    }

    public static IReadOnlyList<PlaybackScenarioFile> RegressionScenarios()
    {
        return RegressionScenarioCache.Value;
    }

    public static IReadOnlyList<PlaybackScenarioFile> AllCanonicalScenarios()
    {
        return CanonicalScenarioCache.Value;
    }

    public static IEnumerable<object[]> StarterScenarioMemberData()
    {
        return StarterScenarios().Select(scenario => new object[] { scenario });
    }

    public static IEnumerable<object[]> RegressionScenarioMemberData()
    {
        return RegressionScenarios().Select(scenario => new object[] { scenario });
    }

    private static IReadOnlyList<PlaybackScenarioFile> LoadStarterScenarios()
    {
        return LoadScenarioDirectory(
            PlaybackTestEnvironment.GetStarterScenarioDirectory(),
            suiteName: "starter");
    }

    private static IReadOnlyList<PlaybackScenarioFile> LoadRegressionScenarios()
    {
        return LoadScenarioDirectory(
            PlaybackTestEnvironment.GetRegressionScenarioDirectory(),
            suiteName: "regressions");
    }

    private static IReadOnlyList<PlaybackScenarioFile> LoadCanonicalScenarios()
    {
        PresenterPlaybackSuiteDefinition suite = PresenterPlaybackSuiteManifestTomlParser.LoadFile(
            PlaybackTestEnvironment.GetSuiteManifestPath(CanonicalSuiteFileName));

        return suite.ScenarioPaths
            .Select(path => CreateScenarioFile(path, ResolveSuiteName(path), isCanonical: true))
            .ToArray();
    }

    private static IReadOnlyList<PlaybackScenarioFile> LoadScenarioDirectory(string directory, string suiteName)
    {
        return Directory.GetFiles(directory, ScenarioExtension, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => CreateScenarioFile(path, suiteName, isCanonical: false))
            .ToArray();
    }

    private static PlaybackScenarioFile CreateScenarioFile(string scenarioPath, string suiteName, bool isCanonical)
    {
        string fullPath = Path.GetFullPath(scenarioPath);
        PresenterPlaybackScenario scenario = PresenterPlaybackTomlParser.LoadFile(fullPath);

        return new PlaybackScenarioFile(
            ScenarioId: scenario.Id,
            ScenarioPath: fullPath,
            SuiteName: suiteName,
            IsCanonical: isCanonical,
            Scenario: scenario);
    }

    private static string ResolveSuiteName(string scenarioPath)
    {
        string normalizedPath = Path.GetFullPath(scenarioPath);

        if (normalizedPath.Contains(
            $"{Path.DirectorySeparatorChar}starter{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains(
                $"{Path.AltDirectorySeparatorChar}starter{Path.AltDirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
        {
            return "starter";
        }

        if (normalizedPath.Contains(
            $"{Path.DirectorySeparatorChar}regressions{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains(
                $"{Path.AltDirectorySeparatorChar}regressions{Path.AltDirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
        {
            return "regressions";
        }

        return "canonical";
    }
}

public sealed record PlaybackScenarioFile(
    string ScenarioId,
    string ScenarioPath,
    string SuiteName,
    bool IsCanonical,
    PresenterPlaybackScenario Scenario);
