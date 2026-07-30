using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutZM0Tests
{
    [Fact]
    public void Default_layer_bounded_z_and_authored_node_order_normalize_to_one_total_paint_key()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            layout Page<0px, 0px> {
                width: 100px;
                height: 100px;
                overlay root {
                    slot first { frame: { x: 0px, y: 0px, width: 100px, height: 100px }; }
                    slot second { frame: { x: 0px, y: 0px, width: 100px, height: 100px }; z: 0; }
                    slot tooltip { frame: { x: 0px, y: 0px, width: 100px, height: 100px }; z: 1; }
                }
            }
            """, "Page.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        NormalizedLayoutNode[] nodes = LayoutDataCompiler.Normalize(compilation.Layouts["Page"]).Root.Children.ToArray();
        Assert.All(nodes, node => Assert.Equal("default", node.LayerIdentity));
        Assert.Equal(0, nodes[0].LocalZ);
        Assert.Equal(0, nodes[1].LocalZ);
        Assert.True(nodes[1].PaintOrder.CompareTo(nodes[0].PaintOrder) > 0);
        Assert.True(nodes[2].PaintOrder.CompareTo(nodes[1].PaintOrder) > 0);

        LayoutReactArtifact first = LayoutDataCompiler.LowerForReact(compilation.Layouts["Page"]);
        LayoutReactArtifact second = LayoutDataCompiler.LowerForReact(compilation.Layouts["Page"]);
        Assert.Equal(first.Css, second.Css);
        Assert.Contains("isolation: isolate;", first.Css, StringComparison.Ordinal);
        Assert.Contains("z-index: 6;", first.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void Semantic_layer_rank_dominates_local_z_and_structural_descendants_inherit_their_container_layer()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            layers AppLayers {
                content;
                overlay;
                modal;
            }
            layout Dialog<0px, 0px> {
                layers: AppLayers;
                width: 100px;
                height: 100px;
                overlay root {
                    slot page { frame: { x: 0px, y: 0px, width: 100px, height: 100px }; layer: content; z: 5; }
                    overlay dialogHost { layer: modal; frame: { x: 0px, y: 0px, width: 100px, height: 100px };
                        slot dialog { frame: { x: 0px, y: 0px, width: 100px, height: 100px }; }
                    }
                    slot tooltip { frame: { x: 0px, y: 0px, width: 100px, height: 100px }; layer: modal; z: 1; }
                }
            }
            """, "Dialog.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundLayoutDeclaration layout = compilation.Layouts["Dialog"];
        NormalizedLayoutNode root = LayoutDataCompiler.Normalize(layout).Root;
        NormalizedLayoutNode page = root.Children.Single(node => node.Name == "page");
        NormalizedLayoutNode dialogHost = root.Children.Single(node => node.Name == "dialogHost");
        NormalizedLayoutNode dialog = Assert.Single(dialogHost.Children);
        NormalizedLayoutNode tooltip = root.Children.Single(node => node.Name == "tooltip");
        Assert.Equal("modal", dialog.LayerIdentity);
        Assert.True(dialogHost.PaintOrder.CompareTo(page.PaintOrder) > 0);
        Assert.True(tooltip.PaintOrder.CompareTo(dialogHost.PaintOrder) > 0);
    }

    [Theory]
    [InlineData("z: 6;", "COPE-LAYOUT-Z-0001")]
    [InlineData("z: -10;", "COPE-LAYOUT-Z-0001")]
    [InlineData("z: 1.5;", "COPE-LAYOUT-Z-0002")]
    [InlineData("z: runtime;", "COPE-LAYOUT-Z-0002")]
    public void Invalid_z_is_never_clamped_or_evaluated_at_runtime(string property, string diagnosticId)
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile($$"""
            layout Invalid<0px, 0px> {
                width: 1px;
                height: 1px;
                overlay root { slot content { frame: { x: 0px, y: 0px, width: 1px, height: 1px }; {{property}} } }
            }
            """, "Invalid.ts");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Layer_sets_are_module_symbols_and_can_be_imported_under_an_alias()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("ui/Layers.ts", "Layers.ts", """
                export layers AppLayers { content; overlay; }
                """),
            new CopelandProjectSource("ui/Page.ts", "Page.ts", """
                import { AppLayers as Layers } from "./Layers";
                export layout Page<0px, 0px> {
                    layers: Layers;
                    width: 1px;
                    height: 1px;
                    overlay root { slot content { frame: { x: 0px, y: 0px, width: 1px, height: 1px }; layer: overlay; } }
                }
                """),
        ]);

        Assert.Empty(project.Diagnostics);
        CopelandProjectModuleCompilation layers = project.Modules.Single(module => module.LogicalPath == "ui/Layers.ts");
        Assert.IsType<LayerSetSymbol>(layers.BoundCompilation!.ModuleScope!.Declarations["AppLayers"]);
        BoundLayoutDeclaration page = project.Modules.Single(module => module.LogicalPath == "ui/Page.ts").BoundCompilation!.Program.Layouts.Single();
        Assert.Equal("AppLayers", page.ResolvedLayerSet.Name);
    }
}
