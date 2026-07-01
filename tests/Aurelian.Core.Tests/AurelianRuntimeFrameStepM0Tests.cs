using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Runtime;
using Aurelian.Runtime.Sessions;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianRuntimeFrameStepM0Tests
{
    [Fact]
    public async Task AurelianRuntimeTickFrameStep_RunAsync_CallsRuntimeTickerWithFrameIdAndDelta()
    {
        var ticker = new RecordingRuntimeTicker(AurelianRuntimeTickStatus.Ticked);
        var step = new AurelianRuntimeTickFrameStep(ticker);
        TimeSpan deltaTime = TimeSpan.FromMilliseconds(16);

        AurelianRuntimeTickFrameStepResult result = await step.RunAsync(new AurelianFrameId(42), deltaTime);

        Assert.True(result.Success, FormatDiagnostics(result));
        AurelianRuntimeTickInput input = Assert.Single(ticker.Inputs);
        Assert.Equal(42UL, input.TickIndex);
        Assert.Equal(deltaTime, input.DeltaTime);
        Assert.NotNull(result.RuntimeResult);
    }

    [Fact]
    public async Task AurelianRuntimeTickFrameStep_RunAsync_PropagatesRuntimeFailure()
    {
        var ticker = new RecordingRuntimeTicker(AurelianRuntimeTickStatus.Failed);
        var step = new AurelianRuntimeTickFrameStep(ticker);

        AurelianRuntimeTickFrameStepResult result = await step.RunAsync(new AurelianFrameId(3), TimeSpan.FromMilliseconds(16));

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeTickFrameStepStatus.Failed, result.Status);
        Assert.Equal(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickFailed, Assert.Single(result.Diagnostics).Code);
        Assert.NotNull(result.RuntimeResult);
        Assert.Equal(AurelianRuntimeTickStatus.Failed, result.RuntimeResult.Status);
    }

    [Fact]
    public async Task AurelianRuntimeTickFrameStep_RunAsync_PropagatesCancellation()
    {
        var ticker = new ThrowingCancelledRuntimeTicker();
        var step = new AurelianRuntimeTickFrameStep(ticker);

        AurelianRuntimeTickFrameStepResult result = await step.RunAsync(new AurelianFrameId(4), TimeSpan.FromMilliseconds(16));

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeTickFrameStepStatus.Cancelled, result.Status);
        Assert.Equal(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickCancelled, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void AurelianRuntimeTickFrameStep_DoesNotReferenceGraphicsOrVulkan()
    {
        string runtimeStepRoot = ProjectPath("src/Aurelian.Core/Engine/Runtime");
        string source = string.Join('\n', Directory.GetFiles(runtimeStepRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        string[] forbidden =
        [
            "Aurelian." + "Gra" + "phics",
            "Sil" + "k",
            "Vul" + "kan",
            "V" + "k",
            "Swap" + "chain",
            "Sur" + "face",
            "Create" + "Vul" + "kan" + "Surface",
            "Window" + ".Create",
        ];

        foreach (string term in forbidden)
        {
            Assert.DoesNotContain(term, source, StringComparison.Ordinal);
        }
    }

    private static string FormatDiagnostics(AurelianRuntimeTickFrameStepResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

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

    private sealed class RecordingRuntimeTicker : IAurelianRuntimeTicker
    {
        private readonly AurelianRuntimeTickStatus status;

        public RecordingRuntimeTicker(AurelianRuntimeTickStatus status)
        {
            this.status = status;
        }

        public List<AurelianRuntimeTickInput> Inputs { get; } = [];

        public Task<AurelianRuntimeTickResult> TickAsync(AurelianRuntimeTickInput input, CancellationToken cancellationToken = default)
        {
            Inputs.Add(input);
            IReadOnlyList<AurelianRuntimeDiagnostic> diagnostics = status == AurelianRuntimeTickStatus.Ticked
                ? []
                : [new AurelianRuntimeDiagnostic(AurelianRuntimeDiagnosticCodes.RunnerFailed, AurelianRuntimeDiagnosticSeverity.Error, "Runtime ticker failed in test.")];
            return Task.FromResult(new AurelianRuntimeTickResult(status, input.TickIndex, input.DeltaTime, diagnostics));
        }
    }

    private sealed class ThrowingCancelledRuntimeTicker : IAurelianRuntimeTicker
    {
        public Task<AurelianRuntimeTickResult> TickAsync(AurelianRuntimeTickInput input, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }
}
