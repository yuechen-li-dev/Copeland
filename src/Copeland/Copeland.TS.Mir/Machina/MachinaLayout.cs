using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.TS.Mir.Machina;

/// <summary>
/// A source-owned span retained in authored and resolved Machina artifacts.
/// A null span is valid for compiler-synthesized nodes.
/// </summary>
public sealed record MachinaSourceSpan(string SourcePath, int Start, int Length)
{
    public override string ToString() => $"{SourcePath}:{Start.ToString(CultureInfo.InvariantCulture)}+{Length.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// A bounded affine length: <c>ui * parentAxis + px</c>. The factory methods
/// are deliberately the only public way to introduce a unit literal.
/// </summary>
public readonly record struct MachinaLength
{
    private MachinaLength(double ui, double px)
    {
        Ui = ui;
        Px = px;
    }

    public double Ui { get; }
    public double Px { get; }

    public static MachinaLength Pixels(double value)
    {
        RequireFinite(value, "px value");
        return new MachinaLength(0, value);
    }

    public static MachinaLength Normalized(double value)
    {
        RequireFinite(value, "ui value");
        if (value is < 0 or > 1)
        {
            throw new MachinaLayoutException(
                "COPE-MACHINA-UI-0001",
                $"A ui literal must be in the inclusive range [0, 1]; received {Format(value)}.");
        }

        return new MachinaLength(value, 0);
    }

    public static MachinaLength operator +(MachinaLength left, MachinaLength right)
        => new(left.Ui + right.Ui, left.Px + right.Px);

    public static MachinaLength operator -(MachinaLength left, MachinaLength right)
        => new(left.Ui - right.Ui, left.Px - right.Px);

    public static MachinaLength operator -(MachinaLength value)
        => new(-value.Ui, -value.Px);

    public double Resolve(double axisSize)
    {
        RequireFinite(axisSize, "parent axis size");
        return Ui * axisSize + Px;
    }

    public string Describe(string axisName)
    {
        if (Ui == 0)
        {
            return $"{Format(Px)}px";
        }

        if (Px == 0)
        {
            return $"{Format(Ui)}ui ({Format(Ui)} * {axisName})";
        }

        string sign = Px < 0 ? "-" : "+";
        return $"{Format(Ui)}ui {sign} {Format(Math.Abs(Px))}px ({Format(Ui)} * {axisName} {sign} {Format(Math.Abs(Px))}px)";
    }

    internal static string Format(double value)
        => value.ToString("0.################", CultureInfo.InvariantCulture);

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new MachinaLayoutException("COPE-MACHINA-LENGTH-0001", $"{name} must be finite.");
        }
    }
}

public sealed class MachinaLayoutException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public readonly record struct MachinaRect(double X, double Y, double Width, double Height)
{
    public MachinaRect Inset(MachinaInsets insets)
    {
        var result = new MachinaRect(
            X + insets.Left,
            Y + insets.Top,
            Width - insets.Left - insets.Right,
            Height - insets.Top - insets.Bottom);
        if (result.Width < 0 || result.Height < 0)
        {
            throw new MachinaLayoutException("COPE-MACHINA-STACK-0001", "Padding produces a negative content frame.");
        }

        return result;
    }
}

public readonly record struct MachinaInsets(double Top, double Right, double Bottom, double Left)
{
    public static MachinaInsets All(double value) => new(value, value, value, value);
    public static MachinaInsets None => new(0, 0, 0, 0);
}

public abstract record MachinaFrameIntent;

public sealed record MachinaAbsoluteFrame(
    MachinaLength X,
    MachinaLength Y,
    MachinaLength Width,
    MachinaLength Height) : MachinaFrameIntent;

public sealed record MachinaAnchorFrame(
    MachinaLength? Left = null,
    MachinaLength? Right = null,
    MachinaLength? Top = null,
    MachinaLength? Bottom = null,
    MachinaLength? Width = null,
    MachinaLength? Height = null) : MachinaFrameIntent;

public sealed record MachinaOffset(MachinaLength? X = null, MachinaLength? Y = null)
{
    public static MachinaOffset None { get; } = new();
}

public enum MachinaAxis
{
    Horizontal,
    Vertical,
}

