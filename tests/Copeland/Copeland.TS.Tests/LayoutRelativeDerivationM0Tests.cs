using Copeland.TS.MachinaSource;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutRelativeDerivationM0Tests
{
    [Fact]
    public void Overlay_boxes_derive_center_alignment_adjacency_and_expansion_immutably()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            stream DialogScene<0px, 0px> {
                width: 1280px;
                height: 720px;
                overlay root {
                    dialog: Dialog() { width: 480px; height: 320px; } with centerIn(root);
                    tooltip: Tooltip() { width: 180px; height: 48px; } with placeAbove(dialog, 8px) with alignRight(dialog);
                    halo: Halo() { } with expandFrom(dialog, 16px);
                }
            }
            """, "DialogScene.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundLayoutDeclaration layout = compilation.Layouts["DialogScene"];
        LayoutInspectionDocument inspection = LayoutInspection.Create(layout, "DialogScene.ts", ".");
        LayoutInspectionBox dialog = Assert.Single(inspection.Boxes, box => box.Name == "dialog");
        LayoutInspectionBox tooltip = Assert.Single(inspection.Boxes, box => box.Name == "tooltip");
        LayoutInspectionBox halo = Assert.Single(inspection.Boxes, box => box.Name == "halo");

        Assert.Equal("relative-derived", dialog.OriginX.Kind);
        Assert.Equal(400, dialog.OriginX.Value!.Value);
        Assert.Equal(200, dialog.OriginY.Value!.Value);
        Assert.Equal(700, tooltip.OriginX.Value!.Value);
        Assert.Equal(144, tooltip.OriginY.Value!.Value);
        Assert.Equal(384, halo.OriginX.Value!.Value);
        Assert.Equal(184, halo.OriginY.Value!.Value);
        Assert.Equal(512, halo.Width!.Value);
        Assert.Equal(352, halo.Height!.Value);

        Assert.Equal(4, inspection.Derivations!.Count);
        Assert.Contains(inspection.Derivations, derivation => derivation.Transform == "CenterIn" && derivation.FieldsWritten.SequenceEqual(["x", "y"]));
        Assert.Contains(inspection.Derivations, derivation => derivation.Transform == "PlaceAbove" && derivation.FieldsWritten.SequenceEqual(["y"]));

        LayoutReactArtifact react = LayoutDataCompiler.LowerForReact(layout);
        Assert.Contains("left: 400px;", react.Css, StringComparison.Ordinal);
        Assert.Contains("top: 144px;", react.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void Relative_derivations_allow_forward_references_in_deterministic_dependency_order()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            stream Forward<0px, 0px> {
                width: 600px;
                height: 400px;
                overlay root {
                    tooltip: Tooltip() { width: 100px; height: 20px; } with placeBelow(dialog, 10px);
                    dialog: Dialog() { width: 200px; height: 100px; } with centerIn(root);
                }
            }
            """, "Forward.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        LayoutInspectionDocument inspection = LayoutInspection.Create(compilation.Layouts["Forward"], "Forward.ts", ".");
        LayoutInspectionBox tooltip = Assert.Single(inspection.Boxes, box => box.Name == "tooltip");
        Assert.Equal(260, tooltip.OriginY.Value!.Value);
        Assert.Equal(["PlaceBelow", "CenterIn"], inspection.Derivations!.OrderBy(item => item.AuthoredOrder).Select(item => item.Transform));
    }

    [Fact]
    public void Csv_derivation_lists_lower_through_the_same_immutable_row_derivation_plan_as_nested_with()
    {
        LayoutDataCompilation nested = LayoutDataCompiler.Compile("""
            stream Nested<0px, 0px> {
                width: 1280px;
                height: 720px;
                overlay root {
                    page: Page() { width: 1280px; height: 720px; }
                    dialog: Dialog() { width: 480px; height: 320px; } with centerIn(root);
                    tooltip: Tooltip() { width: 180px; height: 48px; } with placeAbove(dialog, 8px) with alignRight(dialog);
                    halo: Halo() { } with expandFrom(dialog, 16px);
                }
            }
            """, "Nested.ts");
        LayoutDataCompilation csv = LayoutDataCompiler.Compile("""
            stream Csv<0px, 0px> {
                width: 1280px;
                height: 720px;
                csv overlay root {
                    name, content, width, height, derivations;
                    page, Page(), 1280px, 720px, [];
                    dialog, Dialog(), 480px, 320px, [centerIn(root)];
                    tooltip, Tooltip(), 180px, 48px, [placeAbove(dialog, 8px), alignRight(dialog)];
                    halo, Halo(), derived, derived, [expandFrom(dialog, 16px)];
                }
            }
            """, "Csv.ts");

        Assert.True(nested.Success, string.Join(Environment.NewLine, nested.Diagnostics));
        Assert.True(csv.Success, string.Join(Environment.NewLine, csv.Diagnostics));
        LayoutInspectionDocument nestedInspection = LayoutInspection.Create(nested.Layouts["Nested"], "Nested.ts", ".");
        LayoutInspectionDocument csvInspection = LayoutInspection.Create(csv.Layouts["Csv"], "Csv.ts", ".");

        Assert.Equal(Geometry(nestedInspection), Geometry(csvInspection));
        Assert.Equal(
            nestedInspection.Derivations!.Select(Contract),
            csvInspection.Derivations!.Select(Contract));
        Assert.Equal(4, csvInspection.Derivations!.Count);
    }

    [Fact]
    public void Coherent_ui_derivations_preserve_the_existing_ui_identity_without_px_conversion()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            stream Logical<0px, 0px> {
                width: 1ui;
                height: 1ui;
                overlay root {
                    dialog: Dialog() { width: 0.5ui; height: 0.5ui; } with centerIn(root);
                }
            }
            """, "Logical.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        LayoutInspectionBox dialog = Assert.Single(LayoutInspection.Create(compilation.Layouts["Logical"], "Logical.ts", ".").Boxes, box => box.Name == "dialog");
        Assert.Equal(0.25, dialog.OriginX.Value!.Value);
        Assert.Equal("ui", dialog.OriginX.Value.Unit);
        Assert.Equal(0.25, dialog.OriginY.Value!.Value);
        Assert.Equal("ui", dialog.OriginY.Value.Unit);
    }

    [Fact]
    public void Csv_derivation_lists_share_empty_and_conflict_validation_laws()
    {
        LayoutDataCompilation empty = LayoutDataCompiler.Compile("""
            stream Empty<0px, 0px> {
                width: 100px;
                height: 80px;
                csv overlay root {
                    name, content, width, height, derivations;
                    page, Page(), 100px, 80px, [];
                }
            }
            """, "Empty.ts");
        LayoutDataCompilation conflict = LayoutDataCompiler.Compile("""
            stream Conflict<0px, 0px> {
                width: 100px;
                height: 80px;
                csv overlay root {
                    name, content, x, y, width, height, derivations;
                    dialog, Dialog(), 20px, 0px, 40px, 20px, [centerXIn(root)];
                }
            }
            """, "Conflict.ts");

        Assert.True(empty.Success, string.Join(Environment.NewLine, empty.Diagnostics));
        Assert.Empty(LayoutInspection.Create(empty.Layouts["Empty"], "Empty.ts", ".").Derivations!);
        Assert.Contains(conflict.Diagnostics, diagnostic => diagnostic.Id == "COPE-TABLE-DERIVE-0001");
    }

    [Theory]
    [InlineData("dialog: Dialog() { x: 20px; width: 100px; height: 80px; } with centerXIn(root);", "COPE-TABLE-DERIVE-0001")]
    [InlineData("dialog: Dialog() { width: 100px; height: 80px; } with alignLeft(root) with alignRight(root);", "COPE-TABLE-DERIVE-0004")]
    [InlineData("a: A() { width: 100px; height: 80px; } with placeRightOf(b, 8px); b: B() { width: 100px; height: 80px; } with placeRightOf(a, 8px);", "COPE-TABLE-DERIVE-0002")]
    [InlineData("dialog: Dialog() { width: fill; height: 80px; } with centerXIn(root);", "COPE-TABLE-DERIVE-0005")]
    [InlineData("dialog: Dialog() { width: 100px; height: 80px; } with placeRightOf(root, 8ui);", "COPE-TABLE-DERIVE-0006")]
    public void Relative_derivation_rejects_competing_cycles_unresolved_and_mixed_units(string boxes, string expectedDiagnostic)
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile($$"""
            stream Invalid<0px, 0px> {
                width: 600px;
                height: 400px;
                overlay root { {{boxes}} }
            }
            """, "Invalid.ts");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expectedDiagnostic);
    }

    private static IReadOnlyList<(string Name, double? X, double? Y, double? Width, double? Height)> Geometry(LayoutInspectionDocument inspection)
        => inspection.Boxes.Where(box => box.Name is not "root")
            .Select(box => (box.Name, box.OriginX.Value?.Value, box.OriginY.Value?.Value, box.Width?.Value, box.Height?.Value))
            .OrderBy(box => box.Name, StringComparer.Ordinal)
            .ToArray();

    private static (string Transform, string Source, IReadOnlyList<string> Reads, IReadOnlyList<string> Writes, double? Gap) Contract(LayoutInspectionDerivation derivation)
        => (derivation.Transform, derivation.SourceBoxId.Split('.').Last(), derivation.FieldsRead.Select(field => field.Split('.').Last()).ToArray(), derivation.FieldsWritten, derivation.GapOrPadding?.Value);
}
