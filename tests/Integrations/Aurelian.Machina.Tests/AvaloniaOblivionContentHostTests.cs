using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Machina.Presenter.Sample;
using Machina.Fonts.ReferenceRendering;
using Oblivion.Avalonia;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;
using System.Buffers.Binary;
using System.IO.Compression;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AvaloniaOblivionContentHostTests
{
    [Fact]
    public void Mature_document_host_consumes_one_supplied_light_palette()
    {
        AvaloniaOblivionContentStyle light = new(
            Surface: 0xFFFFFFFF,
            Foreground: 0x27272AFF,
            Heading: 0x09090BFF,
            Muted: 0x52525BFF,
            CodeSurface: 0xF4F4F5FF,
            Border: 0xD4D4D8FF,
            QuoteBorder: 0xA1A1AAFF,
            Link: 0x1D4ED8FF,
            Diagnostic: 0x92400EFF);
        OblivionCard card = CreateCard(
            OblivionCardKind.Note,
            OblivionMarkdownBody.CreateMarkdown("# Light heading\n\nReadable body.", "body/light.md"));
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));

        Border host = Assert.IsType<Border>(AvaloniaOblivionContentHost.Build(
            card,
            plan,
            new FakeDiagramRenderer(),
            Path.GetTempPath(),
            OblivionResolvedAppearance.Light,
            style: light));
        SolidColorBrush hostBackground = Assert.IsType<SolidColorBrush>(host.Background);
        SolidColorBrush hostBorder = Assert.IsType<SolidColorBrush>(host.BorderBrush);
        ScrollViewer scroll = Assert.IsType<ScrollViewer>(host.Child);
        StackPanel content = Assert.IsType<StackPanel>(scroll.Content);
        StackPanel document = Assert.IsType<StackPanel>(Assert.Single(content.Children));
        SelectableTextBlock heading = Assert.IsType<SelectableTextBlock>(document.Children[0]);
        SelectableTextBlock body = Assert.IsType<SelectableTextBlock>(document.Children[1]);

        Assert.Equal(Color.FromArgb(255, 255, 255, 255), hostBackground.Color);
        Assert.Equal(Color.FromArgb(255, 212, 212, 216), hostBorder.Color);
        Assert.Equal(Color.FromArgb(255, 9, 9, 11), Assert.IsType<SolidColorBrush>(heading.Foreground).Color);
        Assert.Equal(Color.FromArgb(255, 39, 39, 42), Assert.IsType<SolidColorBrush>(body.Foreground).Color);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
    }

    [Fact]
    public void Expanded_markdown_host_owns_bounded_vertical_scroll_and_selectable_content()
    {
        OblivionCard card = CreateCard(
            OblivionCardKind.Note,
            OblivionMarkdownBody.CreateMarkdown("# Heading\n\nReadable body.", "body/readme.md"));
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));

        Border host = Assert.IsType<Border>(AvaloniaOblivionContentHost.Build(
            card,
            plan,
            new FakeDiagramRenderer(),
            Path.GetTempPath(),
            OblivionResolvedAppearance.Dark));
        ScrollViewer scroll = Assert.IsType<ScrollViewer>(host.Child);

        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.True(host.ClipToBounds);
        Assert.NotNull(scroll.Content);
    }

    [Fact]
    public void Markdown_heading_line_box_is_tall_enough_for_lowercase_descenders()
    {
        OblivionCard card = CreateCard(
            OblivionCardKind.Note,
            OblivionMarkdownBody.CreateMarkdown("# Typography\n\nReadable body.", "body/typography.md"));
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));

        Border host = Assert.IsType<Border>(AvaloniaOblivionContentHost.Build(
            card,
            plan,
            new FakeDiagramRenderer(),
            Path.GetTempPath(),
            OblivionResolvedAppearance.Dark));
        ScrollViewer scroll = Assert.IsType<ScrollViewer>(host.Child);
        StackPanel content = Assert.IsType<StackPanel>(scroll.Content);
        StackPanel document = Assert.IsType<StackPanel>(Assert.Single(content.Children));
        SelectableTextBlock heading = Assert.IsType<SelectableTextBlock>(document.Children[0]);

        Assert.Equal(28, heading.FontSize);
        Assert.Equal(36, heading.LineHeight);
        Assert.True(heading.LineHeight > heading.FontSize);
    }

    [Fact]
    public void Read_only_code_host_allows_horizontal_overflow_without_becoming_an_editor()
    {
        OblivionCard card = CreateCard(
            OblivionCardKind.CodeFact,
            OblivionMarkdownBody.CreatePlain("public static void Main() { }")) with
        {
            Provenance = new OblivionProvenance(
                OblivionProvenanceSourceKind.WorkspaceAsset,
                "src/Program.cs"),
        };
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));

        Border host = Assert.IsType<Border>(AvaloniaOblivionContentHost.Build(
            card,
            plan,
            new FakeDiagramRenderer(),
            Path.GetTempPath(),
            OblivionResolvedAppearance.Dark));
        ScrollViewer scroll = Assert.IsType<ScrollViewer>(host.Child);

        Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(
            OblivionContentFocusContract.PresenterOwnsSelectionAndCopy,
            Assert.Single(plan.Items).FocusContract);
    }

    [Fact]
    public void Presenter_png_writer_emits_a_standard_zlib_png_that_avalonia_can_decode()
    {
        string path = Path.Combine(Path.GetTempPath(), "machina-m19d-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            RgbaImage image = new(2, 2);
            image.SetPixel(0, 0, new Rgba32(255, 0, 0, 255));
            image.SetPixel(1, 0, new Rgba32(0, 255, 0, 255));
            image.SetPixel(0, 1, new Rgba32(0, 0, 255, 255));
            image.SetPixel(1, 1, new Rgba32(255, 255, 255, 255));

            PresenterPngWriter.Write(path, image);
            byte[] png = File.ReadAllBytes(path);
            byte[] compressed = ReadPngChunk(png, "IDAT");
            using MemoryStream compressedStream = new(compressed);
            using ZLibStream zlib = new(compressedStream, CompressionMode.Decompress);
            using MemoryStream scanlines = new();
            zlib.CopyTo(scanlines);

            Assert.Equal((2 * 4 + 1) * 2, scanlines.Length);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Inspector_raw_source_reuses_the_mature_selectable_code_surface()
    {
        OblivionCard card = CreateCard(
            OblivionCardKind.Note,
            OblivionMarkdownBody.CreateMarkdown("# Inspector\n\nSource body.", "body/inspector.md"));

        Border host = Assert.IsType<Border>(AvaloniaOblivionContentHost.BuildInspectorRawSource(card));
        ScrollViewer scroll = Assert.IsType<ScrollViewer>(host.Child);

        Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.True(host.ClipToBounds);
    }

    [Fact]
    public void Realized_mermaid_png_is_presented_inline_with_uniform_fit_and_bounded_scroll()
    {
        AppBuilder.Configure<Application>()
            .UsePlatformDetect()
            .SetupWithoutStarting();
        string path = Path.Combine(
            Path.GetTempPath(),
            "machina-m19e-diagram-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            RgbaImage diagram = new(320, 120);
            PresenterPngWriter.Write(path, diagram);
            OblivionCard card = CreateCard(
                OblivionCardKind.Note,
                OblivionMarkdownBody.CreateMarkdown(
                    "# Architecture\n\n```mermaid\nflowchart LR\n  Source --> Derived\n```",
                    "body/architecture.md"));
            OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
                card,
                new OblivionCardViewState(true, 0));

            Border host = Assert.IsType<Border>(AvaloniaOblivionContentHost.Build(
                card,
                plan,
                new SuccessfulDiagramRenderer(path),
                Path.GetTempPath(),
                OblivionResolvedAppearance.Light,
                "workspace",
                "architecture"));
            ScrollViewer scroll = Assert.IsType<ScrollViewer>(host.Child);
            StackPanel content = Assert.IsType<StackPanel>(scroll.Content);
            Control realizedDiagram = content.Children[1];
            if (realizedDiagram is SelectableTextBlock diagnostic)
            {
                Assert.Fail(diagnostic.Text);
            }

            Image image = Assert.IsType<Image>(realizedDiagram);

            Assert.Equal(Stretch.Uniform, image.Stretch);
            Assert.Equal(520, image.MaxHeight);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Mermaid_render_failure_keeps_source_in_inline_diagnostic_fallback()
    {
        OblivionCard card = CreateCard(
            OblivionCardKind.Note,
            OblivionMarkdownBody.CreateMarkdown(
                "```mermaid\nflowchart LR\n  Durable --> Visible\n```",
                "body/fallback.md"));
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));

        Border host = Assert.IsType<Border>(AvaloniaOblivionContentHost.Build(
            card,
            plan,
            new FakeDiagramRenderer(),
            Path.GetTempPath(),
            OblivionResolvedAppearance.Dark));
        ScrollViewer scroll = Assert.IsType<ScrollViewer>(host.Child);
        StackPanel content = Assert.IsType<StackPanel>(scroll.Content);
        SelectableTextBlock fallback = Assert.IsType<SelectableTextBlock>(content.Children[1]);

        Assert.Contains("Durable --> Visible", fallback.Text);
    }

    [Theory]
    [InlineData(OblivionResolvedAppearance.Light)]
    [InlineData(OblivionResolvedAppearance.Dark)]
    public void Diagram_host_passes_resolved_appearance_to_render_realization(
        OblivionResolvedAppearance appearance)
    {
        OblivionCard card = CreateCard(
            OblivionCardKind.Note,
            OblivionMarkdownBody.CreateMarkdown(
                "```mermaid\nflowchart LR\n  Durable --> Visible\n```",
                "body/appearance.md"));
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));
        FakeDiagramRenderer renderer = new();

        AvaloniaOblivionContentHost.Build(
            card,
            plan,
            renderer,
            Path.GetTempPath(),
            appearance);

        Assert.Equal(appearance, Assert.Single(renderer.Requests).Appearance);
    }

    private static byte[] ReadPngChunk(byte[] png, string requestedType)
    {
        int offset = 8;
        while (offset + 12 <= png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == requestedType)
            {
                return png.AsSpan(offset + 8, length).ToArray();
            }

            offset += 12 + length;
        }

        throw new InvalidDataException($"PNG chunk '{requestedType}' was not found.");
    }

    private static OblivionCard CreateCard(OblivionCardKind kind, OblivionCardBody body)
    {
        return new OblivionCard(
            new OblivionCardId("host-card"),
            kind,
            OblivionCardStatus.Passing,
            "Host card",
            "Mature presenter host test",
            [],
            body,
            [],
            [],
            OblivionProvenance.Unknown);
    }

    private sealed class FakeDiagramRenderer : IOblivionDiagramRenderer
    {
        public List<OblivionDiagramRenderRequest> Requests { get; } = [];

        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            Requests.Add(request);
            return new OblivionDiagramRenderResult(
                Succeeded: false,
                Renderer: "fake",
                RendererVersion: "test",
                SourceHash: "hash",
                RenderedPath: null,
                MediaType: null,
                Diagnostics: []);
        }
    }

    private sealed class SuccessfulDiagramRenderer : IOblivionDiagramRenderer
    {
        private readonly string _path;

        public SuccessfulDiagramRenderer(string path)
        {
            _path = path;
        }

        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            return new OblivionDiagramRenderResult(
                true,
                "fake",
                "test",
                OblivionMermaidHashing.ComputeSourceHash(request.Source),
                _path,
                "image/png",
                []);
        }
    }
}
