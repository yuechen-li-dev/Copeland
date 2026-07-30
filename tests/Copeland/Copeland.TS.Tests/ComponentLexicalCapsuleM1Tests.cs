using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ComponentLexicalCapsuleM1Tests
{
    [Fact]
    public void Local_stream_captures_component_props_and_immutable_locals()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            record CardProps { title: string; body: string; }

            function RenderText(value: string): ReactNode { return <span>{value}</span>; }

            function FeatureCard(props: CardProps): ReactNode {
                const bodyCopy: string = props.body;

                stream Surface<0px, 0px> {
                    width: fill;
                    height: fill;
                    column root {
                        heading: RenderText(props.title) { height: fill; }
                        body: RenderText(bodyCopy) { height: fill; }
                    }
                }

                return Surface();
            }

            stream Page<0px, 0px> {
                width: 480px;
                height: 180px;
                grid cards: [
                    FeatureCard({ title: "First", body: "One" }),
                    FeatureCard({ title: "Second", body: "Two" })
                ] { columns: 2; height: fill; }
            }
            """);

        Assert.Empty(project.Diagnostics);
        BoundProgram program = Assert.Single(project.Modules).BoundCompilation!.Program;
        BoundComponentDefinition definition = Assert.Single(program.ComponentDefinitions, item => item.Function.Name == "FeatureCard");
        Assert.NotNull(definition.LocalStream);
        BoundLayoutBinding presentation = definition.LocalStream!;

        Assert.Equal(ComponentImplementationKind.NativeMachina, definition.ImplementationKind);
        Assert.True(presentation.IsPrivate);
        Assert.Equal("FeatureCard::Surface", presentation.Layout.BoundLayout!.Name);
        Assert.Equal(["bodyCopy", "props"], definition.Captures.Select(capture => capture.Source.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(2, program.ComponentInstances.Count(instance => instance.Definition == definition));
        BoundComponentInstance[] cardInstances = program.ComponentInstances.Where(instance => instance.Definition == definition).ToArray();
        Assert.All(cardInstances, instance =>
        {
            Assert.Equal("Page.cards", instance.ParentHostBox);
            Assert.Null(instance.ParentComponentInstance);
            Assert.True(instance.HostCapabilities.HasFlag(ComponentHostCapabilities.RendererAttachment));
            Assert.Contains("call@", instance.AuthoredCallIdentity, StringComparison.Ordinal);
        });

        BoundComponentInstance[] textInstances = program.ComponentInstances
            .Where(instance => instance.Definition.Function.Name == "RenderText")
            .ToArray();
        Assert.Equal(4, textInstances.Length);
        Assert.All(textInstances, instance => Assert.Same(definition, instance.ParentComponentInstance!.Definition));
    }

    [Fact]
    public void Local_stream_rejects_mutable_capture()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            function RenderNumber(value: int): ReactNode { return <span>{value}</span>; }

            function Card(): ReactNode {
                let count: int = 1;
                stream Surface<0px, 0px> {
                    width: fill;
                    height: fill;
                    content: RenderNumber(count) { height: fill; }
                }
                return Surface();
            }
            """);

        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-CAPSULE-0003" && diagnostic.Message.Contains("count", StringComparison.Ordinal));
    }

    [Fact]
    public void Local_layout_is_private_and_cannot_escape_its_component()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            function Card(): ReactNode {
                layout Surface<0px, 0px> {
                    width: fill;
                    height: fill;
                    column root { }
                }
                return Surface();
            }

            function InvalidParent(): ReactNode {
                return Surface();
            }
            """);

        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-BIND-0001" && diagnostic.Message.Contains("Undefined function", StringComparison.Ordinal));
        BoundProgram program = Assert.Single(project.Modules).BoundCompilation!.Program;
        BoundComponentDefinition card = Assert.Single(program.ComponentDefinitions, item => item.Function.Name == "Card");
        Assert.True(card.LocalStream!.IsPrivate);
        Assert.Equal("function:module:Page.tsx#Card::presentation::Surface", card.LocalStream.Layout.StableIdentity);
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
