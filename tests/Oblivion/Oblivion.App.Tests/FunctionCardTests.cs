using Oblivion.Model;
using Oblivion.Product;
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

        OblivionFunctionDiscoveryResult theory = application.InspectFunctionCard(
            session,
            "theory-function");
        OblivionFunctionRunResult first = application.RunFunctionCard(session, "passing-function");
        OblivionFunctionRunResult second = application.RunFunctionCard(first.Session, "passing-function");
        OblivionFunctionRunResult failed = application.RunFunctionCard(second.Session, "failing-function");

        Assert.True(theory.Succeeded, string.Join(Environment.NewLine, theory.Diagnostics));
        Assert.Equal(OblivionFunctionTestKind.Theory, theory.Descriptor!.TestKind);
        Assert.Equal(2, theory.Descriptor.CaseCount);
        Assert.Equal(OblivionFunctionExecutionOutcome.Passed, first.Result.Outcome);
        Assert.Equal(OblivionFunctionExecutionOutcome.Passed, second.Result.Outcome);
        Assert.Equal(first.Result.TestIdentity, second.Result.TestIdentity);
        Assert.True(first.Result.Duration > TimeSpan.Zero);
        Assert.Equal(OblivionFunctionExecutionOutcome.Failed, failed.Result.Outcome);
        Assert.Contains("Assert.True", failed.Result.Failure!.Message, StringComparison.Ordinal);
        Assert.EndsWith("ControlledFailure.tsxtest", failed.Result.Failure.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, failed.Result.Failure.SourceLine);
        Assert.DoesNotContain(".g.cs", failed.Result.Failure.SourcePath, StringComparison.OrdinalIgnoreCase);
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
        }

        public string Root { get; }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
