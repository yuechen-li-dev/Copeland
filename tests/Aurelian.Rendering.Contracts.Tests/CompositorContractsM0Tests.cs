using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aurelian.Rendering.Contracts.Compositor;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class CompositorContractsM0Tests
{
    [Fact]
    public void CompositorPolicyKind_ContainsExpectedM0Values()
    {
        CompositorPolicyKind[] values = Enum.GetValues<CompositorPolicyKind>();

        Assert.Equal(
            [
                CompositorPolicyKind.Passthrough,
                CompositorPolicyKind.FullQuality,
                CompositorPolicyKind.ReducedFrequency,
                CompositorPolicyKind.Differential,
            ],
            values);
    }

    [Fact]
    public void PlantOutputRef_ToString_IsDeterministic()
    {
        PlantOutputRef output = new(7, 42, "plant-output/color");

        Assert.Equal("7:42:plant-output/color", output.ToString());
    }

    [Fact]
    public void PresentationTargetRef_ToString_IsDeterministic()
    {
        PresentationTargetRef target = new(3, 2, 99);

        Assert.Equal("3:99:swapchain[2]", target.ToString());
    }

    [Fact]
    public void RequiredPlantOutputSet_IsSatisfiedBy_ReadyOutputs()
    {
        PlantOutputRef required = new(1, 10, "color");
        RequiredPlantOutputSet set = new(10, CompositorPolicyKind.FullQuality, [required]);

        bool satisfied = set.IsSatisfiedBy([
            new PlantOutputReadiness(required, PlantOutputReadinessStatus.Ready, CompletedFenceValue: 12),
        ]);

        Assert.True(satisfied);
    }

    [Fact]
    public void RequiredPlantOutputSet_IsSatisfiedBy_ReusedOutputs()
    {
        PlantOutputRef required = new(1, 9, "trusted-previous-color");
        RequiredPlantOutputSet set = new(10, CompositorPolicyKind.ReducedFrequency, [required]);

        bool satisfied = set.IsSatisfiedBy([
            new PlantOutputReadiness(required, PlantOutputReadinessStatus.Reused),
        ]);

        Assert.True(satisfied);
    }

    [Theory]
    [InlineData(PlantOutputReadinessStatus.Pending)]
    [InlineData(PlantOutputReadinessStatus.Missing)]
    [InlineData(PlantOutputReadinessStatus.Failed)]
    public void RequiredPlantOutputSet_IsSatisfiedBy_ReturnsFalseForPendingOrMissing(
        PlantOutputReadinessStatus status)
    {
        PlantOutputRef required = new(1, 10, "color");
        RequiredPlantOutputSet set = new(10, CompositorPolicyKind.Passthrough, [required]);

        bool satisfied = set.IsSatisfiedBy([
            new PlantOutputReadiness(required, status),
        ]);

        Assert.False(satisfied);
    }

    [Fact]
    public void RequiredPlantOutputSet_IsSatisfiedBy_IgnoresExtraReadiness()
    {
        PlantOutputRef required = new(1, 10, "color");
        PlantOutputRef extra = new(2, 10, "shadow-color");
        RequiredPlantOutputSet set = new(10, CompositorPolicyKind.Passthrough, [required]);

        bool satisfied = set.IsSatisfiedBy([
            new PlantOutputReadiness(extra, PlantOutputReadinessStatus.Failed),
            new PlantOutputReadiness(required, PlantOutputReadinessStatus.Ready),
        ]);

        Assert.True(satisfied);
    }

    [Fact]
    public void CompositorDiagnostics_Empty_HasNoAgreementRateOrMetrics()
    {
        Assert.Null(CompositorDiagnostics.Empty.AgreementRate);
        Assert.Empty(CompositorDiagnostics.Empty.Metrics);
    }

    [Fact]
    public void CompositorDispatchRequest_HoldsInputsAndTarget()
    {
        PlantOutputRef input = new(1, 10, "color");
        PresentationTargetRef target = new(1, 0, 10);

        CompositorDispatchRequest request = new(
            10,
            CompositorPolicyKind.Passthrough,
            [input],
            target);

        Assert.True(request.HasInputs);
        Assert.Equal(10UL, request.FrameId);
        Assert.Equal(CompositorPolicyKind.Passthrough, request.Policy);
        Assert.Equal(input, Assert.Single(request.Inputs));
        Assert.Equal(target, request.Target);
    }

    [Fact]
    public void CompositorDispatchResult_DispatchedWithoutErrors_IsSuccess()
    {
        CompositorDispatchResult result = new(
            CompositorDispatchStatus.Dispatched,
            10,
            CompositorPolicyKind.Passthrough,
            new PresentationTargetRef(1, 0, 10),
            CompositorDiagnostics.Empty,
            [new CompositorDispatchDiagnostic(
                CompositorDispatchDiagnosticCodes.DiagnosticsInvalid,
                CompositorDispatchDiagnosticSeverity.Info,
                "Diagnostic was ignored.")]);

        Assert.True(result.Success);
    }

    [Fact]
    public void CompositorDispatchResult_FailedWithError_IsNotSuccess()
    {
        CompositorDispatchResult result = new(
            CompositorDispatchStatus.Failed,
            10,
            CompositorPolicyKind.Passthrough,
            new PresentationTargetRef(1, 0, 10),
            CompositorDiagnostics.Empty,
            [new CompositorDispatchDiagnostic(
                CompositorDispatchDiagnosticCodes.DispatchFailed,
                CompositorDispatchDiagnosticSeverity.Error,
                "Dispatch failed.")]);

        Assert.False(result.Success);
    }

    [Fact]
    public void CompositorContracts_DoNotReferenceGraphicsRuntimeDomina\u0074usOrWorld()
    {
        string projectRoot = FindProjectRoot();
        string contractsProject = Path.Combine(
            projectRoot,
            "src",
            "Aurelian.Rendering.Contracts",
            "Aurelian.Rendering.Contracts.csproj");

        string projectText = File.ReadAllText(contractsProject);
        string[] forbiddenReferences =
        [
            string.Join('.', "Aurelian", "Graphics"),
            string.Join('.', "Aurelian", "Runtime"),
            string.Join('.', "Aurelian", "World"),
            string.Concat("Domina", "tus"),
        ];

        foreach (string forbiddenReference in forbiddenReferences)
        {
            Assert.DoesNotContain(forbiddenReference, projectText, StringComparison.Ordinal);
        }
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Aurelian.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
