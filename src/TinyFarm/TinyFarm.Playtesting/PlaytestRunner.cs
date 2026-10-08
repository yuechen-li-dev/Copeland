namespace TinyFarm.Playtesting;

/// <summary>Compatibility facade; Aurelian owns script execution and input/artifact mechanics.</summary>
public sealed class PlaytestRunner(PlaytestTarget target, string outputDirectory)
    : Aurelian.Playtesting.PlaytestRunner<PlaytestObservation>(target, outputDirectory,
        PlaytestJsonContext.Default.PlaytestObservation);