public abstract record MachinaTrack;
public sealed record MachinaFixedTrack(MachinaLength Size) : MachinaTrack;
public sealed record MachinaFillTrack(double Weight = 1) : MachinaTrack
{
    public double Weight { get; } = Weight > 0 && double.IsFinite(Weight)
        ? Weight
        : throw new MachinaLayoutException("COPE-MACHINA-TRACK-0001", "A Fill track weight must be finite and greater than zero.");
}

/// <summary>
/// Content is a deliberate measurement hole. M1 permits it only for text and
/// does not use it to reposition following stack siblings.
/// </summary>
public sealed record MachinaContentTrack : MachinaTrack;

public sealed record MachinaStackOptions(
    MachinaAxis Axis,
    MachinaLength Gap,
    MachinaInsets Padding)
{
    public static MachinaStackOptions Vertical(MachinaLength gap, MachinaInsets? padding = null)
        => new(MachinaAxis.Vertical, gap, padding ?? MachinaInsets.None);

    public static MachinaStackOptions Horizontal(MachinaLength gap, MachinaInsets? padding = null)
        => new(MachinaAxis.Horizontal, gap, padding ?? MachinaInsets.None);
}

public enum MachinaViewKind
{
    Root,
    Container,
    VStack,
    HStack,
    Text,
    Button,
    Toggle,
}

public enum MachinaMeasurementDependency
{
    TextWrap,
}

public sealed record MachinaBoxStyle(double? Padding = null);
public sealed record MachinaSurfaceStyle(string? Fill = null, MachinaLength? Radius = null, double? Opacity = null);
public sealed record MachinaTextStyle(string? Color = null, MachinaLength? Size = null, int? Weight = null, double? LineHeight = null);
public sealed record MachinaBorderStyle(MachinaLength? Width = null, string? Color = null, string? Style = null);
public sealed record MachinaEffectStyle(string? Shadow = null);

/// <summary>
/// Immutable, backend-neutral style record. Copeland records and <c>with</c>
/// normalize to this same shape; CSS is generated only after resolution.
/// </summary>
public sealed record MachinaStyle(
    MachinaBoxStyle? Box = null,
    MachinaSurfaceStyle? Surface = null,
    MachinaTextStyle? Text = null,
    MachinaBorderStyle? Border = null,
    MachinaEffectStyle? Effect = null)
{
    public static MachinaStyle Empty { get; } = new();
}

/// <summary>
/// Tree-shaped authored MIR. Stable identities are assigned from source order
/// by the resolver, not handwritten by an author.
/// </summary>
public sealed record MachinaView(
    MachinaViewKind Kind,
    IReadOnlyList<MachinaView> Children,
    MachinaFrameIntent? Frame = null,
    MachinaStackOptions? Stack = null,
    MachinaTrack? MainTrack = null,
    MachinaTrack? CrossTrack = null,
    MachinaStyle? Style = null,
    MachinaOffset? Offset = null,
    string? Text = null,
    string? EventName = null,
    bool RequiresTextMeasurement = false,
    MachinaSourceSpan? Source = null)
{
    public MachinaStyle EffectiveStyle => Style ?? MachinaStyle.Empty;
    public MachinaOffset EffectiveOffset => Offset ?? MachinaOffset.None;
}

/// <summary>Functions-first construction surface used by the compiler profile.</summary>
public static class Machina
{
    public static MachinaView Root(IReadOnlyList<MachinaView> children, MachinaStyle? style = null, MachinaSourceSpan? source = null)
        => new(MachinaViewKind.Root, children, Style: style, Source: source);

    public static MachinaView Container(IReadOnlyList<MachinaView> children, MachinaFrameIntent frame, MachinaStyle? style = null, MachinaOffset? offset = null, MachinaSourceSpan? source = null)
        => new(MachinaViewKind.Container, children, frame, Style: style, Offset: offset, Source: source);

    public static MachinaView VStack(IReadOnlyList<MachinaView> children, MachinaFrameIntent? frame, MachinaLength gap, MachinaInsets? padding = null, MachinaStyle? style = null, MachinaOffset? offset = null, MachinaTrack? mainTrack = null, MachinaTrack? crossTrack = null, MachinaSourceSpan? source = null)
        => new(MachinaViewKind.VStack, children, frame, MachinaStackOptions.Vertical(gap, padding), MainTrack: mainTrack, CrossTrack: crossTrack, Style: style, Offset: offset, Source: source);

