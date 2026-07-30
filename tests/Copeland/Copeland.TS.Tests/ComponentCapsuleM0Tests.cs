using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ComponentCapsuleM0Tests
{
    [Fact]
    public void Ordinary_render_functions_have_renderer_neutral_definitions_and_host_scoped_instances()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            record CardProps { title: string; }

            function FeatureCard(props: CardProps): ReactNode {
                return <article>{props.title}</article>;
            }

            function LocalCopy(): ReactNode { return <span>Local</span>; }
            stream FeatureCardSurface<0px, 0px> {
                width: 200px;
                height: 120px;
                content: LocalCopy() { height: fill; }
            }

            function NativeCard(): ReactNode {
                return FeatureCardSurfaceStream();
            }

            stream Page<0px, 0px> {
                width: 600px;
                height: 240px;
                grid cards: [
                    FeatureCard({ title: "First" }),
                    FeatureCard({ title: "Second" }),
                    NativeCard()
                ] { columns: 3; height: fill; }
            }
            """);

        Assert.Empty(project.Diagnostics);
        BoundProgram program = Assert.Single(project.Modules).BoundCompilation!.Program;
        BoundComponentDefinition featureCard = Assert.Single(program.ComponentDefinitions, definition => definition.Function.Name == "FeatureCard");
        BoundComponentDefinition nativeCard = Assert.Single(program.ComponentDefinitions, definition => definition.Function.Name == "NativeCard");

        Assert.Equal(ComponentImplementationKind.React, featureCard.ImplementationKind);
        Assert.Equal(ComponentImplementationKind.NativeMachina, nativeCard.ImplementationKind);
        Assert.Equal("FeatureCardSurface", nativeCard.LocalStream!.Layout.Name);
        Assert.True(nativeCard.HostCapabilities.HasFlag(ComponentHostCapabilities.FillAssignedBox));
        Assert.True(nativeCard.HostCapabilities.HasFlag(ComponentHostCapabilities.RendererAttachment));

        BoundComponentInstance[] featureInstances = program.ComponentInstances
            .Where(instance => instance.Definition.Function.Name == "FeatureCard")
            .ToArray();
        Assert.Equal(2, featureInstances.Length);
        Assert.All(featureInstances, instance => Assert.Equal("Page.cards", instance.ParentHostBox));
        Assert.Equal([0, 1], featureInstances.Select(instance => instance.Ordinal));
        Assert.All(featureInstances, instance => Assert.Single(instance.Props));
    }

    private static CopelandProjectCompilation Compile(string source)
        => CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Page.tsx", "Page.tsx", source)],
            new CopelandCompilationOptions
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
            });
}
