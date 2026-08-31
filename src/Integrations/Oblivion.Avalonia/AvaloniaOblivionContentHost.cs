using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Copeland.Markdown;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.Avalonia;

public sealed record AvaloniaOblivionContentStyle(
    uint Surface,
    uint Foreground,
    uint Heading,
    uint Muted,
    uint CodeSurface,
    uint Border,
    uint QuoteBorder,
    uint Link,
    uint Diagnostic)
{
    public static AvaloniaOblivionContentStyle Dark { get; } = new(
        0x0F172AFF,
        0xE2E8F0FF,
        0xFFFFFFFF,
        0xA8B8CCFF,
        0x111827FF,
        0x475569FF,
        0x64748BFF,
        0x93C5FDFF,
        0xFBBF24FF);
}

public static class AvaloniaOblivionContentHost
{
    private static readonly FontFamily Monospace = new("Cascadia Mono, Consolas, monospace");

    public static Control Build(
        OblivionCard card,
        OblivionContentPresentationPlan plan,
        IOblivionDiagramRenderer diagramRenderer,
        string diagramOutputDirectory,
        OblivionResolvedAppearance resolvedAppearance,
        string? workspaceId = null,
        string? pageId = null,
        double? maximumReadableWidth = null,
        AvaloniaOblivionContentStyle? style = null,
        OblivionDiagramViewportState? diagramViewportState = null,
        Action<OblivionDiagramViewportState>? diagramViewportStateChanged = null,
        bool fillDiagramViewport = false)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(diagramRenderer);

