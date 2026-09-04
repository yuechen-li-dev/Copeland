using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Machina.VectorAssets;

public sealed record VectorSourceDiagnostic(string Element, string? Attribute, string Reason);

public sealed record VectorSourceParseResult(VectorShape? Shape, IReadOnlyList<VectorSourceDiagnostic> Diagnostics)
{
    public bool Success => Shape is not null && Diagnostics.Count == 0;
}

public static partial class SvgVectorIconParser
{
    private static readonly HashSet<string> SupportedElements = new(StringComparer.Ordinal)
    {
        "svg", "g", "path", "rect", "circle", "ellipse",
    };

    private static readonly HashSet<string> UnsupportedElements = new(StringComparer.Ordinal)
    {
        "text", "image", "filter", "mask", "clipPath", "pattern", "linearGradient",
        "radialGradient", "animate", "animateTransform", "script", "foreignObject", "style", "use",
    };

    public static VectorSourceParseResult Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Failure("svg", null, "Vector source is empty.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(source, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            return Failure("svg", null, $"Malformed XML at line {ex.LineNumber}, column {ex.LinePosition}: {ex.Message}");
        }

        XElement? root = document.Root;
        if (root is null || root.Name.LocalName != "svg")
        {
            return Failure(root?.Name.LocalName ?? "document", null, "The root element must be <svg>.");
        }

        List<VectorSourceDiagnostic> diagnostics = [];
        ValidateElements(root, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new VectorSourceParseResult(null, diagnostics);
        }

        if (!TryReadViewBox(root, out ViewBox viewBox, out string? viewBoxError))
        {
            return Failure("svg", "viewBox", viewBoxError!);
        }

        List<VectorContour> contours = [];
        try
        {
            AffineTransform viewBoxNormalization = new(1, 0, 0, -1, -viewBox.MinX, viewBox.MinY + viewBox.Height);
            ParseElement(root, AffineTransform.Identity, viewBoxNormalization, contours);
            VectorShape shape = new(contours, VectorFillRule.NonZero);
            return new VectorSourceParseResult(shape, []);
        }
        catch (VectorSourceException ex)
        {
            return Failure(ex.Element, ex.Attribute, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return Failure("svg", null, ex.Message);
        }
    }

