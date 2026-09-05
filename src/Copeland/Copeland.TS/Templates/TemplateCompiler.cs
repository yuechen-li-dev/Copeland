using Copeland.TS.Diagnostics;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoslynCSharpSyntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using RoslynDiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Copeland.TS.Templates;

public sealed class TemplateEvaluationResult(
    string templateName,
    ProjectTree? project,
    IReadOnlyList<Diagnostic> diagnostics,
    IReadOnlyList<string> instantiationChain,
    Diagram? diagram = null,
    TemplateTypedValue? value = null)
{
    public string TemplateName { get; } = templateName;
    public ProjectTree? Project { get; } = project;
    public Diagram? Diagram { get; } = diagram;
    public TemplateTypedValue? Value { get; } = value;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public IReadOnlyList<string> InstantiationChain { get; } = instantiationChain;
    public bool Success => (Project is not null || Diagram is not null || Value is not null) && Diagnostics.Count == 0;
}

public sealed record TemplateTypedValue(TypeSymbol Type, object? Value, string DeterministicHash);

/// <summary>
/// Evaluates only the explicitly supported structural subset. It has no callback
/// into the CLR, JavaScript, filesystem, process, clock, or environment.
/// </summary>
public static class TemplateCompiler
{
    /// <summary>Evaluates declarations already discovered and type-bound by the ordinary compiler.</summary>
    public static TemplateEvaluationResult Evaluate(BoundCompilation boundCompilation, string? entryName = null)
        => Evaluate([boundCompilation], entryName);

    /// <summary>
    /// Evaluates a project-wide set of already bound modules. Calls are linked by
    /// <see cref="TemplateSymbol"/> identity, so aliases and same-named templates
    /// in separate modules cannot collide.
    /// </summary>
    public static TemplateEvaluationResult Evaluate(IReadOnlyList<BoundCompilation> boundCompilations, string? entryName = null)
        => Evaluate(boundCompilations, entryName, []);

    /// <summary>Invokes the selected template through the same bound plan used by nested template calls.</summary>
    public static TemplateEvaluationResult Evaluate(IReadOnlyList<BoundCompilation> boundCompilations, string? entryName, IReadOnlyList<object?> entryArguments)
    {
        var diagnostics = boundCompilations.SelectMany(compilation => compilation.Diagnostics).ToList();
        IReadOnlyList<BoundTemplateDeclaration> templates = boundCompilations
            .SelectMany(compilation => compilation.Program.Templates)
            .OrderBy(template => template.Symbol.StableIdentity, StringComparer.Ordinal)
            .ToArray();
        if (templates.Count == 0)
        {
            diagnostics.Add(new Diagnostic("COPE-TEMPLATE-0001", "No template declaration was found.", 0, 0));
            return new TemplateEvaluationResult(entryName ?? string.Empty, null, diagnostics, []);
        }

        BoundTemplateDeclaration? entry = entryName is null
            ? templates.OrderBy(template => template.Symbol.StableIdentity, StringComparer.Ordinal).First()
            : templates.FirstOrDefault(template => string.Equals(template.Symbol.Name, entryName, StringComparison.Ordinal));
        if (entry is null)
        {
            diagnostics.Add(new Diagnostic("COPE-TEMPLATE-0001", $"Template '{entryName}' was not found.", 0, 0));
            return new TemplateEvaluationResult(entryName!, null, diagnostics, []);
        }
        TypeParameterSymbol? missingCliType = entry.Symbol.TypeParameters
            .Where((_, index) => entry.Symbol.TypeParameterDefaults.ElementAtOrDefault(index) is null)
            .FirstOrDefault();
        if (missingCliType is not null)
        {
            diagnostics.Add(new Diagnostic(
                "COPE-TEMPLATE-0010",
                $"CLI-facing template '{entry.Symbol.Name}' requires a default for type parameter '{missingCliType.Name}'. Source instantiations may bind it explicitly with 'instantiate'.",
                entry.Syntax.Identifier.Position,
                entry.Syntax.Identifier.Text.Length));
            return new TemplateEvaluationResult(entry.Symbol.Name, null, diagnostics, []);
        }

        diagnostics.AddRange(TemplatePlanValidator.Validate(templates));
        var evaluator = new BoundPlanEvaluator(boundCompilations, templates, diagnostics);
        object? result = evaluator.EvaluateTemplate(entry, entryArguments);
        Diagram? diagram = result as Diagram;
        TemplateTypedValue? typedValue = result switch
        {
            ProjectTree or DotNetSolutionValue or ArtifactNode or Diagram or null => null,
            _ => new TemplateTypedValue(entry.Symbol.ReturnType, result, HashTypedValue(entry.Symbol.ReturnType, result)),
        };
        ProjectTree? project = result switch
        {
            ProjectTree tree => tree,
            DotNetSolutionValue solution when solution.TryLower(out ProjectTree? tree, out IReadOnlyList<Diagnostic> loweringDiagnostics)
                => tree,
            DotNetSolutionValue solution => ReportLoweringFailure(solution),
            ArtifactNode artifact => LowerArtifact(artifact),
            null => null,
            Diagram => null,
            _ => null,
        };
        return new TemplateEvaluationResult(entry.Symbol.Name, project, diagnostics, evaluator.InstantiationChain, diagram, typedValue);

        ProjectTree? ReportLoweringFailure(DotNetSolutionValue solution)
        {
            _ = solution.TryLower(out _, out IReadOnlyList<Diagnostic> loweringDiagnostics);
            diagnostics.AddRange(loweringDiagnostics);
            return null;
        }

        ProjectTree? LowerArtifact(ArtifactNode artifact)
        {
            if (ProjectTree.TryCreate([artifact], out ProjectTree? tree, out IReadOnlyList<Diagnostic> artifactDiagnostics))
            {
                return tree;
            }

            diagnostics.AddRange(artifactDiagnostics);
            return null;
        }
    }

