using System.Xml.Linq;
using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProfileLayerCompositionM1Tests
{
    [Fact]
    public void Pelican_bicycle_uses_five_semantic_layers_and_no_numeric_selector()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "samples",
            "copeland-ts",
            "profile-pelican-bicycle",
            "pelican-bicycle.profile.tsx"));

        ProfileCompositionCompilationResult result = Compile(source);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(
            new[] { "Wheels", "Bicycle Frame", "Pelican Legs", "Pelican Body", "Pelican Details" },
            result.Composition!.Layers.Select(layer => layer.Id.Name));
        Assert.Equal(17, result.Composition.Layers.Sum(layer => layer.Items.Count));
        Assert.DoesNotContain("const Layer: int", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildLayer(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_layers_group_multiple_profiles_and_preserve_source_paint_order()
    {
        ProfileCompositionCompilationResult result = Compile("""
            const Ink: ProfileStyle = { fill: "#193747" };
            const Bike: ProfileStyle = Ink with { fill: "#238f91" };

            function BicycleLayers(ink: ProfileStyle, bike: ProfileStyle): ProfileLayer[] {
                return [
                    Layer("Wheels", [
                        Profile({ name: "RearWheel", shape: Circle({ radius: 12.0, x: -20.0 }), operations: [], yieldState: "Base", style: ink }),
                        Profile({ name: "FrontWheel", shape: Circle({ radius: 12.0, x: 20.0 }), operations: [], yieldState: "Base", style: ink })
                    ]),
                    Layer("Frame", [
                        Profile({ name: "FrameBar", shape: Rectangle({ width: 50.0, height: 4.0 }), operations: [], yieldState: "Base", style: bike })
                    ])
                ];
            }

            export default (Layers(BicycleLayers(Ink, Bike)));
            """);

        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal(new[] { "Wheels", "Frame" }, result.Composition!.Layers.Select(layer => layer.Id.Name));
        Assert.Equal(new[] { "RearWheel", "FrontWheel" }, result.Composition.Layers[0].Items.Select(item => item.Id));
        XElement[] groups = XElement.Parse(result.Svg!).Elements().ToArray();
        Assert.Equal(new[] { "wheels", "frame" }, groups.Select(group => group.Attribute("id")!.Value));
        Assert.Equal(new[] { "RearWheel", "FrontWheel" }, groups[0].Elements().Select(path => path.Attribute("data-profile-id")!.Value));
    }

    [Fact]
    public void Reordering_layers_changes_semantic_and_svg_hashes_but_not_geometry_hash()
    {
        const string declaration = """
            const Back: ProfileLayer = Layer("Back", [
                Profile({ name: "CircleItem", shape: Circle({ radius: 10.0 }), operations: [], yieldState: "Base" })
            ]);
            const Front: ProfileLayer = Layer("Front", [
                Profile({ name: "BarItem", shape: Rectangle({ width: 30.0, height: 4.0 }), operations: [], yieldState: "Base" })
            ]);
            """;
        ProfileCompositionCompilationResult first = Compile(declaration + "\nexport default (Layers([Back, Front]));");
        ProfileCompositionCompilationResult second = Compile(declaration + "\nexport default (Layers([Front, Back]));");

        Assert.True(first.Success, Diagnostics(first));
        Assert.True(second.Success, Diagnostics(second));
        Assert.NotEqual(first.CompositionHash, second.CompositionHash);
        Assert.NotEqual(first.Svg, second.Svg);
        Assert.Equal(first.CanonicalGeometryHash, second.CanonicalGeometryHash);
        Assert.Equal(
            first.Composition!.Layers.SelectMany(layer => layer.Items).OrderBy(item => item.Id).Select(item => item.ProfileIrHash),
            second.Composition!.Layers.SelectMany(layer => layer.Items).OrderBy(item => item.Id).Select(item => item.ProfileIrHash));
    }

    [Fact]
    public void Moving_profile_between_layers_preserves_resolved_profile_identity()
    {
        const string profile = "Profile({ name: \"Wing\", shape: Ellipse({ radiusX: 8.0, radiusY: 3.0 }), operations: [], yieldState: \"Base\" })";
        ProfileCompositionCompilationResult behind = Compile($"export default (Layers([Layer(\"Behind\", [{profile}]), Layer(\"Body\", [])]));");
        ProfileCompositionCompilationResult inFront = Compile($"export default (Layers([Layer(\"Behind\", []), Layer(\"Body\", [{profile}])]));");

        Assert.True(behind.Success, Diagnostics(behind));
        Assert.True(inFront.Success, Diagnostics(inFront));
        ResolvedProfilePaintItem first = Assert.Single(behind.Composition!.Layers.SelectMany(layer => layer.Items));
        ResolvedProfilePaintItem second = Assert.Single(inFront.Composition!.Layers.SelectMany(layer => layer.Items));
        Assert.Equal(first.ProfileIrHash, second.ProfileIrHash);
        Assert.Equal(first.CanonicalContourHash, second.CanonicalContourHash);
        Assert.Equal(behind.CanonicalGeometryHash, inFront.CanonicalGeometryHash);
        Assert.NotEqual(behind.CompositionHash, inFront.CompositionHash);
    }

    [Fact]
    public void Empty_layers_erase_and_duplicate_layer_names_reject()
    {
        ProfileCompositionCompilationResult empty = Compile("""
            export default (Layers([
                Layer("Guides", []),
                Layer("Ink", [Profile({ name: "Dot", shape: Circle({ radius: 2.0 }), operations: [], yieldState: "Base" })])
            ]));
            """);
        ProfileCompositionCompilationResult duplicate = Compile("""
            export default (Layers([
                Layer("Ink", [Profile({ name: "A", shape: Circle({ radius: 2.0 }), operations: [], yieldState: "Base" })]),
                Layer("Ink", [Profile({ name: "B", shape: Circle({ radius: 3.0 }), operations: [], yieldState: "Base" })])
            ]));
            """);

        Assert.True(empty.Success, Diagnostics(empty));
        Assert.Equal("Ink", Assert.Single(empty.Composition!.Layers).Id.Name);
        Assert.False(duplicate.Success);
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-COMPOSE-0005");
    }

    [Fact]
    public void Svg_group_and_profile_names_are_sanitized_deterministically()
    {
        ProfileCompositionCompilationResult result = Compile("""
            export default (Layers([
                Layer("Pelican Details", [
                    Profile({ name: "Head & Eye", shape: Circle({ radius: 2.0 }), operations: [], yieldState: "Base" })
                ]),
                Layer("123 foreground", [
                    Profile({ name: "Bill", shape: Rectangle({ width: 8.0, height: 2.0 }), operations: [], yieldState: "Base" })
                ])
            ]));
            """);

        Assert.True(result.Success, Diagnostics(result));
        XElement[] groups = XElement.Parse(result.Svg!).Elements().ToArray();
        Assert.Equal("pelican-details", groups[0].Attribute("id")!.Value);
        Assert.Equal("pelican-details-head-eye", groups[0].Elements().Single().Attribute("id")!.Value);
        Assert.StartsWith("layer-", groups[1].Attribute("id")!.Value);
        Assert.Equal(result.Svg, Compile("""
            export default (Layers([
                Layer("Pelican Details", [
                    Profile({ name: "Head & Eye", shape: Circle({ radius: 2.0 }), operations: [], yieldState: "Base" })
                ]),
                Layer("123 foreground", [
                    Profile({ name: "Bill", shape: Rectangle({ width: 8.0, height: 2.0 }), operations: [], yieldState: "Base" })
                ])
            ]));
            """).Svg);
    }

    [Fact]
    public void Unrelated_default_values_and_invalid_styles_are_rejected_by_type_or_profile_diagnostics()
    {
        ProfileCompositionCompilationResult unrelated = Compile("export default ([Circle({ radius: 2.0 })]);");
        ProfileCompositionCompilationResult invalidStyle = Compile("""
            const Bad: ProfileStyle = { fill: "red" };
            export default (Layers([Layer("Ink", [
                Profile({ name: "Dot", shape: Circle({ radius: 2.0 }), operations: [], yieldState: "Base", style: Bad })
            ])]));
            """);

        Assert.False(unrelated.Success);
        Assert.Contains(unrelated.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0002" || diagnostic.Id == "COPE-PROFILE-COMPOSE-0002");
        Assert.False(invalidStyle.Success);
        Assert.Contains(invalidStyle.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-TSX-0051");
    }

    private static ProfileCompositionCompilationResult Compile(string source)
        => ProfileTsxCompiler.CompileComposition(source, "Composition.profile.tsx");

    private static string Diagnostics(ProfileCompositionCompilationResult result)
        => string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.TS.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
