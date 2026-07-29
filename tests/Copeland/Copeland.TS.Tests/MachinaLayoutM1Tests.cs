using Copeland.TS.Mir.Machina;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class MachinaLayoutM1Tests
{
    [Fact]
    public void Absolute_ui_and_px_resolve_as_an_affine_parent_axis_expression()
    {
        MachinaView document = Machina.Root(
        [
            Machina.Container(
                [],
                Machina.Absolute(
                    MachinaLength.Normalized(0.25) - MachinaLength.Pixels(2),
                    MachinaLength.Normalized(0.5),
                    MachinaLength.Normalized(0.5) + MachinaLength.Pixels(8),
                    MachinaLength.Pixels(40))),
        ]);

        MachinaResolvedNode node = MachinaLayoutResolver.Resolve(document, new MachinaRect(0, 0, 400, 200)).Nodes.Single(item => item.Identity == "root/0");

        Assert.Equal(98, node.Frame.X);
        Assert.Equal(100, node.Frame.Y);
        Assert.Equal(208, node.Frame.Width);
        Assert.Equal(40, node.Frame.Height);
        Assert.Contains("0.25ui - 2px", node.GeometryExplanation[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400, 200, 40, 320, 20, 48)]
    [InlineData(800, 300, 80, 640, 20, 48)]
    public void Anchor_uses_exact_edge_equations_for_each_parent_profile(double parentWidth, double parentHeight, double expectedX, double expectedWidth, double expectedY, double expectedHeight)
    {
        MachinaView document = Machina.Root(
        [
            Machina.Container(
                [],
                Machina.Anchor(
                    left: MachinaLength.Normalized(0.1),
                    right: MachinaLength.Normalized(0.1),
                    top: MachinaLength.Pixels(20),
                    height: MachinaLength.Pixels(48))),
        ]);

        MachinaResolvedNode node = MachinaLayoutResolver.Resolve(document, new MachinaRect(0, 0, parentWidth, parentHeight)).Nodes.Single(item => item.Identity == "root/0");

        Assert.Equal(expectedX, node.Frame.X);
        Assert.Equal(expectedWidth, node.Frame.Width);
        Assert.Equal(expectedY, node.Frame.Y);
        Assert.Equal(expectedHeight, node.Frame.Height);
    }

    [Fact]
    public void VStack_resolves_fill_and_a_two_pixel_offset_without_reallocating_siblings()
    {
        MachinaView document = Machina.Root(
        [
            Machina.VStack(
                [
                    Machina.Text(
                        "Status",
                        Machina.Absolute(MachinaLength.Pixels(0), MachinaLength.Pixels(0), MachinaLength.Pixels(0), MachinaLength.Pixels(0)),
                        offset: new MachinaOffset(X: MachinaLength.Pixels(-2)),
                        mainTrack: Machina.Fixed(MachinaLength.Pixels(20))),
                    Machina.Button(
                        "Save",
                        "Save",
                        Machina.Absolute(MachinaLength.Pixels(0), MachinaLength.Pixels(0), MachinaLength.Pixels(0), MachinaLength.Pixels(0)),
                        mainTrack: Machina.Fill()),
                    Machina.Toggle(
                        false,
                        "Toggle",
                        Machina.Absolute(MachinaLength.Pixels(0), MachinaLength.Pixels(0), MachinaLength.Pixels(0), MachinaLength.Pixels(0)),
                        mainTrack: Machina.Fixed(MachinaLength.Pixels(30))),
                ],
                Machina.Anchor(
                    left: MachinaLength.Pixels(10),
                    right: MachinaLength.Pixels(10),
                    top: MachinaLength.Pixels(10),
                    bottom: MachinaLength.Pixels(10)),
                MachinaLength.Pixels(5)),
        ]);

        MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(document, new MachinaRect(0, 0, 300, 200));
        MachinaResolvedNode status = resolved.Nodes.Single(item => item.Identity == "root/0/0");
        MachinaResolvedNode save = resolved.Nodes.Single(item => item.Identity == "root/0/1");
        MachinaResolvedNode toggle = resolved.Nodes.Single(item => item.Identity == "root/0/2");

        Assert.Equal(8, status.Frame.X);
        Assert.Equal(10, status.Frame.Y);
        Assert.Equal(20, status.Frame.Height);
        Assert.Equal(35, save.Frame.Y);
        Assert.Equal(120, save.Frame.Height);
        Assert.Equal(160, toggle.Frame.Y);
        Assert.Equal(30, toggle.Frame.Height);
    }

    [Fact]
    public void Text_measurement_is_explicit_while_its_outer_frame_remains_resolved()
    {
        MachinaView document = Machina.Root(
        [
            Machina.Text(
                "A deliberately long paragraph that the browser may wrap inside this fixed outer box.",
                Machina.Absolute(
                    MachinaLength.Pixels(20),
                    MachinaLength.Pixels(20),
                    MachinaLength.Pixels(180),
                    MachinaLength.Pixels(80)),
                requiresTextMeasurement: true,
                source: new MachinaSourceSpan("Settings.ts", 42, 12)),
            Machina.Button(
                "Save",
                "Save",
                Machina.Absolute(
                    MachinaLength.Pixels(20),
                    MachinaLength.Pixels(120),
                    MachinaLength.Pixels(100),
                    MachinaLength.Pixels(40))),
        ]);

        MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(document, new MachinaRect(0, 0, 300, 200));

        Assert.Equal(MachinaMeasurementDependency.TextWrap, resolved.Nodes.Single(item => item.Identity == "root/0").MeasurementDependency);
        Assert.Null(resolved.Nodes.Single(item => item.Identity == "root/1").MeasurementDependency);
        Assert.Equal(120, resolved.Nodes.Single(item => item.Identity == "root/1").Frame.Y);
        Assert.Contains("source=Settings.ts:42+12", resolved.ToDebugText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_artifact_is_deterministic_deduplicates_immutable_styles_and_has_no_flex_or_grid_layout()
    {
        MachinaStyle buttonBase = new(
            Surface: new MachinaSurfaceStyle("#182238", MachinaLength.Pixels(8)),
            Text: new MachinaTextStyle("#ffffff", MachinaLength.Pixels(14), 600),
            Border: new MachinaBorderStyle(MachinaLength.Pixels(1), "#334155", "solid"));
        MachinaStyle primaryButton = buttonBase with
        {
            Surface = buttonBase.Surface! with { Fill = "#2563eb" },
        };
        MachinaView document = Machina.Root(
        [
            Machina.Button("Save", "Save", Machina.Absolute(MachinaLength.Pixels(10), MachinaLength.Pixels(10), MachinaLength.Pixels(100), MachinaLength.Pixels(40)), primaryButton),
            Machina.Button("Cancel", "Cancel", Machina.Absolute(MachinaLength.Pixels(120), MachinaLength.Pixels(10), MachinaLength.Pixels(100), MachinaLength.Pixels(40)), primaryButton),
        ]);

        MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(document, new MachinaRect(0, 0, 240, 80));
        MachinaBrowserArtifact first = MachinaBrowserLowerer.Lower(resolved);
        MachinaBrowserArtifact second = MachinaBrowserLowerer.Lower(resolved);

        Assert.Equal(first.Html, second.Html);
        Assert.Equal(first.Css, second.Css);
        Assert.Contains("<button", first.Html, StringComparison.Ordinal);
        Assert.Contains("data-machina-event=\"Save\"", first.Html, StringComparison.Ordinal);
        Assert.Equal(1, first.Css.Split("background: #2563eb;", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("display: flex", first.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display: grid", first.Css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("position: absolute", first.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void React_projection_reuses_native_resolved_geometry_and_leaves_semantic_elements_to_the_caller()
    {
        MachinaStyle panel = new(
            Surface: new MachinaSurfaceStyle("#0b1024", MachinaLength.Pixels(16)),
            Border: new MachinaBorderStyle(MachinaLength.Pixels(1), "#22d8ff", "solid"));
        MachinaView document = Machina.Root(
        [
            Machina.VStack(
            [
                Machina.Text("Heading", mainTrack: Machina.Fixed(MachinaLength.Pixels(40))),
                Machina.Button("Copy", "WebsiteEvent.Copy", mainTrack: Machina.Fill()),
            ],
            Machina.Anchor(
                left: MachinaLength.Pixels(20),
                right: MachinaLength.Pixels(20),
                top: MachinaLength.Pixels(16),
                bottom: MachinaLength.Pixels(16)),
            MachinaLength.Pixels(8),
            style: panel),
        ]);

        MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(document, new MachinaRect(0, 0, 320, 160));
        MachinaReactArtifact first = MachinaBrowserLowerer.LowerForReact(resolved);
        MachinaReactArtifact second = MachinaBrowserLowerer.LowerForReact(resolved);

        Assert.Equal(first.Css, second.Css);
        Assert.Equal(first.ClassesByIdentity.Count, second.ClassesByIdentity.Count);
        foreach ((string identity, string classes) in first.ClassesByIdentity)
        {
            Assert.Equal(classes, second.ClassesByIdentity[identity]);
        }
        Assert.StartsWith("m-node m-frame-root m-style-", first.ClassesByIdentity["root"], StringComparison.Ordinal);
        Assert.Contains("m-frame-root-0", first.ClassesByIdentity["root/0"], StringComparison.Ordinal);
        Assert.Contains("position: absolute", first.Css, StringComparison.Ordinal);
        Assert.DoesNotContain("display: flex", first.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display: grid", first.Css, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Ui_literal_range_is_checked_at_the_language_boundary()
    {
        MachinaLayoutException exception = Assert.Throws<MachinaLayoutException>(() => MachinaLength.Normalized(1.01));

        Assert.Equal("COPE-MACHINA-UI-0001", exception.Code);
    }
}
