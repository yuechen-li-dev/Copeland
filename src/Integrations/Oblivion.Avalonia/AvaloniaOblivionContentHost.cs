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
        AvaloniaOblivionContentStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(diagramRenderer);

        AvaloniaOblivionContentStyle effectiveStyle = style ?? AvaloniaOblivionContentStyle.Dark;
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
                    effectiveStyle),
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
        AvaloniaOblivionContentStyle style)
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
            return new Image
            {
                Source = new Bitmap(result.RenderedPath),
                Stretch = Stretch.Uniform,
                MaxHeight = 520,
            };
        }
        catch (Exception exception)
        {
            return BuildDiagnostic(
                $"Rendered Mermaid artifact could not be presented: {exception.Message}",
                style);
        }
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
