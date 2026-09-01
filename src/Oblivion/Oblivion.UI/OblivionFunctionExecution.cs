namespace Oblivion.Product;

public enum OblivionFunctionExecutionOutcome
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped,
    Error,
}

public enum OblivionFunctionTestKind
{
    Fact,
    Theory,
}

public enum OblivionFunctionRealizationKind
{
    Cold,
    Warm,
}

public sealed record OblivionFunctionTestDescriptor(
    string TestIdentity,
    string DisplayName,
    OblivionFunctionTestKind TestKind,
    int CaseCount,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Traits,
    string SourceReference,
    string SourceHash,
    string ProjectPath,
    string TestProjectPath,
    string RunnerIdentity,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    string RealizationFingerprint = "",
    string TestAssemblyPath = "")
{
    public bool Discovered => Diagnostics.All(diagnostic =>
        diagnostic.Severity != Oblivion.Model.OblivionDiagnosticSeverity.Error);
}

public sealed record OblivionFunctionFailure(
    string Message,
    string? ExceptionType,
    string? SourcePath,
    int? SourceLine,
    string? StackTrace);

public sealed record OblivionFunctionExecutionResult(
    string CardId,
    string TestIdentity,
    string DisplayName,
    OblivionFunctionExecutionOutcome Outcome,
    TimeSpan? Duration,
    OblivionFunctionFailure? Failure,
    string SourceReference,
    string SourceHash,
    string RunnerIdentity,
    int CaseCount,
    int PassedCases,
    int FailedCases,
    int SkippedCases,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    OblivionFunctionRealizationKind Realization = OblivionFunctionRealizationKind.Cold,
    string RealizationFingerprint = "",
    string? ResultIdentity = null,
    bool MaterializationInvoked = false,
    bool DiscoveryInvoked = false,
    bool ExecutionInvoked = false)
{
    public static OblivionFunctionExecutionResult Running(
        string cardId,
        OblivionFunctionTestDescriptor descriptor)
    {
        return new OblivionFunctionExecutionResult(
            cardId,
            descriptor.TestIdentity,
            descriptor.DisplayName,
            OblivionFunctionExecutionOutcome.Running,
            null,
            null,
            descriptor.SourceReference,
            descriptor.SourceHash,
            descriptor.RunnerIdentity,
            descriptor.CaseCount,
            0,
            0,
            0,
            null,
            descriptor.Diagnostics,
            RealizationFingerprint: descriptor.RealizationFingerprint);
    }
}