    public static MachinaView HStack(IReadOnlyList<MachinaView> children, MachinaFrameIntent? frame, MachinaLength gap, MachinaInsets? padding = null, MachinaStyle? style = null, MachinaOffset? offset = null, MachinaTrack? mainTrack = null, MachinaTrack? crossTrack = null, MachinaSourceSpan? source = null)
        => new(MachinaViewKind.HStack, children, frame, MachinaStackOptions.Horizontal(gap, padding), MainTrack: mainTrack, CrossTrack: crossTrack, Style: style, Offset: offset, Source: source);

    public static MachinaView Text(string text, MachinaFrameIntent? frame = null, MachinaStyle? style = null, MachinaOffset? offset = null, bool requiresTextMeasurement = false, MachinaTrack? mainTrack = null, MachinaTrack? crossTrack = null, MachinaSourceSpan? source = null)
        => new(MachinaViewKind.Text, [], frame, MainTrack: mainTrack, CrossTrack: crossTrack, Style: style, Offset: offset, Text: text, RequiresTextMeasurement: requiresTextMeasurement, Source: source);

    public static MachinaView Button(string label, string eventName, MachinaFrameIntent? frame = null, MachinaStyle? style = null, MachinaOffset? offset = null, MachinaTrack? mainTrack = null, MachinaTrack? crossTrack = null, MachinaSourceSpan? source = null)
        => new(MachinaViewKind.Button, [], frame, MainTrack: mainTrack, CrossTrack: crossTrack, Style: style, Offset: offset, Text: label, EventName: eventName, Source: source);

    public static MachinaView Toggle(bool value, string eventName, MachinaFrameIntent? frame = null, MachinaStyle? style = null, MachinaOffset? offset = null, MachinaTrack? mainTrack = null, MachinaTrack? crossTrack = null, MachinaSourceSpan? source = null)
        => new(MachinaViewKind.Toggle, [], frame, MainTrack: mainTrack, CrossTrack: crossTrack, Style: style, Offset: offset, Text: value ? "true" : "false", EventName: eventName, Source: source);

    public static MachinaAbsoluteFrame Absolute(MachinaLength x, MachinaLength y, MachinaLength width, MachinaLength height)
        => new(x, y, width, height);

    public static MachinaAnchorFrame Anchor(MachinaLength? left = null, MachinaLength? right = null, MachinaLength? top = null, MachinaLength? bottom = null, MachinaLength? width = null, MachinaLength? height = null)
        => new(left, right, top, bottom, width, height);

    public static MachinaFixedTrack Fixed(MachinaLength size) => new(size);
    public static MachinaFillTrack Fill(double weight = 1) => new(weight);
    public static MachinaContentTrack Content() => new();
}

public sealed record MachinaResolvedNode(
    string Identity,
    string? ParentIdentity,
    MachinaViewKind Kind,
    MachinaRect Frame,
    MachinaView Authored,
    IReadOnlyList<string> GeometryExplanation,
    MachinaMeasurementDependency? MeasurementDependency);

public sealed class MachinaResolvedDocument(
    IReadOnlyList<MachinaResolvedNode> nodes,
    MachinaRect viewport)
{
    public IReadOnlyList<MachinaResolvedNode> Nodes { get; } = nodes;
    public MachinaRect Viewport { get; } = viewport;

    public string ToDebugText()
    {
        var builder = new StringBuilder();
        foreach (MachinaResolvedNode node in Nodes)
        {
            builder.Append(node.Identity)
                .Append(" | ")
                .Append(node.Kind)
                .Append(" | x=").Append(MachinaLength.Format(node.Frame.X))
                .Append(" y=").Append(MachinaLength.Format(node.Frame.Y))
                .Append(" width=").Append(MachinaLength.Format(node.Frame.Width))
                .Append(" height=").Append(MachinaLength.Format(node.Frame.Height));
            if (node.MeasurementDependency is not null)
            {
                builder.Append(" | measurement=").Append(node.MeasurementDependency);
            }
            if (node.Authored.Source is not null)
            {
                builder.Append(" | source=").Append(node.Authored.Source);
            }
            builder.AppendLine();
            foreach (string explanation in node.GeometryExplanation)
            {
                builder.Append("  ").AppendLine(explanation);
            }
        }
        return builder.ToString();
    }
}

