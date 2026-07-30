using Copeland.TS.MachinaSource;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ContentFitM0Tests
{
    [Fact]
    public void Overflow_and_declared_text_policy_project_without_changing_sibling_boxes()
    {
        const string source = """
            stream Page<0px, 0px> {
                width: 400px;
                height: 300px;
                overlay root {
                    title: Title() { x: 20px; y: 20px; width: 300px; height: 96px; overflow: clip; fontSize: 48px; minFontSize: 32px; lines: 2; wrap: wrap; textFit: scaleDown; textFallback: ellipsis; }
                    actions: Actions() { x: 20px; y: 130px; width: 300px; height: 40px; overflow: visible; }
                    code: Code() { x: 20px; y: 190px; width: 300px; height: 80px; overflow: auto; }
                }
            }
            """;

        LayoutDataCompilation compilation = LayoutDataCompiler.Compile(source, "Page.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        LayoutInspectionDocument inspection = LayoutInspection.Create(compilation.Layouts["Page"], "Page.ts", ".");
        LayoutInspectionBox title = Assert.Single(inspection.Boxes, box => box.Name == "title");
        LayoutInspectionBox actions = Assert.Single(inspection.Boxes, box => box.Name == "actions");
        LayoutInspectionBox code = Assert.Single(inspection.Boxes, box => box.Name == "code");
        Assert.Equal("clip", title.OverflowPolicy);
        Assert.Equal("clip", title.OverflowX);
        Assert.Equal(48, title.TextPolicy!.PreferredFontSize);
        Assert.Equal(32, title.TextPolicy.MinimumFontSize);
        Assert.Equal("scaledown", title.TextPolicy.FitMode);
        Assert.Equal("visible", actions.OverflowPolicy);
        Assert.Equal("auto", code.OverflowPolicy);
        Assert.Equal(300, actions.Width!.Value);
        Assert.Equal(40, actions.Height!.Value);
    }

    [Fact]
    public void Text_fit_rejects_a_minimum_larger_than_preferred()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            stream Page<0px, 0px> {
                width: 100px; height: 100px;
                overlay root { title: Title() { x: 0px; y: 0px; width: 100px; height: 50px; fontSize: 20px; minFontSize: 24px; lines: 1; textFit: scaleDown; } }
            }
            """, "Invalid.ts");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEXT-FIT-0004");
    }
}
