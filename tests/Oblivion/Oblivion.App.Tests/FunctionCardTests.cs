using Oblivion.Model;
using Oblivion.Product;
using System.Text.Json;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class FunctionCardTests
{
    [Fact]
    public void Real_copeland_xunit_path_discovers_runs_maps_source_and_reruns()
    {
        OblivionApplication application = new();
        OblivionWorkspaceSession session = Assert.IsType<OblivionWorkspaceSession>(
            application.OpenWorkspace(SourceFixtureRoot).Session);

        OblivionFunctionRunResult first = application.RunFunctionCard(session, "passing-function");
        OblivionFunctionRunResult second = application.RunFunctionCard(first.Session, "passing-function");
        OblivionFunctionRunResult theory = application.RunFunctionCard(second.Session, "theory-function");
        OblivionFunctionRunResult failed = application.RunFunctionCard(theory.Session, "failing-function");
        OblivionFunctionRunResult failedAgain = application.RunFunctionCard(failed.Session, "failing-function");

        Assert.Equal(OblivionFunctionTestKind.Theory, theory.Descriptor!.TestKind);
        Assert.Equal(2, theory.Descriptor.CaseCount);
        Assert.Equal(2, theory.Result.PassedCases);
        Assert.Equal(OblivionFunctionExecutionOutcome.Passed, first.Result.Outcome);
        Assert.Equal(OblivionFunctionExecutionOutcome.Passed, second.Result.Outcome);
        Assert.Equal(first.Result.TestIdentity, second.Result.TestIdentity);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, first.Realization);
        Assert.Equal(OblivionFunctionRealizationKind.Warm, second.Realization);
        Assert.Equal(OblivionFunctionRealizationKind.Warm, theory.Realization);
        Assert.True(first.MaterializationInvoked);
        Assert.True(first.DiscoveryInvoked);
        Assert.False(second.MaterializationInvoked);
        Assert.False(second.DiscoveryInvoked);
        Assert.True(first.ExecutionInvoked);
        Assert.True(second.ExecutionInvoked);
        Assert.NotEqual(first.Result.ResultIdentity, second.Result.ResultIdentity);
        Assert.True(first.Result.Duration > TimeSpan.Zero);
        Assert.Equal(OblivionFunctionExecutionOutcome.Failed, failed.Result.Outcome);
        Assert.Equal(OblivionFunctionRealizationKind.Warm, failed.Realization);
        Assert.Equal(OblivionFunctionRealizationKind.Warm, failedAgain.Realization);
        Assert.Contains("Assert.True", failed.Result.Failure!.Message, StringComparison.Ordinal);
        Assert.EndsWith("ControlledFailure.tsxtest", failed.Result.Failure.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, failed.Result.Failure.SourceLine);
        Assert.DoesNotContain(".g.cs", failed.Result.Failure.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(failed.Result.Failure.SourcePath, failedAgain.Result.Failure!.SourcePath);
        Assert.Equal(failed.Result.Failure.SourceLine, failedAgain.Result.Failure.SourceLine);
        Assert.NotEqual(failed.Result.ResultIdentity, failedAgain.Result.ResultIdentity);
    }

    [Fact]
    public void Focused_command_uses_the_same_app_operation_and_reload_clears_session_result()
    {
        FakeFunctionRunner runner = new();
        OblivionApplication application = new(functionRunner: runner);
        OblivionWorkspaceSession session = application.OpenWorkspace(SourceFixtureRoot).Session!;
        string pageId = session.ActivePage.Id.Value;
        session = session with
        {
            State = session.State.WithSelectedCard(pageId, "passing-function"),
        };

        OblivionCommandExecutionResult command = new OblivionCommandRegistry().Run(
            application,
            session,
            OblivionCommandId.FunctionRun);
        OblivionWorkspaceSessionReloadResult reload = application.ReloadWorkspace(command.Session);

        Assert.True(command.Succeeded, string.Join(Environment.NewLine, command.Diagnostics));
        Assert.Equal(1, command.AffectedCards);
        Assert.Equal(1, runner.RunCount);
        Assert.Equal(
            OblivionFunctionExecutionOutcome.Passed,
            command.Session.State.GetFunctionExecution("passing-function")!.Outcome);
        Assert.Null(reload.Session.State.GetFunctionExecution("passing-function"));
    }

    [Fact]
    public void Missing_source_is_an_infrastructure_error_without_test_execution()
    {
        OblivionCard card = CreateFunctionCard("missing.tsxtest", "missing_test");
        FakeFunctionRunner runner = new()
        {
            Discovery = new OblivionFunctionDiscoveryResult(
                null,
                TimeSpan.Zero,
                TimeSpan.Zero,
                [new OblivionCardDiagnostic(
                    "OBLIVION-FUNCTION-SOURCE-NOT-FOUND",
                    OblivionDiagnosticSeverity.Error,
                    "Source missing.",
                    "missing.tsxtest")]),
        };
        OblivionApplication application = new(functionRunner: runner);
        OblivionWorkspaceSession session = CreateSession(card);

        OblivionFunctionRunResult result = application.RunFunctionCard(session, card.Id.Value);

        Assert.Equal(OblivionFunctionExecutionOutcome.Error, result.Result.Outcome);
        Assert.Equal(0, runner.RunCount);
        Assert.Contains(result.Result.Diagnostics, diagnostic =>
            diagnostic.Code == "OBLIVION-FUNCTION-SOURCE-NOT-FOUND");
    }

    [Fact]
    public void Open_source_uses_the_typed_host_capability_with_the_safe_tsxtest_path()
    {
        OblivionApplication application = new(functionRunner: new FakeFunctionRunner());
        OblivionWorkspaceSession session = application.OpenWorkspace(SourceFixtureRoot).Session!;
        OblivionOpenPathCapabilityRequest? captured = null;
        OblivionLocalHostCapabilities host = new(OpenPath: request =>
        {
            captured = request;
            return new OblivionHostCapabilityResult(true, "opened");
        });

        OblivionHostCapabilityResult result = application.OpenFunctionSource(
            session,
            "passing-function",
            host);

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Equal(OblivionHostPathTargetKind.Source, captured.TargetKind);
        Assert.Equal("source/FunctionCardExecution.tsxtest", captured.DeclaredReference);
        Assert.EndsWith("FunctionCardExecution.tsxtest", captured.ResolvedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_test_name_is_reported_from_xunit_discovery()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.a_different_test")));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);

        OblivionFunctionDiscoveryResult result = runner.Discover(
            CreateFunctionCard("source/function.tsxtest", "missing_test"),
            fixture.Root);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "OBLIVION-FUNCTION-TEST-NOT-DISCOVERED");
        Assert.Equal(3, processes.Requests.Count);
    }

    [Fact]
    public void Duplicate_test_selector_is_reported_as_ambiguous()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput(
                "Fixture.First.duplicate_test",
                "Fixture.Second.duplicate_test")));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);

        OblivionFunctionDiscoveryResult result = runner.Discover(
            CreateFunctionCard("source/function.tsxtest", "duplicate_test"),
            fixture.Root);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "OBLIVION-FUNCTION-TEST-AMBIGUOUS");
        Assert.Equal(3, processes.Requests.Count);
    }

    [Fact]
    public void Copeland_compile_failure_stops_before_discovery()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new(new OblivionProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: 1,
            StandardOutput: string.Empty,
            StandardError: "controlled compile failure"));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);

        OblivionFunctionDiscoveryResult result = runner.Discover(
            CreateFunctionCard("source/function.tsxtest", "test"),
            fixture.Root);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "OBLIVION-FUNCTION-BUILD-FAILED" &&
            diagnostic.Message.Contains("controlled compile failure", StringComparison.Ordinal));
        Assert.Single(processes.Requests);
    }

    [Fact]
    public void Warm_realization_reuses_project_materialization_and_exact_discovery()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);
        OblivionCard card = CreateFunctionCard("source/function.tsxtest", "test");

        OblivionFunctionDiscoveryResult cold = runner.Discover(card, fixture.Root);
        OblivionFunctionDiscoveryResult warm = runner.Discover(card, fixture.Root);

        Assert.True(cold.Succeeded);
        Assert.True(warm.Succeeded);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, cold.Realization);
        Assert.Equal(OblivionFunctionRealizationKind.Warm, warm.Realization);
        Assert.Equal(cold.RealizationFingerprint, warm.RealizationFingerprint);
        Assert.Equal(cold.Descriptor!.TestIdentity, warm.Descriptor!.TestIdentity);
        Assert.True(cold.MaterializationInvoked);
        Assert.True(cold.DiscoveryInvoked);
        Assert.False(warm.MaterializationInvoked);
        Assert.False(warm.DiscoveryInvoked);
        Assert.Equal(3, processes.Requests.Count);
    }

    [Theory]
    [InlineData("source/function.tsxtest", "changed test source")]
    [InlineData("source/production.tsx", "changed production source")]
    [InlineData("fixture.csproj", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>")]
    public void Relevant_source_and_project_changes_invalidate_realization(string relativePath, string replacement)
    {
        using FunctionRunnerFixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.Root, "source", "production.tsx"), "export function value(): int { return 1; }");
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")),
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);
        OblivionCard card = CreateFunctionCard("source/function.tsxtest", "test");
        OblivionFunctionDiscoveryResult first = runner.Discover(card, fixture.Root);

        File.WriteAllText(Path.Combine(fixture.Root, relativePath), replacement);
        OblivionFunctionDiscoveryResult invalidated = runner.Discover(card, fixture.Root);

        Assert.True(first.Succeeded);
        Assert.True(invalidated.Succeeded);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, invalidated.Realization);
        Assert.NotEqual(first.RealizationFingerprint, invalidated.RealizationFingerprint);
        Assert.True(invalidated.MaterializationInvoked);
        Assert.True(invalidated.DiscoveryInvoked);
        Assert.Equal(6, processes.Requests.Count);
    }

    [Fact]
    public void Missing_realization_output_invalidates_and_never_reuses_stale_descriptor()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")),
            Success(),
            Success());
        OblivionXunitFunctionRunner runner = new(processRunner: processes);
        OblivionCard card = CreateFunctionCard("source/function.tsxtest", "test");
        OblivionFunctionDiscoveryResult first = runner.Discover(card, fixture.Root);
        File.Delete(fixture.TestAssemblyPath);

        OblivionFunctionDiscoveryResult invalidated = runner.Discover(card, fixture.Root);

        Assert.True(first.Succeeded);
        Assert.False(invalidated.Succeeded);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, invalidated.Realization);
        Assert.True(invalidated.MaterializationInvoked);
        Assert.Contains(invalidated.Diagnostics, diagnostic =>
            diagnostic.Code == "OBLIVION-FUNCTION-TEST-ASSEMBLY-MISSING");
        Assert.Equal(5, processes.Requests.Count);
    }

    [Fact]
    public void Failed_rebuild_after_invalidation_does_not_publish_or_fall_back_to_stale_realization()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")),
            new OblivionProcessResult(true, false, 1, string.Empty, "rebuild failed"));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);
        OblivionCard card = CreateFunctionCard("source/function.tsxtest", "test");
        Assert.True(runner.Discover(card, fixture.Root).Succeeded);
        File.WriteAllText(Path.Combine(fixture.Root, "source", "function.tsxtest"), "changed");

        OblivionFunctionDiscoveryResult failed = runner.Discover(card, fixture.Root);

        Assert.False(failed.Succeeded);
        Assert.Null(failed.Descriptor);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, failed.Realization);
        Assert.Contains(failed.Diagnostics, diagnostic =>
            diagnostic.Code == "OBLIVION-FUNCTION-BUILD-FAILED" &&
            diagnostic.Message.Contains("rebuild failed", StringComparison.Ordinal));
        Assert.Equal(4, processes.Requests.Count);
    }

    [Fact]
    public void Corrupt_realization_output_hash_invalidates_before_reuse()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")),
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);
        OblivionCard card = CreateFunctionCard("source/function.tsxtest", "test");
        Assert.True(runner.Discover(card, fixture.Root).Succeeded);
        File.WriteAllText(fixture.TestAssemblyPath, "corrupt replacement");

        OblivionFunctionDiscoveryResult invalidated = runner.Discover(card, fixture.Root);

        Assert.True(invalidated.Succeeded);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, invalidated.Realization);
        Assert.True(invalidated.MaterializationInvoked);
        Assert.True(invalidated.DiscoveryInvoked);
        Assert.Equal(6, processes.Requests.Count);
    }

    [Fact]
    public void Passive_inspection_does_not_materialize_or_discover()
    {
        using FunctionRunnerFixture fixture = new();
        QueueProcessRunner processes = new();
        OblivionXunitFunctionRunner runner = new(processRunner: processes);

        OblivionFunctionDiscoveryResult inspection = runner.Inspect(
            CreateFunctionCard("source/function.tsxtest", "test"),
            fixture.Root);

        Assert.False(inspection.Succeeded);
        Assert.Empty(inspection.Diagnostics);
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public void Distinct_owning_projects_do_not_share_realization_state()
    {
        using FunctionRunnerFixture firstFixture = new();
        using FunctionRunnerFixture secondFixture = new();
        QueueProcessRunner processes = new(
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")),
            Success(),
            Success(),
            Success(DiscoveryOutput("Fixture.Tests.test")));
        OblivionXunitFunctionRunner runner = new(processRunner: processes);
        OblivionCard card = CreateFunctionCard("source/function.tsxtest", "test");

        OblivionFunctionDiscoveryResult first = runner.Discover(card, firstFixture.Root);
        OblivionFunctionDiscoveryResult second = runner.Discover(card, secondFixture.Root);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, first.Realization);
        Assert.Equal(OblivionFunctionRealizationKind.Cold, second.Realization);
        Assert.NotEqual(first.RealizationFingerprint, second.RealizationFingerprint);
        Assert.Equal(6, processes.Requests.Count);
    }

    [Fact]
    public void M20g_real_dogfood_evidence_can_be_captured_on_demand()
    {
        string? evidenceDirectory = Environment.GetEnvironmentVariable("OBLIVION_M20G_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(evidenceDirectory);
        string sourcePath = Path.Combine(SourceFixtureRoot, "source", "FunctionCardExecution.tsxtest");
        byte[] originalSource = File.ReadAllBytes(sourcePath);
        try
        {
            OblivionApplication application = new();
            OblivionWorkspaceSession session = application.OpenWorkspace(SourceFixtureRoot).Session!;
            OblivionFunctionRunResult cold = application.RunFunctionCard(session, "passing-function");
            WriteEvidence(evidenceDirectory, "cold-run.json", cold);
            OblivionFunctionRunResult warmOne = application.RunFunctionCard(cold.Session, "passing-function");
            WriteEvidence(evidenceDirectory, "warm-run-1.json", warmOne);
            OblivionFunctionRunResult warmTwo = application.RunFunctionCard(warmOne.Session, "passing-function");
            WriteEvidence(evidenceDirectory, "warm-run-2.json", warmTwo);

            File.AppendAllText(sourcePath, Environment.NewLine + "// M20g invalidation dogfood probe" + Environment.NewLine);
            OblivionFunctionRunResult invalidated = application.RunFunctionCard(
                warmTwo.Session,
                "passing-function");
            WriteEvidence(evidenceDirectory, "invalidation-run.json", invalidated);
            OblivionFunctionRunResult warmAfterInvalidation = application.RunFunctionCard(
                invalidated.Session,
                "passing-function");
            WriteEvidence(evidenceDirectory, "warm-after-invalidation-run.json", warmAfterInvalidation);

            Assert.Equal(OblivionFunctionRealizationKind.Cold, cold.Realization);
            Assert.Equal(OblivionFunctionRealizationKind.Warm, warmOne.Realization);
            Assert.Equal(OblivionFunctionRealizationKind.Warm, warmTwo.Realization);
            Assert.Equal(OblivionFunctionRealizationKind.Cold, invalidated.Realization);
            Assert.Equal(OblivionFunctionRealizationKind.Warm, warmAfterInvalidation.Realization);
            Assert.All(
                new[] { cold, warmOne, warmTwo, invalidated, warmAfterInvalidation },
                run => Assert.True(run.ExecutionInvoked));
        }
        finally
        {
            File.WriteAllBytes(sourcePath, originalSource);
        }
    }

    private static void WriteEvidence(
        string evidenceDirectory,
        string fileName,
        OblivionFunctionRunResult run)
    {
        object evidence = new
        {
            milestone = "M20g",
            realizationFingerprint = run.RealizationFingerprint,
            realization = run.Realization.ToString().ToLowerInvariant(),
            materializationInvoked = run.MaterializationInvoked,
            discoveryInvoked = run.DiscoveryInvoked,
            executionInvoked = run.ExecutionInvoked,
            stages = new
            {
                resolutionMs = run.ResolutionDuration.TotalMilliseconds,
                fingerprintingMs = run.FingerprintingDuration.TotalMilliseconds,
                materializationMs = run.BuildDuration.TotalMilliseconds,
                discoveryMs = run.DiscoveryDuration.TotalMilliseconds,
                executionMs = run.RunnerDuration.TotalMilliseconds,
                totalMs = run.ResolutionDuration.TotalMilliseconds +
                    run.FingerprintingDuration.TotalMilliseconds +
                    run.BuildDuration.TotalMilliseconds +
                    run.DiscoveryDuration.TotalMilliseconds +
                    run.RunnerDuration.TotalMilliseconds,
            },
            testOutcome = run.Result.Outcome.ToString(),
            sourceHash = run.Result.SourceHash,
            trxResultIdentity = run.Result.ResultIdentity,
            testIdentity = run.Result.TestIdentity,
            runner = run.Result.RunnerIdentity,
            completedAt = run.Result.CompletedAt,
        };
        File.WriteAllText(
            Path.Combine(evidenceDirectory, fileName),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static OblivionProcessResult Success(string standardOutput = "")
    {
        return new OblivionProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: 0,
            StandardOutput: standardOutput,
            StandardError: string.Empty);
    }

    private static string DiscoveryOutput(params string[] tests)
    {
        return "The following Tests are available:" + Environment.NewLine +
            string.Join(Environment.NewLine, tests.Select(test => "    " + test));
    }

    private static OblivionCard CreateFunctionCard(string reference, string test)
    {
        return new OblivionCard(
            new OblivionCardId("function"),
            OblivionCardKind.Function,
            OblivionCardStatus.Idle,
            "Function",
            null,
            [],
            new OblivionCardBody(OblivionCardBodyFormat.Plain, new OblivionPlainTextContent(string.Empty)),
            [],
            [],
            OblivionProvenance.Unknown,
            new OblivionPageId("page"),
            new OblivionWorkspaceId("workspace"),
            Function: new OblivionFunctionSource(OblivionFunctionSourceKind.CopelandXunit, reference, test));
    }

    private static OblivionWorkspaceSession CreateSession(OblivionCard card)
    {
        OblivionWorkspacePage page = new(new OblivionPageId("page"), "Page", null, [], [card]);
        OblivionWorkspace workspace = new(
            new OblivionWorkspaceId("workspace"),
            "Workspace",
            page.Id,
            [new OblivionWorkspaceSection("section", "Section", [page])]);
        return new OblivionWorkspaceSession(
            workspace,
            page,
            OblivionSessionState.Empty.ReconcilePage(page.Id.Value, page.Cards),
            new Oblivion.Persistence.OblivionWorkspaceLocation(Path.GetTempPath(), "workspace.json"));
    }

    private static string SourceFixtureRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "src",
                    "Oblivion",
                    "Oblivion.Standalone",
                    "M20fFunctionCards.oblivion");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the M20f Function Card fixture.");
        }
    }

    private sealed class FakeFunctionRunner : IOblivionFunctionRunner
    {
        public OblivionFunctionDiscoveryResult? Discovery { get; init; }
        public int RunCount { get; private set; }

        public OblivionFunctionDiscoveryResult Discover(OblivionCard card, string workspaceRoot)
        {
            return Discovery ?? new OblivionFunctionDiscoveryResult(
                new OblivionFunctionTestDescriptor(
                    "Fixture.Tests." + card.Function!.Test,
                    card.Function.Test,
                    OblivionFunctionTestKind.Fact,
                    1,
                    new Dictionary<string, IReadOnlyList<string>>(),
                    card.Function.Reference,
                    "HASH",
                    "fixture.csproj",
                    "fixture.tests.csproj",
                    OblivionXunitFunctionRunner.RunnerIdentity,
                    []),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                []);
        }

        public OblivionFunctionExecutionResult Run(
            OblivionCard card,
            string workspaceRoot,
            OblivionFunctionTestDescriptor descriptor)
        {
            RunCount++;
            return new OblivionFunctionExecutionResult(
                card.Id.Value,
                descriptor.TestIdentity,
                descriptor.DisplayName,
                OblivionFunctionExecutionOutcome.Passed,
                TimeSpan.FromMilliseconds(2),
                null,
                descriptor.SourceReference,
                descriptor.SourceHash,
                descriptor.RunnerIdentity,
                1,
                1,
                0,
                0,
                DateTimeOffset.UtcNow,
                []);
        }
    }

    private sealed class QueueProcessRunner : IOblivionProcessRunner
    {
        private readonly Queue<OblivionProcessResult> _results;

        public QueueProcessRunner(params OblivionProcessResult[] results)
        {
            _results = new Queue<OblivionProcessResult>(results);
        }

        public List<OblivionProcessRequest> Requests { get; } = [];

        public OblivionProcessResult Run(OblivionProcessRequest request)
        {
            Requests.Add(request);
            return _results.Dequeue();
        }
    }

    private sealed class FunctionRunnerFixture : IDisposable
    {
        public FunctionRunnerFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "oblivion-function-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "source"));
            Directory.CreateDirectory(Path.Combine(Root, "obj", "CopelandTests"));
            File.WriteAllText(Path.Combine(Root, "fixture.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(Root, "source", "function.tsxtest"), "using Xunit;");
            File.WriteAllText(
                Path.Combine(Root, "obj", "CopelandTests", "fixture.CopelandTests.csproj"),
                "<Project />");
            string outputDirectory = Path.Combine(Root, "obj", "CopelandTests", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(
                Path.Combine(outputDirectory, "fixture.CopelandTests.dll"),
                "test assembly placeholder");
        }

        public string Root { get; }

        public string TestAssemblyPath => Path.Combine(
            Root,
            "obj",
            "CopelandTests",
            "bin",
            "Debug",
            "net10.0",
            "fixture.CopelandTests.dll");

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