        AvaloniaOblivionContentStyle effectiveStyle = style ?? AvaloniaOblivionContentStyle.Dark;
        if (fillDiagramViewport &&
            plan.Items.Count == 1 &&
            plan.Items[0].PresenterKind == OblivionContentPresenterKind.ExternalMermaidRenderer)
        {
            Control diagram = BuildDiagram(
                card,
                plan.Items[0],
                diagramRenderer,
                diagramOutputDirectory,
                resolvedAppearance,
                workspaceId,
                pageId,
                effectiveStyle,
                diagramViewportState ?? OblivionDiagramViewportState.Fit,
                diagramViewportStateChanged);
            return new Border
            {
                Background = ToBrush(effectiveStyle.Surface),
                BorderBrush = ToBrush(effectiveStyle.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Padding = new Thickness(8),
                Child = diagram,
            };
        }

        StackPanel content = new()
        {
            Spacing = OblivionReadingTypographyBaseline.MatureReadOnly.ParagraphSpacing,
            MaxWidth = maximumReadableWidth ?? OblivionReadingTypographyBaseline.MatureReadOnly.MaximumReadableWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        foreach (OblivionContentPresentationItem item in plan.Items)
        {
            Control realized = item.PresenterKind switch
            {
                OblivionContentPresenterKind.AvaloniaReadOnlyDocument => BuildDocument(card.Body, effectiveStyle),
                OblivionContentPresenterKind.AvaloniaReadOnlyCode => BuildCode(item.Source, item.Language, effectiveStyle),
                OblivionContentPresenterKind.AvaloniaImage => BuildImage(item, effectiveStyle),
                OblivionContentPresenterKind.ExternalMermaidRenderer => BuildDiagram(
                    card,
                    item,
                    diagramRenderer,
                    diagramOutputDirectory,
                    resolvedAppearance,
                    workspaceId,
                    pageId,
                    effectiveStyle,
                    diagramViewportState ?? OblivionDiagramViewportState.Fit,
                    diagramViewportStateChanged),
                _ => BuildText(item.Source, effectiveStyle),
            };
            content.Children.Add(realized);
        }

        ScrollViewer scrollViewer = new()
        {
            Content = content,
            Padding = new Thickness(OblivionReadingTypographyBaseline.MatureReadOnly.ContentPadding),
            HorizontalScrollBarVisibility = plan.Items.Any(item =>
                item.ScrollContract == OblivionContentScrollContract.HostHorizontalAndVerticalWhenBounded)
                ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = plan.AllowsInternalScroll
                ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        return new Border
        {
            Background = ToBrush(effectiveStyle.Surface),
            BorderBrush = ToBrush(effectiveStyle.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = scrollViewer,
        };
    }

    public static Control BuildInspectorRawSource(
        OblivionCard card,
        AvaloniaOblivionContentStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(card);

        AvaloniaOblivionContentStyle effectiveStyle = style ?? AvaloniaOblivionContentStyle.Dark;
        ScrollViewer scrollViewer = new()
        {
            Content = BuildCode(card.Body.RawText, "markdown source", effectiveStyle),
            Padding = new Thickness(OblivionReadingTypographyBaseline.MatureReadOnly.InspectorBodyPadding),
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        return new Border
        {
            Background = ToBrush(effectiveStyle.Surface),
            BorderBrush = ToBrush(effectiveStyle.Border),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = scrollViewer,
        };
    }

    private static Control BuildDocument(
        OblivionCardBody body,
        AvaloniaOblivionContentStyle style)
    {
        OblivionMarkdownProjection projection = OblivionMarkdownBody.Project(body);
        if (projection.Document is null)
        {
            return BuildText(projection.Source, style);
        }

        StackPanel document = new()
        {
            Spacing = OblivionReadingTypographyBaseline.MatureReadOnly.ParagraphSpacing,
        };
        foreach (DocumentBlockMir block in projection.Document.Blocks)
        {
            Control? control = BuildBlock(block, style);
            if (control is not null)
            {
                document.Children.Add(control);
            }
        }

        return document;
    }

    private static Control? BuildBlock(
        DocumentBlockMir block,
        AvaloniaOblivionContentStyle style)
    {
        return block switch
        {
            HeadingMir heading => BuildHeading(heading, style),
            ParagraphMir paragraph => BuildInlineText(paragraph.Inlines, style),
            QuoteMir quote => BuildQuote(quote.Inlines, style),
            CalloutMir callout => BuildQuote(callout.Inlines, style),
            ListMir list => BuildList(list, style),
            CodeBlockMir code when string.Equals(code.Language, "mermaid", StringComparison.OrdinalIgnoreCase) => null,
            CodeBlockMir code => BuildCode(code.Text, code.Language, style),
            ThematicBreakMir => new global::Avalonia.Controls.Separator { Margin = new Thickness(0, 6) },
            BreakMir => new Border { Height = 8 },
            _ => null,
        };
    }

    private static Control BuildHeading(
        HeadingMir heading,
        AvaloniaOblivionContentStyle style)
    {
        double fontSize = OblivionReadingTypographyBaseline.MatureReadOnly.HeadingFontSizes
            .GetValueOrDefault(heading.Level, 16);
        SelectableTextBlock text = BuildInlineText(heading.Inlines, style);
        text.FontSize = fontSize;
        text.LineHeight = Math.Max(
            OblivionReadingTypographyBaseline.MatureReadOnly.BodyLineHeight,
            fontSize + 8);
        text.FontWeight = FontWeight.SemiBold;
        text.Foreground = ToBrush(style.Heading);
        return text;
    }

    private static SelectableTextBlock BuildInlineText(
        IReadOnlyList<DocumentInlineMir> inlines,
        AvaloniaOblivionContentStyle style)
    {
        SelectableTextBlock text = CreateBodyTextBlock(style);
        foreach (DocumentInlineMir inline in inlines)
        {
            AppendInline(text.Inlines!, inline, style);
        }

        return text;
    }

    private static void AppendInline(
        InlineCollection destination,
        DocumentInlineMir inline,
        AvaloniaOblivionContentStyle style)
    {
        switch (inline)
        {
            case TextMir text:
                destination.Add(new Run(text.Text));
                break;
            case CodeSpanMir code:
                destination.Add(new Run(code.Text)
                {
                    FontFamily = Monospace,
                    Background = ToBrush(style.CodeSurface),
                });
                break;
            case EmphasisMir emphasis:
                destination.Add(new Run(FlattenInlineText(emphasis.Children))
                {
                    FontStyle = FontStyle.Italic,
                });
                break;
            case StrongMir strong:
                destination.Add(new Run(FlattenInlineText(strong.Children))
                {
                    FontWeight = FontWeight.SemiBold,
                });
                break;
            case LinkMir link:
                destination.Add(new Run($"{FlattenInlineText(link.Label)} ({link.Target})")
                {
                    Foreground = ToBrush(style.Link),
                    TextDecorations = TextDecorations.Underline,
                });
                break;
            case EmbeddedValueMir embedded:
                destination.Add(new Run($"{{{embedded.SlotId}}}") { Foreground = ToBrush(style.Muted) });
                break;
        }
    }

    private static Control BuildList(
        ListMir list,
        AvaloniaOblivionContentStyle style)
    {
        StackPanel panel = new() { Spacing = 6 };
        for (int index = 0; index < list.Items.Count; index++)
        {
            string marker = list.Kind == DocumentListKind.Ordered ? $"{index + 1}." : "•";
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("28,*"),
            };
            row.Children.Add(new global::Avalonia.Controls.TextBlock
            {
                Text = marker,
                Foreground = ToBrush(style.Muted),
                FontSize = OblivionReadingTypographyBaseline.MatureReadOnly.BodyFontSize,
            });
            SelectableTextBlock item = BuildInlineText(list.Items[index].Inlines, style);
            Grid.SetColumn(item, 1);
            row.Children.Add(item);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control BuildQuote(
        IReadOnlyList<DocumentInlineMir> inlines,
        AvaloniaOblivionContentStyle style)
    {
        return new Border
        {
            BorderBrush = ToBrush(style.QuoteBorder),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 4),
            Child = BuildInlineText(inlines, style),
        };
    }

    private static Control BuildCode(
        string source,
        string? language,
        AvaloniaOblivionContentStyle style)
    {
        StackPanel panel = new() { Spacing = 6 };
        if (!string.IsNullOrWhiteSpace(language))
        {
            panel.Children.Add(new global::Avalonia.Controls.TextBlock
            {
                Text = language,
                Foreground = ToBrush(style.Muted),
                FontSize = 12,
            });
        }

        panel.Children.Add(new SelectableTextBlock
        {
            Text = source,
            FontFamily = Monospace,
            FontSize = OblivionReadingTypographyBaseline.MatureReadOnly.CodeFontSize,
            LineHeight = OblivionReadingTypographyBaseline.MatureReadOnly.CodeLineHeight,
            Foreground = ToBrush(style.Foreground),
            TextWrapping = TextWrapping.NoWrap,
        });
        return new Border
        {
            Background = ToBrush(style.CodeSurface),
            Padding = new Thickness(12),
            Child = panel,
        };
    }

    private static Control BuildImage(
        OblivionContentPresentationItem item,
        AvaloniaOblivionContentStyle style)
    {
        if (item.Artifact?.Exists != true || string.IsNullOrWhiteSpace(item.Artifact.ResolvedPath))
        {
            return BuildDiagnostic("PNG preview unavailable. Use the product-owned Open artifact action.", style);
        }

        try
        {
            return new Image
            {
                Source = new Bitmap(item.Artifact.ResolvedPath),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MaxHeight = 560,
            };
        }
        catch (Exception exception)
        {
            return BuildDiagnostic($"PNG preview failed: {exception.Message}", style);
        }
    }

    private static Control BuildDiagram(
        OblivionCard card,
        OblivionContentPresentationItem item,
        IOblivionDiagramRenderer renderer,
        string outputDirectory,
        OblivionResolvedAppearance resolvedAppearance,
        string? workspaceId,
        string? pageId,
        AvaloniaOblivionContentStyle style,
        OblivionDiagramViewportState viewportState,
        Action<OblivionDiagramViewportState>? viewportStateChanged)
    {
        OblivionDiagramRenderResult result = renderer.Render(new OblivionDiagramRenderRequest(
            item.ContentId,
            item.Source,
            item.SourceReference,
            outputDirectory,
            resolvedAppearance,
            workspaceId,
            pageId,
            card.Id.Value));
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RenderedPath))
        {
            string message = result.Diagnostics.FirstOrDefault()?.Message
                ?? "Mermaid rendering was unavailable.";
            return BuildDiagnostic(
                message + Environment.NewLine + Environment.NewLine + item.Source,
                style);
        }

        try
        {
            if (result.RendererKind == OblivionDiagramRendererKind.NativeSvg &&
                result.ResolvedDiagram is not null)
            {
                OblivionResolvedDiagram resolved = result.ResolvedDiagram;
                return new AvaloniaOblivionDiagramCanvas(
                    BuildNativeDiagramVisual(resolved, resolvedAppearance),
                    resolved.Width,
                    resolved.Height,
                    viewportState,
                    viewportStateChanged)
                {
                    MinHeight = 240,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
            }

            return new AvaloniaOblivionDiagramCanvas(
                new Bitmap(result.RenderedPath),
                viewportState,
                viewportStateChanged)
            {
                MinHeight = 240,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
        }
        catch (Exception exception)
        {
            return BuildDiagnostic(
                $"Rendered Mermaid artifact could not be presented: {exception.Message}",
                style);
        }
    }

    private static Control BuildNativeDiagramVisual(
        OblivionResolvedDiagram diagram,
        OblivionResolvedAppearance appearance)
    {
        bool dark = appearance == OblivionResolvedAppearance.Dark;
        uint background = dark ? 0x0F172AFFu : 0xFFFFFFFFu;
        uint nodeFill = dark ? 0x111827FFu : 0xF8FAFCFFu;
        uint nodeStroke = dark ? 0x38BDF8FFu : 0x2563EBFFu;
        uint foreground = dark ? 0xE2E8F0FFu : 0x18181BFFu;
        uint edgeColor = dark ? 0x94A3B8FFu : 0x475569FFu;
        global::Avalonia.Controls.Canvas canvas = new()
        {
            Width = diagram.Width,
            Height = diagram.Height,
            Background = ToBrush(background),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        foreach (OblivionResolvedDiagramEdge edge in diagram.Edges)
        {
            for (int index = 1; index < edge.Route.Count; index++)
            {
                OblivionNativeDiagramPoint start = edge.Route[index - 1];
                OblivionNativeDiagramPoint end = edge.Route[index];
                canvas.Children.Add(new global::Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Point(start.X, start.Y),
                    EndPoint = new Point(end.X, end.Y),
                    Stroke = ToBrush(edgeColor),
                    StrokeThickness = 2,
                });
            }

            AddArrowhead(canvas, edge.Route, edgeColor);

            if (!string.IsNullOrWhiteSpace(edge.DisplayLabel))
            {
                global::Avalonia.Controls.TextBlock label = new()
                {
                    Text = edge.DisplayLabel,
                    FontFamily = new FontFamily("Segoe UI, sans-serif"),
                    FontSize = 13,
                    Foreground = ToBrush(foreground),
                    Background = ToBrush(background),
                    Padding = new Thickness(2, 0),
                };
                global::Avalonia.Controls.Canvas.SetLeft(label, edge.LabelAnchor.X);
                global::Avalonia.Controls.Canvas.SetTop(label, edge.LabelAnchor.Y - 14);
                canvas.Children.Add(label);
            }
        }

        foreach (OblivionResolvedDiagramNode node in diagram.Nodes)
        {
            Border visual = new()
            {
                Width = node.Width,
                Height = node.Height,
                Background = ToBrush(nodeFill),
                BorderBrush = ToBrush(nodeStroke),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Child = new global::Avalonia.Controls.TextBlock
                {
                    Text = node.Label,
                    FontFamily = new FontFamily("Segoe UI, sans-serif"),
                    FontSize = 16,
                    Foreground = ToBrush(foreground),
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            global::Avalonia.Controls.Canvas.SetLeft(visual, node.X);
            global::Avalonia.Controls.Canvas.SetTop(visual, node.Y);
            canvas.Children.Add(visual);
        }

        return canvas;
    }

    private static void AddArrowhead(
        global::Avalonia.Controls.Canvas canvas,
        IReadOnlyList<OblivionNativeDiagramPoint> route,
        uint edgeColor)
    {
        if (route.Count < 2)
        {
            return;
        }

        OblivionNativeDiagramPoint end = route[^1];
        OblivionNativeDiagramPoint? previous = null;
        for (int index = route.Count - 2; index >= 0; index--)
        {
            OblivionNativeDiagramPoint candidate = route[index];
            if (Math.Abs(candidate.X - end.X) > 0.001 || Math.Abs(candidate.Y - end.Y) > 0.001)
            {
                previous = candidate;
                break;
            }
        }
        if (previous is null)
        {
            return;
        }

        double deltaX = end.X - previous.X;
        double deltaY = end.Y - previous.Y;
        string glyph;
        double left;
        double top;
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            glyph = deltaX >= 0 ? "▶" : "◀";
            left = deltaX >= 0 ? end.X - 11 : end.X - 2;
            top = end.Y - 10;
        }
        else
        {
            glyph = deltaY >= 0 ? "▼" : "▲";
            left = end.X - 7;
            top = deltaY >= 0 ? end.Y - 13 : end.Y - 2;
        }

        global::Avalonia.Controls.TextBlock arrowhead = new()
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Symbol, sans-serif"),
            FontSize = 13,
            Foreground = ToBrush(edgeColor),
        };
        global::Avalonia.Controls.Canvas.SetLeft(arrowhead, left);
        global::Avalonia.Controls.Canvas.SetTop(arrowhead, top);
        canvas.Children.Add(arrowhead);
    }

    private static SelectableTextBlock BuildText(
        string source,
        AvaloniaOblivionContentStyle style)
    {
        SelectableTextBlock text = CreateBodyTextBlock(style);
        text.Text = source;
        return text;
    }

    private static SelectableTextBlock BuildDiagnostic(
        string message,
        AvaloniaOblivionContentStyle style)
    {
        SelectableTextBlock text = CreateBodyTextBlock(style);
        text.Text = message;
        text.Foreground = ToBrush(style.Diagnostic);
        return text;
    }

    private static SelectableTextBlock CreateBodyTextBlock(AvaloniaOblivionContentStyle style)
    {
        return new SelectableTextBlock
        {
            FontSize = OblivionReadingTypographyBaseline.MatureReadOnly.BodyFontSize,
            LineHeight = OblivionReadingTypographyBaseline.MatureReadOnly.BodyLineHeight,
            Foreground = ToBrush(style.Foreground),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private static string FlattenInlineText(IReadOnlyList<DocumentInlineMir> inlines)
    {
        return string.Concat(inlines.Select(inline => inline switch
        {
            TextMir text => text.Text,
            CodeSpanMir code => code.Text,
            EmphasisMir emphasis => FlattenInlineText(emphasis.Children),
            StrongMir strong => FlattenInlineText(strong.Children),
            LinkMir link => FlattenInlineText(link.Label),
            EmbeddedValueMir embedded => $"{{{embedded.SlotId}}}",
            _ => string.Empty,
        }));
    }

    private static IBrush ToBrush(uint rgba)
    {
        byte red = (byte)(rgba >> 24);
        byte green = (byte)(rgba >> 16);
        byte blue = (byte)(rgba >> 8);
        byte alpha = (byte)rgba;
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }
}