    private static string HashTypedValue(TypeSymbol type, object? value)
    {
        string text = type.Name + ":" + DescribeTypedValue(value);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string DescribeTypedValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case StaticPrimitiveValue primitive:
                return primitive.Type.Name
                    + ":"
                    + Convert.ToString(primitive.Value, System.Globalization.CultureInfo.InvariantCulture);
            case StaticEnumValue enumValue:
                return enumValue.Type.Name
                    + "."
                    + enumValue.Case.Name
                    + "("
                    + string.Join(",", enumValue.Payloads.Select(DescribeTypedValue))
                    + ")";
            case StaticRecordValue record:
                IEnumerable<string> recordFields = record.RecordType.Fields
                    .OrderBy(field => field.Id.Ordinal)
                    .Select(field => field.Name + "=" + DescribeTypedValue(record.Fields.GetValueOrDefault(field)));
                return record.Type.Name + "{" + string.Join(",", recordFields) + "}";
            case StaticArrayValue array:
                return array.Type.Name
                    + "["
                    + string.Join(",", array.Elements.Select(DescribeTypedValue))
                    + "]";
            case object?[] array:
                return "[" + string.Join(",", array.Select(DescribeTypedValue)) + "]";
            case IReadOnlyDictionary<string, object?> fields:
                IEnumerable<string> orderedFields = fields
                    .OrderBy(field => field.Key, StringComparer.Ordinal)
                    .Select(field => field.Key + "=" + DescribeTypedValue(field.Value));
                return "{" + string.Join(",", orderedFields) + "}";
            default:
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                    ?? string.Empty;
        }
    }

    public static TemplateEvaluationResult Evaluate(string sourceText, string? entryName = null)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(sourceText);
        return compilation.BoundCompilation is null
            ? new TemplateEvaluationResult(entryName ?? string.Empty, null, compilation.Diagnostics, [])
            : Evaluate(compilation.BoundCompilation, entryName);
    }

    // Retained only as an implementation-local parser regression helper while
    // migrating historic tests. Production compiler and CLI paths call the
    // bound-plan overloads above.
    private static TemplateEvaluationResult EvaluateLegacySyntax(string sourceText, string? entryName = null)
    {
        SyntaxTree tree = SyntaxTree.Parse(sourceText);
        var diagnostics = tree.Diagnostics.ToList();
        var templates = tree.Root.Members.OfType<TemplateDeclarationSyntax>()
            .ToDictionary(template => template.Identifier.Text, StringComparer.Ordinal);
        if (templates.Count == 0)
        {
            diagnostics.Add(new Diagnostic("COPE-TEMPLATE-0001", "No template declaration was found.", 0, 0));
            return new TemplateEvaluationResult(entryName ?? string.Empty, null, diagnostics, []);
        }

        TemplateDeclarationSyntax? entry = entryName is null
            ? templates.Values.OrderBy(template => template.Identifier.Position).First()
            : templates.GetValueOrDefault(entryName);
        if (entry is null)
        {
            diagnostics.Add(new Diagnostic("COPE-TEMPLATE-0001", $"Template '{entryName}' was not found.", 0, 0));
            return new TemplateEvaluationResult(entryName!, null, diagnostics, []);
        }

        var declaredConstraintTypes = tree.Root.Members
            .Where(member => member is RecordDeclarationSyntax or InterfaceDeclarationSyntax)
            .Select(member => member switch
            {
                RecordDeclarationSyntax record => record.Identifier.Text,
                InterfaceDeclarationSyntax @interface => @interface.Identifier.Text,
                _ => string.Empty,
            })
            .ToHashSet(StringComparer.Ordinal);
        var evaluator = new Evaluator(templates, declaredConstraintTypes, diagnostics);
        ProjectTree? project = evaluator.EvaluateTemplate(entry, []);
        return new TemplateEvaluationResult(entry.Identifier.Text, project, diagnostics, evaluator.InstantiationChain);
    }

    private sealed class Evaluator(
        IReadOnlyDictionary<string, TemplateDeclarationSyntax> templates,
        IReadOnlySet<string> declaredConstraintTypes,
        List<Diagnostic> diagnostics)
    {
        private readonly Stack<string> _activeTemplates = new();
        private readonly List<string> _instantiationChain = [];

        public IReadOnlyList<string> InstantiationChain => _instantiationChain;

        public ProjectTree? EvaluateTemplate(TemplateDeclarationSyntax template, IReadOnlyList<object?> arguments)
        {
            if (_activeTemplates.Contains(template.Identifier.Text, StringComparer.Ordinal))
            {
                string chain = string.Join(" -> ", _activeTemplates.Reverse().Append(template.Identifier.Text));
                Report("COPE-TEMPLATE-0004", $"Recursive template expansion is not supported: {chain}.", template.Identifier);
                return null;
            }

            if (!HasProjectTreeResult(template))
            {
                Report("COPE-TEMPLATE-0005", $"Template '{template.Identifier.Text}' must declare ProjectTree as its result type.", template.Identifier);
                return null;
            }

            if (template.Parameters.Count != arguments.Count)
            {
                Report("COPE-TEMPLATE-0002", $"Template '{template.Identifier.Text}' expects {template.Parameters.Count} static argument(s), but received {arguments.Count}.", template.Identifier);
                return null;
            }

            _activeTemplates.Push(template.Identifier.Text);
            _instantiationChain.Add(string.Join(" -> ", _activeTemplates.Reverse()));
            try
            {
                var context = new EvaluationContext();
                for (int index = 0; index < template.Parameters.Count; index++)
                {
                    context.Values[template.Parameters[index].Identifier.Text] = arguments[index];
                }

                ExecuteStatement(template.Body, context);
                if (context.ReturnValue is ProjectTree directProject)
                {
                    return directProject;
                }

                if (context.ReturnValue is not null)
                {
                    Report("COPE-TEMPLATE-0005", $"Template '{template.Identifier.Text}' returned a non-ProjectTree artifact value.", template.Identifier);
                    return null;
                }

                if (context.Emitted.Count == 0)
                {
                    Report("COPE-TEMPLATE-0005", $"Template '{template.Identifier.Text}' did not return a ProjectTree.", template.Identifier);
                    return null;
                }

                return ProjectTree.TryCreate(context.Emitted, out ProjectTree? project, out IReadOnlyList<Diagnostic> artifactDiagnostics)
                    ? project
                    : ReportArtifacts(artifactDiagnostics);
            }
            finally
            {
                _activeTemplates.Pop();
            }
        }

        private ProjectTree? ReportArtifacts(IReadOnlyList<Diagnostic> artifactDiagnostics)
        {
            diagnostics.AddRange(artifactDiagnostics);
            return null;
        }

        private void ExecuteStatement(StatementSyntax statement, EvaluationContext context)
        {
            if (context.ReturnValue is not null) return;
            switch (statement)
            {
                case BlockStatementSyntax block:
                    foreach (StatementSyntax child in block.Statements) ExecuteStatement(child, context);
                    break;
                case VariableDeclarationStatementSyntax variable:
                    if (variable.Keyword.Kind != SyntaxKind.ConstKeyword)
                    {
                        Report("COPE-STATIC-0001", "Template-local values must be immutable 'const' declarations.", variable.Keyword);
                        break;
                    }
                    context.Values[variable.Identifier.Text] = EvaluateExpression(variable.Initializer, context);
                    break;
                case ExpressionStatementSyntax expressionStatement:
                    ExecuteExpressionStatement(expressionStatement.Expression, context);
                    break;
                case ReturnStatementSyntax returned:
                    context.ReturnValue = returned.Expression is null ? null : EvaluateExpression(returned.Expression, context);
                    break;
                case StaticIfStatementSyntax staticIf:
                    object? condition = EvaluateExpression(staticIf.Condition, context);
                    if (condition is not bool selected)
                    {
                        Report("COPE-STATIC-0001", "Static if condition must be a statically evaluable boolean.", staticIf.StaticKeyword);
                        break;
                    }
                    ExecuteStatement(selected ? staticIf.ThenStatement : staticIf.ElseStatement ?? new BlockStatementSyntax(staticIf.OpenParenToken, [], staticIf.CloseParenToken), context);
                    break;
                case StaticForStatementSyntax staticFor:
                    object? iterable = EvaluateExpression(staticFor.Iterable, context);
                    if (iterable is not IReadOnlyList<object?> values)
                    {
                        Report("COPE-STATIC-0006", "Static for requires a tuple or array with statically known contents.", staticFor.StaticKeyword);
                        break;
                    }
                    foreach (object? value in values)
                    {
                        context.Values[staticFor.Identifier.Text] = value;
                        ExecuteStatement(staticFor.Body, context);
                    }
                    context.Values.Remove(staticFor.Identifier.Text);
                    break;
                case StaticMatchStatementSyntax staticMatch:
                    ExecuteStaticMatch(staticMatch, context);
                    break;
                case WhileStatementSyntax:
                case ForStatementSyntax:
                case ForOfStatementSyntax:
                    Report("COPE-STATIC-0002", "Unbounded static operation. Templates do not permit while or ordinary for loops.", statement.GetChildren().OfType<SyntaxToken>().FirstOrDefault()!);
                    break;
                default:
                    Report("COPE-STATIC-0003", "Unsupported static construct. Templates permit only const, emit, return, static if, static match, and finite static for.", statement.GetChildren().OfType<SyntaxToken>().FirstOrDefault()!);
                    break;
            }
        }

        private void ExecuteStaticMatch(StaticMatchStatementSyntax staticMatch, EvaluationContext context)
        {
            object? value = EvaluateExpression(staticMatch.Expression, context);
            if (value is bool && (!staticMatch.Arms.Any(arm => arm.Pattern.CaseIdentifier.Kind == SyntaxKind.TrueKeyword)
                                  || !staticMatch.Arms.Any(arm => arm.Pattern.CaseIdentifier.Kind == SyntaxKind.FalseKeyword)))
            {
                Report("COPE-STATIC-0004", "Static match over boolean values must include both true and false arms.", staticMatch.MatchKeyword);
                return;
            }
            StaticMatchArmSyntax? selected = staticMatch.Arms.FirstOrDefault(arm => PatternEquals(arm.Pattern.CaseIdentifier.Text, value));
            if (selected is null)
            {
                Report("COPE-STATIC-0004", "Static match is not exhaustive for the selected static value.", staticMatch.MatchKeyword);
                return;
            }
            ExecuteStatement(selected.Statement, context);
        }

        private static bool PatternEquals(string pattern, object? value)
            => value switch
            {
                bool boolean => string.Equals(pattern, boolean ? "true" : "false", StringComparison.Ordinal),
                string text => string.Equals(pattern, text, StringComparison.Ordinal),
                int number => string.Equals(pattern, number.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal),
                _ => false,
            };

        private void ExecuteExpressionStatement(ExpressionSyntax expression, EvaluationContext context)
        {
            if (expression is not CallExpressionSyntax { Target: NameExpressionSyntax { IdentifierToken.Text: "emit" }, Arguments.Count: 1 } call)
            {
                Report("COPE-STATIC-0003", "Only emit(artifact) is allowed as a template expression statement.", FirstToken(expression));
                return;
            }
            object? value = EvaluateExpression(call.Arguments[0], context);
            if (value is ArtifactNode artifact)
            {
                context.Emitted.Add(artifact);
                return;
            }
            if (value is ProjectTree project)
            {
                context.Emitted.AddRange(project.Files);
                return;
            }
            Report("COPE-TEMPLATE-0005", "emit requires an artifact or ProjectTree value.", call.OpenParenToken);
        }

        private object? EvaluateExpression(ExpressionSyntax expression, EvaluationContext context)
        {
            return expression switch
            {
                LiteralExpressionSyntax literal => EvaluateLiteral(literal.LiteralToken),
                TemplateExpressionSyntax template => EvaluateTemplateText(template, context),
                NameExpressionSyntax name => Lookup(name, context),
                ArrayLiteralExpressionSyntax array => array.Elements.Select(item => EvaluateExpression(item, context)).ToArray(),
                ObjectLiteralExpressionSyntax record => record.Properties.ToDictionary(property => property.NameToken.Text, property => EvaluateExpression(property.ValueExpression, context), StringComparer.Ordinal),
                ParenthesizedExpressionSyntax parenthesized => EvaluateExpression(parenthesized.Expression, context),
                UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.BangToken && EvaluateExpression(unary.Operand, context) is bool value => !value,
                BinaryExpressionSyntax binary => EvaluateBinary(binary, context),
                MemberAccessExpressionSyntax access => EvaluateMember(access, context),
                CallExpressionSyntax call => EvaluateCall(call, context),
                GenericCallExpressionSyntax genericCall => EvaluateGenericCall(genericCall, context),
                _ => UnsupportedExpression(expression),
            };
        }

        private object? EvaluateGenericCall(GenericCallExpressionSyntax call, EvaluationContext context)
        {
            if (call.Target is not NameExpressionSyntax name || !templates.TryGetValue(name.IdentifierToken.Text, out TemplateDeclarationSyntax? template))
            {
                return UnsupportedExpression(call);
            }
            if (call.TypeArguments.Any(type => type is not IdentifierTypeSyntax))
            {
                Report("COPE-TEMPLATE-0003", "Template type arguments must name declared Copeland record or interface types in M0.", call.LessToken);
                return null;
            }
            ValidateTemplateConstraints(template, call.TypeArguments, call.LessToken);
            return EvaluateTemplate(template, []);
        }

        private void ValidateTemplateConstraints(TemplateDeclarationSyntax template, IReadOnlyList<TypeSyntax> arguments, SyntaxToken anchor)
        {
            if (arguments.Count != template.TypeParameters.Count)
            {
                Report("COPE-TEMPLATE-0002", $"Template '{template.Identifier.Text}' expects {template.TypeParameters.Count} type argument(s).", anchor);
                return;
            }
            for (int index = 0; index < arguments.Count; index++)
            {
                if (template.TypeParameters[index].RequirementNames.Count == 0) continue;
                string argument = ((IdentifierTypeSyntax)arguments[index]).Identifier.Text;
                foreach (SyntaxToken required in template.TypeParameters[index].RequirementNames)
                {
                    if (!declaredConstraintTypes.Contains(required.Text))
                    {
                        Report("COPE-TEMPLATE-0002", $"Template type constraint '{required.Text}' is not a declared Copeland record or interface.", anchor);
                        continue;
                    }
                    if (!string.Equals(argument, required.Text, StringComparison.Ordinal))
                    {
                        Report("COPE-TEMPLATE-0002", $"Template type constraint failure: '{argument}' does not satisfy '{required.Text}'. M0 constraints reuse declared record/interface names.", anchor);
                    }
                }
            }
        }

        private object? EvaluateCall(CallExpressionSyntax call, EvaluationContext context)
        {
            if (call.Target is not NameExpressionSyntax name)
            {
                return UnsupportedExpression(call);
            }
            string nameText = name.IdentifierToken.Text;
            object?[] arguments = call.Arguments.Select(argument => EvaluateExpression(argument, context)).ToArray();
            if (templates.TryGetValue(nameText, out TemplateDeclarationSyntax? template))
            {
                return EvaluateTemplate(template, arguments);
            }
            return nameText switch
            {
                "textFile" => CreateFile(arguments, "text", call.OpenParenToken),
                "sourceFile" => CreateFile(arguments, "source", call.OpenParenToken),
                "directory" => CreateDirectory(arguments, call.OpenParenToken),
                "project" => CreateProject(arguments, call.OpenParenToken),
                _ => ForbiddenCall(name.IdentifierToken),
            };
        }

        private object? CreateFile(IReadOnlyList<object?> arguments, string kind, SyntaxToken anchor)
        {
            if (arguments.Count != 2 || arguments[0] is not string path || arguments[1] is not string contents)
            {
                Report("COPE-TEMPLATE-0005", $"{kind}File requires (path: string, contents: string).", anchor);
                return null;
            }
            string provenance = string.Join(" -> ", _activeTemplates.Reverse());
            return kind == "source"
                ? new SourceFileArtifact(path, ProjectTree.EncodeText(contents), provenance)
                : new TextFileArtifact(path, ProjectTree.EncodeText(contents), provenance);
        }

        private object? CreateDirectory(IReadOnlyList<object?> arguments, SyntaxToken anchor)
        {
            if (arguments.Count != 2 || arguments[0] is not string path || arguments[1] is not IReadOnlyList<object?> children)
            {
                Report("COPE-TEMPLATE-0005", "directory requires (path: string, children: ArtifactNode[]).", anchor);
                return null;
            }
            ArtifactNode[] artifacts = children.OfType<ArtifactNode>().ToArray();
            if (artifacts.Length != children.Count)
            {
                Report("COPE-TEMPLATE-0005", "directory children must all be artifacts.", anchor);
                return null;
            }
            return new DirectoryArtifact(path, artifacts, string.Join(" -> ", _activeTemplates.Reverse()));
        }

        private object? CreateProject(IReadOnlyList<object?> arguments, SyntaxToken anchor)
        {
            IEnumerable<ArtifactNode> nodes = arguments.Count switch
            {
                0 => [],
                1 when arguments[0] is IReadOnlyList<object?> children => children.OfType<ArtifactNode>(),
                _ => arguments.OfType<ArtifactNode>(),
            };
            if (!ProjectTree.TryCreate(nodes, out ProjectTree? project, out IReadOnlyList<Diagnostic> artifactDiagnostics))
            {
                diagnostics.AddRange(artifactDiagnostics);
                return null;
            }
            return project;
        }

        private object? EvaluateBinary(BinaryExpressionSyntax binary, EvaluationContext context)
        {
            object? left = EvaluateExpression(binary.Left, context);
            object? right = EvaluateExpression(binary.Right, context);
            return binary.OperatorToken.Kind switch
            {
                SyntaxKind.PlusToken when left is string leftText && right is string rightText => leftText + rightText,
                SyntaxKind.EqualsEqualsToken or SyntaxKind.EqualsEqualsEqualsToken => Equals(left, right),
                SyntaxKind.BangEqualsToken or SyntaxKind.BangEqualsEqualsToken => !Equals(left, right),
                SyntaxKind.AmpersandAmpersandToken when left is bool leftBoolean && right is bool rightBoolean => leftBoolean && rightBoolean,
                SyntaxKind.PipePipeToken when left is bool leftBoolean && right is bool rightBoolean => leftBoolean || rightBoolean,
                _ => UnsupportedExpression(binary),
            };
        }

        private object? EvaluateMember(MemberAccessExpressionSyntax access, EvaluationContext context)
        {
            object? target = EvaluateExpression(access.Target, context);
            if (target is IReadOnlyDictionary<string, object?> record && record.TryGetValue(access.NameToken.Text, out object? value)) return value;
            return UnsupportedExpression(access);
        }

        private string EvaluateTemplateText(TemplateExpressionSyntax template, EvaluationContext context)
        {
            var text = new System.Text.StringBuilder();
            foreach (TemplatePartSyntax part in template.Parts)
            {
                if (part is TemplateTextPartSyntax literal) text.Append(literal.Text);
                if (part is TemplateInterpolationPartSyntax interpolation) text.Append(EvaluateExpression(interpolation.Expression, context)?.ToString());
            }
            return text.ToString();
        }

        private static object? EvaluateLiteral(SyntaxToken token)
            => token.Kind switch
            {
                SyntaxKind.TrueKeyword => true,
                SyntaxKind.FalseKeyword => false,
                SyntaxKind.NullKeyword => null,
                _ => token.Value,
            };

        private object? Lookup(NameExpressionSyntax name, EvaluationContext context)
        {
            if (context.Values.TryGetValue(name.IdentifierToken.Text, out object? value)) return value;
            Report("COPE-TEMPLATE-0003", $"'{name.IdentifierToken.Text}' is not a static template value.", name.IdentifierToken);
            return null;
        }

        private object? ForbiddenCall(SyntaxToken token)
        {
            Report("COPE-STATIC-0005", $"Forbidden static side effect or runtime call '{token.Text}'. Templates may call only artifact constructors and other templates.", token);
            return null;
        }

        private object? UnsupportedExpression(ExpressionSyntax expression)
        {
            Report("COPE-STATIC-0003", "Unsupported static expression. Use literals, immutable records, arrays, template parameters, and approved artifact constructors.", FirstToken(expression));
            return null;
        }

        private static bool HasProjectTreeResult(TemplateDeclarationSyntax template)
            => template.ReturnType is IdentifierTypeSyntax { Identifier.Text: "ProjectTree" };

        private void Report(string id, string message, SyntaxToken token)
            => diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length)));

        private static SyntaxToken FirstToken(SyntaxNode node)
            => node.GetChildren().OfType<SyntaxToken>().FirstOrDefault() ?? new SyntaxToken(SyntaxKind.BadToken, 0, string.Empty, null);

        private sealed class EvaluationContext
        {
            public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);
            public List<ArtifactNode> Emitted { get; } = [];
            public object? ReturnValue { get; set; }
        }
    }

    /// <summary>
    /// Mechanical evaluator for compiler-bound template plans. It deliberately
    /// has no syntax, name-resolution, or type-binding dependency.
    /// </summary>
    private sealed class BoundPlanEvaluator
    {
        private readonly IReadOnlyDictionary<TemplateSymbol, BoundTemplateDeclaration> _templates;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<BoundSemanticCallSite>> _callSitesByCaller;
        private readonly List<Diagnostic> _diagnostics;
        private readonly Stack<TemplateSymbol> _active = new();
        private readonly List<string> _instantiationChain = [];
        private readonly Dictionary<string, object?> _completedInvocations = new(StringComparer.Ordinal);
        private readonly StaticEvaluator _ordinaryEvaluator;
        private int _instantiationCount;
        private int _metadataIterations;

        private const int MaximumInstantiationDepth = 64;
        private const int MaximumInstantiations = 4_096;
        private const int MaximumMetadataIterations = 100_000;
        private const int MaximumGeneratedArtifacts = 100_000;
        private const int MaximumReflectedCallSites = 256;
        private const int MaximumReflectedMetadataBytes = 262_144;

        public BoundPlanEvaluator(
            IReadOnlyList<BoundCompilation> boundCompilations,
            IReadOnlyList<BoundTemplateDeclaration> templates,
            List<Diagnostic> diagnostics)
        {
            _templates = templates.ToDictionary(template => template.Symbol);
            BoundFunctionDeclaration[] functions = boundCompilations
                .SelectMany(compilation => compilation.Program.Functions)
                .ToArray();
            IReadOnlyDictionary<FunctionSymbol, FunctionEffectSummary> summaries = boundCompilations
                .SelectMany(compilation => compilation.Program.FunctionEffects)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            _ordinaryEvaluator = new StaticEvaluator(functions, summaries, StaticEvaluationLimits.M1);
            _callSitesByCaller = boundCompilations
                .SelectMany(compilation => compilation.Program.SemanticCallSites)
                .GroupBy(call => call.Caller.Id, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<BoundSemanticCallSite>)group
                        .OrderBy(call => call.Source.Path, StringComparer.Ordinal)
                        .ThenBy(call => call.Source.StartLine)
                        .ThenBy(call => call.Source.StartColumn)
                        .ThenBy(call => call.Callee?.Id, StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal);
            _diagnostics = diagnostics;
        }

        public IReadOnlyList<string> InstantiationChain => _instantiationChain;

        public object? EvaluateTemplate(
            BoundTemplateDeclaration declaration,
            IReadOnlyList<object?> arguments,
            IReadOnlyList<TypeSymbol>? suppliedTypeArguments = null)
        {
            _instantiationCount++;
            if (_instantiationCount > MaximumInstantiations)
            {
                Report("COPE-TEMPLATE-0016", $"Template instantiation count exceeded the M1 limit of {MaximumInstantiations}.", declaration.Symbol.Name, 0, 1);
                return null;
            }
            if (_active.Count >= MaximumInstantiationDepth)
            {
                Report("COPE-TEMPLATE-0016", $"Template instantiation depth exceeded the M1 limit of {MaximumInstantiationDepth}.", declaration.Symbol.Name, 0, 1);
                return null;
            }
            if (_active.Contains(declaration.Symbol))
            {
                string chain = string.Join(" -> ", _active.Reverse().Append(declaration.Symbol).Select(Describe));
                Report("COPE-TEMPLATE-0004", $"Recursive template expansion is not supported: {chain}.", declaration.Symbol.Name, 0, 1);
                return null;
            }
            if (declaration.Plan is null)
            {
                Report("COPE-TEMPLATE-0003", $"Template '{declaration.Symbol.Name}' has no bound static plan.", declaration.Symbol.Name, 0, 1);
                return null;
            }
            TypeSymbol[] typeArguments = declaration.Symbol.TypeParameters
                .Select((_, index) => suppliedTypeArguments?.ElementAtOrDefault(index)
                    ?? declaration.Symbol.TypeParameterDefaults.ElementAtOrDefault(index)
                    ?? PrimitiveTypeSymbol.Error)
                .ToArray();
            if (arguments.Count > declaration.Symbol.Parameters.Count)
            {
                Report("COPE-TEMPLATE-0002", $"Template '{declaration.Symbol.Name}' expects at most {declaration.Symbol.Parameters.Count} static argument(s), but received {arguments.Count}.", declaration.Symbol.Name, 0, 1);
                return null;
            }

            for (int index = 0; index < arguments.Count; index++)
            {
                TypeSymbol expected = declaration.Symbol.Parameters[index].Type;
                if (!MatchesPrimitiveStaticArgument(expected, arguments[index]))
                {
                    Report("COPE-STATIC-0007", $"Static argument '{declaration.Symbol.Parameters[index].Name}' must have type '{expected.Name}'.", declaration.Symbol.Name, 0, 1);
                    return null;
                }
            }

            _active.Push(declaration.Symbol);
            _instantiationChain.Add(string.Join(" -> ", _active.Reverse().Select(Describe)));
            try
            {
                var context = new BoundEvaluationContext(typeArguments);
                for (int index = 0; index < declaration.Symbol.Parameters.Count; index++)
                {
                    // Parameter symbols are not re-created by the plan; template
                    // declarations with value parameters bind local references by name.
                    VariableSymbol parameter = declaration.Parameters[index];
                    if (index < arguments.Count)
                    {
                        context.Values[parameter] = arguments[index];
                        continue;
                    }

                    BoundTemplateValue? defaultValue = declaration.ParameterDefaults.ElementAtOrDefault(index);
                    if (defaultValue is null)
                    {
                        Report("COPE-TEMPLATE-0002", $"Missing required static parameter '{parameter.Name}' for template '{declaration.Symbol.Name}'.", declaration.Symbol.Name, 0, 1);
                        return null;
                    }
                    context.Values[parameter] = EvaluateValue(defaultValue, context);
                }
                Execute(declaration.Plan, context);
                if (context.DidReturn)
                {
                    return context.ReturnValue;
                }
                if (declaration.Symbol.ReturnType != ArtifactTypeSymbol.ProjectTree)
                {
                    Report("COPE-TEMPLATE-0005", $"Template '{declaration.Symbol.Name}' must explicitly return its declared '{declaration.Symbol.ReturnType.Name}' result.", declaration.Symbol.Name, 0, 1);
                    return null;
                }
                return ProjectTree.TryCreate(context.Emitted, out ProjectTree? project, out IReadOnlyList<Diagnostic> artifactDiagnostics)
                    ? project
                    : ReportArtifacts(artifactDiagnostics);
            }
            finally
            {
                _active.Pop();
            }
        }

        private ProjectTree? ReportArtifacts(IReadOnlyList<Diagnostic> diagnostics)
        {
            _diagnostics.AddRange(diagnostics);
            return null;
        }

        private void Execute(BoundTemplateStatement statement, BoundEvaluationContext context)
        {
            if (context.DidReturn) return;
            switch (statement)
            {
                case BoundTemplateBlock block:
                    foreach (BoundTemplateStatement child in block.Statements) Execute(child, context);
                    break;
                case BoundTemplateLocal local:
                    context.Values[local.Local] = EvaluateValue(local.Initializer, context);
                    break;
                case BoundTemplateEmit emit:
                    object? emitted = EvaluateValue(emit.Value, context);
                    if (emitted is ArtifactNode node) context.Emitted.Add(node);
                    else if (emitted is ProjectTree project) context.Emitted.AddRange(project.Files);
                    else Report("COPE-TEMPLATE-0005", "emit requires an artifact or ProjectTree value.", emit.Anchor);
                    if (context.Emitted.Count > MaximumGeneratedArtifacts)
                    {
                        Report("COPE-TEMPLATE-0016", $"Generated artifact count exceeded the M1 limit of {MaximumGeneratedArtifacts}.", emit.Anchor);
                        context.DidReturn = true;
                    }
                    break;
                case BoundTemplateReturn returned:
                    context.ReturnValue = returned.Value is null ? null : EvaluateValue(returned.Value, context);
                    context.DidReturn = true;
                    break;
                case BoundStaticIf conditional:
                    if (EvaluateValue(conditional.Condition, context) is bool selected)
                    {
                        Execute(selected ? conditional.ThenStatement : conditional.ElseStatement ?? new BoundTemplateBlock(conditional.Anchor, []), context);
                    }
                    else
                    {
                        Report("COPE-STATIC-0001", "Bound static-if condition did not evaluate to boolean.", conditional.Anchor);
                    }
                    break;
                case BoundStaticFor loop:
                    if (EvaluateValue(loop.Values, context) is not object?[] values)
                    {
                        Report("COPE-STATIC-0006", "Bound static-for iterable did not evaluate to a finite array.", loop.Anchor);
                        break;
                    }
                    foreach (object? value in values)
                    {
                        _metadataIterations++;
                        if (_metadataIterations > MaximumMetadataIterations)
                        {
                            Report("COPE-TEMPLATE-0016", $"Static metadata iteration exceeded the M1 limit of {MaximumMetadataIterations}.", loop.Anchor);
                            context.DidReturn = true;
                            break;
                        }
                        context.Values[loop.Local] = value;
                        Execute(loop.Body, context);
                        if (context.DidReturn) break;
                    }
                    context.Values.Remove(loop.Local);
                    break;
                case BoundStaticMatch match:
                    object? input = EvaluateValue(match.Input, context);
                    BoundStaticMatchArm? arm = match.Arms.FirstOrDefault(candidate => Equals(candidate.Pattern.Value, input));
                    if (arm is null)
                    {
                        Report("COPE-STATIC-0004", "Static match has no bound arm for its selected static value.", match.Anchor);
                    }
                    else
                    {
                        Execute(arm.Statement, context);
                    }
                    break;
                default:
                    Report("COPE-STATIC-0003", "Template plan contains an unsupported static statement.", statement.Anchor);
                    break;
            }
        }

        private object? EvaluateValue(BoundTemplateValue value, BoundEvaluationContext context)
        {
            switch (value)
            {
                case BoundTemplateLiteral literal:
                    return literal.Value;
                case BoundTemplateLocalReference local:
                    return context.Values.GetValueOrDefault(local.Local);
                case BoundTemplateTypeName typeName:
                    return context.TypeArguments.ElementAtOrDefault(typeName.ParameterIndex)?.Name ?? "<missing-type>";
                case BoundTemplateReflection reflection:
                    return EvaluateValue(reflection.Value, context);
                case BoundTemplateTypeMetadataArray metadata:
                    return EvaluateTypeMetadata(metadata, context);
                case BoundTemplateCallMetadataArray calls:
                    return EvaluateCallMetadata(calls);
                case BoundTemplateArray array:
                    return array.Elements.Select(element => EvaluateValue(element, context)).ToArray();
                case BoundTemplateStructuralObject structural:
                    return structural.Fields.ToDictionary(
                        field => field.Name,
                        field => EvaluateValue(field.Value, context),
                        StringComparer.Ordinal);
                case BoundTemplateMemberAccess access:
                    object? receiver = EvaluateValue(access.Receiver, context);
                    if (receiver is IReadOnlyDictionary<string, object?> objectValue
                        && objectValue.TryGetValue(access.MemberName, out object? member))
                    {
                        return member;
                    }
                    Report("COPE-STATIC-0003", $"Bound static value has no member '{access.MemberName}'.", access.Anchor);
                    return null;
                case BoundTemplateString text:
                    return string.Concat(text.Parts.Select(part => EvaluateValue(part, context)?.ToString()));
                case BoundTemplateBinary { OperatorKind: SyntaxKind.PlusToken } binary:
                    return (EvaluateValue(binary.Left, context)?.ToString() ?? string.Empty)
                        + (EvaluateValue(binary.Right, context)?.ToString() ?? string.Empty);
                case BoundArtifactConstructor artifact:
                    return EvaluateArtifactConstructor(artifact, context);
                case BoundTemplateInvocation invocation:
                    return EvaluateInvocation(invocation, context);
                case BoundTemplateOrdinaryExpression ordinary:
                    return EvaluateOrdinaryExpression(ordinary, context);
                case BoundTemplateXmlElement xml:
                    return EvaluateXmlElement(xml, context);
                case BoundTypedSourceArtifact source:
                    return EvaluateTypedSourceArtifact(source, context);
                default:
                    Report("COPE-TEMPLATE-0003", "Template plan contains an unresolved static value.", value.Anchor);
                    return null;
            }
        }

        private StaticValue EvaluateOrdinaryExpression(
            BoundTemplateOrdinaryExpression ordinary,
            BoundEvaluationContext context)
        {
            var values = new Dictionary<VariableSymbol, StaticValue>();
            foreach ((VariableSymbol variable, object? value) in context.Values)
            {
                values[variable] = ToStaticValue(variable.Type, value);
            }
            try
            {
                return _ordinaryEvaluator.Evaluate(ordinary.Expression, values);
            }
            catch (StaticEvaluationException exception)
            {
                Report(exception.DiagnosticId, exception.Message, ordinary.Anchor);
                return new StaticPrimitiveValue(null, PrimitiveTypeSymbol.Error);
            }
        }

        private static StaticValue ToStaticValue(TypeSymbol type, object? value)
        {
            if (value is StaticValue staticValue)
            {
                return staticValue;
            }
            if (type is ArrayTypeSymbol arrayType && value is object?[] elements)
            {
                return new StaticArrayValue(
                    elements.Select(element => ToStaticValue(arrayType.ElementType, element)).ToArray(),
                    arrayType);
            }
            if (type is RecordTypeSymbol recordType
                && value is IReadOnlyDictionary<string, object?> fields)
            {
                return new StaticRecordValue(
                    recordType.Fields
                        .Where(field => fields.ContainsKey(field.Name))
                        .ToDictionary(
                            field => field,
                            field => ToStaticValue(field.Type, fields[field.Name])),
                    recordType);
            }
            return new StaticPrimitiveValue(value, type);
        }

        private object? EvaluateInvocation(BoundTemplateInvocation invocation, BoundEvaluationContext context)
        {
            if (!_templates.TryGetValue(invocation.Template, out BoundTemplateDeclaration? target))
            {
                Report("COPE-TEMPLATE-0003", $"Resolved template '{invocation.Template.Name}' is unavailable to this evaluation plan.", invocation.Anchor);
                return null;
            }
            object?[] values = invocation.Arguments.Select(argument => EvaluateValue(argument, context)).ToArray();
            TypeSymbol[] typeArguments = invocation.TypeArguments
                .Select(type => ResolveTypeArgument(type, context.TypeArguments))
                .ToArray();
            string cacheIdentity = invocation.Template.StableIdentity
                + "<" + string.Join(",", typeArguments.Select(TypeIdentity)) + ">"
                + "(" + string.Join(",", values.Select(ValueIdentity)) + ")";
            if (_completedInvocations.TryGetValue(cacheIdentity, out object? completed))
            {
                return completed;
            }
            object? result = EvaluateTemplate(target, values, typeArguments);
            if (result is not null && !_diagnostics.Any(diagnostic => diagnostic.Id == "COPE-TEMPLATE-0016"))
            {
                _completedInvocations[cacheIdentity] = result;
            }
            return result;
        }

        private static TypeSymbol ResolveTypeArgument(
            TypeSymbol type,
            IReadOnlyList<TypeSymbol> activeTypeArguments)
        {
            return type switch
            {
                TypeParameterTypeSymbol parameter => activeTypeArguments.ElementAtOrDefault(parameter.Ordinal)
                    ?? PrimitiveTypeSymbol.Error,
                ArrayTypeSymbol array => new ArrayTypeSymbol(
                    ResolveTypeArgument(array.ElementType, activeTypeArguments)),
                MutableArrayTypeSymbol array => new MutableArrayTypeSymbol(
                    ResolveTypeArgument(array.ElementType, activeTypeArguments)),
                SpanTypeSymbol span => new SpanTypeSymbol(
                    ResolveTypeArgument(span.ElementType, activeTypeArguments)),
                ResultTypeSymbol result => new ResultTypeSymbol(
                    ResolveTypeArgument(result.SuccessType, activeTypeArguments),
                    ResolveTypeArgument(result.ErrorType, activeTypeArguments)),
                _ => type,
            };
        }

        private static string TypeIdentity(TypeSymbol type)
            => type switch
            {
                RecordTypeSymbol record => record.StableIdentity ?? record.Name,
                EnumTypeSymbol @enum => @enum.StableIdentity ?? @enum.Name,
                ArrayTypeSymbol array => "array(" + TypeIdentity(array.ElementType) + ")",
                SpanTypeSymbol span => "span(" + TypeIdentity(span.ElementType) + ")",
                ResultTypeSymbol result => "result(" + TypeIdentity(result.SuccessType) + "," + TypeIdentity(result.ErrorType) + ")",
                _ => type.Name,
            };

        private static string ValueIdentity(object? value)
            => value switch
            {
                null => "null",
                bool boolean => boolean ? "true" : "false",
                int integer => "int:" + integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
                double number => "number:" + number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                string text => "string:" + text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + text,
                object?[] values => "[" + string.Join(",", values.Select(ValueIdentity)) + "]",
                IReadOnlyDictionary<string, object?> fields => "{" + string.Join(",", fields.OrderBy(field => field.Key, StringComparer.Ordinal).Select(field => field.Key + "=" + ValueIdentity(field.Value))) + "}",
                _ => value.GetType().FullName + ":" + value,
            };

        private object?[] EvaluateTypeMetadata(
            BoundTemplateTypeMetadataArray metadata,
            BoundEvaluationContext context)
        {
            TypeSymbol target = context.TypeArguments.ElementAtOrDefault(metadata.ParameterIndex)
                ?? PrimitiveTypeSymbol.Error;
            if (metadata.MetadataKind == BoundTemplateTypeMetadataKind.Fields)
            {
                IEnumerable<StructuralFieldSymbol> fields = target switch
                {
                    StructuralObjectTypeSymbol structural => structural.Fields.OrderBy(field => field.Ordinal),
                    RecordTypeSymbol record => record.Fields
                        .OrderBy(field => field.Id.Ordinal)
                        .Select(field => new StructuralFieldSymbol(
                            field.Name,
                            field.Type,
                            field.Id.Ordinal,
                            field.IsOptional,
                            true)),
                    _ => [],
                };
                object?[] values = fields
                    .Select(field => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["name"] = field.Name,
                        ["typeName"] = field.Type.Name,
                        ["optional"] = field.IsOptional,
                        ["readonly"] = field.IsReadOnly,
                    })
                    .ToArray();
                if (values.Length == 0 && target is not StructuralObjectTypeSymbol and not RecordTypeSymbol)
                {
                    Report("COPE-REFLECT-0005", $"reflect fieldsOf<T>() requires a structural type or record, not '{target.Name}'.", metadata.Anchor);
                }
                return values;
            }

            if (target is not EnumTypeSymbol enumType)
            {
                Report("COPE-REFLECT-0006", $"reflect enumCasesOf<T>() requires a payload enum, not '{target.Name}'.", metadata.Anchor);
                return [];
            }
            return enumType.Cases
                .Select(@case => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = @case.Name,
                    ["payloadCount"] = @case.PayloadFields.Count,
                    ["payloadTypes"] = @case.PayloadFields.Select(field => (object?)field.Type.Name).ToArray(),
                })
                .ToArray();
        }

        private object?[] EvaluateCallMetadata(BoundTemplateCallMetadataArray metadata)
        {
            IReadOnlyList<BoundSemanticCallSite> callSites = _callSitesByCaller
                .GetValueOrDefault(metadata.Target.StableIdentity, []);
            if (callSites.Count > MaximumReflectedCallSites)
            {
                Report(
                    "COPE-REFLECT-0008",
                    $"reflect callsOf<F>() exceeded the direct call-site limit of {MaximumReflectedCallSites} for '{metadata.Target.Name}'.",
                    metadata.Anchor);
                return [];
            }

            object?[] values = callSites
                .Select(call => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["caller"] = CreateCallableValue(call.Caller),
                    ["callee"] = call.Callee is null ? null : CreateCallableValue(call.Callee),
                    ["kind"] = call.Kind.ToString().ToLowerInvariant(),
                    ["source"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["path"] = call.Source.Path,
                        ["startLine"] = call.Source.StartLine,
                        ["startColumn"] = call.Source.StartColumn,
                        ["endLine"] = call.Source.EndLine,
                        ["endColumn"] = call.Source.EndColumn,
                    },
                    ["unresolvedDisplayName"] = call.UnresolvedDisplayName,
                })
                .ToArray();
            int metadataBytes = JsonSerializer.SerializeToUtf8Bytes(values).Length;
            if (metadataBytes > MaximumReflectedMetadataBytes)
            {
                Report(
                    "COPE-REFLECT-0008",
                    $"reflect callsOf<F>() exceeded the metadata limit of {MaximumReflectedMetadataBytes} bytes for '{metadata.Target.Name}'.",
                    metadata.Anchor);
                return [];
            }
            return values;
        }

        private static IReadOnlyDictionary<string, object?> CreateCallableValue(CallableIdentity callable)
            => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = callable.Id,
                ["name"] = callable.Name,
                ["displayName"] = callable.DisplayName,
                ["module"] = callable.Module,
                ["containingType"] = callable.ContainingType,
                ["parameterTypes"] = callable.ParameterTypes.Cast<object?>().ToArray(),
                ["genericArity"] = callable.GenericArity,
            };

        private object? EvaluateArtifactConstructor(BoundArtifactConstructor constructor, BoundEvaluationContext context)
        {
            object?[] arguments = constructor.Arguments.Select(argument => EvaluateValue(argument, context)).ToArray();
            string provenance = string.Join(" -> ", _active.Reverse().Select(Describe));
            try
            {
                return constructor.Intrinsic switch
                {
                    BoundArtifactIntrinsic.TextFile when arguments is [string path, string content]
                        => new TextFileArtifact(path, ProjectTree.EncodeText(content), provenance),
                    BoundArtifactIntrinsic.SourceFile when arguments is [string path, string content]
                        => new SourceFileArtifact(path, ProjectTree.EncodeText(content), provenance),
                    BoundArtifactIntrinsic.TestFile when arguments is [string path, string content]
                        => new TestFileArtifact(path, ProjectTree.EncodeText(content), provenance),
                    BoundArtifactIntrinsic.Directory when arguments is [string path, object?[] children]
                        => new DirectoryArtifact(path, children.OfType<ArtifactNode>().ToArray(), provenance),
                    BoundArtifactIntrinsic.Project when arguments is [object?[] children]
                        => CreateProject(children.OfType<ArtifactNode>(), constructor.Anchor),
                    BoundArtifactIntrinsic.CsProjectFile when arguments is [string path, TemplateXmlElementValue xml]
                        => new ProjectFileArtifact(path, ProjectTree.EncodeText(SerializeXml(xml)), provenance),
                    BoundArtifactIntrinsic.SlnxFile when arguments is [string path, string projectPath]
                        => new ProjectFileArtifact(path, ProjectTree.EncodeText($"<Solution>\n  <Project Path=\"{EscapeXml(projectPath)}\" />\n</Solution>\n"), provenance),
                    BoundArtifactIntrinsic.NpmDependency when arguments is [string name, string version]
                        => new NpmDependencyValue(name, version, provenance),
                    BoundArtifactIntrinsic.NpmPackageManifest when arguments is [string name, string version, object?[] dependencies]
                        => new NpmPackageManifestValue(name, version, dependencies.OfType<NpmDependencyValue>().ToArray(), provenance),
                    BoundArtifactIntrinsic.JsonFile when arguments is [string path, NpmPackageManifestValue manifest]
                        => new ProjectFileArtifact(path, ProjectTree.EncodeText(SerializePackageManifest(manifest)), provenance),
                    BoundArtifactIntrinsic.CopelandSourceSet when arguments is [object?[] includes]
                        => new CopelandSourceSetValue(includes.OfType<string>().ToArray(), provenance),
                    BoundArtifactIntrinsic.CopelandProjectTypeSet when arguments is [object?[] projectTypes]
                        => new CopelandProjectTypeSetValue(projectTypes.OfType<string>().ToArray(), provenance),
                    BoundArtifactIntrinsic.TypeScriptWorkspace when arguments is [string projectPath, CopelandSourceSetValue includes, CopelandProjectTypeSetValue projectTypes]
                        => new TypeScriptWorkspaceValue(projectPath, includes.Includes, projectTypes.Types, provenance),
                    BoundArtifactIntrinsic.WorkspaceFile when arguments is [string path, TypeScriptWorkspaceValue workspace]
                        => new ProjectFileArtifact(path, ProjectTree.EncodeText(SerializeWorkspace(workspace)), provenance),
                    BoundArtifactIntrinsic.DotNetProject when arguments is [string name, object?[] files]
                        => new DotNetProjectValue(name, files.OfType<ArtifactNode>().ToArray(), provenance),
                    BoundArtifactIntrinsic.DotNetSolution when arguments is [string name, DotNetProjectValue project, object?[] files]
                        => new DotNetSolutionValue(name, project, files.OfType<ArtifactNode>().ToArray(), provenance),
                    BoundArtifactIntrinsic.DiagramNode when arguments is [string id, string label]
                        => new DiagramNode(id, label),
                    BoundArtifactIntrinsic.DiagramEdge when arguments is [string from, string to, string label]
                        => new DiagramEdge(from, to, string.IsNullOrEmpty(label) ? null : label),
                    BoundArtifactIntrinsic.Diagram when arguments is [object?[] nodes, object?[] edges, string direction]
                        => CreateDiagram(
                            nodes.OfType<DiagramNode>(),
                            edges.OfType<DiagramEdge>(),
                            direction,
                            null,
                            constructor.Anchor),
                    BoundArtifactIntrinsic.RecordDiagram when arguments is [string typeName, object?[] fields, string direction]
                        => CreateRecordDiagram(typeName, fields, direction, constructor.Anchor),
                    BoundArtifactIntrinsic.EnumDiagram when arguments is [string typeName, object?[] cases, string direction]
                        => CreateEnumDiagram(typeName, cases, direction, constructor.Anchor),
                    BoundArtifactIntrinsic.CallGraphDiagram when arguments is [object?[] calls]
                        => CreateCallGraphDiagram(calls, constructor.Anchor),
                    _ => ReportInvalidIntrinsic(constructor),
                };
            }
            catch (ArgumentException exception)
            {
                Report("COPE-ARTIFACT-0001", exception.Message, constructor.Anchor);
                return null;
            }
        }

        private object? EvaluateTypedSourceArtifact(BoundTypedSourceArtifact source, BoundEvaluationContext context)
        {
            string? path = EvaluateValue(source.Path, context) as string;
            if (path is null) return null;
            var imports = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BoundTemplateStructuralField field in source.Parameters.Fields)
            {
                if (imports.ContainsKey(field.Name))
                {
                    Report("COPE-ARTIFACT-0009", $"Duplicate imported parameter '{field.Name}'.", source.Anchor);
                    return null;
                }
                if (EvaluateValue(field.Value, context) is not string value)
                {
                    Report("COPE-ARTIFACT-0008", $"Imported source parameter '{field.Name}' did not evaluate to a string.", source.Anchor);
                    return null;
                }
                imports[field.Name] = value;
            }

            if (!TryGetLanguage(source.LanguageName, out ArtifactLanguage language)) return null;
            if (!ValidateExtension(path, language, source.Anchor)) return null;
            string text = source.Body.BodyText;
            foreach ((string name, string value) in imports)
            {
                if (!IsIdentifier(value))
                {
                    Report("COPE-ARTIFACT-0010", $"Imported parameter '{name}' has value '{value}', which is not valid in the M0 identifier insertion role.", source.Anchor);
                    return null;
                }
                text = Regex.Replace(text, $@"(?<![A-Za-z0-9_$]){Regex.Escape(name)}(?![A-Za-z0-9_$])", value);
            }

            if (!ValidateEmbeddedSource(text, language, source.Anchor, path)) return null;
            string provenance = string.Join(" -> ", _active.Reverse().Select(Describe));
            return source.ArtifactKind == "testFile"
                ? new TestFileArtifact(path, ProjectTree.EncodeText(text), provenance)
                : new SourceFileArtifact(path, ProjectTree.EncodeText(text), provenance);
        }

        private bool TryGetLanguage(string name, out ArtifactLanguage language)
        {
            language = name switch
            {
                "CopelandTS" => ArtifactLanguage.CopelandTS,
                "CopelandTest" => ArtifactLanguage.CopelandTest,
                "CSharp" => ArtifactLanguage.CSharp,
                _ => default,
            };
            if (name is "CopelandTS" or "CopelandTest" or "CSharp") return true;
            Report("COPE-ARTIFACT-0003", $"Unknown artifact language type '{name}'.", new SyntaxToken(SyntaxKind.IdentifierToken, 0, name, name));
            return false;
        }

        private bool ValidateExtension(string path, ArtifactLanguage language, SyntaxToken anchor)
        {
            bool valid = language switch
            {
                ArtifactLanguage.CSharp => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase),
                ArtifactLanguage.CopelandTest => path.EndsWith(".tsxtest", StringComparison.OrdinalIgnoreCase),
                ArtifactLanguage.CopelandTS => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (!valid) Report("COPE-ARTIFACT-0011", $"Artifact path '{path}' does not match selected language '{language}'.", anchor);
            return valid;
        }

        private bool ValidateEmbeddedSource(string text, ArtifactLanguage language, SyntaxToken anchor, string path)
        {
            if (language == ArtifactLanguage.CSharp)
            {
                Microsoft.CodeAnalysis.SyntaxTree csharpTree = RoslynCSharpSyntaxTree.ParseText(text, path: path);
                RoslynDiagnostic? error = csharpTree.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == RoslynDiagnosticSeverity.Error);
                if (error is not null)
                {
                    Report("COPE-ARTIFACT-0012", $"Malformed C# source for generated artifact '{path}': {error.GetMessage()}", anchor);
                    return false;
                }
                return true;
            }

            // Copeland test modules add the bounded Xunit [Fact] declaration
            // marker; it is retained in output but is not an ordinary CTS
            // expression, so exclude only that marker for CTS syntax parsing.
            string validationText = language == ArtifactLanguage.CopelandTest
                ? Regex.Replace(text, @"\[Fact\]\s*", string.Empty)
                : text;
            SourceFileKind kind = path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                ? SourceFileKind.TypeScriptXml
                : SourceFileKind.TypeScriptModule;
            Copeland.TS.Syntax.SyntaxTree tree = Copeland.TS.Syntax.SyntaxTree.Parse(validationText, kind);
            Copeland.TS.Diagnostics.Diagnostic? diagnostic = tree.Diagnostics.FirstOrDefault();
            if (diagnostic is not null)
            {
                Report("COPE-ARTIFACT-0013", $"Malformed Copeland source for generated artifact '{path}': {diagnostic.Message}", anchor);
                return false;
            }
            return true;
        }

        private static bool IsIdentifier(string value)
            => Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$");

        private TemplateXmlElementValue EvaluateXmlElement(BoundTemplateXmlElement element, BoundEvaluationContext context)
        {
            KeyValuePair<string, string>[] attributes = element.Attributes
                .Select(attribute => new KeyValuePair<string, string>(
                    attribute.Name,
                    EvaluateValue(attribute.Value, context)?.ToString() ?? string.Empty))
                .ToArray();
            object[] children = element.Children.Select(child => child switch
            {
                BoundTemplateXmlText text => (object)text.Text,
                BoundTemplateXmlValue value => EvaluateValue(value.Value, context)?.ToString() ?? string.Empty,
                BoundTemplateXmlNested nested => EvaluateXmlElement(nested.Element, context),
                _ => string.Empty,
            }).ToArray();
            return new TemplateXmlElementValue(element.Name, attributes, children);
        }

        private static string SerializeXml(TemplateXmlElementValue root)
        {
            var builder = new System.Text.StringBuilder();
            WriteElement(root, builder, 0);
            return builder.ToString();

            static void WriteElement(TemplateXmlElementValue element, System.Text.StringBuilder builder, int depth)
            {
                string indent = new(' ', depth * 2);
                builder.Append(indent).Append('<').Append(element.Name);
                foreach (KeyValuePair<string, string> attribute in element.Attributes)
                {
                    builder.Append(' ').Append(attribute.Key).Append("=\"").Append(EscapeXml(attribute.Value)).Append('"');
                }
                if (element.Children.Count == 0)
                {
                    builder.Append(" />\n");
                    return;
                }
                bool textOnly = element.Children.All(child => child is string);
                if (textOnly)
                {
                    builder.Append('>').Append(string.Concat(element.Children.Cast<string>().Select(EscapeXml)))
                        .Append("</").Append(element.Name).Append(">\n");
                    return;
                }
                builder.Append(">\n");
                foreach (object child in element.Children)
                {
                    if (child is TemplateXmlElementValue nested) WriteElement(nested, builder, depth + 1);
                    else if (child is string text && text.Length > 0) builder.Append(new string(' ', (depth + 1) * 2)).Append(EscapeXml(text)).Append('\n');
                }
                builder.Append(indent).Append("</").Append(element.Name).Append(">\n");
            }
        }

        private static string EscapeXml(string value)
            => System.Security.SecurityElement.Escape(value) ?? string.Empty;

        private static string SerializePackageManifest(NpmPackageManifestValue manifest)
            => JsonSerializer.Serialize(
                new
                {
                    name = manifest.Name.ToLowerInvariant(),
                    version = manifest.Version,
                    @private = true,
                    type = "module",
                    dependencies = manifest.Dependencies.ToDictionary(dependency => dependency.Name, dependency => dependency.Version, StringComparer.Ordinal),
                },
                new JsonSerializerOptions { WriteIndented = true }) + "\n";

        private static string SerializeWorkspace(TypeScriptWorkspaceValue workspace)
        {
            string includes = string.Join(", ", workspace.Includes.Select(value => $"\"{value}\""));
            string types = string.Join(", ", workspace.ProjectTypes.Select(value => $"\"{value}\""));
            return $"import {{ defineTypeScriptWorkspace }} from \"copeland/workspace\";\n\nexport default defineTypeScriptWorkspace({{\n    ownership: \"strict\",\n    tscl: {{\n        project: \"{workspace.ProjectPath}\",\n        include: [{includes}],\n        types: [{types}]\n    }}\n}});\n";
        }

        private Diagram? CreateRecordDiagram(
            string typeName,
            IReadOnlyList<object?> fields,
            string direction,
            SyntaxToken anchor)
        {
            string rootId = "type:" + typeName;
            var nodes = new List<DiagramNode> { new(rootId, typeName) };
            var edges = new List<DiagramEdge>();
            foreach (IReadOnlyDictionary<string, object?> field in fields.OfType<IReadOnlyDictionary<string, object?>>())
            {
                string name = field.GetValueOrDefault("name") as string ?? "<unknown>";
                string type = field.GetValueOrDefault("typeName") as string ?? "<unknown>";
                bool optional = field.GetValueOrDefault("optional") is true;
                string id = "field:" + name;
                nodes.Add(new DiagramNode(id, name + (optional ? "?" : string.Empty) + " : " + type));
                edges.Add(new DiagramEdge(rootId, id));
            }
            return CreateDiagram(nodes, edges, direction, typeName, anchor);
        }

        private Diagram? CreateEnumDiagram(
            string typeName,
            IReadOnlyList<object?> cases,
            string direction,
            SyntaxToken anchor)
        {
            string rootId = "type:" + typeName;
            var nodes = new List<DiagramNode> { new(rootId, typeName) };
            var edges = new List<DiagramEdge>();
            foreach (IReadOnlyDictionary<string, object?> item in cases.OfType<IReadOnlyDictionary<string, object?>>())
            {
                string name = item.GetValueOrDefault("name") as string ?? "<unknown>";
                string[] payloadTypes = item.GetValueOrDefault("payloadTypes") is object?[] payload
                    ? payload.OfType<string>().ToArray()
                    : [];
                string label = payloadTypes.Length == 0
                    ? name
                    : name + "(" + string.Join(", ", payloadTypes) + ")";
                string id = "case:" + name;
                nodes.Add(new DiagramNode(id, label));
                edges.Add(new DiagramEdge(rootId, id));
            }
            return CreateDiagram(nodes, edges, direction, typeName, anchor);
        }

        private Diagram? CreateCallGraphDiagram(
            IReadOnlyList<object?> calls,
            SyntaxToken anchor)
        {
            IReadOnlyDictionary<string, object?>[] sites = calls
                .OfType<IReadOnlyDictionary<string, object?>>()
                .ToArray();
            if (sites.Length == 0
                || sites[0].GetValueOrDefault("caller") is not IReadOnlyDictionary<string, object?> caller)
            {
                Report("COPE-DIAGRAM-0004", "callGraphDiagram requires at least one reflected call site.", anchor);
                return null;
            }

            string callerId = caller.GetValueOrDefault("id") as string ?? string.Empty;
            string callerName = caller.GetValueOrDefault("displayName") as string ?? "<unknown>";
            string rootId = "callable:" + callerId;
            var nodes = new List<DiagramNode> { new(rootId, callerName) };
            var edges = new List<DiagramEdge>();
            var resolved = sites
                .Select(site => site.GetValueOrDefault("callee"))
                .OfType<IReadOnlyDictionary<string, object?>>()
                .GroupBy(callee => callee.GetValueOrDefault("id") as string ?? string.Empty, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            HashSet<string> ambiguousNames = resolved
                .Select(group => group.First().GetValueOrDefault("name") as string ?? "<unknown>")
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (IGrouping<string, IReadOnlyDictionary<string, object?>> group in resolved)
            {
                IReadOnlyDictionary<string, object?> callee = group.First();
                string name = callee.GetValueOrDefault("name") as string ?? "<unknown>";
                string label = ambiguousNames.Contains(name)
                    ? callee.GetValueOrDefault("displayName") as string ?? name
                    : name;
                string nodeId = "callable:" + group.Key;
                nodes.Add(new DiagramNode(nodeId, label));
                edges.Add(new DiagramEdge(rootId, nodeId, group.Count() > 1 ? "×" + group.Count() : null));
            }

            return CreateDiagram(nodes, edges, "LeftRight", callerId, anchor);
        }

        private Diagram? CreateDiagram(
            IEnumerable<DiagramNode> nodes,
            IEnumerable<DiagramEdge> edges,
            string directionText,
            string? reflectedType,
            SyntaxToken anchor)
        {
            DiagramDirection? direction = directionText switch
            {
                "TopDown" => DiagramDirection.TopDown,
                "LeftRight" => DiagramDirection.LeftRight,
                _ => null,
            };
            if (direction is null)
            {
                Report("COPE-DIAGRAM-0003", $"Unknown Diagram direction '{directionText}'. Use 'TopDown' or 'LeftRight'.", anchor);
                return null;
            }

            string template = _active.TryPeek(out TemplateSymbol? symbol) ? symbol.Name : "<unknown-template>";
            if (Diagram.TryCreate(
                nodes,
                edges,
                direction.Value,
                new DiagramProvenance(template, reflectedType),
                out Diagram? diagram,
                out IReadOnlyList<Diagnostic> diagnostics))
            {
                return diagram;
            }
            _diagnostics.AddRange(diagnostics);
            return null;
        }

        private ProjectTree? CreateProject(IEnumerable<ArtifactNode> nodes, SyntaxToken anchor)
            => ProjectTree.TryCreate(nodes, out ProjectTree? project, out IReadOnlyList<Diagnostic> diagnostics)
                ? project
                : ReportProjectDiagnostics(diagnostics, anchor);

        private ProjectTree? ReportProjectDiagnostics(IReadOnlyList<Diagnostic> diagnostics, SyntaxToken _) 
        {
            _diagnostics.AddRange(diagnostics);
            return null;
        }

        private object? ReportInvalidIntrinsic(BoundArtifactConstructor constructor)
        {
            Report("COPE-TEMPLATE-0005", $"Bound artifact intrinsic '{constructor.Intrinsic}' received invalid values.", constructor.Anchor);
            return null;
        }

        private static string Describe(TemplateSymbol symbol)
            => symbol.StableIdentity + " (" + symbol.Name + ")";

        private static bool MatchesPrimitiveStaticArgument(TypeSymbol expected, object? value)
            => expected switch
            {
                PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.String => value is string,
                PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.Boolean => value is bool,
                PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.Int => value is int,
                PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.Float || primitive == PrimitiveTypeSymbol.Number => value is double or float or int,
                _ => true,
            };

        private void Report(string id, string message, SyntaxToken token)
            => _diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length)));

        private void Report(string id, string message, string _, int position, int length)
            => _diagnostics.Add(new Diagnostic(id, message, position, length));

        private sealed class BoundEvaluationContext(IReadOnlyList<TypeSymbol> typeArguments)
        {
            public IReadOnlyList<TypeSymbol> TypeArguments { get; } = typeArguments;
            public Dictionary<VariableSymbol, object?> Values { get; } = [];
            public List<ArtifactNode> Emitted { get; } = [];
            public object? ReturnValue { get; set; }
            public bool DidReturn { get; set; }
        }
    }

    internal static class TemplatePlanValidator
    {
        public static IReadOnlyList<Diagnostic> Validate(IReadOnlyList<BoundTemplateDeclaration> declarations)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (BoundTemplateDeclaration declaration in declarations)
            {
                if (declaration.Plan is null)
                {
                    diagnostics.Add(new Diagnostic("COPE-TEMPLATE-0003", $"Template '{declaration.Symbol.Name}' has no bound plan.", declaration.Syntax.Identifier.Position, Math.Max(1, declaration.Syntax.Identifier.Text.Length)));
                    continue;
                }
                VisitStatement(declaration.Plan, diagnostics);
            }
            return diagnostics;
        }

        private static void VisitStatement(BoundTemplateStatement statement, List<Diagnostic> diagnostics)
        {
            switch (statement)
            {
                case BoundTemplateBlock block:
                    foreach (BoundTemplateStatement child in block.Statements) VisitStatement(child, diagnostics);
                    break;
                case BoundTemplateEmit emit:
                    VisitValue(emit.Value, diagnostics);
                    break;
                case BoundTemplateLocal local:
                    VisitValue(local.Initializer, diagnostics);
                    break;
                case BoundStaticIf conditional:
                    VisitValue(conditional.Condition, diagnostics);
                    VisitStatement(conditional.ThenStatement, diagnostics);
                    if (conditional.ElseStatement is not null) VisitStatement(conditional.ElseStatement, diagnostics);
                    break;
                case BoundStaticFor loop:
                    VisitValue(loop.Values, diagnostics);
                    VisitStatement(loop.Body, diagnostics);
                    break;
                case BoundStaticMatch match:
                    VisitValue(match.Input, diagnostics);
                    foreach (BoundStaticMatchArm arm in match.Arms)
                    {
                        VisitValue(arm.Pattern, diagnostics);
                        VisitStatement(arm.Statement, diagnostics);
                    }
                    break;
                case BoundTemplateReturn returned when returned.Value is not null:
                    VisitValue(returned.Value, diagnostics);
                    break;
            }
        }

        private static void VisitValue(BoundTemplateValue value, List<Diagnostic> diagnostics)
        {
            if (value.Type == PrimitiveTypeSymbol.Error)
            {
                diagnostics.Add(new Diagnostic("COPE-TEMPLATE-0003", "Template plan contains an unresolved or runtime-only value.", value.Anchor.Position, Math.Max(1, value.Anchor.Text.Length)));
            }
            switch (value)
            {
                case BoundTemplateReflection reflection:
                    VisitValue(reflection.Value, diagnostics);
                    break;
                case BoundTemplateArray array:
                    foreach (BoundTemplateValue element in array.Elements) VisitValue(element, diagnostics);
                    break;
                case BoundTemplateStructuralObject structural:
                    foreach (BoundTemplateStructuralField field in structural.Fields) VisitValue(field.Value, diagnostics);
                    break;
                case BoundTemplateMemberAccess access:
                    VisitValue(access.Receiver, diagnostics);
                    break;
                case BoundTemplateString text:
                    foreach (BoundTemplateValue part in text.Parts) VisitValue(part, diagnostics);
                    break;
                case BoundTemplateBinary binary:
                    VisitValue(binary.Left, diagnostics);
                    VisitValue(binary.Right, diagnostics);
                    break;
                case BoundArtifactConstructor artifact:
                    foreach (BoundTemplateValue argument in artifact.Arguments) VisitValue(argument, diagnostics);
                    break;
                case BoundTemplateInvocation invocation:
                    foreach (BoundTemplateValue argument in invocation.Arguments) VisitValue(argument, diagnostics);
                    break;
            }
        }
    }
}
