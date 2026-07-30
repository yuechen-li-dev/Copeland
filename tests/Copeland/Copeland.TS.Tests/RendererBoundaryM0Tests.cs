using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class RendererBoundaryM0Tests
{
    [Fact]
    public void React_bridge_presentation_keeps_component_identity_separate_from_renderer_identity()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Page.tsx", "Page.tsx", """
                import { createElement } from "react";

                function Badge(): ReactNode { return <span>ready</span>; }

                stream Page<0px, 0px> {
                    width: 120px;
                    height: 40px;
                    content: Badge() { height: fill; }
                }
                """)],
            ReactOptions());

        Assert.Empty(project.Diagnostics);
        BoundComponentDefinition badge = Assert.Single(project.Modules.Single().BoundCompilation!.Program.ComponentDefinitions);
        BoundComponentInstance instance = Assert.Single(project.Modules.Single().BoundCompilation!.Program.ComponentInstances);

        Assert.Equal(RendererAdapterIdentity.React, badge.RendererAdapter);
        Assert.Equal(ComponentPresentationKind.RendererPayload, badge.Presentation.Kind);
        Assert.True(badge.RequiredContentCapabilities.HasFlag(ComponentContentCapabilities.ReactSubtree));
        Assert.True(badge.RequiredHostCapabilities.HasFlag(ComponentHostCapabilities.StableMountPoint));
        Assert.Equal(badge.StableIdentity, instance.Definition.StableIdentity);
        Assert.NotEqual(instance.StableIdentity, instance.Definition.StableIdentity);
        Assert.True(instance.HostCapabilities.HasFlag(ComponentHostCapabilities.StableMountPoint));
    }

    [Fact]
    public void Adapter_contracts_report_stable_capability_diagnostics()
    {
        RendererAdapterValidation unavailable = Assert.Single(RendererAdapterContracts.Validate(
            (RendererAdapterIdentity)999,
            ComponentContentCapabilities.ReactSubtree,
            ComponentHostCapabilities.None));
        RendererAdapterValidation contentMismatch = Assert.Single(RendererAdapterContracts.Validate(
            RendererAdapterIdentity.CustomElement,
            ComponentContentCapabilities.ReactSubtree,
            ComponentHostCapabilities.RendererAttachment | ComponentHostCapabilities.StableMountPoint | ComponentHostCapabilities.ResolvedWidth | ComponentHostCapabilities.ResolvedHeight));
        RendererAdapterValidation hostMismatch = Assert.Single(RendererAdapterContracts.Validate(
            RendererAdapterIdentity.React,
            ComponentContentCapabilities.ReactSubtree,
            ComponentHostCapabilities.RendererAttachment));

        Assert.Equal("COPE-RENDERER-0001", unavailable.Id);
        Assert.Equal("COPE-RENDERER-0002", contentMismatch.Id);
        Assert.Equal("COPE-RENDERER-0003", hostMismatch.Id);
    }

    [Fact]
    public void Attachment_registry_enforces_mount_update_and_cleanup_lifecycle()
    {
        var registry = new RendererAttachmentRegistry();

        Assert.Null(registry.Mount("component-a", "host-a", RendererAdapterIdentity.React));
        RendererRuntimeDiagnostic? duplicate = registry.Mount("component-b", "host-a", RendererAdapterIdentity.CustomElement);
        RendererRuntimeDiagnostic? missing = registry.Mount(null, "host-b", RendererAdapterIdentity.React);
        Assert.Equal("COPE-RENDERER-0004", Assert.IsType<RendererRuntimeDiagnostic>(duplicate).Id);
        Assert.Equal("COPE-RENDERER-0007", Assert.IsType<RendererRuntimeDiagnostic>(missing).Id);

        Assert.Null(registry.Update("component-a", RendererAdapterIdentity.React));
        Assert.Null(registry.Unmount("component-a", RendererAdapterIdentity.React, () => { }));
        RendererRuntimeDiagnostic? updateAfterUnmount = registry.Update("component-a", RendererAdapterIdentity.React);
        Assert.Equal("COPE-RENDERER-0005", Assert.IsType<RendererRuntimeDiagnostic>(updateAfterUnmount).Id);

        Assert.Null(registry.Mount("component-c", "host-c", RendererAdapterIdentity.React));
        RendererRuntimeDiagnostic? cleanupFailure = registry.Unmount("component-c", RendererAdapterIdentity.React, () => throw new InvalidOperationException("boom"));
        Assert.Equal("COPE-RENDERER-0006", Assert.IsType<RendererRuntimeDiagnostic>(cleanupFailure).Id);
    }

    [Fact]
    public void Custom_element_bridge_is_a_component_presentation_not_a_react_host_capability()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Page.tsx", "Page.tsx", """
                import { createElement } from "react";

                function Status(): ReactNode { return <copeland-status-badge></copeland-status-badge>; }

                stream Page<0px, 0px> {
                    width: 120px;
                    height: 40px;
                    content: Status() { height: fill; }
                }
                """)],
            ReactOptions());

        Assert.Empty(project.Diagnostics);
        BoundComponentDefinition status = Assert.Single(project.Modules.Single().BoundCompilation!.Program.ComponentDefinitions);
        BoundComponentInstance instance = Assert.Single(project.Modules.Single().BoundCompilation!.Program.ComponentInstances);

        Assert.Equal(RendererAdapterIdentity.CustomElement, status.RendererAdapter);
        Assert.Equal(ComponentContentCapabilities.CustomElement, status.RequiredContentCapabilities);
        Assert.Equal(status.RendererAdapter, instance.Definition.RendererAdapter);
    }

    private static CopelandCompilationOptions ReactOptions()
        => new()
        {
            SourcePath = "Page.tsx",
            TsXmlProfile = CopelandTsXmlProfile.ReactM0,
            NpmPackages =
            [
                new CopelandNpmPackageContract(
                    "react",
                    "19.2.7",
                    [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
            ],
        };
}
