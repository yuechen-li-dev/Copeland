using Copeland.Profile;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;

namespace Copeland.TS.Profiles;

public static class ProfileTsxCompiler
{
    public static ProfileCompilationResult Compile(string source, string sourcePath = "Asset.profile.tsx")
        => CompileCore(source, sourcePath, null);

    public static ProfileCompilationResult CompileWithTemplates(
        string source,
        string templateSource,
        string sourcePath = "Asset.profile.tsx",
        string templateSourcePath = "ProfileTemplates.ts")
    {
        ArgumentNullException.ThrowIfNull(templateSource);
        return CompileCore(source, sourcePath, new TemplateLibrary(templateSource, templateSourcePath));
    }

    private static ProfileCompilationResult CompileCore(
        string source,
        string sourcePath,
        TemplateLibrary? templateLibrary)
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
            ExpressionSyntax childExpression = Unwrap(expressionChild.Expression);
            if (childExpression is TemplateInstantiationExpressionSyntax instantiation)
            {
                if (templateLibrary is null)
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0001", "Profile template specialization requires a supplied template library.", instantiation, sourcePath));
                    continue;
                }
                IReadOnlyList<ProfileOperation> generated = templateLibrary.Evaluate(
                    instantiation,
                    currentState,
                    sourcePath,
                    diagnostics);
                foreach (ProfileOperation generatedOperation in generated)
                {
                    operations.Add(generatedOperation);
                    currentState = generatedOperation.OutputState;
                }
                continue;
            }
            if (childExpression is not CallExpressionSyntax call || call.Target is not NameExpressionSyntax target)
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

    private sealed class TemplateLibrary(string source, string sourcePath)
    {
        public IReadOnlyList<ProfileOperation> Evaluate(
            TemplateInstantiationExpressionSyntax instantiation,
            string inputState,
            string profileSourcePath,
            List<ProfileDiagnostic> diagnostics)
        {
            SyntaxTree libraryTree = SyntaxTree.Parse(source, sourcePath);
            TemplateDeclarationSyntax? declaration = libraryTree.Root.Members
                .OfType<TemplateDeclarationSyntax>()
                .FirstOrDefault(candidate => candidate.Identifier.Text == instantiation.TemplateIdentifier.Text);
            if (declaration is null)
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0002", $"Profile template '{instantiation.TemplateIdentifier.Text}' was not found.", instantiation, profileSourcePath));
                return [];
            }
            if (instantiation.TypeArguments.Count > 0)
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0003", "Profile numeric specialization uses static value parameters, not type arguments.", instantiation, profileSourcePath));
                return [];
            }

            var supplied = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (TemplateInstantiationArgumentSyntax argument in instantiation.StaticArguments)
            {
                if (supplied.ContainsKey(argument.Identifier.Text))
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0006", $"Duplicate static argument '{argument.Identifier.Text}'.", argument, profileSourcePath));
                    continue;
                }
                if (!TryStaticArgument(argument.Value, out object? value))
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0004", $"Static argument '{argument.Identifier.Text}' must be a compile-time literal.", argument, profileSourcePath));
                    continue;
                }
                supplied[argument.Identifier.Text] = value;
            }
            if (diagnostics.Count > 0)
            {
                return [];
            }

            var arguments = new List<object?>();
            foreach (TemplateParameterSyntax parameter in declaration.Parameters)
            {
                if (supplied.TryGetValue(parameter.Identifier.Text, out object? value))
                {
                    arguments.Add(value);
                }
                else if (parameter.DefaultValue is null)
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0005", $"Missing static argument '{parameter.Identifier.Text}'.", instantiation, profileSourcePath));
                }
                else if (TryStaticArgument(parameter.DefaultValue, out object? defaultValue))
                {
                    arguments.Add(defaultValue);
                }
                else
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0004", $"Default for static argument '{parameter.Identifier.Text}' must be a compile-time literal at the Profile boundary.", instantiation, profileSourcePath));
                }
            }
            foreach (string unknown in supplied.Keys.Where(name => declaration.Parameters.All(parameter => parameter.Identifier.Text != name)))
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0006", $"Unknown static argument '{unknown}'.", instantiation, profileSourcePath));
            }
            if (diagnostics.Count > 0)
            {
                return [];
            }

            TemplateEvaluationResult result = CopelandProjectCompiler.CompileTemplates(
                [
                    new CopelandProjectSource("Profile.ts", "Profile.ts", ProfileTemplateFunctions.Source),
                    new CopelandProjectSource("ProfileTemplates.ts", sourcePath, source),
                ],
                declaration.Identifier.Text,
                arguments);
            foreach (Copeland.TS.Diagnostics.Diagnostic diagnostic in result.Diagnostics)
            {
                diagnostics.Add(new ProfileDiagnostic(
                    diagnostic.Id,
                    diagnostic.Message,
                    new ProfileSourceSpan(diagnostic.SourcePath ?? sourcePath, diagnostic.Position, Math.Max(1, diagnostic.Length))));
            }
            if (!result.Success || result.Value?.Value is not object?[] values)
            {
                if (result.Diagnostics.Count == 0)
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0007", $"Template '{declaration.Identifier.Text}' must return ProfileOperation[].", instantiation, profileSourcePath));
                }
                return [];
            }

            ProfileSourceSpan span = Span(instantiation, profileSourcePath);
            var operations = new List<ProfileOperation>();
            string currentState = inputState;
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] is not StaticEnumValue value || value.Type.Name != "ProfileOperation")
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0007", $"Template '{declaration.Identifier.Text}' returned a non-ProfileOperation element at index {index}.", instantiation, profileSourcePath));
                    continue;
                }
                ProfileOperation? operation = DecodeOperation(value, currentState, span, diagnostics);
                if (operation is null)
                {
                    continue;
                }
                operation = operation with
                {
                    TemplateProvenance = new ProfileTemplateProvenance(
                        declaration.Identifier.Text,
                        instantiation.StaticArguments.Select(argument => argument.Identifier.Text + "=" + Convert.ToString(supplied[argument.Identifier.Text], System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                        span,
                        index),
                };
                operations.Add(operation);
                currentState = operation.OutputState;
            }
            return operations;
        }

        private static bool TryStaticArgument(ExpressionSyntax expression, out object? value)
        {
            expression = Unwrap(expression);
            if (expression is LiteralExpressionSyntax literal)
            {
                value = literal.LiteralToken.Value;
                return value is int or double or string or bool;
            }
            if (expression is UnaryExpressionSyntax { OperatorToken.Text: "-" } unary
                && TryStaticArgument(unary.Operand, out object? positive))
            {
                value = positive switch
                {
                    int integer => -integer,
                    double number => -number,
                    _ => null,
                };
                return value is not null;
            }
            value = null;
            return false;
        }

        private static ProfileOperation? DecodeOperation(
            StaticEnumValue value,
            string input,
            ProfileSourceSpan span,
            List<ProfileDiagnostic> diagnostics)
        {
            if (value.Payloads.Count != 1 || value.Payloads[0] is not StaticRecordValue args)
            {
                diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TEMPLATE-0008", $"Profile operation '{value.Case.Name}' has an invalid semantic payload.", span));
                return null;
            }
            string id = Text(args, "id");
            string output = Text(args, "as");
            return value.Case.Name switch
            {
                "Add" => new AddProfileOperation(id, input, output, Shape(args, span), span),
                "Subtract" => new SubtractProfileOperation(id, input, output, Shape(args, span), span),
                "Hole" => new HoleProfileOperation(
                    id,
                    input,
                    output,
                    new CircleProfileShape(
                        Number(args, "radius"),
                        Number(args, "x"),
                        Number(args, "y"),
                        span),
                    span),
                "Tab" => EdgeOperation(true),
                "Notch" => EdgeOperation(false),
                "RepeatRadial" => new RepeatRadialProfileOperation(
                    id,
                    input,
                    output,
                    Integer(args, "count"),
                    Number(args, "toothDepth"),
                    Number(args, "toothFraction"),
                    Number(args, "rotation"),
                    span),
                "Translate" => new TransformProfileOperation(
                    id,
                    input,
                    output,
                    "Translate",
                    Number(args, "x"),
                    Number(args, "y"),
                    span),
                "Rotate" => new TransformProfileOperation(
                    id,
                    input,
                    output,
                    "Rotate",
                    Number(args, "degrees"),
                    0,
                    span),
                "Scale" => new TransformProfileOperation(
                    id,
                    input,
                    output,
                    "Scale",
                    Number(args, "x"),
                    Number(args, "y"),
                    span),
                "Mirror" => new TransformProfileOperation(
                    id,
                    input,
                    output,
                    "Mirror",
                    Text(args, "axis") == "X" ? 1 : 0,
                    0,
                    span),
                _ => Unknown(),
            };

            ProfileOperation EdgeOperation(bool tab)
            {
                ProfileEdge edge = Enum.Parse<ProfileEdge>(EnumCase(args, "edge"));
                return tab
                    ? new TabProfileOperation(
                        id,
                        input,
                        output,
                        edge,
                        Number(args, "width"),
                        Number(args, "depth"),
                        Number(args, "position"),
                        span)
                    : new NotchProfileOperation(
                        id,
                        input,
                        output,
                        edge,
                        Number(args, "width"),
                        Number(args, "depth"),
                        Number(args, "position"),
                        span);
            }

            ProfileOperation? Unknown()
            {
                diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TEMPLATE-0009", $"Unknown typed Profile operation '{value.Case.Name}'.", span));
                return null;
            }
        }

        private static ProfileShapeSpec Shape(StaticRecordValue operation, ProfileSourceSpan span)
        {
            StaticEnumValue shape = (StaticEnumValue)Field(operation, "shape");
            StaticRecordValue args = (StaticRecordValue)shape.Payloads[0];
            return shape.Case.Name switch
            {
                "Circle" => new CircleProfileShape(Number(args, "radius"), Number(args, "x"), Number(args, "y"), span),
                "Rectangle" => new RectangleProfileShape(Number(args, "width"), Number(args, "height"), span),
                _ => throw new InvalidOperationException($"Unknown Profile shape '{shape.Case.Name}'."),
            };
        }

        private static StaticValue Field(StaticRecordValue record, string name)
            => record.Fields.Single(field => field.Key.Name == name).Value;

        private static string Text(StaticRecordValue record, string name)
            => Convert.ToString(((StaticPrimitiveValue)Field(record, name)).Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        private static double Number(StaticRecordValue record, string name)
            => Convert.ToDouble(((StaticPrimitiveValue)Field(record, name)).Value, System.Globalization.CultureInfo.InvariantCulture);

        private static int Integer(StaticRecordValue record, string name)
            => Convert.ToInt32(((StaticPrimitiveValue)Field(record, name)).Value, System.Globalization.CultureInfo.InvariantCulture);

        private static string EnumCase(StaticRecordValue record, string name)
            => ((StaticEnumValue)Field(record, name)).Case.Name;
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
