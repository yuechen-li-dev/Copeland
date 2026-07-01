using Aurelian.Core.Engine;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianEngineM0Tests
{
    [Fact]
    public void AurelianEngine_Start_TransitionsToStarted()
    {
        var engine = new AurelianEngine();

        AurelianEngineResult result = engine.Start();

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianEngineStatus.Started, result.Status);
        Assert.Equal(AurelianEngineStatus.Started, engine.Status);
    }

    [Fact]
    public void AurelianEngine_Start_WhenAlreadyStarted_ReturnsDiagnostic()
    {
        var engine = new AurelianEngine();
        engine.Start();

        AurelianEngineResult result = engine.Start();

        Assert.False(result.Success);
        Assert.Equal(AurelianEngineStatus.Started, engine.Status);
        Assert.Equal(AurelianEngineDiagnosticCodes.EngineAlreadyStarted, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void AurelianEngine_Stop_TransitionsToStopped()
    {
        var engine = new AurelianEngine();
        engine.Start();

        AurelianEngineResult result = engine.Stop();

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianEngineStatus.Stopped, result.Status);
        Assert.Equal(AurelianEngineStatus.Stopped, engine.Status);
    }

    [Fact]
    public void AurelianEngine_Stop_WhenNotStarted_ReturnsDiagnostic()
    {
        var engine = new AurelianEngine();

        AurelianEngineResult result = engine.Stop();

        Assert.False(result.Success);
        Assert.Equal(AurelianEngineStatus.Created, engine.Status);
        Assert.Equal(AurelianEngineDiagnosticCodes.EngineAlreadyStopped, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void AurelianEngine_Options_DefaultNameIsAurelian()
    {
        var engine = new AurelianEngine();

        Assert.Equal("Aurelian", engine.Options.Name);
    }

    private static string FormatDiagnostics(AurelianEngineResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