/// <summary>Deterministic pre-resolution for static Machina authored MIR.</summary>
public static class MachinaLayoutResolver
{
    public static MachinaResolvedDocument Resolve(MachinaView root, MachinaRect viewport)
    {
        if (root.Kind != MachinaViewKind.Root)
        {
            throw new MachinaLayoutException("COPE-MACHINA-ROOT-0001", "A Machina document must begin with Root.");
        }
        if (viewport.Width < 0 || viewport.Height < 0)
        {
            throw new MachinaLayoutException("COPE-MACHINA-ROOT-0002", "Viewport dimensions must be non-negative.");
        }

        var nodes = new List<MachinaResolvedNode>();
        ResolveNode(root, "root", null, viewport, viewport, nodes, ["root frame = viewport"]);
        return new MachinaResolvedDocument(nodes, viewport);
    }

    private static void ResolveNode(MachinaView node, string identity, string? parentIdentity, MachinaRect parentFrame, MachinaRect frame, List<MachinaResolvedNode> nodes, IReadOnlyList<string> explanation)
    {
        MachinaRect offsetFrame = ApplyOffset(frame, node.EffectiveOffset, parentFrame);
        MachinaMeasurementDependency? measurement = node.Kind == MachinaViewKind.Text && node.RequiresTextMeasurement
            ? MachinaMeasurementDependency.TextWrap
            : null;
        nodes.Add(new MachinaResolvedNode(identity, parentIdentity, node.Kind, offsetFrame, node, explanation, measurement));

        if (node.Stack is not null)
        {
            ResolveStackChildren(node, identity, offsetFrame, nodes);
            return;
        }

        for (int index = 0; index < node.Children.Count; index++)
        {
            MachinaView child = node.Children[index];
            MachinaRect childFrame = ResolveRequiredFrame(child, offsetFrame);
            ResolveNode(child, identity + "/" + index.ToString(CultureInfo.InvariantCulture), identity, offsetFrame, childFrame, nodes, ExplainFrame(child.Frame!, offsetFrame));
        }
    }

    private static void ResolveStackChildren(MachinaView parent, string parentIdentity, MachinaRect parentFrame, List<MachinaResolvedNode> nodes)
    {
        MachinaStackOptions stack = parent.Stack!;
        MachinaRect content = parentFrame.Inset(stack.Padding);
        bool horizontal = stack.Axis == MachinaAxis.Horizontal;
        double mainAxis = horizontal ? content.Width : content.Height;
        double crossAxis = horizontal ? content.Height : content.Width;
        double gap = stack.Gap.Resolve(mainAxis);
        if (gap < 0)
        {
            throw new MachinaLayoutException("COPE-MACHINA-STACK-0002", "A stack gap must resolve to a non-negative value.");
        }

        var mainSizes = new double[parent.Children.Count];
        double fixedSize = 0;
        double totalFillWeight = 0;
        for (int index = 0; index < parent.Children.Count; index++)
        {
            MachinaView child = parent.Children[index];
            MachinaTrack track = child.MainTrack ?? throw new MachinaLayoutException(
                "COPE-MACHINA-STACK-0003",
                $"Stack child {index.ToString(CultureInfo.InvariantCulture)} requires an explicit main-axis Fixed or Fill track.");
            switch (track)
            {
                case MachinaFixedTrack fixedTrack:
                    mainSizes[index] = fixedTrack.Size.Resolve(mainAxis);
                    if (mainSizes[index] < 0)
                    {
                        throw new MachinaLayoutException("COPE-MACHINA-STACK-0004", "A fixed stack track cannot resolve to a negative size.");
                    }
                    fixedSize += mainSizes[index];
                    break;
                case MachinaFillTrack fillTrack:
                    totalFillWeight += fillTrack.Weight;
                    break;
                case MachinaContentTrack:
                    throw new MachinaLayoutException(
                        "COPE-MACHINA-TEXT-0001",
                        "Content-sized stack tracks are deferred in M1 because they would move following siblings. Use an explicit outer text frame and mark text measurement separately.");
                default:
                    throw new UnreachableException();
            }
        }

        double remaining = mainAxis - fixedSize - gap * Math.Max(0, parent.Children.Count - 1);
        if (remaining < 0)
        {
            throw new MachinaLayoutException("COPE-MACHINA-STACK-0005", "Fixed stack tracks and gaps exceed the available content frame.");
        }

        for (int index = 0; index < parent.Children.Count; index++)
        {
            if (parent.Children[index].MainTrack is MachinaFillTrack fillTrack)
            {
                mainSizes[index] = remaining * fillTrack.Weight / totalFillWeight;
            }
        }

        double cursor = horizontal ? content.X : content.Y;
        for (int index = 0; index < parent.Children.Count; index++)
        {
            MachinaView child = parent.Children[index];
            double crossSize = ResolveStackCrossSize(child, crossAxis);
            MachinaRect childFrame = horizontal
                ? new MachinaRect(cursor, content.Y, mainSizes[index], crossSize)
                : new MachinaRect(content.X, cursor, crossSize, mainSizes[index]);
            string childIdentity = parentIdentity + "/" + index.ToString(CultureInfo.InvariantCulture);
            var explanation = new List<string>
            {
                $"stack axis = {stack.Axis}",
                $"main allocation = {MachinaLength.Format(mainSizes[index])}px",
                $"cross allocation = {MachinaLength.Format(crossSize)}px",
            };
            ResolveNode(child, childIdentity, parentIdentity, parentFrame, childFrame, nodes, explanation);
            cursor += mainSizes[index] + gap;
        }
    }

