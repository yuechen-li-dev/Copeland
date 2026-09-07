using Aurelian.Profile.Graphics;
using Copeland.Profile;
using Copeland.TS.Profiles;
using Xunit;

namespace Aurelian.Profile.Graphics.Tests;

public sealed class ProfileNativeRealizationTests
{
    [Fact]
    public void M18AssetCompilesToOrderedNativeDrawPlan()
    {
        string root = RepositoryRoot();
        string assetDirectory = Path.Combine(root, "samples", "Integrations", "Aurelian.StrategyDemo", "Assets");
        string sourcePath = Path.Combine(assetDirectory, "hq.profile.tsx");
        string source = File.ReadAllText(Path.Combine(assetDirectory, "StrategyArt.ts"))
            + "\n"
            + File.ReadAllText(sourcePath);
        ProfileCompositionCompilationResult compiled = ProfileTsxCompiler.CompileComposition(source, sourcePath);

        Assert.True(compiled.Success, string.Join("; ", compiled.Diagnostics.Select(static item => item.Message)));
        ProfileNativeCompileResult native = ProfileNativeCompiler.Compile(compiled.Composition!, sourcePath);

        Assert.True(native.Success, string.Join("; ", native.Diagnostics.Select(static item => item.Message)));
        ProfileNativeCompositionResource resource = native.Resource!;
        Assert.Equal(compiled.CompositionHash, resource.CompositionHash);
        Assert.Equal(Enumerable.Range(0, resource.DrawPlan.Count), resource.DrawPlan.Select(static item => item.PainterIndex));
        Assert.Equal(
            compiled.Composition!.Layers.SelectMany(static layer => layer.Items).Select(static item => item.Id),
            resource.DrawPlan.Select(static item => item.ItemId));
        Assert.All(resource.DrawPlan, item => Assert.Equal(sourcePath, item.SourcePath));
        Assert.InRange(resource.GeometryCount, 1, resource.DrawPlan.Count);
        Assert.True(resource.MinimumFieldDimension >= ProfileNativeCompiler.DefaultMinimumShortAxis);
        Assert.True(resource.MaximumFieldDimension <= ProfileNativeCompiler.DefaultQualitySize);
        Assert.True(resource.AtlasWidth >= resource.MaximumFieldDimension);
        Assert.True(resource.AtlasHeight >= resource.MaximumFieldDimension);
    }

    [Fact]
    public void OpenContourFailsWithSourceLinkedDiagnostic()
    {
        var shape = new VectorShape([
            new VectorContour([
                new VectorLine(new VectorPoint(0, 0), new VectorPoint(4, 0)),
                new VectorLine(new VectorPoint(4, 0), new VectorPoint(4, 4)),
            ]),
        ]);
        var item = new ResolvedProfilePaintItem("open", shape, new ProfileStyle("#112233"), "ir", "contour");
        var composition = new ProfileComposition([new ProfileLayer(new ProfileLayerId("layer"), [item])]);

        ProfileNativeCompileResult result = ProfileNativeCompiler.Compile(composition, "assets/open.profile.tsx");

        ProfileNativeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProfileNativeDiagnosticCodes.OpenContour, diagnostic.Id);
        Assert.Equal("assets/open.profile.tsx", diagnostic.SourcePath);
        Assert.Equal("layer", diagnostic.LayerId);
        Assert.Equal("open", diagnostic.ItemId);
    }

    [Fact]
    public void UnsupportedPaintFailsInsteadOfApproximating()
    {
        VectorShape shape = ClosedSquare();
        var item = new ResolvedProfilePaintItem("paint", shape, new ProfileStyle("linear-gradient(red, blue)"), "ir", "contour");
        var composition = new ProfileComposition([new ProfileLayer(new ProfileLayerId("layer"), [item])]);

        ProfileNativeCompileResult result = ProfileNativeCompiler.Compile(composition, "assets/gradient.profile.tsx");

        ProfileNativeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProfileNativeDiagnosticCodes.UnsupportedPaint, diagnostic.Id);
        Assert.Contains("#RRGGBB/#RRGGBBAA", diagnostic.Message, StringComparison.Ordinal);
    }

    private static VectorShape ClosedSquare()
    {
        VectorPoint a = new(0, 0);
        VectorPoint b = new(4, 0);
        VectorPoint c = new(4, 4);
        VectorPoint d = new(0, 4);
        return new VectorShape([new VectorContour([
            new VectorLine(a, b),
            new VectorLine(b, c),
            new VectorLine(c, d),
            new VectorLine(d, a),
        ])]);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException();
    }
}