    private static void ValidateElements(XElement root, List<VectorSourceDiagnostic> diagnostics)
    {
        foreach (XElement element in root.DescendantsAndSelf())
        {
            string name = element.Name.LocalName;
            if (UnsupportedElements.Contains(name) || !SupportedElements.Contains(name))
            {
                diagnostics.Add(new VectorSourceDiagnostic(name, null, $"Element <{name}> is unsupported by the bounded static vector-icon subset."));
                continue;
            }

            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }
                string attributeName = attribute.Name.LocalName;
                if (attributeName is "style" or "class" or "href" or "stroke" or "filter" or "mask" or "clip-path"
                    || attributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new VectorSourceDiagnostic(name, attributeName, $"Attribute '{attributeName}' is unsupported; CSS, strokes, references, and effects are outside M5."));
                }
                if (attributeName == "fill-rule" && attribute.Value != "nonzero")
                {
                    diagnostics.Add(new VectorSourceDiagnostic(name, attributeName, "Only the non-zero fill law is supported."));
                }
                if (attributeName == "fill" && attribute.Value is not "currentColor" and not "#000" and not "#000000" and not "black")
                {
                    diagnostics.Add(new VectorSourceDiagnostic(name, attributeName, "M5 accepts only a single monochrome fill; color is supplied by runtime tint."));
                }
            }
        }
    }

    private static void ParseElement(
        XElement element,
        AffineTransform parentTransform,
        AffineTransform viewBoxNormalization,
        List<VectorContour> contours)
    {
        AffineTransform local = ParseTransform(element);
        AffineTransform combined = parentTransform.Then(local);
        string name = element.Name.LocalName;

        switch (name)
        {
            case "path":
                string path = RequiredAttribute(element, "d");
                try
                {
                    contours.AddRange(PathDataParser.Parse(path).Select(contour => Transform(contour, combined.Then(viewBoxNormalization))));
                }
                catch (FormatException ex)
                {
                    throw Error(element, "d", ex.Message);
                }
                break;
            case "rect":
                contours.Add(Transform(CreateRect(element), combined.Then(viewBoxNormalization)));
                break;
            case "circle":
                contours.Add(Transform(CreateEllipse(element, true), combined.Then(viewBoxNormalization)));
                break;
            case "ellipse":
                contours.Add(Transform(CreateEllipse(element, false), combined.Then(viewBoxNormalization)));
                break;
        }

        foreach (XElement child in element.Elements())
        {
            ParseElement(child, combined, viewBoxNormalization, contours);
        }
    }

    private static VectorContour CreateRect(XElement element)
    {
        double x = OptionalNumber(element, "x", 0);
        double y = OptionalNumber(element, "y", 0);
        double width = RequiredNumber(element, "width");
        double height = RequiredNumber(element, "height");
        if (width <= 0 || height <= 0)
        {
            throw Error(element, null, "Rectangle width and height must be positive.");
        }

        VectorPoint a = new(x, y);
        VectorPoint b = new(x + width, y);
        VectorPoint c = new(x + width, y + height);
        VectorPoint d = new(x, y + height);
        return new VectorContour([new VectorLine(a, b), new VectorLine(b, c), new VectorLine(c, d), new VectorLine(d, a)]);
    }

    private static VectorContour CreateEllipse(XElement element, bool circle)
    {
        double cx = OptionalNumber(element, "cx", 0);
        double cy = OptionalNumber(element, "cy", 0);
        double rx = RequiredNumber(element, circle ? "r" : "rx");
        double ry = circle ? rx : RequiredNumber(element, "ry");
        if (rx <= 0 || ry <= 0)
        {
            throw Error(element, null, "Ellipse radii must be positive.");
        }

        const double kappa = 0.5522847498307936;
        VectorPoint top = new(cx, cy - ry);
        VectorPoint right = new(cx + rx, cy);
        VectorPoint bottom = new(cx, cy + ry);
        VectorPoint left = new(cx - rx, cy);
        return new VectorContour([
            new VectorCubic(top, new(cx + (kappa * rx), cy - ry), new(cx + rx, cy - (kappa * ry)), right),
            new VectorCubic(right, new(cx + rx, cy + (kappa * ry)), new(cx + (kappa * rx), cy + ry), bottom),
            new VectorCubic(bottom, new(cx - (kappa * rx), cy + ry), new(cx - rx, cy + (kappa * ry)), left),
            new VectorCubic(left, new(cx - rx, cy - (kappa * ry)), new(cx - (kappa * rx), cy - ry), top),
        ]);
    }

    private static VectorContour Transform(VectorContour contour, AffineTransform transform)
    {
        return new VectorContour(contour.Segments.Select<VectorSegment, VectorSegment>(segment => segment switch
        {
            VectorLine line => new VectorLine(transform.Apply(line.P0), transform.Apply(line.P1)),
            VectorQuadratic quadratic => new VectorQuadratic(transform.Apply(quadratic.P0), transform.Apply(quadratic.P1), transform.Apply(quadratic.P2)),
            VectorCubic cubic => new VectorCubic(transform.Apply(cubic.P0), transform.Apply(cubic.P1), transform.Apply(cubic.P2), transform.Apply(cubic.P3)),
            _ => throw new InvalidOperationException(),
        }).ToArray());
    }

    private static AffineTransform ParseTransform(XElement element)
    {
        string? source = element.Attribute("transform")?.Value;
        if (string.IsNullOrWhiteSpace(source))
        {
            return AffineTransform.Identity;
        }

        AffineTransform result = AffineTransform.Identity;
        int consumed = 0;
        foreach (Match match in TransformPattern().Matches(source))
        {
            if (!string.IsNullOrWhiteSpace(source[consumed..match.Index]))
            {
                throw Error(element, "transform", "Malformed transform list.");
            }
            consumed = match.Index + match.Length;
            double[] values = ParseNumbers(match.Groups[2].Value);
            AffineTransform item = match.Groups[1].Value switch
            {
                "translate" when values.Length is 1 or 2 => AffineTransform.Translate(values[0], values.Length == 2 ? values[1] : 0),
                "scale" when values.Length is 1 or 2 => AffineTransform.Scale(values[0], values.Length == 2 ? values[1] : values[0]),
                "rotate" when values.Length == 1 => AffineTransform.Rotate(values[0]),
                "rotate" when values.Length == 3 => AffineTransform.Translate(-values[1], -values[2]).Then(AffineTransform.Rotate(values[0])).Then(AffineTransform.Translate(values[1], values[2])),
                "matrix" when values.Length == 6 => new(values[0], values[1], values[2], values[3], values[4], values[5]),
                _ => throw Error(element, "transform", $"Unsupported or malformed transform '{match.Value}'."),
            };
            result = result.Then(item);
        }
        if (!string.IsNullOrWhiteSpace(source[consumed..]))
        {
            throw Error(element, "transform", "Malformed transform list.");
        }
        return result;
    }

    private static bool TryReadViewBox(XElement root, out ViewBox viewBox, out string? error)
    {
        viewBox = default;
        error = null;
        string? source = root.Attribute("viewBox")?.Value;
        if (source is null)
        {
            error = "A finite, positive viewBox is required for deterministic intrinsic sizing.";
            return false;
        }
        double[] values;
        try
        {
            values = ParseNumbers(source);
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
        if (values.Length != 4 || values.Any(static value => !double.IsFinite(value)) || values[2] <= 0 || values[3] <= 0)
        {
            error = "viewBox must contain four finite numbers with positive width and height.";
            return false;
        }
        viewBox = new ViewBox(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static double[] ParseNumbers(string source)
    {
        MatchCollection matches = NumberPattern().Matches(source);
        string residue = NumberPattern().Replace(source, string.Empty).Replace(",", string.Empty).Trim();
        if (residue.Length > 0)
        {
            throw new FormatException($"Malformed numeric list near '{residue}'.");
        }
        return matches
            .Cast<Match>()
            .Select(match => double.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        string? value = element.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? throw Error(element, name, $"Attribute '{name}' is required.") : value;
    }

    private static double RequiredNumber(XElement element, string name)
    {
        return ParseNumber(element, name, RequiredAttribute(element, name));
    }

    private static double OptionalNumber(XElement element, string name, double fallback)
    {
        string? value = element.Attribute(name)?.Value;
        return value is null ? fallback : ParseNumber(element, name, value);
    }

    private static double ParseNumber(XElement element, string name, string source)
    {
        if (!double.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result))
        {
            throw Error(element, name, $"Attribute '{name}' must be a finite number.");
        }
        return result;
    }

    private static VectorSourceException Error(XElement element, string? attribute, string reason)
    {
        IXmlLineInfo line = element;
        string location = line.HasLineInfo() ? $" at line {line.LineNumber}" : string.Empty;
        return new VectorSourceException(element.Name.LocalName, attribute, reason + location);
    }

    private static VectorSourceParseResult Failure(string element, string? attribute, string reason)
    {
        return new VectorSourceParseResult(null, [new VectorSourceDiagnostic(element, attribute, reason)]);
    }

    [GeneratedRegex(@"(translate|scale|rotate|matrix)\s*\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformPattern();

    [GeneratedRegex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    private readonly record struct ViewBox(double MinX, double MinY, double Width, double Height);

    private readonly record struct AffineTransform(double A, double B, double C, double D, double E, double F)
    {
        public static AffineTransform Identity => new(1, 0, 0, 1, 0, 0);

        public static AffineTransform Translate(double x, double y) => new(1, 0, 0, 1, x, y);

        public static AffineTransform Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

        public static AffineTransform Rotate(double degrees)
        {
            double radians = degrees * Math.PI / 180d;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            return new(cosine, sine, -sine, cosine, 0, 0);
        }

        public VectorPoint Apply(VectorPoint point)
        {
            return new VectorPoint((A * point.X) + (C * point.Y) + E, (B * point.X) + (D * point.Y) + F);
        }

        public AffineTransform Then(AffineTransform next)
        {
            return new AffineTransform(
                (next.A * A) + (next.C * B),
                (next.B * A) + (next.D * B),
                (next.A * C) + (next.C * D),
                (next.B * C) + (next.D * D),
                (next.A * E) + (next.C * F) + next.E,
                (next.B * E) + (next.D * F) + next.F);
        }
    }

    private sealed class VectorSourceException(string element, string? attribute, string message) : Exception(message)
    {
        public string Element { get; } = element;

        public string? Attribute { get; } = attribute;
    }
}

internal sealed class PathDataParser
{
    private readonly string source;
    private int index;
    private VectorPoint current;
    private VectorPoint contourStart;
    private char command;
    private readonly List<VectorContour> contours = [];
    private List<VectorSegment>? segments;

    private PathDataParser(string source)
    {
        this.source = source;
    }

    public static IReadOnlyList<VectorContour> Parse(string source)
    {
        PathDataParser parser = new(source);
        parser.Run();
        return parser.contours;
    }

    private void Run()
    {
        while (SkipSeparators())
        {
            if (char.IsLetter(source[index]))
            {
                command = source[index++];
            }
            else if (command == default)
            {
                throw new FormatException($"Path data requires a command at offset {index}.");
            }

            bool relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    Move(relative);
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    Line(relative);
                    break;
                case 'H':
                    Horizontal(relative);
                    break;
                case 'V':
                    Vertical(relative);
                    break;
                case 'Q':
                    Quadratic(relative);
                    break;
                case 'C':
                    Cubic(relative);
                    break;
                case 'Z':
                    Close();
                    command = default;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{command}' at offset {index - 1}.");
            }
        }
        if (segments is not null)
        {
            throw new FormatException("Every M5 path contour must close with Z.");
        }
        if (contours.Count == 0)
        {
            throw new FormatException("Path data contains no closed geometry.");
        }
    }

    private void Move(bool relative)
    {
        VectorPoint target = Point(relative);
        if (segments is not null)
        {
            throw new FormatException("A new contour cannot start before the previous contour is closed.");
        }
        current = target;
        contourStart = target;
        segments = [];
    }

    private void Line(bool relative)
    {
        EnsureContour();
        VectorPoint target = Point(relative);
        segments!.Add(new VectorLine(current, target));
        current = target;
    }

    private void Horizontal(bool relative)
    {
        EnsureContour();
        double x = Number();
        VectorPoint target = new(relative ? current.X + x : x, current.Y);
        segments!.Add(new VectorLine(current, target));
        current = target;
    }

    private void Vertical(bool relative)
    {
        EnsureContour();
        double y = Number();
        VectorPoint target = new(current.X, relative ? current.Y + y : y);
        segments!.Add(new VectorLine(current, target));
        current = target;
    }

    private void Quadratic(bool relative)
    {
        EnsureContour();
        VectorPoint control = Point(relative);
        VectorPoint target = Point(relative);
        segments!.Add(new VectorQuadratic(current, control, target));
        current = target;
    }

    private void Cubic(bool relative)
    {
        EnsureContour();
        VectorPoint control1 = Point(relative);
        VectorPoint control2 = Point(relative);
        VectorPoint target = Point(relative);
        segments!.Add(new VectorCubic(current, control1, control2, target));
        current = target;
    }

    private void Close()
    {
        EnsureContour();
        if (current != contourStart)
        {
            segments!.Add(new VectorLine(current, contourStart));
        }
        contours.Add(new VectorContour(segments!));
        segments = null;
        current = contourStart;
    }

    private VectorPoint Point(bool relative)
    {
        double x = Number();
        double y = Number();
        return relative ? new VectorPoint(current.X + x, current.Y + y) : new VectorPoint(x, y);
    }

    private double Number()
    {
        SkipSeparators();
        Match match = SvgVectorIconParserNumberPattern().Match(source, index);
        if (!match.Success || match.Index != index)
        {
            throw new FormatException($"Expected a finite number at path offset {index}.");
        }
        index += match.Length;
        double result = double.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(result))
        {
            throw new FormatException($"Non-finite number at path offset {match.Index}.");
        }
        return result;
    }

    private bool SkipSeparators()
    {
        while (index < source.Length && (char.IsWhiteSpace(source[index]) || source[index] == ','))
        {
            index++;
        }
        return index < source.Length;
    }

    private void EnsureContour()
    {
        if (segments is null)
        {
            throw new FormatException($"Path command '{command}' requires an active contour.");
        }
    }

    private static Regex SvgVectorIconParserNumberPattern()
    {
        return new Regex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant);
    }
}