    private static double ResolveStackCrossSize(MachinaView child, double crossAxis)
    {
        return child.CrossTrack switch
        {
            null or MachinaFillTrack => crossAxis,
            MachinaFixedTrack fixedTrack => RequireNonNegative(fixedTrack.Size.Resolve(crossAxis), "COPE-MACHINA-STACK-0006", "A fixed cross-axis track cannot resolve to a negative size."),
            MachinaContentTrack => throw new MachinaLayoutException("COPE-MACHINA-TEXT-0002", "Content-sized cross-axis tracks are not supported in M1."),
            _ => throw new UnreachableException(),
        };
    }

    private static MachinaRect ResolveRequiredFrame(MachinaView node, MachinaRect parentFrame)
    {
        if (node.Frame is null)
        {
            throw new MachinaLayoutException("COPE-MACHINA-FRAME-0001", $"{node.Kind} requires Absolute or Anchor frame intent outside a stack.");
        }

        return node.Frame switch
        {
            MachinaAbsoluteFrame absolute => ResolveAbsolute(absolute, parentFrame),
            MachinaAnchorFrame anchor => ResolveAnchor(anchor, parentFrame),
            _ => throw new UnreachableException(),
        };
    }

    private static MachinaRect ResolveAbsolute(MachinaAbsoluteFrame frame, MachinaRect parent)
    {
        double width = RequireNonNegative(frame.Width.Resolve(parent.Width), "COPE-MACHINA-FRAME-0002", "Absolute width cannot resolve to a negative size.");
        double height = RequireNonNegative(frame.Height.Resolve(parent.Height), "COPE-MACHINA-FRAME-0003", "Absolute height cannot resolve to a negative size.");
        return new MachinaRect(
            parent.X + frame.X.Resolve(parent.Width),
            parent.Y + frame.Y.Resolve(parent.Height),
            width,
            height);
    }

    private static MachinaRect ResolveAnchor(MachinaAnchorFrame frame, MachinaRect parent)
    {
        (double x, double width) = ResolveAnchorAxis(frame.Left, frame.Right, frame.Width, parent.X, parent.Width, "horizontal");
        (double y, double height) = ResolveAnchorAxis(frame.Top, frame.Bottom, frame.Height, parent.Y, parent.Height, "vertical");
        return new MachinaRect(x, y, width, height);
    }

    private static (double Start, double Size) ResolveAnchorAxis(MachinaLength? start, MachinaLength? end, MachinaLength? size, double parentStart, double parentSize, string axis)
    {
        int constraints = (start is not null ? 1 : 0) + (end is not null ? 1 : 0) + (size is not null ? 1 : 0);
        if (constraints != 2)
        {
            throw new MachinaLayoutException("COPE-MACHINA-ANCHOR-0001", $"An Anchor frame must specify exactly two {axis} constraints: start, end, size.");
        }

        double? resolvedStart = start?.Resolve(parentSize);
        double? resolvedEnd = end?.Resolve(parentSize);
        double? resolvedSize = size?.Resolve(parentSize);
        if (resolvedSize is < 0)
        {
            throw new MachinaLayoutException("COPE-MACHINA-ANCHOR-0002", "An explicit Anchor size cannot resolve to a negative value.");
        }

        double resultStart;
        double resultSize;
        if (resolvedStart is not null && resolvedSize is not null)
        {
            resultStart = parentStart + resolvedStart.Value;
            resultSize = resolvedSize.Value;
        }
        else if (resolvedEnd is not null && resolvedSize is not null)
        {
            resultStart = parentStart + parentSize - resolvedEnd.Value - resolvedSize.Value;
            resultSize = resolvedSize.Value;
        }
        else
        {
            resultStart = parentStart + resolvedStart!.Value;
            resultSize = parentSize - resolvedStart.Value - resolvedEnd!.Value;
        }

        return (resultStart, RequireNonNegative(resultSize, "COPE-MACHINA-ANCHOR-0003", "Anchor constraints resolve to a negative size."));
    }

