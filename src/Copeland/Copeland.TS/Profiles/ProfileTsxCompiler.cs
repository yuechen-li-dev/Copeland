using Copeland.Profile;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;

namespace Copeland.TS.Profiles;

public static partial class ProfileTsxCompiler
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

    private static IReadOnlyList<ProfileOperation> DecodeProfileBodyValue(
        StaticValue value,
        string inputState,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
    {
        if (value is StaticEnumValue operationValue && operationValue.Type.Name == "ProfileOperation")
        {
            ProfileOperation? operation = DecodeOperation(operationValue, inputState, span, diagnostics);
            return operation is null ? [] : [operation];
        }
        if (value is StaticArrayValue array
            && array.ArrayType.ElementType.Name == "ProfileOperation")
        {
            var operations = new List<ProfileOperation>();
            string currentState = inputState;
            foreach (StaticValue element in array.Elements)
            {
                if (element is not StaticEnumValue elementOperationValue)
                {
                    diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0004", "Profile operation arrays may contain only ProfileOperation values.", span));
                    continue;
                }
                ProfileOperation? operation = DecodeOperation(elementOperationValue, currentState, span, diagnostics);
                if (operation is null)
                {
                    continue;
                }
                operations.Add(operation);
                currentState = operation.OutputState;
            }
            return operations;
        }

        diagnostics.Add(new ProfileDiagnostic(
            "COPE-PROFILE-TSX-0004",
            $"Profile body expression has type '{value.Type.Name}'; expected ProfileOperation or ProfileOperation[].",
            span));
        return [];
    }

    private static ProfileOperation? DecodeOperation(
        StaticEnumValue value,
        string input,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
    {
        if (value.Type.Name != "ProfileOperation"
            || value.Payloads.Count != 1
            || value.Payloads[0] is not StaticRecordValue args)
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TEMPLATE-0008", "Profile operation has an invalid typed semantic payload.", span));
            return null;
        }

        string id = Text(args, "id");
        string output = Text(args, "as");
        return value.Case.Name switch
        {
            "Add" => new AddProfileOperation(id, input, output, RequireShape(args, span, diagnostics), span),
            "Subtract" => new SubtractProfileOperation(id, input, output, RequireShape(args, span, diagnostics), span),
            "Hole" => new HoleProfileOperation(id, input, output, new CircleProfileShape(Number(args, "radius"), Number(args, "x", 0), Number(args, "y", 0), span), span),
            "Tab" => EdgeOperation(true),
            "Notch" => EdgeOperation(false),
            "RepeatRadial" => new RepeatRadialProfileOperation(id, input, output, Integer(args, "count"), Number(args, "toothDepth"), Number(args, "toothFraction", 0.5), Number(args, "rotation", 0), span),
            "Translate" => new TransformProfileOperation(id, input, output, "Translate", Number(args, "x"), Number(args, "y"), span),
            "Rotate" => new TransformProfileOperation(id, input, output, "Rotate", Number(args, "degrees"), 0, span),
            "Scale" => new TransformProfileOperation(id, input, output, "Scale", Number(args, "x"), Number(args, "y"), span),
            "Mirror" => new TransformProfileOperation(id, input, output, "Mirror", Text(args, "axis") == "X" ? 1 : 0, 0, span),
            _ => Unknown(),
        };

        ProfileOperation EdgeOperation(bool tab)
        {
            ProfileEdge edge = Enum.Parse<ProfileEdge>(EnumCase(args, "edge"));
            double position = Number(args, "position", 0.5);
            return tab
                ? new TabProfileOperation(id, input, output, edge, Number(args, "width"), Number(args, "depth"), position, span)
                : new NotchProfileOperation(id, input, output, edge, Number(args, "width"), Number(args, "depth"), position, span);
        }

        ProfileOperation? Unknown()
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TEMPLATE-0009", $"Unknown typed Profile operation case '{value.Case.Name}'.", span));
            return null;
        }
    }

    private static ProfileShapeSpec RequireShape(
        StaticRecordValue operation,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
        => DecodeShape(Field(operation, "shape"), span, diagnostics)
            ?? new CircleProfileShape(double.NaN, 0, 0, span);

    private static ProfileShapeSpec? DecodeShape(
        StaticValue value,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
    {
        if (value is not StaticEnumValue { Type.Name: "ProfileShape", Payloads.Count: 1 } shape
            || shape.Payloads[0] is not StaticRecordValue args)
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0020", $"Profile base expression has type '{value.Type.Name}'; expected ProfileShape.", span));
            return null;
        }
        return shape.Case.Name switch
        {
            "Circle" => new CircleProfileShape(Number(args, "radius"), Number(args, "x", 0), Number(args, "y", 0), span),
            "Rectangle" => new RectangleProfileShape(Number(args, "width"), Number(args, "height"), span),
            "RoundedRectangle" => new RoundedRectangleProfileShape(Number(args, "width"), Number(args, "height"), Number(args, "radius"), span),
            "Ellipse" => new EllipseProfileShape(Number(args, "radiusX"), Number(args, "radiusY"), Number(args, "x", 0), Number(args, "y", 0), span),
            "RegularPolygon" => new RegularPolygonProfileShape(Integer(args, "sides"), Number(args, "radius"), Number(args, "rotation", 90), span),
            "Polygon" => new PolygonProfileShape(PointArray(args, "points"), span),
            _ => Unknown(),
        };

        ProfileShapeSpec? Unknown()
        {
            diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0021", $"Unknown typed Profile shape case '{shape.Case.Name}'.", span));
            return null;
        }
    }

    private static IReadOnlyList<VectorPoint> PointArray(StaticRecordValue record, string name)
    {
        StaticArrayValue points = (StaticArrayValue)Field(record, name);
        return points.Elements.Select(point =>
        {
            StaticArrayValue pair = (StaticArrayValue)point;
            return new VectorPoint(
                Convert.ToDouble(((StaticPrimitiveValue)pair.Elements[0]).Value, System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToDouble(((StaticPrimitiveValue)pair.Elements[1]).Value, System.Globalization.CultureInfo.InvariantCulture));
        }).ToArray();
    }

    private static StaticValue Field(StaticRecordValue record, string name)
        => record.Fields.Single(field => field.Key.Name == name).Value;

    private static StaticValue? OptionalField(StaticRecordValue record, string name)
        => record.Fields.FirstOrDefault(field => field.Key.Name == name).Value;

    private static string Text(StaticRecordValue record, string name)
        => Convert.ToString(((StaticPrimitiveValue)Field(record, name)).Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static double Number(StaticRecordValue record, string name, double? defaultValue = null)
    {
        StaticValue? value = OptionalField(record, name);
        if (value is StaticEnumValue { Type: OptionTypeSymbol, Case.Name: "None" })
        {
            value = null;
        }
        else if (value is StaticEnumValue { Type: OptionTypeSymbol, Case.Name: "Some", Payloads.Count: 1 } some)
        {
            value = some.Payloads[0];
        }
        return value is null && defaultValue.HasValue
            ? defaultValue.Value
            : Convert.ToDouble(((StaticPrimitiveValue)value!).Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int Integer(StaticRecordValue record, string name)
        => Convert.ToInt32(((StaticPrimitiveValue)Field(record, name)).Value, System.Globalization.CultureInfo.InvariantCulture);

    private static string EnumCase(StaticRecordValue record, string name)
        => ((StaticEnumValue)Field(record, name)).Case.Name;

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
        ExpressionSyntax? styleExpression = attributes.Expression("style", required: false);
        attributes.RejectUnknown("name", "baseState", "base", "style");
        var ordinaryExpressions = new List<(string Name, ExpressionSyntax Expression)>();
        if (styleExpression is not null)
        {
            ordinaryExpressions.Add(("__cope_profile_style", styleExpression));
        }
        if (baseExpression is not null)
        {
            ordinaryExpressions.Add(("__cope_profile_base", baseExpression));
        }
        var bodyExpressions = new List<(ExpressionSyntax Expression, TemplateInstantiationExpressionSyntax? Template, string? ProbeName)>();
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
                bodyExpressions.Add((childExpression, instantiation, null));
                continue;
            }
            if (childExpression is CallExpressionSyntax call
                && call.Target is NameExpressionSyntax target
                && target.IdentifierToken.Text == "Yield")
            {
                yieldState = ReadYield(call, sourcePath, diagnostics);
                continue;
            }
            string probeName = "__cope_profile_child_" + bodyExpressions.Count;
            ordinaryExpressions.Add((probeName, childExpression));
            bodyExpressions.Add((childExpression, null, probeName));
        }

        ProfileExpressionEvaluation evaluation = ProfileExpressionEvaluator.Evaluate(
            source,
            sourcePath,
            exports[0],
            ordinaryExpressions,
            templateLibrary?.SourceText,
            templateLibrary?.SourcePath);
        diagnostics.AddRange(evaluation.Diagnostics);

        ProfileStyle style = ProfileStyle.Default;
        if (styleExpression is not null
            && evaluation.Values.TryGetValue("__cope_profile_style", out StaticValue? styleValue))
        {
            if (styleValue is StaticRecordValue record && record.Type.Name == "ProfileStyle")
            {
                style = new ProfileStyle(Text(record, "fill"));
                if (!style.IsValid)
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0051", "ProfileStyle.fill requires black, white, or #RRGGBB.", styleExpression, sourcePath));
                }
            }
            else
            {
                diagnostics.Add(Diagnostic("COPE-PROFILE-TSX-0050", "Profile style must be a typed ProfileStyle record.", styleExpression, sourcePath));
            }
        }

        ProfileShapeSpec? baseShape = null;
        if (baseExpression is not null
            && evaluation.Values.TryGetValue("__cope_profile_base", out StaticValue? baseValue))
        {
            baseShape = DecodeShape(baseValue, Span(baseExpression, sourcePath), diagnostics);
        }

        List<ProfileOperation> operations = [];
        string currentState = baseState;
        foreach ((ExpressionSyntax bodyExpression, TemplateInstantiationExpressionSyntax? instantiation, string? probeName) in bodyExpressions)
        {
            IReadOnlyList<ProfileOperation> generated;
            if (instantiation is not null)
            {
                if (templateLibrary is null)
                {
                    diagnostics.Add(Diagnostic("COPE-PROFILE-TEMPLATE-0001", "Profile template specialization requires a supplied template library.", instantiation, sourcePath));
                    continue;
                }
                generated = templateLibrary.Evaluate(instantiation, currentState, sourcePath, diagnostics);
            }
            else
            {
                if (probeName is null || !evaluation.Values.TryGetValue(probeName, out StaticValue? value))
                {
                    continue;
                }
                generated = DecodeProfileBodyValue(value, currentState, Span(bodyExpression, sourcePath), diagnostics);
            }
            foreach (ProfileOperation operation in generated)
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
        if (result.Success && styleExpression is not null)
        {
            result = result with
            {
                Style = style,
                Svg = ProfileSvgExporter.ExportLayers([new ProfileSvgLayer(name, result.Shape!, style)], padding: 0)
            };
        }
        return result.Diagnostics.Count == 0
            ? result
            : result with { Diagnostics = diagnostics.Concat(result.Diagnostics).ToArray() };
    }

    private sealed class TemplateLibrary(string source, string sourcePath)
    {
        public string SourceText => source;
        public string SourcePath => sourcePath;

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

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }
        return expression;
    }

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

    private sealed record ProfileExpressionEvaluation(
        IReadOnlyDictionary<string, StaticValue> Values,
        IReadOnlyList<ProfileDiagnostic> Diagnostics);

    private static class ProfileExpressionEvaluator
    {
        private const string AuthoringLogicalPath = "ProfileAuthoring.ts";

        public static ProfileExpressionEvaluation Evaluate(
            string source,
            string sourcePath,
            ExportDefaultDeclarationSyntax profileExport,
            IReadOnlyList<(string Name, ExpressionSyntax Expression)> expressions,
            string? librarySource,
            string? librarySourcePath)
        {
            string evaluationSource = BuildEvaluationSource(source, profileExport, expressions);
            var sources = new List<CopelandProjectSource>
            {
                new("Profile.ts", "Profile.ts", ProfileTemplateFunctions.Source),
                new(AuthoringLogicalPath, sourcePath, evaluationSource),
            };
            if (librarySource is not null)
            {
                sources.Add(new("ProfileTemplates.ts", librarySourcePath ?? "ProfileTemplates.ts", librarySource));
            }

            CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources);
            var diagnostics = compilation.Diagnostics
                .Select(diagnostic => new ProfileDiagnostic(
                    diagnostic.Id,
                    diagnostic.Message,
                    new ProfileSourceSpan(
                        diagnostic.SourcePath ?? sourcePath,
                        diagnostic.Position,
                        Math.Max(1, diagnostic.Length))))
                .ToList();
            if (diagnostics.Count > 0)
            {
                return new ProfileExpressionEvaluation(new Dictionary<string, StaticValue>(), diagnostics);
            }

            BoundCompilation[] boundCompilations = compilation.Modules
                .Select(module => module.BoundCompilation)
                .OfType<BoundCompilation>()
                .ToArray();
            BoundFunctionDeclaration[] functions = boundCompilations
                .SelectMany(bound => bound.Program.Functions)
                .ToArray();
            IReadOnlyDictionary<FunctionSymbol, FunctionEffectSummary> summaries = boundCompilations
                .SelectMany(bound => bound.Program.FunctionEffects)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var evaluator = new StaticEvaluator(functions, summaries, StaticEvaluationLimits.M1);
            var environment = new Dictionary<VariableSymbol, StaticValue>();
            var values = new Dictionary<string, StaticValue>(StringComparer.Ordinal);
            BoundCompilation? authoring = compilation.Modules
                .FirstOrDefault(module => module.LogicalPath == AuthoringLogicalPath)
                ?.BoundCompilation;
            if (authoring is null)
            {
                diagnostics.Add(new ProfileDiagnostic("COPE-PROFILE-TSX-0006", "Ordinary Profile expression module did not bind.", new(sourcePath, 0, 1)));
                return new ProfileExpressionEvaluation(values, diagnostics);
            }

            foreach (BoundStatement statement in authoring.Program.GlobalStatements)
            {
                if (statement is not BoundVariableDeclaration variable)
                {
                    continue;
                }
                try
                {
                    StaticValue value = evaluator.Evaluate(variable.Initializer, environment);
                    environment[variable.Variable] = value;
                    if (expressions.Any(expression => expression.Name == variable.Variable.Name))
                    {
                        values[variable.Variable.Name] = value;
                    }
                }
                catch (StaticEvaluationException exception)
                {
                    diagnostics.Add(new ProfileDiagnostic(
                        exception.DiagnosticId,
                        exception.Message,
                        new ProfileSourceSpan(sourcePath, 0, 1)));
                }
            }
            return new ProfileExpressionEvaluation(values, diagnostics);
        }

        private static string BuildEvaluationSource(
            string source,
            ExportDefaultDeclarationSyntax profileExport,
            IReadOnlyList<(string Name, ExpressionSyntax Expression)> expressions)
        {
            char[] text = source.ToCharArray();
            ProfileSourceSpan exportSpan = Span(profileExport, string.Empty);
            for (int index = exportSpan.Start; index < exportSpan.Start + exportSpan.Length && index < text.Length; index++)
            {
                if (text[index] != '\r' && text[index] != '\n')
                {
                    text[index] = ' ';
                }
            }

            var builder = new System.Text.StringBuilder(new string(text));
            builder.AppendLine();
            if (!source.Contains("from \"./Profile\"", StringComparison.Ordinal)
                && !source.Contains("from './Profile'", StringComparison.Ordinal))
            {
                builder.AppendLine("import { Add, Circle, EdgeOperationArgs, Ellipse, Hole, HoleArgs, Layer, LayerId, Layers, Mirror, Notch, Polygon, Profile, ProfileEdge, ProfileLayer, ProfileLayerId, ProfileOperation, ProfileShape, ProfileSource, ProfileStyle, Rectangle, RegularPolygon, RepeatRadial, RepeatRadialArgs, Rotate, RoundedRectangle, Scale, ShapeOperationArgs, Subtract, Tab, Translate } from \"./Profile\";");
            }
            builder.AppendLine("const Top: ProfileEdge = ProfileEdge.Top;");
            builder.AppendLine("const Right: ProfileEdge = ProfileEdge.Right;");
            builder.AppendLine("const Bottom: ProfileEdge = ProfileEdge.Bottom;");
            builder.AppendLine("const Left: ProfileEdge = ProfileEdge.Left;");
            foreach ((string name, ExpressionSyntax expression) in expressions)
            {
                builder.Append("const ").Append(name).Append(" = ")
                    .Append(SourceText(source, expression)).AppendLine(";");
            }
            return builder.ToString();
        }

        private static string SourceText(string source, SyntaxNode node)
        {
            ProfileSourceSpan span = Span(node, string.Empty);
            return source.Substring(span.Start, span.Length);
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

}
