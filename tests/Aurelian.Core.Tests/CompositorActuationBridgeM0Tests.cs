using Aurelian.Core.Compositor;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class CompositorActuationBridgeM0Tests
{
    private static readonly PlantOutputRef Output = new(0, 1, "final-color");
    private static readonly PresentationTargetRef Target = new(0, 0, 1);

    [Fact]
    public async Task CompositorActuationBridge_HandleAsync_ForwardsNeutralRequestToMechanism()
    {
        var mechanism = new FakeCompositorMechanism();
        var bridge = new CompositorActuationBridge(mechanism);
        var request = Request(1);

        await bridge.HandleAsync(new CompositorDispatchAct(request));

        Assert.Equal(request, mechanism.LastRequest);
    }

    [Fact]
    public async Task CompositorActuationBridge_HandleAsync_ReturnsMechanismResult()
    {
        var expected = Result(CompositorDispatchStatus.Dispatched, 2);
        var mechanism = new FakeCompositorMechanism(expected);
        var bridge = new CompositorActuationBridge(mechanism);

        CompositorDispatchResult actual = await bridge.HandleAsync(new CompositorDispatchAct(Request(2)));

        Assert.Equal(expected, actual);
        Assert.True(actual.Success, FormatDiagnostics(actual));
    }

    [Fact]
    public async Task CompositorActuationBridge_HandleAsync_PropagatesFailedDispatchResult()
    {
        var failed = Result(
            CompositorDispatchStatus.Failed,
            3,
            [new CompositorDispatchDiagnostic("ACOMP-FAKE", CompositorDispatchDiagnosticSeverity.Error, "Fake compositor dispatch failure.")]);
        var mechanism = new FakeCompositorMechanism(failed);
        var bridge = new CompositorActuationBridge(mechanism);

        CompositorDispatchResult actual = await bridge.HandleAsync(new CompositorDispatchAct(Request(3)));

        Assert.Equal(failed, actual);
        Assert.False(actual.Success);
        Assert.Equal("ACOMP-FAKE", Assert.Single(actual.DispatchDiagnostics).Code);
    }

    [Fact]
    public void CompositorActuationBridge_DoesNotRequireVulkanToHandleAbstractMechanism()
    {
        string coreProject = File.ReadAllText(ProjectPath("src/Aurelian.Core/Aurelian.Core.csproj"));
        string testProject = File.ReadAllText(ProjectPath("tests/Aurelian.Core.Tests/Aurelian.Core.Tests.csproj"));
        string[] sourceFiles = Directory.GetFiles(ProjectPath("src/Aurelian.Core"), "*.cs", SearchOption.AllDirectories);
        string source = string.Join('\n', sourceFiles.Select(File.ReadAllText));

        Assert.Contains(GraphicsProjectName(), coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphicsProjectName(), testProject, StringComparison.Ordinal);
        Assert.Contains("Aurelian.Rendering.Contracts", coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Sil" + "k", source, StringComparison.Ordinal);
    }

    private static string GraphicsProjectName() => "Aurelian." + "Gra" + "phics";

    private static CompositorDispatchRequest Request(ulong frameId) =>
        new(frameId, CompositorPolicyKind.Passthrough, [Output], Target);

    private static CompositorDispatchResult Result(
        CompositorDispatchStatus status,
        ulong frameId,
        IReadOnlyList<CompositorDispatchDiagnostic>? dispatchDiagnostics = null) =>
        new(status, frameId, CompositorPolicyKind.Passthrough, Target, CompositorDiagnostics.Empty, dispatchDiagnostics ?? []);

    private static string FormatDiagnostics(CompositorDispatchResult result) =>
        string.Join(Environment.NewLine, result.DispatchDiagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string ProjectPath(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }

    private sealed class FakeCompositorMechanism : ICompositorMechanism
    {
        private readonly CompositorDispatchResult? _result;

        public FakeCompositorMechanism(CompositorDispatchResult? result = null)
        {
            _result = result;
        }

        public CompositorDispatchRequest? LastRequest { get; private set; }

        public Task<CompositorDispatchResult> DispatchAsync(
            CompositorDispatchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result ?? Result(CompositorDispatchStatus.Dispatched, request.FrameId));
        }
    }
}
