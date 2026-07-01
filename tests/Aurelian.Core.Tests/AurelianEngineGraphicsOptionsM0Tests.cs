using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Graphics;
using Aurelian.Rendering.Contracts.Compositor;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianEngineGraphicsOptionsM0Tests
{
    [Fact]
    public void AurelianEngineGraphicsOptions_Headless_UsesExternalOwnership()
    {
        Assert.Equal(AurelianEngineGraphicsMode.Headless, AurelianEngineGraphicsOptions.Headless.Mode);
        Assert.Equal(AurelianEngineGraphicsOwnership.External, AurelianEngineGraphicsOptions.Headless.Ownership);
    }

    [Fact]
    public void AurelianEngineGraphicsOptions_PreparedVisible_UsesExternalOwnership()
    {
        Assert.Equal(AurelianEngineGraphicsMode.PreparedVisible, AurelianEngineGraphicsOptions.PreparedVisible.Mode);
        Assert.Equal(AurelianEngineGraphicsOwnership.External, AurelianEngineGraphicsOptions.PreparedVisible.Ownership);
    }

    [Fact]
    public void AurelianEngineOptions_DefaultGraphics_IsHeadless()
    {
        var options = new AurelianEngineOptions();

        Assert.Equal(AurelianEngineGraphicsOptions.Headless, options.Graphics);
    }

    [Fact]
    public void AurelianPreparedGraphicsSubsystemValidation_Headless_AllowsNoMechanisms()
    {
        var subsystem = new AurelianPreparedGraphicsSubsystem(
            AurelianEngineGraphicsOptions.Headless,
            CompositorMechanism: null,
            PresentationMechanism: null);

        AurelianPreparedGraphicsSubsystemResult result = AurelianPreparedGraphicsSubsystemValidation.Validate(subsystem);

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianPreparedGraphicsSubsystemStatus.Valid, result.Status);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AurelianPreparedGraphicsSubsystemValidation_PreparedVisible_RequiresCompositorMechanism()
    {
        var subsystem = new AurelianPreparedGraphicsSubsystem(
            AurelianEngineGraphicsOptions.PreparedVisible,
            CompositorMechanism: null,
            PresentationMechanism: new FakePresentationMechanism());

        AurelianPreparedGraphicsSubsystemResult result = AurelianPreparedGraphicsSubsystemValidation.Validate(subsystem);

        Assert.False(result.Success);
        Assert.Equal(AurelianPreparedGraphicsSubsystemStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == AurelianPreparedGraphicsSubsystemDiagnosticCodes.PreparedVisibleRequiresCompositorMechanism
            && diagnostic.Severity == AurelianPreparedGraphicsSubsystemDiagnosticSeverity.Error);
    }

    [Fact]
    public void AurelianPreparedGraphicsSubsystemValidation_PreparedVisible_RequiresPresentationMechanism()
    {
        var subsystem = new AurelianPreparedGraphicsSubsystem(
            AurelianEngineGraphicsOptions.PreparedVisible,
            new FakeCompositorMechanism(),
            PresentationMechanism: null);

        AurelianPreparedGraphicsSubsystemResult result = AurelianPreparedGraphicsSubsystemValidation.Validate(subsystem);

        Assert.False(result.Success);
        Assert.Equal(AurelianPreparedGraphicsSubsystemStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == AurelianPreparedGraphicsSubsystemDiagnosticCodes.PreparedVisibleRequiresPresentationMechanism
            && diagnostic.Severity == AurelianPreparedGraphicsSubsystemDiagnosticSeverity.Error);
    }

    [Fact]
    public void AurelianPreparedGraphicsSubsystemValidation_PreparedVisible_WithFakeMechanisms_IsValid()
    {
        var subsystem = new AurelianPreparedGraphicsSubsystem(
            AurelianEngineGraphicsOptions.PreparedVisible,
            new FakeCompositorMechanism(),
            new FakePresentationMechanism());

        AurelianPreparedGraphicsSubsystemResult result = AurelianPreparedGraphicsSubsystemValidation.Validate(subsystem);

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianPreparedGraphicsSubsystemStatus.Valid, result.Status);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AurelianPreparedGraphicsSubsystemValidation_HeadlessWithPresentation_ReturnsWarning()
    {
        var subsystem = new AurelianPreparedGraphicsSubsystem(
            AurelianEngineGraphicsOptions.Headless,
            CompositorMechanism: null,
            PresentationMechanism: new FakePresentationMechanism());

        AurelianPreparedGraphicsSubsystemResult result = AurelianPreparedGraphicsSubsystemValidation.Validate(subsystem);

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianPreparedGraphicsSubsystemStatus.Valid, result.Status);
        AurelianPreparedGraphicsSubsystemDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(AurelianPreparedGraphicsSubsystemDiagnosticCodes.HeadlessIgnoresPresentationMechanism, diagnostic.Code);
        Assert.Equal(AurelianPreparedGraphicsSubsystemDiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void AurelianPreparedGraphicsSubsystem_DoesNotReferenceVulkanTypes()
    {
        string[] sourceFiles = Directory.GetFiles(ProjectPath("src/Aurelian.Core/Engine/Graphics"), "*.cs", SearchOption.AllDirectories);
        string source = string.Join('\n', sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("Vul" + "kan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sil" + "k", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Swap" + "chain", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Surface", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Vk", source, StringComparison.Ordinal);
    }

    private static string FormatDiagnostics(AurelianPreparedGraphicsSubsystemResult result) =>
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

    private sealed class FakeCompositorMechanism : ICompositorMechanism
    {
        public Task<CompositorDispatchResult> DispatchAsync(
            CompositorDispatchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompositorDispatchResult(
                CompositorDispatchStatus.Dispatched,
                request.FrameId,
                request.Policy,
                request.Target,
                CompositorDiagnostics.Empty,
                []));
    }

    private sealed class FakePresentationMechanism : IPresentationMechanism
    {
        public Task PresentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