    private static MachinaRect ApplyOffset(MachinaRect frame, MachinaOffset offset, MachinaRect parent)
        => frame with
        {
            X = frame.X + (offset.X?.Resolve(parent.Width) ?? 0),
            Y = frame.Y + (offset.Y?.Resolve(parent.Height) ?? 0),
        };

    private static IReadOnlyList<string> ExplainFrame(MachinaFrameIntent frame, MachinaRect parent)
    {
        return frame switch
        {
            MachinaAbsoluteFrame absolute =>
            [
                $"x = parent.x + {absolute.X.Describe("parent.width")}",
                $"y = parent.y + {absolute.Y.Describe("parent.height")}",
                $"width = {absolute.Width.Describe("parent.width")}",
                $"height = {absolute.Height.Describe("parent.height")}",
            ],
            MachinaAnchorFrame anchor =>
            [
                $"x/width = Anchor({Describe(anchor.Left, "left", "parent.width")}, {Describe(anchor.Right, "right", "parent.width")}, {Describe(anchor.Width, "width", "parent.width")})",
                $"y/height = Anchor({Describe(anchor.Top, "top", "parent.height")}, {Describe(anchor.Bottom, "bottom", "parent.height")}, {Describe(anchor.Height, "height", "parent.height")})",
            ],
            _ => throw new UnreachableException(),
        };
    }

    private static string Describe(MachinaLength? value, string name, string axis)
        => value is null ? name + "=unset" : name + "=" + value.Value.Describe(axis);

    private static double RequireNonNegative(double value, string code, string message)
    {
        if (value < 0)
        {
            throw new MachinaLayoutException(code, message);
        }
        return value;
    }
}

public sealed record MachinaBrowserArtifact(string Html, string Css);

/// <summary>
/// React adapters consume the same resolved geometry and immutable style CSS as
/// the standalone browser proof. The class map deliberately contains no React
/// dependency: callers remain free to select semantic React elements.
/// </summary>
public sealed record MachinaReactArtifact(
    string Css,
    IReadOnlyDictionary<string, string> ClassesByIdentity);

/// <summary>
/// Small M1 browser host. State is held by one reducer-owned value and every
/// DOM event carries the compiler profile's static event symbol. Rebuilding a
/// full view tree is intentionally unnecessary for this bounded proof.
/// </summary>
public static class MachinaBrowserPageBuilder
{
    public static string Create(MachinaResolvedDocument document, string title)
    {
        MachinaBrowserArtifact artifact = MachinaBrowserLowerer.Lower(document);
        return string.Concat(
            """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>
            """,
            Escape(title),
            """
            </title>
              <style>
            """,
            artifact.Css,
            """
              </style>
            </head>
            <body>
            """,
            artifact.Html,
            """
              <script type="module">
                let state = { darkMode: false, status: "ready" };

                function reduce(current, event) {
                  if (event.endsWith(".ToggleDarkMode")) {
                    const darkMode = !current.darkMode;
                    return { darkMode, status: darkMode ? "dark mode enabled" : "dark mode disabled" };
                  }
                  if (event.endsWith(".Save")) {
                    return { ...current, status: "saved" };
                  }
                  return current;
                }

                function render(next) {
                  const status = document.querySelector("p");
                  if (status !== null) {
                    status.textContent = "Status: " + next.status;
                  }
                  const toggle = document.querySelector('input[type="checkbox"]');
                  if (toggle !== null) {
                    toggle.checked = next.darkMode;
                  }
                }

                function dispatch(event) {
                  state = reduce(state, event);
                  render(state);
                }

                for (const element of document.querySelectorAll("[data-machina-event]")) {
                  const event = element.dataset.machinaEvent;
                  element.addEventListener(element instanceof HTMLInputElement ? "change" : "click", () => dispatch(event));
                }
                render(state);
              </script>
            </body>
            </html>
            """);
    }

