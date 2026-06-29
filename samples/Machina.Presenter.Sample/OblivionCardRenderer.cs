using System.Text;
using Machina.Core.Authoring;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public sealed record OblivionCardRenderOptions(
    double Width,
    double Height,
    double TitleHeight = 24,
    double SubtitleHeight = 18,
    double RowHeight = 24,
    double SmallGap = 6,
    double SectionGap = 10,
    double BodyLineHeight = 16,
    double BodyLineGap = 6,
    int MaxTagsToShow = 4,
    int MaxActionsToShow = 3,
    int MaxArtifactsToShow = 3);

public static class OblivionCardRenderer
{
    private const string BodyFrameSuffix = ".body-frame";
    private const string TitleSuffix = ".title";
    private const string SubtitleSuffix = ".subtitle";
    private const string MetaRowSuffix = ".meta-row";
    private const string TagsRowSuffix = ".tags-row";
    private const string BodyStackSuffix = ".body-stack";
    private const string BodyLineSuffixPrefix = ".body-line-";
    private const string ActionsRowSuffix = ".actions-row";
    private const string ArtifactsRowSuffix = ".artifacts-row";
    private const string Ellipsis = "...";

    public static UiNode BuildCard(
        OblivionCard card,
        StandardTheme theme,
        OblivionCardRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        StandardCardStyle cardStyle = theme.Card.Default;
        double innerWidth = Math.Max(0, options.Width - (cardStyle.ContentInset * 2));
        double cursorTop = 0;

        List<UiNode> layoutChildren =
        [
            UI.Anchor(
                UI.Text(
                    card.Title,
                    id: card.Id.Value + TitleSuffix,
                    size: TextSize.Md,
                    color: theme.Colors.Foreground),
                id: card.Id.Value + TitleSuffix + ".slot",
                left: 0,
                right: 0,
                top: cursorTop,
                height: options.TitleHeight),
        ];

        cursorTop += options.TitleHeight;

        if (!string.IsNullOrWhiteSpace(card.Subtitle))
        {
            cursorTop += options.SmallGap;
            layoutChildren.Add(
                UI.Anchor(
                    UI.Text(
                        card.Subtitle,
                        id: card.Id.Value + SubtitleSuffix,
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: card.Id.Value + SubtitleSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: cursorTop,
                    height: options.SubtitleHeight));
            cursorTop += options.SubtitleHeight;
        }

        cursorTop += options.SectionGap;
        layoutChildren.Add(
            UI.Anchor(
                UI.Row(
                    id: card.Id.Value + MetaRowSuffix,
                    gap: 8,
                    children:
                    [
                        BuildBadge($"{KindLabel(card.Kind)}", card.Id.Value + ".kind", theme),
                        BuildBadge($"{StatusLabel(card.Status)}", card.Id.Value + ".status", theme),
                    ]),
                id: card.Id.Value + MetaRowSuffix + ".slot",
                left: 0,
                right: 0,
                top: cursorTop,
                height: options.RowHeight));
        cursorTop += options.RowHeight;

        IReadOnlyList<string> tags = LimitLabels(card.Tags, options.MaxTagsToShow);
        if (tags.Count > 0)
        {
            cursorTop += options.SmallGap;
            layoutChildren.Add(
                UI.Anchor(
                    UI.Row(
                        id: card.Id.Value + TagsRowSuffix,
                        gap: 8,
                        children: tags
                            .Select((tag, index) => BuildBadge(tag, $"{card.Id.Value}.tag-{index}", theme))
                            .ToArray()),
                    id: card.Id.Value + TagsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: cursorTop,
                    height: options.RowHeight));
            cursorTop += options.RowHeight;
        }

        cursorTop += options.SectionGap;
        double bodyHeight = Math.Max(0, (options.Height - (cardStyle.ContentInset * 2)) - cursorTop);
        layoutChildren.Add(
            UI.Anchor(
                BuildBody(card, theme, options, innerWidth, bodyHeight),
                id: card.Id.Value + BodyFrameSuffix + ".slot",
                left: 0,
                right: 0,
                top: cursorTop,
                bottom: 0));

        return StandardUI.Card(
            id: card.Id.Value,
            theme: theme,
            width: options.Width,
            height: options.Height,
            child: UI.Layer(
                id: card.Id.Value + ".layout",
                children: layoutChildren));
    }

    public static PresenterCardFrame DescribeFrame(ResolvedLayoutDocument resolved, string cardId)
    {
        return PresenterCard.DescribeFrame(resolved, cardId);
    }

    private static UiNode BuildBody(
        OblivionCard card,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        double innerWidth,
        double bodyHeight)
    {
        IReadOnlyList<string> actionLabels = LimitLabels(
            card.Actions.Select(action => $"{action.Label} {(action.Enabled ? "ready" : "disabled")}").ToArray(),
            options.MaxActionsToShow);
        IReadOnlyList<string> artifactLabels = LimitLabels(
            card.Artifacts.Select(artifact => $"{artifact.Label} ({artifact.Kind})").ToArray(),
            options.MaxArtifactsToShow);

        double reservedRows = 0;
        if (actionLabels.Count > 0)
        {
            reservedRows += options.RowHeight + options.SmallGap;
        }

        if (artifactLabels.Count > 0)
        {
            reservedRows += options.RowHeight + options.SmallGap;
        }

        double bodyTextHeight = Math.Max(0, bodyHeight - reservedRows);
        IReadOnlyList<string> visibleLines = ClipLinesToFit(
            card.BodyLines,
            innerWidth,
            bodyTextHeight,
            options,
            theme.Colors.MutedForeground);

        List<UiNode> children = [];
        double currentTop = 0;

        foreach ((string line, int index) in visibleLines.Select((line, index) => (line, index)))
        {
            children.Add(
                UI.Anchor(
                    UI.Text(
                        line,
                        id: card.Id.Value + BodyLineSuffixPrefix + index,
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: $"{card.Id.Value}{BodyLineSuffixPrefix}{index}.slot",
                    left: 0,
                    right: 0,
                    top: currentTop,
                    height: options.BodyLineHeight));
            currentTop += options.BodyLineHeight + options.BodyLineGap;
        }

        if (actionLabels.Count > 0)
        {
            children.Add(
                UI.Anchor(
                    UI.Row(
                        id: card.Id.Value + ActionsRowSuffix,
                        gap: 8,
                        children: actionLabels
                            .Select((label, index) => BuildBadge(label, $"{card.Id.Value}.action-{index}", theme))
                            .ToArray()),
                    id: card.Id.Value + ActionsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: currentTop,
                    height: options.RowHeight));
            currentTop += options.RowHeight + options.SmallGap;
        }

        if (artifactLabels.Count > 0)
        {
            children.Add(
                UI.Anchor(
                    UI.Row(
                        id: card.Id.Value + ArtifactsRowSuffix,
                        gap: 8,
                        children: artifactLabels
                            .Select((label, index) => BuildBadge(label, $"{card.Id.Value}.artifact-{index}", theme))
                            .ToArray()),
                    id: card.Id.Value + ArtifactsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: currentTop,
                    height: options.RowHeight));
        }

        return UI.Rect(
            child: UI.Layer(
                id: card.Id.Value + ".body-layout",
                children: children),
            id: card.Id.Value + BodyFrameSuffix,
            style: new UiStyle(
                Background: ColorToken.Hex(0x0B1220FF),
                BorderColor: ColorToken.Hex(0x334155FF),
                BorderThickness: 1));
    }

    private static UiNode BuildBadge(string label, string id, StandardTheme theme)
    {
        return StandardUI.Badge(
            label,
            id: id,
            theme: theme,
            variant: BadgeVariant.Secondary);
    }

    private static IReadOnlyList<string> LimitLabels(IReadOnlyList<string> values, int maxToShow)
    {
        if (values.Count <= maxToShow)
        {
            return values;
        }

        List<string> limited = values.Take(maxToShow).ToList();
        limited.Add($"+{values.Count - maxToShow}");
        return limited;
    }

    private static IReadOnlyList<string> ClipLinesToFit(
        IReadOnlyList<string> lines,
        double width,
        double height,
        OblivionCardRenderOptions options,
        ColorToken color)
    {
        int maxLineCount = ComputeLineCapacity(height, options);
        if (maxLineCount <= 0)
        {
            return [];
        }

        TextStyle style = new(
            Color: color,
            Size: TextSize.Sm,
            AlignX: TextAlignX.Left,
            AlignY: TextAlignY.Top);

        List<string> visibleLines = [];
        foreach (string line in lines)
        {
            if (visibleLines.Count == maxLineCount)
            {
                break;
            }

            visibleLines.Add(ClipLineToWidth(line, width, style));
        }

        if (visibleLines.Count < lines.Count && visibleLines.Count > 0)
        {
            visibleLines[^1] = ClipLineToWidth(visibleLines[^1] + " " + Ellipsis, width, style);
        }

        return visibleLines;
    }

    private static int ComputeLineCapacity(double height, OblivionCardRenderOptions options)
    {
        double lineSpan = options.BodyLineHeight + options.BodyLineGap;
        if (height <= 0 || lineSpan <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Floor((height + options.BodyLineGap) / lineSpan));
    }

    private static string ClipLineToWidth(string text, double width, TextStyle style)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            return string.Empty;
        }

        if (Measure(text, style) <= width)
        {
            return text;
        }

        if (Measure(Ellipsis, style) > width)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (char character in text)
        {
            string candidate = builder.ToString() + character + Ellipsis;
            if (Measure(candidate, style) > width)
            {
                break;
            }

            builder.Append(character);
        }

        return builder.Length == 0
            ? Ellipsis
            : builder.ToString().TrimEnd() + Ellipsis;
    }

    private static double Measure(string text, TextStyle style)
    {
        return DeterministicTextMeasurer.Instance.MeasureText(text, style).Width;
    }

    private static string KindLabel(OblivionCardKind kind)
    {
        return kind switch
        {
            OblivionCardKind.Note => "Note",
            OblivionCardKind.Status => "Status",
            OblivionCardKind.UiPreview => "UI Preview",
            OblivionCardKind.Artifact => "Artifact",
            OblivionCardKind.CodeFact => "Code Fact",
            OblivionCardKind.CodeTheory => "Code Theory",
            _ => kind.ToString(),
        };
    }

    private static string StatusLabel(OblivionCardStatus status)
    {
        return status switch
        {
            OblivionCardStatus.Idle => "Idle",
            OblivionCardStatus.Passing => "Passing",
            OblivionCardStatus.Failing => "Failing",
            OblivionCardStatus.Warning => "Warning",
            OblivionCardStatus.Deferred => "Deferred",
            OblivionCardStatus.Placeholder => "Placeholder",
            _ => status.ToString(),
        };
    }
}
