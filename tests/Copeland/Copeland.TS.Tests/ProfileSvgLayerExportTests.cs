using System.Xml.Linq;
using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileSvgLayerExportTests
{
    [Fact]
    public void Ordinary_style_records_with_edits_change_paint_without_changing_geometry()
    {
        const string source = """
            const BaseStyle: ProfileStyle = { fill: "#193747" };
            const BikeStyle: ProfileStyle = BaseStyle with { fill: "#238f91" };
            export default (
                <Profile name="Styled" base={Circle({ radius: 12.0 })} style={BikeStyle}>
                    {Yield(Base)}
                </Profile>
            );
            """;
        ProfileCompilationResult teal = ProfileTsxCompiler.Compile(source);
        ProfileCompilationResult gold = ProfileTsxCompiler.Compile(source.Replace("#238f91", "#e6a52e", StringComparison.Ordinal));
        Assert.True(teal.Success);
        Assert.True(gold.Success);
        Assert.Equal(teal.ProfileIrHash, gold.ProfileIrHash);
        Assert.Equal(teal.CanonicalContourHash, gold.CanonicalContourHash);
        Assert.NotEqual(teal.Svg, gold.Svg);
        Assert.Equal("#238f91", XElement.Parse(teal.Svg!).Elements().Single().Attribute("fill")!.Value);
        string layered = ProfileSvgExporter.ExportLayers([new("Styled", teal.Shape!, teal.Style)]);
        Assert.Equal("#238f91", XElement.Parse(layered).Elements().Single().Attribute("fill")!.Value);
    }

    [Theory]
    [InlineData("const Style: ProfileStyle = { fill: \"url(external)\" };", "COPE-PROFILE-TSX-0051")]
    [InlineData("const Style: string = \"red\";", "COPE-PROFILE-TSX-0050")]
    public void Unsupported_style_values_have_explicit_diagnostics(string declaration, string diagnosticId)
    {
        string source = $$"""
            {{declaration}}
            export default (
                <Profile name="Invalid" base={Circle({ radius: 2.0 })} style={Style}>
                    {Yield(Base)}
                </Profile>
            );
            """;
        ProfileCompilationResult result = ProfileTsxCompiler.Compile(source);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item => item.Id == diagnosticId);
    }

    [Fact]
    public void Overlapping_layers_preserve_individual_paths_holes_and_paint_order()
    {
        ProfileCompilationResult ring = Compile("""
            <Profile name="Ring" base={Circle({ radius: 20.0 })}>
                {Hole({ id: "Hub", as: "Cut", radius: 12.0 })}
                {Yield(Cut)}
            </Profile>
            """);
        ProfileCompilationResult bar = Compile("""
            <Profile name="Bar" base={Rectangle({ width: 60.0, height: 4.0 })}>
                {Yield(Base)}
            </Profile>
            """);
        ProfileSvgLayer[] layers = [new("Ring", ring.Shape!), new("Bar", bar.Shape!)];

        string svg = ProfileSvgExporter.ExportLayers(layers, padding: 5);
        XElement root = XElement.Parse(svg);
        XElement[] paths = root.Elements().ToArray();

        Assert.Equal("-35 -25 70 50", root.Attribute("viewBox")!.Value);
        Assert.Equal(new[] { "Ring", "Bar" }, paths.Select(path => path.Attribute("id")!.Value));
        Assert.Equal(PathData(ring.Svg!), paths[0].Attribute("d")!.Value);
        Assert.Equal(PathData(bar.Svg!), paths[1].Attribute("d")!.Value);
        Assert.Equal(svg, ProfileSvgExporter.ExportLayers(layers, padding: 5));
        Assert.NotEqual(svg, ProfileSvgExporter.ExportLayers(layers.Reverse().ToArray(), padding: 5));
    }

    [Fact]
    public void Layer_names_are_xml_escaped_without_changing_identity()
    {
        ProfileCompilationResult circle = Compile("""
            <Profile name="Circle" base={Circle({ radius: 2.0 })}>
                {Yield(Base)}
            </Profile>
            """);
        const string name = "Wing<&\"detail";
        string svg = ProfileSvgExporter.ExportLayers([new(name, circle.Shape!)]);
        Assert.Equal(name, XElement.Parse(svg).Elements().Single().Attribute("id")!.Value);
    }

    [Fact]
    public void Invalid_layer_collections_and_padding_fail_at_export_boundary()
    {
        ProfileCompilationResult circle = Compile("""
            <Profile name="Circle" base={Circle({ radius: 2.0 })}>
                {Yield(Base)}
            </Profile>
            """);
        var layer = new ProfileSvgLayer("Circle", circle.Shape!);
        Assert.Throws<ArgumentException>(() => ProfileSvgExporter.ExportLayers([]));
        Assert.Throws<ArgumentException>(() => ProfileSvgExporter.ExportLayers([layer, layer]));
        Assert.Throws<ArgumentException>(() => ProfileSvgExporter.ExportLayers([layer with { Name = " " }]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfileSvgExporter.ExportLayers([layer], double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfileSvgExporter.ExportLayers([layer], -1));
    }

    private static string PathData(string svg)
    {
        return XElement.Parse(svg).Elements().Single().Attribute("d")!.Value;
    }

    private static ProfileCompilationResult Compile(string profile)
    {
        ProfileCompilationResult result = ProfileTsxCompiler.Compile($"export default ({profile});");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
        return result;
    }
}
