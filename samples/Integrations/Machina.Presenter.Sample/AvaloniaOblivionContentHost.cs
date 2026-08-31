using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Copeland.Markdown;

namespace Machina.Presenter.Sample;

public static class AvaloniaOblivionContentHost
{
    private static readonly IBrush Surface = Brush.Parse("#0F172A");
    private static readonly IBrush Foreground = Brush.Parse("#E2E8F0");
    private static readonly IBrush Muted = Brush.Parse("#A8B8CC");
    private static readonly IBrush CodeSurface = Brush.Parse("#111827");
    private static readonly IBrush BorderBrush = Brush.Parse("#475569");
    private static readonly FontFamily Monospace = new("Cascadia Mono, Consolas, monospace");

    public static Control Build(
        OblivionCard card,
        OblivionContentPresentationPlan plan,
        IOblivionDiagramRenderer diagramRenderer,
        string diagramOutputDirectory,
        string? workspaceId = null,
        string? pageId = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(diagramRenderer);

        StackPanel content = new()
        {
            Spacing = OblivionReadingTypographyBaseline.MatureReadOnly.ParagraphSpacing,
            MaxWidth = OblivionReadingTypographyBaseline.MatureReadOnly.MaximumReadableWidth,
        };

        foreach (OblivionContentPresentationItem item in plan.Items)
        {
            Control realized = item.PresenterKind switch
            {
                OblivionContentPresenterKind.AvaloniaReadOnlyDocument => BuildDocument(card.Body),
                OblivionContentPresenterKind.AvaloniaReadOnlyCode => BuildCode(item.Source, item.Language),
                OblivionContentPresenterKind.AvaloniaImage => BuildImage(item),
                OblivionContentPresenterKind.ExternalMermaidRenderer => BuildDiagram(
                    card,
                    item,
                    diagramRenderer,
                    diagramOutputDirectory,
                    workspaceId,
                    pageId),
                _ => BuildText(item.Source),
            };
            content.Children.Add(realized);
        }

        ScrollViewer scrollViewer = new()
        {
            Content = content,
            Padding = new Thickness(OblivionReadingTypographyBaseline.MatureReadOnly.ContentPadding),
            HorizontalScrollBarVisibility = plan.Items.Any(item =>
                item.ScrollContract == OblivionContentScrollContract.HostHorizontalAndVerticalWhenBounded)
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = plan.AllowsInternalScroll
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        return new Border
        {
            Background = Surface,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = scrollViewer,
        };
    }

    public static Control BuildInspectorRawSource(OblivionCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        ScrollViewer scrollViewer = new()
        {
            Content = BuildCode(card.Body.RawText, "markdown source"),
            Padding = new Thickness(OblivionReadingTypographyBaseline.MatureReadOnly.InspectorBodyPadding),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        return new Border
        {
            Background = Surface,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = scrollViewer,
        };
    }

    private static Control BuildDocument(OblivionCardBody body)
    {
        OblivionMarkdownProjection projection = OblivionMarkdownBody.Project(body);
        if (projection.Document is null)
        {
            return BuildText(projection.Source);
        }

        StackPanel document = new()
        {
            Spacing = OblivionReadingTypographyBaseline.MatureReadOnly.ParagraphSpacing,
        };
        foreach (DocumentBlockMir block in projection.Document.Blocks)
        {
            Control? control = BuildBlock(block);
            if (control is not null)
            {
                document.Children.Add(control);
            }
        }

        return document;
    }

    private static Control? BuildBlock(DocumentBlockMir block)
    {
        return block switch
        {
            HeadingMir heading => BuildHeading(heading),
            ParagraphMir paragraph => BuildInlineText(paragraph.Inlines),
            QuoteMir quote => BuildQuote(quote.Inlines),
            CalloutMir callout => BuildQuote(callout.Inlines),
            ListMir list => BuildList(list),
            CodeBlockMir code when string.Equals(code.Language, "mermaid", StringComparison.OrdinalIgnoreCase) => null,
            CodeBlockMir code => BuildCode(code.Text, code.Language),
            ThematicBreakMir => new Avalonia.Controls.Separator { Margin = new Thickness(0, 6) },
            BreakMir => new Border { Height = 8 },
            _ => null,
        };
    }

    private static Control BuildHeading(HeadingMir heading)
    {
        double fontSize = OblivionReadingTypographyBaseline.MatureReadOnly.HeadingFontSizes
            .GetValueOrDefault(heading.Level, 16);
        SelectableTextBlock text = BuildInlineText(heading.Inlines);
        text.FontSize = fontSize;
        text.FontWeight = FontWeight.SemiBold;
        text.Foreground = Brushes.White;
        return text;
    }

    private static SelectableTextBlock BuildInlineText(IReadOnlyList<DocumentInlineMir> inlines)
    {
        SelectableTextBlock text = CreateBodyTextBlock();
        foreach (DocumentInlineMir inline in inlines)
        {
            AppendInline(text.Inlines!, inline);
        }

        return text;
    }

    private static void AppendInline(InlineCollection destination, DocumentInlineMir inline)
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
                    Background = CodeSurface,
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
                    Foreground = Brush.Parse("#93C5FD"),
                    TextDecorations = TextDecorations.Underline,
                });
                break;
            case EmbeddedValueMir embedded:
                destination.Add(new Run($"{{{embedded.SlotId}}}") { Foreground = Muted });
                break;
        }
    }

    private static Control BuildList(ListMir list)
    {
        StackPanel panel = new() { Spacing = 6 };
        for (int index = 0; index < list.Items.Count; index++)
        {
            string marker = list.Kind == DocumentListKind.Ordered ? $"{index + 1}." : "•";
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("28,*"),
            };
            row.Children.Add(new Avalonia.Controls.TextBlock
            {
                Text = marker,
                Foreground = Muted,
                FontSize = OblivionReadingTypographyBaseline.MatureReadOnly.BodyFontSize,
            });
            SelectableTextBlock item = BuildInlineText(list.Items[index].Inlines);
            Grid.SetColumn(item, 1);
            row.Children.Add(item);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control BuildQuote(IReadOnlyList<DocumentInlineMir> inlines)
    {
        return new Border
        {
            BorderBrush = Brush.Parse("#64748B"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 4),
            Child = BuildInlineText(inlines),
        };
    }

    private static Control BuildCode(string source, string? language)
    {
        StackPanel panel = new() { Spacing = 6 };
        if (!string.IsNullOrWhiteSpace(language))
        {
            panel.Children.Add(new Avalonia.Controls.TextBlock
            {
                Text = language,
                Foreground = Muted,
                FontSize = 12,
            });
        }

        panel.Children.Add(new SelectableTextBlock
        {
            Text = source,
            FontFamily = Monospace,
            FontSize = OblivionReadingTypographyBaseline.MatureReadOnly.CodeFontSize,
            LineHeight = OblivionReadingTypographyBaseline.MatureReadOnly.CodeLineHeight,
            Foreground = Foreground,
            TextWrapping = TextWrapping.NoWrap,
        });
        return new Border
        {
            Background = CodeSurface,
            Padding = new Thickness(12),
            Child = panel,
        };
    }

    private static Control BuildImage(OblivionContentPresentationItem item)
    {
        if (item.Artifact?.Exists != true || string.IsNullOrWhiteSpace(item.Artifact.ResolvedPath))
        {
            return BuildDiagnostic("PNG preview unavailable. Use the product-owned Open artifact action.");
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
            return BuildDiagnostic($"PNG preview failed: {exception.Message}");
        }
    }

    private static Control BuildDiagram(
        OblivionCard card,
        OblivionContentPresentationItem item,
        IOblivionDiagramRenderer renderer,
        string outputDirectory,
        string? workspaceId,
        string? pageId)
    {
        OblivionDiagramRenderResult result = renderer.Render(new OblivionDiagramRenderRequest(
            item.ContentId,
            item.Source,
            item.SourceReference,
            outputDirectory,
            workspaceId,
            pageId,
            card.Id.Value));
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RenderedPath))
        {
            string message = result.Diagnostics.FirstOrDefault()?.Message
                ?? "Mermaid rendering was unavailable.";
            return BuildDiagnostic(message + Environment.NewLine + Environment.NewLine + item.Source);
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
            return BuildDiagnostic($"Rendered Mermaid artifact could not be presented: {exception.Message}");
        }
    }

    private static SelectableTextBlock BuildText(string source)
    {
        SelectableTextBlock text = CreateBodyTextBlock();
        text.Text = source;
        return text;
    }

    private static SelectableTextBlock BuildDiagnostic(string message)
    {
        SelectableTextBlock text = CreateBodyTextBlock();
        text.Text = message;
        text.Foreground = Brush.Parse("#FBBF24");
        return text;
    }

    private static SelectableTextBlock CreateBodyTextBlock()
    {
        return new SelectableTextBlock
        {
            FontSize = OblivionReadingTypographyBaseline.MatureReadOnly.BodyFontSize,
            LineHeight = OblivionReadingTypographyBaseline.MatureReadOnly.BodyLineHeight,
            Foreground = Foreground,
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
}