    private static string Escape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}

/// <summary>
/// Browser lowering consumes resolved boxes. It deliberately emits absolute
/// positioned frames; flex and grid are not a hidden second layout resolver.
/// </summary>
public static class MachinaBrowserLowerer
{
    public static MachinaBrowserArtifact Lower(MachinaResolvedDocument document)
    {
        var builder = new StringBuilder();
        var css = new StringBuilder("/* Generated by Copeland Machina. Edit Copeland source, not this CSS. */\n\n");
        var styleClasses = BuildStyleClasses(document.Nodes);
        foreach ((MachinaStyle style, string className) in styleClasses.OrderBy(entry => entry.Value))
        {
            WriteStyleRule(style, className, css);
        }
        if (styleClasses.Count > 0)
        {
            css.Append('\n');
        }
        var byParent = document.Nodes
            .Where(node => node.ParentIdentity is not null)
            .GroupBy(node => node.ParentIdentity!)
            .ToDictionary(group => group.Key, group => group.ToList());
        WriteNode("root", document.Nodes.Single(node => node.Identity == "root"), null, builder, css, styleClasses, byParent);
        return new MachinaBrowserArtifact(builder.ToString(), css.ToString());
    }

    /// <summary>
    /// Projects native Machina MIR into classes that a React component can
    /// attach to its own semantic DOM. This preserves Machina's resolved
    /// absolute/anchor/stack layout while leaving browser realization and
    /// accessibility semantics with React.
    /// </summary>
    public static MachinaReactArtifact LowerForReact(MachinaResolvedDocument document)
    {
        MachinaBrowserArtifact browserArtifact = Lower(document);
        Dictionary<MachinaStyle, string> styleClasses = BuildStyleClasses(document.Nodes);
        var classesByIdentity = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (MachinaResolvedNode node in document.Nodes)
        {
            string frameClass = "m-frame-" + node.Identity.Replace('/', '-');
            string styleClass = styleClasses[node.Authored.EffectiveStyle];
            classesByIdentity.Add(node.Identity, "m-node " + frameClass + " " + styleClass);
        }

        return new MachinaReactArtifact(browserArtifact.Css, classesByIdentity);
    }

    private static Dictionary<MachinaStyle, string> BuildStyleClasses(IReadOnlyList<MachinaResolvedNode> nodes)
    {
        var result = new Dictionary<MachinaStyle, string>();
        foreach (MachinaStyle style in nodes.Select(node => node.Authored.EffectiveStyle).Distinct())
        {
            string canonical = CanonicalStyle(style);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            result[style] = "m-style-" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }
        return result;
    }

    private static void WriteNode(string identity, MachinaResolvedNode node, MachinaResolvedNode? parent, StringBuilder html, StringBuilder css, Dictionary<MachinaStyle, string> styleClasses, Dictionary<string, List<MachinaResolvedNode>> byParent)
    {
        string frameClass = "m-frame-" + identity.Replace('/', '-');
        string styleClass = styleClasses[node.Authored.EffectiveStyle];
        string tag = TagFor(node.Kind);
        string classes = "m-node " + frameClass + " " + styleClass;
        html.Append('<').Append(tag).Append(" class=\"").Append(classes).Append('"');
        if (node.Authored.EventName is not null)
        {
            html.Append(" data-machina-event=\"").Append(Escape(node.Authored.EventName)).Append('"');
        }
        if (node.Kind == MachinaViewKind.Toggle && node.Authored.Text == "true")
        {
            html.Append(" checked");
        }
        if (node.Kind == MachinaViewKind.Toggle)
        {
            html.Append(" type=\"checkbox\"");
        }
        html.Append('>');
        if (node.Authored.Text is not null && node.Kind != MachinaViewKind.Toggle)
        {
            html.Append(Escape(node.Authored.Text));
        }
        if (byParent.TryGetValue(identity, out List<MachinaResolvedNode>? children))
        {
            foreach (MachinaResolvedNode child in children)
            {
                WriteNode(child.Identity, child, node, html, css, styleClasses, byParent);
            }
        }
        html.Append("</").Append(tag).Append('>');

        double left = parent is null ? node.Frame.X : node.Frame.X - parent.Frame.X;
        double top = parent is null ? node.Frame.Y : node.Frame.Y - parent.Frame.Y;
        css.Append('.').Append(frameClass).Append(" {\n  position: ").Append(parent is null ? "relative" : "absolute").Append(";\n");
        if (parent is not null)
        {
            css.Append("  left: ").Append(MachinaLength.Format(left)).Append("px;\n")
                .Append("  top: ").Append(MachinaLength.Format(top)).Append("px;\n");
        }
        css.Append("  width: ").Append(MachinaLength.Format(node.Frame.Width)).Append("px;\n")
            .Append("  height: ").Append(MachinaLength.Format(node.Frame.Height)).Append("px;\n")
            .Append("  box-sizing: border-box;\n}\n\n");
    }

