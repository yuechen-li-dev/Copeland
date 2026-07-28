using Copeland.TS.Diagnostics;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Templates;

public sealed class TemplateEvaluationResult(
    string templateName,
    ProjectTree? project,
    IReadOnlyList<Diagnostic> diagnostics,
    IReadOnlyList<string> instantiationChain)
{
    public string TemplateName { get; } = templateName;
    public ProjectTree? Project { get; } = project;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public IReadOnlyList<string> InstantiationChain { get; } = instantiationChain;
    public bool Success => Project is not null && Diagnostics.Count == 0;
}

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

        diagnostics.AddRange(TemplatePlanValidator.Validate(templates));
        var evaluator = new BoundPlanEvaluator(templates, diagnostics);
        ProjectTree? project = evaluator.EvaluateTemplate(entry, []);
        return new TemplateEvaluationResult(entry.Symbol.Name, project, diagnostics, evaluator.InstantiationChain);
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
        private readonly List<Diagnostic> _diagnostics;
        private readonly Stack<TemplateSymbol> _active = new();
        private readonly List<string> _instantiationChain = [];

        public BoundPlanEvaluator(IReadOnlyList<BoundTemplateDeclaration> templates, List<Diagnostic> diagnostics)
        {
            _templates = templates.ToDictionary(template => template.Symbol);
            _diagnostics = diagnostics;
        }

        public IReadOnlyList<string> InstantiationChain => _instantiationChain;

        public ProjectTree? EvaluateTemplate(BoundTemplateDeclaration declaration, IReadOnlyList<object?> arguments)
        {
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
            if (arguments.Count != declaration.Symbol.Parameters.Count)
            {
                Report("COPE-TEMPLATE-0002", $"Template '{declaration.Symbol.Name}' expects {declaration.Symbol.Parameters.Count} static argument(s), but received {arguments.Count}.", declaration.Symbol.Name, 0, 1);
                return null;
            }

            _active.Push(declaration.Symbol);
            _instantiationChain.Add(string.Join(" -> ", _active.Reverse().Select(Describe)));
            try
            {
                var context = new BoundEvaluationContext();
                for (int index = 0; index < arguments.Count; index++)
                {
                    // Parameter symbols are not re-created by the plan; template
                    // declarations with value parameters bind local references by name.
                    VariableSymbol parameter = declaration.Parameters[index];
                    context.Values[parameter] = arguments[index];
                }
                Execute(declaration.Plan, context);
                if (context.ReturnValue is ProjectTree returned)
                {
                    return returned;
                }
                if (context.DidReturn)
                {
                    Report("COPE-TEMPLATE-0005", $"Template '{declaration.Symbol.Name}' returned a non-ProjectTree artifact value.", declaration.Symbol.Name, 0, 1);
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
                    foreach (BoundTemplateValue value in loop.Values.Elements)
                    {
                        context.Values[loop.Local] = EvaluateValue(value, context);
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
                default:
                    Report("COPE-TEMPLATE-0003", "Template plan contains an unresolved static value.", value.Anchor);
                    return null;
            }
        }

        private object? EvaluateInvocation(BoundTemplateInvocation invocation, BoundEvaluationContext context)
        {
            if (!_templates.TryGetValue(invocation.Template, out BoundTemplateDeclaration? target))
            {
                Report("COPE-TEMPLATE-0003", $"Resolved template '{invocation.Template.Name}' is unavailable to this evaluation plan.", invocation.Anchor);
                return null;
            }
            object?[] values = invocation.Arguments.Select(argument => EvaluateValue(argument, context)).ToArray();
            return EvaluateTemplate(target, values);
        }

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
                    BoundArtifactIntrinsic.Directory when arguments is [string path, object?[] children]
                        => new DirectoryArtifact(path, children.OfType<ArtifactNode>().ToArray(), provenance),
                    BoundArtifactIntrinsic.Project when arguments is [object?[] children]
                        => CreateProject(children.OfType<ArtifactNode>(), constructor.Anchor),
                    _ => ReportInvalidIntrinsic(constructor),
                };
            }
            catch (ArgumentException exception)
            {
                Report("COPE-ARTIFACT-0001", exception.Message, constructor.Anchor);
                return null;
            }
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

        private void Report(string id, string message, SyntaxToken token)
            => _diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length)));

        private void Report(string id, string message, string _, int position, int length)
            => _diagnostics.Add(new Diagnostic(id, message, position, length));

        private sealed class BoundEvaluationContext
        {
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
