using Copeland.Profile;
using Copeland.TS.Syntax;

namespace Copeland.TS.Profiles;

public static class ProfileTsxCompiler
{
    public static ProfileCompilationResult Compile(string source, string sourcePath = "Asset.profile.tsx")
    {
        ArgumentNullException.ThrowIfNull(source);
        SyntaxTree tree = SyntaxTree.Parse(source, sourcePath);
        List<ProfileDiagnostic> diagnostics = tree.Diagnostics
            .Select(item => new ProfileDiagnostic(
                item.Id,
                item.Message,
                new ProfileSourceSpan(sourcePath, item.Position, item.Length)))
            .ToList();
        if (diagnostics.Count > 0)
        {
            return Failed(diagnostics);
        }

        ExportDefaultDeclarationSyntax[] exports = tree.Root.Members.OfType<ExportDefaultDeclarationSyntax>().ToArray();
        if (exports.Length != 1)
        {
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0001", "Profile authoring requires exactly one export default <Profile> root.", tree.Root, sourcePath));
            return Failed(diagnostics);
        }
        ExpressionSyntax expression = Unwrap(exports[0].Expression);
        if (expression is not TsXmlElementExpressionSyntax root || root.NameToken.Text != "Profile")
        {
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0002", "The default export must be a <Profile> element.", exports[0], sourcePath));
            return Failed(diagnostics);
        }

        AttributeReader attributes = new(root, sourcePath, diagnostics);
        string? name = attributes.String("name", required: true);
        string baseState = attributes.String("baseState", required: false) ?? "Base";
        ExpressionSyntax? baseExpression = attributes.Expression("base", required: true);
        attributes.RejectUnknown("name", "baseState", "base");
        ProfileShapeSpec? baseShape = baseExpression is null
            ? null
            : ParseShape(baseExpression, sourcePath, diagnostics);

        List<ProfileOperation> operations = [];
        string currentState = baseState;
        string? yieldState = null;
        foreach (TsXmlChildSyntax child in root.Children)
        {
            if (child is TsXmlTextSyntax text && string.IsNullOrWhiteSpace(text.TextToken.Text))
            {
                continue;
            }
            if (child is not TsXmlExpressionChildSyntax expressionChild)
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0003", "Profile children must be semantic operation expressions in braces.", child, sourcePath));
                continue;
            }
            if (Unwrap(expressionChild.Expression) is not CallExpressionSyntax call || call.Target is not NameExpressionSyntax target)
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0004", "Profile child must call Add, Subtract, Hole, Tab, Notch, RepeatRadial, a transform, or Yield.", expressionChild, sourcePath));
                continue;
            }
            if (target.IdentifierToken.Text == "Yield")
            {
                yieldState = ReadYield(call, sourcePath, diagnostics);
                continue;
            }
            ProfileOperation? operation = ParseOperation(call, target.IdentifierToken.Text, currentState, sourcePath, diagnostics);
            if (operation is not null)
            {
                operations.Add(operation);
                currentState = operation.OutputState;
            }
        }

        if (name is null || baseShape is null || yieldState is null || diagnostics.Count > 0)
        {
            if (yieldState is null)
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0005", "Profile requires an explicit {Yield(State)} child.", root, sourcePath));
            }
            return Failed(diagnostics);
        }

        ProfileDefinition definition = new(
            name,
            baseState,
            baseShape,
            operations,
            yieldState,
            Span(root, sourcePath));
        ProfileCompilationResult result = ProfileCompiler.Compile(definition);
        return result.Diagnostics.Count == 0
            ? result
            : result with { Diagnostics = diagnostics.Concat(result.Diagnostics).ToArray() };
    }

    private static ProfileOperation? ParseOperation(
        CallExpressionSyntax call,
        string kind,
        string input,
        string sourcePath,
        List<ProfileDiagnostic> diagnostics)
    {
        if (call.Arguments.Count != 1 || Unwrap(call.Arguments[0]) is not ObjectLiteralExpressionSyntax options)
        {
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0010", $"{kind} requires one object-literal argument.", call, sourcePath));
            return null;
        }
        OptionReader reader = new(options, sourcePath, diagnostics);
        string? output = reader.String("as", required: true);
        string featureId = reader.String("id", required: false) ?? output ?? kind;
        if (output is null)
        {
            return null;
        }
        ProfileSourceSpan span = Span(call, sourcePath);
        ProfileOperation? result = kind switch
        {
            "Add" => ShapeOperation(true),
            "Subtract" => ShapeOperation(false),
            "Hole" => Hole(),
            "Tab" => EdgeOperation(true),
            "Notch" => EdgeOperation(false),
            "RepeatRadial" => new RepeatRadialProfileOperation(
                featureId,
                input,
                output,
                reader.Int("count", true) ?? 0,
                reader.Number("toothDepth", true) ?? double.NaN,
                reader.Number("toothFraction", false) ?? 0.5,
                reader.Number("rotation", false) ?? 0,
                span),
            "Translate" => new TransformProfileOperation(featureId, input, output, "Translate", reader.Number("x", true) ?? double.NaN, reader.Number("y", true) ?? double.NaN, span),
            "Rotate" => new TransformProfileOperation(featureId, input, output, "Rotate", reader.Number("degrees", true) ?? double.NaN, 0, span),
            "Scale" => new TransformProfileOperation(featureId, input, output, "Scale", reader.Number("x", true) ?? double.NaN, reader.Number("y", true) ?? double.NaN, span),
            "Mirror" => new TransformProfileOperation(featureId, input, output, "Mirror", reader.Name("axis", true) == "X" ? 1 : 0, 0, span),
            _ => null,
        };
        if (result is null)
        {
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0011", $"Unknown Profile operation '{kind}'.", call, sourcePath));
            return null;
        }
        reader.RejectUnread();
        return result;

        ProfileOperation? ShapeOperation(bool add)
        {
            ExpressionSyntax? shapeExpression = reader.Expression("shape", true);
            ProfileShapeSpec? shape = shapeExpression is null ? null : ParseShape(shapeExpression, sourcePath, diagnostics);
            if (shape is null)
            {
                return null;
            }
            return add
                ? new AddProfileOperation(featureId, input, output, shape, span)
                : new SubtractProfileOperation(featureId, input, output, shape, span);
        }

        ProfileOperation Hole()
        {
            double radius = reader.Number("radius", true) ?? double.NaN;
            double x = reader.Number("x", false) ?? 0;
            double y = reader.Number("y", false) ?? 0;
            return new HoleProfileOperation(featureId, input, output, new CircleProfileShape(radius, x, y, span), span);
        }

        ProfileOperation EdgeOperation(bool tab)
        {
            string? edgeName = reader.Name("edge", true);
            ProfileEdge edge = Enum.TryParse(edgeName, out ProfileEdge parsed) ? parsed : (ProfileEdge)(-1);
            double width = reader.Number("width", true) ?? double.NaN;
            double depth = reader.Number("depth", true) ?? double.NaN;
            double position = reader.Number("position", false) ?? 0.5;
            return tab
                ? new TabProfileOperation(featureId, input, output, edge, width, depth, position, span)
                : new NotchProfileOperation(featureId, input, output, edge, width, depth, position, span);
        }
    }

    private static ProfileShapeSpec? ParseShape(
        ExpressionSyntax expression,
        string sourcePath,
        List<ProfileDiagnostic> diagnostics)
    {
        expression = Unwrap(expression);
        if (expression is not CallExpressionSyntax call || call.Target is not NameExpressionSyntax target
            || call.Arguments.Count != 1 || Unwrap(call.Arguments[0]) is not ObjectLiteralExpressionSyntax options)
        {
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0020", "Shape must be a supported compile-time function call with one options object.", expression, sourcePath));
            return null;
        }
        string kind = target.IdentifierToken.Text;
        OptionReader reader = new(options, sourcePath, diagnostics);
        ProfileSourceSpan span = Span(call, sourcePath);
        ProfileShapeSpec? shape = kind switch
        {
            "Rectangle" => new RectangleProfileShape(reader.Number("width", true) ?? double.NaN, reader.Number("height", true) ?? double.NaN, span),
            "RoundedRectangle" => new RoundedRectangleProfileShape(reader.Number("width", true) ?? double.NaN, reader.Number("height", true) ?? double.NaN, reader.Number("radius", true) ?? double.NaN, span),
            "Circle" => new CircleProfileShape(reader.Number("radius", true) ?? double.NaN, reader.Number("x", false) ?? 0, reader.Number("y", false) ?? 0, span),
            "Ellipse" => new EllipseProfileShape(reader.Number("radiusX", true) ?? double.NaN, reader.Number("radiusY", true) ?? double.NaN, reader.Number("x", false) ?? 0, reader.Number("y", false) ?? 0, span),
            "RegularPolygon" => new RegularPolygonProfileShape(reader.Int("sides", true) ?? 0, reader.Number("radius", true) ?? double.NaN, reader.Number("rotation", false) ?? 90, span),
            "Polygon" => Polygon(),
            _ => null,
        };
        if (shape is null)
        {
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0021", $"Unknown Profile shape '{kind}'.", call, sourcePath));
            return null;
        }
        reader.RejectUnread();
        return shape;

        ProfileShapeSpec Polygon()
        {
            ExpressionSyntax? pointsExpression = reader.Expression("points", true);
            List<VectorPoint> points = [];
            if (UnwrapNullable(pointsExpression) is ArrayLiteralExpressionSyntax array)
            {
                foreach (ExpressionSyntax item in array.Elements)
                {
                    if (Unwrap(item) is ArrayLiteralExpressionSyntax pair && pair.Elements.Count == 2
                        && TryNumber(pair.Elements[0], out double x) && TryNumber(pair.Elements[1], out double y))
                    {
                        points.Add(new VectorPoint(x, y));
                    }
                    else
                    {
                        diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0022", "Polygon points must be [x, y] numeric pairs.", item, sourcePath));
                    }
                }
            }
            return new PolygonProfileShape(points, span);
        }
    }

    private static string? ReadYield(CallExpressionSyntax call, string sourcePath, List<ProfileDiagnostic> diagnostics)
    {
        if (call.Arguments.Count == 1 && Unwrap(call.Arguments[0]) is NameExpressionSyntax name)
        {
            return name.IdentifierToken.Text;
        }
        if (call.Arguments.Count == 1 && Unwrap(call.Arguments[0]) is LiteralExpressionSyntax literal && literal.LiteralToken.Value is string value)
        {
            return value;
        }
        diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0030", "Yield requires one named profile state.", call, sourcePath));
        return null;
    }

    private static bool TryNumber(ExpressionSyntax expression, out double value)
    {
        expression = Unwrap(expression);
        if (expression is LiteralExpressionSyntax literal && literal.LiteralToken.Value is IConvertible convertible)
        {
            value = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(value);
        }
        if (expression is UnaryExpressionSyntax unary && unary.OperatorToken.Text == "-" && TryNumber(unary.Operand, out double positive))
        {
            value = -positive;
            return true;
        }
        value = double.NaN;
        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }
        return expression;
    }

    private static ExpressionSyntax? UnwrapNullable(ExpressionSyntax? expression) => expression is null ? null : Unwrap(expression);

    private static ProfileCompilationResult Failed(IReadOnlyList<ProfileDiagnostic> diagnostics)
    {
        return new ProfileCompilationResult(null, null, [], diagnostics, null, null, null);
    }

    private static ProfileDiagnostic Diagnostic(string id, string message, SyntaxNode node, string path)
    {
        return new ProfileDiagnostic(id, message, Span(node, path));
    }

    private static ProfileSourceSpan Span(SyntaxNode node, string path)
    {
        SyntaxToken[] tokens = Tokens(node).ToArray();
        if (tokens.Length == 0)
        {
            return new ProfileSourceSpan(path, 0, 1);
        }
        int start = tokens.Min(token => token.Position);
        int end = tokens.Max(token => token.Position + Math.Max(1, token.Text.Length));
        return new ProfileSourceSpan(path, start, Math.Max(1, end - start));
    }

    private static IEnumerable<SyntaxToken> Tokens(SyntaxNode node)
    {
        foreach (object child in node.GetChildren())
        {
            if (child is SyntaxToken token)
            {
                yield return token;
            }
            else if (child is SyntaxNode nested)
            {
                foreach (SyntaxToken descendant in Tokens(nested))
                {
                    yield return descendant;
                }
            }
        }
    }

    private sealed class AttributeReader
    {
        private readonly Dictionary<string, TsXmlAttributeSyntax> attributes;
        private readonly string path;
        private readonly List<ProfileDiagnostic> diagnostics;

        public AttributeReader(TsXmlElementExpressionSyntax element, string path, List<ProfileDiagnostic> diagnostics)
        {
            this.path = path;
            this.diagnostics = diagnostics;
            attributes = [];
            foreach (TsXmlAttributeSyntax attribute in element.Attributes)
            {
                if (!attributes.TryAdd(attribute.NameToken.Text, attribute))
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0040", $"Duplicate attribute '{attribute.NameToken.Text}'.", attribute, path));
                }
            }
        }

        public string? String(string name, bool required)
        {
            if (!attributes.Remove(name, out TsXmlAttributeSyntax? attribute))
            {
                if (required) diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0041", $"Missing '{name}' attribute.", new(path, 0, 1)));
                return null;
            }
            if (attribute.StringValueToken?.Value is string value) return value;
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0042", $"Attribute '{name}' must be a string literal.", attribute, path));
            return null;
        }

        public ExpressionSyntax? Expression(string name, bool required)
        {
            if (!attributes.Remove(name, out TsXmlAttributeSyntax? attribute))
            {
                if (required) diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0041", $"Missing '{name}' attribute.", new(path, 0, 1)));
                return null;
            }
            if (attribute.ExpressionValue is not null) return attribute.ExpressionValue;
            diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0043", $"Attribute '{name}' must be an expression.", attribute, path));
            return null;
        }

        public void RejectUnknown(params string[] allowed)
        {
            foreach (TsXmlAttributeSyntax attribute in attributes.Values)
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0044", $"Unknown Profile attribute '{attribute.NameToken.Text}'.", attribute, path));
            }
        }
    }

    private sealed class OptionReader
    {
        private readonly Dictionary<string, ObjectPropertySyntax> properties;
        private readonly string path;
        private readonly List<ProfileDiagnostic> diagnostics;

        public OptionReader(ObjectLiteralExpressionSyntax options, string path, List<ProfileDiagnostic> diagnostics)
        {
            this.path = path;
            this.diagnostics = diagnostics;
            properties = [];
            foreach (ObjectPropertySyntax property in options.Properties)
            {
                if (!properties.TryAdd(property.NameToken.Text, property))
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0050", $"Duplicate option '{property.NameToken.Text}'.", property, path));
                }
            }
        }

        public ExpressionSyntax? Expression(string name, bool required)
        {
            if (properties.Remove(name, out ObjectPropertySyntax? property)) return property.ValueExpression;
            if (required) diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0051", $"Missing '{name}' option.", new(path, 0, 1)));
            return null;
        }

        public string? String(string name, bool required)
        {
            ExpressionSyntax? expression = Expression(name, required);
            if (UnwrapNullable(expression) is LiteralExpressionSyntax literal && literal.LiteralToken.Value is string value) return value;
            if (expression is not null) diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0052", $"Option '{name}' must be a string.", expression, path));
            return null;
        }

        public string? Name(string name, bool required)
        {
            ExpressionSyntax? expression = Expression(name, required);
            if (UnwrapNullable(expression) is NameExpressionSyntax value) return value.IdentifierToken.Text;
            if (expression is not null) diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0053", $"Option '{name}' must be a semantic name.", expression, path));
            return null;
        }

        public double? Number(string name, bool required)
        {
            ExpressionSyntax? expression = Expression(name, required);
            if (expression is not null && TryNumber(expression, out double value)) return value;
            if (expression is not null) diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0054", $"Option '{name}' must be a finite number.", expression, path));
            return null;
        }

        public int? Int(string name, bool required)
        {
            double? value = Number(name, required);
            if (value is not null && value == Math.Truncate(value.Value)) return (int)value.Value;
            if (value is not null) diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0055", $"Option '{name}' must be an integer.", new(path, 0, 1)));
            return null;
        }

        public void RejectUnread()
        {
            foreach (ObjectPropertySyntax property in properties.Values)
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0056", $"Unknown option '{property.NameToken.Text}'.", property, path));
            }
        }
    }
}
