using Machina.Core.Authoring;
using Machina.Core.Assets;
using Machina.Core.Lowering;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Presentation;
using Machina.VectorAssets;
using Xunit;

namespace Machina.Presentation.Tests;

public sealed class MachinaVectorIconPresentationM5Tests
{
    [Fact]
    public void UiIcon_LowersSemanticIdentityThroughOrdinaryLayout()
    {
        VectorIconMsdfArtifact artifact = VectorIconFixtures.CompileCanonical()["Settings"];
        var ui = UI.Icon(artifact.Identity, 24, id: "settings-icon", tint: ColorToken.Gold);
        UiLoweringResult lowering = UiLowerer.Lower(ui);
        LayoutDocument document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 24, 24));

        MachinaPresentationFrame frame = MachinaPresentationFrameBuilder.Build(
            lowering,
            resolved,
            new MachinaPresentationViewport(24, 24));

        MachinaVectorIconPresentationPrimitive primitive = Assert.Single(frame.Operations.OfType<MachinaVectorIconPresentationPrimitive>());
        Assert.Equal(artifact.Identity, primitive.Icon);
        Assert.Equal(new Rect(0, 0, 24, 24), primitive.DestinationRect);
        Assert.Equal(ColorToken.Gold, primitive.Tint);
    }

    [Fact]
    public void Primitive_UsesDestinationRectangleWithoutTextBaselineSemantics()
    {
        MachinaVectorIconId identity = VectorIconFixtures.CompileCanonical()["Play"].Identity;
        var primitive = new MachinaVectorIconPresentationPrimitive(
            "play-icon",
            identity,
            new Rect(10, 20, 32, 48),
            ColorToken.White,
            new Rect(12, 22, 20, 20));

        Assert.Equal(new Rect(10, 20, 32, 48), primitive.DestinationRect);
        Assert.Equal(new Rect(12, 22, 20, 20), primitive.ClipRect);
    }
}