    private static string CanonicalStyle(MachinaStyle style)
        => string.Join("|", new[]
        {
            FormatNullable(style.Box?.Padding),
            style.Surface?.Fill ?? string.Empty,
            style.Surface?.Radius?.ToString() ?? string.Empty,
            FormatNullable(style.Surface?.Opacity),
            style.Text?.Color ?? string.Empty,
            style.Text?.Size?.ToString() ?? string.Empty,
            style.Text?.Weight?.ToString() ?? string.Empty,
            FormatNullable(style.Text?.LineHeight),
            style.Border?.Width?.ToString() ?? string.Empty,
            style.Border?.Color ?? string.Empty,
            style.Border?.Style ?? string.Empty,
            style.Effect?.Shadow ?? string.Empty,
        });

    private static string FormatNullable(double? value)
        => value?.ToString("0.################", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void WriteStyleRule(MachinaStyle style, string className, StringBuilder css)
    {
        css.Append('.').Append(className).Append(" {\n");
        if (style.Box?.Padding is double padding)
        {
            css.Append("  padding: ").Append(MachinaLength.Format(padding)).Append("px;\n");
        }
        if (style.Surface?.Fill is string fill)
        {
            css.Append("  background: ").Append(fill).Append(";\n");
        }
        if (style.Surface?.Radius is MachinaLength radius)
        {
            css.Append("  border-radius: ").Append(ToStaticCssLength(radius, "surface.radius")).Append(";\n");
        }
        if (style.Surface?.Opacity is double opacity)
        {
            css.Append("  opacity: ").Append(MachinaLength.Format(opacity)).Append(";\n");
        }
        if (style.Text?.Color is string color)
        {
            css.Append("  color: ").Append(color).Append(";\n");
        }
        if (style.Text?.Size is MachinaLength size)
        {
            css.Append("  font-size: ").Append(ToStaticCssLength(size, "text.size")).Append(";\n");
        }
        if (style.Text?.Weight is int weight)
        {
            css.Append("  font-weight: ").Append(weight.ToString(CultureInfo.InvariantCulture)).Append(";\n");
        }
        if (style.Text?.LineHeight is double lineHeight)
        {
            css.Append("  line-height: ").Append(MachinaLength.Format(lineHeight)).Append(";\n");
        }
        if (style.Border?.Width is MachinaLength borderWidth)
        {
            css.Append("  border-width: ").Append(ToStaticCssLength(borderWidth, "border.width")).Append(";\n");
        }
        if (style.Border?.Color is string borderColor)
        {
            css.Append("  border-color: ").Append(borderColor).Append(";\n");
        }
        if (style.Border?.Style is string borderStyle)
        {
            css.Append("  border-style: ").Append(borderStyle).Append(";\n");
        }
        if (style.Effect?.Shadow is string shadow)
        {
            css.Append("  box-shadow: ").Append(shadow).Append(";\n");
        }
        css.Append("}\n\n");
    }

    private static string ToStaticCssLength(MachinaLength value, string fieldName)
    {
        if (value.Ui != 0)
        {
            throw new MachinaLayoutException(
                "COPE-MACHINA-STYLE-0001",
                $"{fieldName} cannot use ui because style CSS is not a geometry resolver. Use px or put ui in a frame.");
        }
        return MachinaLength.Format(value.Px) + "px";
    }

    private static string TagFor(MachinaViewKind kind) => kind switch
    {
        MachinaViewKind.Root => "main",
        MachinaViewKind.Text => "p",
        MachinaViewKind.Button => "button",
        MachinaViewKind.Toggle => "input",
        MachinaViewKind.Container or MachinaViewKind.VStack or MachinaViewKind.HStack => "section",
        _ => "div",
    };

    private static string Escape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
