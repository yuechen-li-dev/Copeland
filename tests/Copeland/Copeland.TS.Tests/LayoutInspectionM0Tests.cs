using Copeland.TS.MachinaSource;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutInspectionM0Tests
{
    [Fact]
    public void Inspection_projects_normalized_constraints_topology_and_source_without_backend_lowering()
    {
        const string source = """
            layers AppLayers { content; modal; }
            layout DialogScene<0px, 0px> {
                width: 320px;
                height: 180px;
                layers: AppLayers;
                overlay root {
                    slot page { x: 0px; y: 0px; width: 320px; height: 180px; layer: content; z: 5; }
                    slot dialog { x: 20px; y: 20px; width: 260px; height: 120px; layer: modal; z: -1; }
                }
            }
            """;

        LayoutDataCompilation compilation = LayoutDataCompiler.Compile(source, "DialogScene.ts");
        Assert.True(compilation.Success);
        LayoutInspectionDocument inspection = LayoutInspection.Create(compilation.Layouts["DialogScene"], "DialogScene.ts", ".");
        LayoutInspectionBox page = Assert.Single(inspection.Boxes, box => box.Name == "page");
        LayoutInspectionBox dialog = Assert.Single(inspection.Boxes, box => box.Name == "dialog");

        Assert.Equal(LayoutInspection.SchemaVersion, inspection.SchemaVersion);
        Assert.Equal("DialogScene.root.page", page.SemanticPath);
        Assert.Equal("DialogScene.root", page.Parent);
        Assert.Equal("fixed", page.Width!.Kind);
        Assert.Equal("fixed", dialog.Width!.Kind);
        Assert.Equal(260, dialog.Width.Value);
        Assert.Equal("px", dialog.Width.Unit);
        Assert.Equal("modal", dialog.Layer);
        Assert.Equal(1, dialog.LayerRank);
        Assert.Equal(-1, dialog.Z);
        Assert.NotNull(dialog.Source);
        Assert.True(page.PaintKey.CompareTo(dialog.PaintKey) < 0);
    }

    [Fact]
    public void Inspection_keeps_ui_units_and_inferred_stream_contracts()
    {
        const string source = """
            stream Shell<10ui, 0px> {
                width: 1ui;
                height: 20px;
                body: App() { width: fill; height: fit; }
            }
            """;
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile(source, "Shell.ts");
        Assert.True(compilation.Success);

        LayoutInspectionDocument inspection = LayoutInspection.Create(compilation.Layouts["Shell"], "Shell.ts", ".");
        Assert.Equal("ShellShape", inspection.Layout.Contract);
        Assert.True(inspection.Layout.Conformance);
        Assert.Equal("ui", inspection.Layout.OriginX.Value!.Unit);
        LayoutInspectionBox body = Assert.Single(inspection.Boxes, box => box.Name == "body");
        Assert.Equal("fill", body.Width!.Kind);
        Assert.Equal("fit", body.Height!.Kind);
    }

    [Fact]
    public void Nested_and_csv_overlay_forms_have_the_same_canonical_box_rows()
    {
        LayoutDataCompilation nested = LayoutDataCompiler.Compile("""
            stream Nested<0px, 0px> {
                width: 320px;
                height: 180px;
                overlay root {
                    page: Page() { frame: { x: 0px, y: 0px, width: 320px, height: 180px }; }
                    dialog: Dialog() { frame: { x: 20px, y: 20px, width: 260px, height: 120px }; z: 1; }
                }
            }
            """, "Nested.ts");
        LayoutDataCompilation csv = LayoutDataCompiler.Compile("""
            stream Csv<0px, 0px> {
                width: 320px;
                height: 180px;
                csv overlay root {
                    name, content, x, y, width, height, z;
                    page, Page(), 0px, 0px, 320px, 180px, 0;
                    dialog, Dialog(), 20px, 20px, 260px, 120px, 1;
                }
            }
            """, "Csv.ts");
        Assert.True(nested.Success, string.Join(Environment.NewLine, nested.Diagnostics));
        Assert.True(csv.Success, string.Join(Environment.NewLine, csv.Diagnostics));

        LayoutInspectionDocument nestedRows = LayoutInspection.Create(nested.Layouts["Nested"], "Nested.ts", ".");
        LayoutInspectionDocument csvRows = LayoutInspection.Create(csv.Layouts["Csv"], "Csv.ts", ".");
        Assert.Equal(CanonicalRows(nestedRows), CanonicalRows(csvRows));
    }

    [Fact]
    public void Explicit_layout_and_stream_column_forms_have_the_same_canonical_topology()
    {
        LayoutDataCompilation explicitLayout = LayoutDataCompiler.Compile("""
            layout Explicit<0px, 0px> {
                width: 800px;
                height: 600px;
                column root {
                    slot header { height: 64px; }
                    slot content { height: fill; }
                    slot footer { height: 48px; }
                }
            }
            """, "Explicit.ts");
        LayoutDataCompilation stream = LayoutDataCompiler.Compile("""
            stream Stream<0px, 0px> {
                width: 800px;
                height: 600px;
                header: Header() { height: 64px; }
                content: Content() { height: fill; }
                footer: Footer() { height: 48px; }
            }
            """, "Stream.ts");
        Assert.True(explicitLayout.Success, string.Join(Environment.NewLine, explicitLayout.Diagnostics));
        Assert.True(stream.Success, string.Join(Environment.NewLine, stream.Diagnostics));

        Assert.Equal(
            CanonicalRows(LayoutInspection.Create(explicitLayout.Layouts["Explicit"], "Explicit.ts", ".")),
            CanonicalRows(LayoutInspection.Create(stream.Layouts["Stream"], "Stream.ts", ".")));
    }

    private static IReadOnlyList<(string Name, string? Parent, string Kind, string X, string Y, string Width, string Height, int Z, int Order, int Paint)> CanonicalRows(LayoutInspectionDocument document)
        => document.Boxes.Select(box => (
            box.Name,
            box.Parent?.Split('.').Last(),
            box.Kind,
            LayoutInspection.FormatLength(box.OriginX.Value),
            LayoutInspection.FormatLength(box.OriginY.Value),
            LayoutInspection.FormatLength(box.Width),
            LayoutInspection.FormatLength(box.Height),
            box.Z,
            box.AuthoredOrder,
            box.PaintOrder)).ToArray();
}
