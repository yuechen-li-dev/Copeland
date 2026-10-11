using Copeland.TS.Diagnostics;

namespace Copeland.TS.Syntax;

/// <summary>
/// Reduces explicit execution states to ordinary typed value functions before
/// either host or GPU binding. Original declarations remain the FLOW graph;
/// generated functions use the same expression/effect/type laws as user code.
/// </summary>
internal sealed partial class FlowExecutionProjection
{
    private readonly Dictionary<string, FlowDeclarationSyntax> flows;
    private readonly Dictionary<string, FlowDeclarationSyntax> declarations;
    private readonly List<Diagnostic> diagnostics;
    private readonly List<MemberSyntax> generated = [];
    private readonly bool gpu;
    private FlowDeclarationSyntax current = null!;
    private SyntaxToken anchor = null!;

    public FlowExecutionProjection(CompilationUnitSyntax root, List<Diagnostic> diagnostics, bool gpu)
    {
        declarations = root.Members.OfType<FlowDeclarationSyntax>()
            .GroupBy(flow => flow.Identifier.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        flows = declarations.Values.Where(flow => flow.HasExecution)
            .ToDictionary(flow => flow.Identifier.Text, StringComparer.Ordinal);
        this.diagnostics = diagnostics;
        this.gpu = gpu;
    }

    public static string Name(string flow, string part)
    {
        return part is "Frame" or "Outcome" or "Board" or "State" or "StepKind"
            ? flow + part
            : "__flow_" + flow + "_" + part;
    }

    public CompilationUnitSyntax Expand(CompilationUnitSyntax root)
    {
        if (flows.Count == 0) return root;
        var pending = new Queue<FlowDeclarationSyntax>(flows.Values);
        while (pending.TryDequeue(out FlowDeclarationSyntax? parent))
        {
            foreach (FlowControlStatementSyntax control in parent.States.SelectMany(state => Controls(StateBody(state))))
            {
                if (control is { Keyword.Text: "push" or "run", Value: CallExpressionSyntax { Target: NameExpressionSyntax child } }
                    && declarations.TryGetValue(child.IdentifierToken.Text, out FlowDeclarationSyntax? declaration)
                    && flows.TryAdd(child.IdentifierToken.Text, declaration))
                {
                    pending.Enqueue(declaration);
                }
            }
        }
        foreach (FlowDeclarationSyntax flow in flows.Values)
        {
            current = flow;
            anchor = flow.Identifier;
            GenerateFlow();
        }
        var members = root.Members.Select(RewriteMember).Concat(generated).ToArray();
        return root with { Members = members };
    }

    private void GenerateFlow()
    {
        string flowName = current.Identifier.Text;
        string boardName = Name(flowName, "Board");
        string frameName = Name(flowName, "Frame");
        string stateName = Name(flowName, "State");
        string outcomeName = Name(flowName, "Outcome");
        FlowStateSyntax[] initial = current.States.Where(state => state.InitialKeyword is not null).ToArray();
        if (initial.Length != 1)
        {
            Error("COPE-FLOW-0012", "An execution flow requires exactly one initial state.", current.Identifier);
            return;
        }
        if (current.States.Select(state => state.Identifier.Text).Distinct(StringComparer.Ordinal).Count() != current.States.Count)
        {
            Error("COPE-FLOW-0011", "Execution state names must be unique.", current.Identifier);
            return;
        }
        if (current.Events.Count > 0 || current.States.Any(state => state.Transitions.Count > 0))
        {
            Error("COPE-FLOW-EXEC-0001", "Reaction/execution mixing requires the event-to-frame adapter; keep this execution flow event-free.", current.Identifier);
            return;
        }
        bool hasPush = current.States.SelectMany(state => Controls(StateBody(state))).Any(control => control.Keyword.Text == "push");
        bool hasRun = current.States.SelectMany(state => Controls(StateBody(state))).Any(control => control.Keyword.Text == "run");
        if (gpu && hasPush)
        {
            Error("COPE-GPU-FLOW-0002", "GPU FLOW uses static composition: replace push with run and express repetition with state transitions.", current.Identifier);
            return;
        }
        if (hasRun)
        {
            if (!gpu)
            {
                Error("COPE-FLOW-EXEC-0017", "Static run composition currently requires the GPU profile; host FLOW retains its explicit push API.", current.Identifier);
                return;
            }
            GenerateTapeFlow();
            return;
        }
        if (hasPush)
        {
            GenerateStackFlow();
            return;
        }
        if (current.States.Any(state => state.Parameters.Count > 0))
        {
            Error("COPE-FLOW-EXEC-0008", "State entry payloads require typed child frame lowering.", current.Identifier);
            return;
        }
        if (current.StackCapacityToken is { } capacity
            && (!int.TryParse(capacity.Text, out int count) || count is < 1 or > 64))
        {
            Error("COPE-FLOW-EXEC-0009", "Stack capacity must be an integer from 1 through 64.", capacity);
            return;
        }

        TypeSyntax result = current.ResultType is ResultTypeSyntax fallible
            ? fallible.SuccessType
            : current.ResultType ?? Type("void");
        TypeSyntax? failure = (current.ResultType as ResultTypeSyntax)?.ErrorType;
        bool isVoid = result is IdentifierTypeSyntax { Identifier.Text: "void" } or PredefinedTypeSyntax { Keyword.Kind: SyntaxKind.VoidKeyword };
        var boardFields = new List<RecordFieldSyntax>();
        foreach (FlowBoardFieldSyntax field in current.Board?.Fields ?? [])
        {
            if (field.Type is null || field.Initializer is null)
            {
                Error("COPE-FLOW-EXEC-0002", "Execution board fields require an explicit type and initializer.", field.Identifier);
                return;
            }
            boardFields.Add(Field(field.Identifier.Text, field.Type));
        }
        if (boardFields.Count == 0) boardFields.Add(Field("__empty", Type(gpu ? "bool" : "boolean")));
        generated.Add(Record(boardName, boardFields));
        generated.Add(Enum(stateName, current.States.Select(state => Case(state.Identifier.Text)).ToArray()));
        var outcomes = new List<EnumCaseSyntax>
        {
            Case("Active"),
            Case("Yielded"),
            isVoid ? Case("Completed") : Case("Completed", ("value", result)),
        };
        if (failure is not null) outcomes.Add(Case("Failed", ("error", failure)));
        generated.Add(Enum(outcomeName, outcomes));
        var frameFields = new List<RecordFieldSyntax>
        {
            Field("state", Type(stateName)),
            Field("board", Type(boardName)),
            Field("outcome", Type(outcomeName)),
        };
        frameFields.AddRange(current.Parameters.Select(parameter => Field("parameter_" + parameter.Identifier.Text, parameter.Type!)));
        generated.Add(Record(frameName, frameFields));

        var boardValues = (current.Board?.Fields ?? [])
            .Select(field => (field.Identifier.Text, RewriteExpression(field.Initializer!))).ToList();
        if (boardValues.Count == 0) boardValues.Add(("__empty", Boolean(false)));
        var frameValues = new List<(string, ExpressionSyntax)>
        {
            ("state", Member(Value(stateName), initial[0].Identifier.Text)),
            ("board", Object(boardValues)),
            ("outcome", Member(Value(outcomeName), "Active")),
        };
        frameValues.AddRange(current.Parameters.Select(parameter =>
            ("parameter_" + parameter.Identifier.Text, (ExpressionSyntax)Value(parameter.Identifier.Text))));
        generated.Add(Function(Name(flowName, "Start"), current.Parameters, Type(frameName), [Return(Object(frameValues))]));

        foreach (FlowStateSyntax state in current.States)
        {
            BlockStatementSyntax body = StateBody(state);
            if (!EndsStep(body))
            {
                Error("COPE-FLOW-EXEC-0003", "Every execution-state path must end with transition, yield, finish, fail, or push.", state.Identifier);
            }
            var statements = new List<StatementSyntax>
            {
                Local("board", Type(boardName), Member(Value("__frame"), "board"), mutable: true),
            };
            statements.AddRange(current.Parameters.Select(parameter =>
                Local(parameter.Identifier.Text, parameter.Type!, Member(Value("__frame"), "parameter_" + parameter.Identifier.Text))));
            statements.AddRange(RewriteExecutionList(body.Statements));
            generated.Add(Function(Name(flowName, "State_" + state.Identifier.Text),
                [Parameter("__frame", Type(frameName))], Type(frameName), statements));
        }

        var statusArms = outcomes.Select(variant => Arm(outcomeName, variant.Identifier.Text,
            variant.PayloadFields.Count == 0 ? [] : ["__ignored"],
            Boolean(variant.Identifier.Text is not ("Active" or "Yielded")))).ToArray();
        var stateArms = current.States.Select(state => Arm(stateName, state.Identifier.Text, [],
            Call(Name(flowName, "State_" + state.Identifier.Text), Value("__frame")))).ToArray();
        generated.Add(Function(Name(flowName, "Step"), [Parameter("__frame", Type(frameName))], Type(frameName),
        [
            If(Match(Member(Value("__frame"), "outcome"), statusArms), Return(Value("__frame"))),
            Return(Match(Member(Value("__frame"), "state"), stateArms)),
        ]));
    }

    internal static BlockStatementSyntax StateBody(FlowStateSyntax state)
    {
        if (state.Body is not null) return state.Body;
        IReadOnlyList<StatementSyntax> statements = state.Terminal is null
            ? []
            : [new FlowControlStatementSyntax(state.Terminal.Keyword, state.Terminal.Expression, null, null, state.Terminal.SemicolonToken)];
        return new BlockStatementSyntax(state.OpenBraceToken, statements, state.CloseBraceToken);
    }

    private StatementSyntax RewriteExecutionStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case FlowControlStatementSyntax control:
                return RewriteControl(control);
            case YieldStatementSyntax yield:
                if (yield.Expression is not null || yield.StarToken is not null
                    || yield.BreakKeyword is not null || yield.ReturnKeyword is not null)
                {
                    Error("COPE-FLOW-EXEC-0004", "Execution flow yield is bare; it re-enters the state rather than yielding a generator value.", yield.YieldKeyword);
                }
                return FrameReturn("Yielded");
            case ReturnStatementSyntax returned:
                Error("COPE-FLOW-EXEC-0005", "Use finish to complete an execution flow.", returned.ReturnKeyword);
                return returned;
            case BlockStatementSyntax block:
                return block with { Statements = RewriteExecutionList(block.Statements) };
            case IfStatementSyntax conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    ThenStatement = RewriteExecutionStatement(conditional.ThenStatement),
                    ElseStatement = conditional.ElseStatement is null ? null : RewriteExecutionStatement(conditional.ElseStatement),
                };
            case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } expression
                when assignment.Left is MemberAccessExpressionSyntax { Target: NameExpressionSyntax { IdentifierToken.Text: "board" } } field:
                {
                    ExpressionSyntax value = RewriteExpression(assignment.Right);
                    if (assignment.EqualsToken.Kind != SyntaxKind.EqualsToken)
                    {
                        SyntaxKind operation = assignment.EqualsToken.Kind switch
                        {
                            SyntaxKind.PlusEqualsToken => SyntaxKind.PlusToken,
                            SyntaxKind.MinusEqualsToken => SyntaxKind.MinusToken,
                            SyntaxKind.StarEqualsToken => SyntaxKind.StarToken,
                            SyntaxKind.SlashEqualsToken => SyntaxKind.SlashToken,
                            SyntaxKind.PercentEqualsToken => SyntaxKind.PercentToken,
                            _ => SyntaxKind.BadToken,
                        };
                        value = new BinaryExpressionSyntax(field, Token(operation, SyntaxFacts.GetText(operation) ?? ""), value);
                    }
                    return expression with
                    {
                        Expression = new AssignmentExpressionSyntax(Value("board"), Token(SyntaxKind.EqualsToken, "="),
                            With(Value("board"), (field.NameToken.Text, value))),
                    };
                }
            case WhileStatementSyntax loop:
                return loop with { Condition = RewriteExpression(loop.Condition), Body = RewriteExecutionStatement(loop.Body) };
            case ForStatementSyntax loop:
                return loop with
                {
                    Initializer = loop.Initializer is StatementSyntax initializer ? RewriteStatement(initializer) : loop.Initializer,
                    Condition = loop.Condition is null ? null : RewriteExpression(loop.Condition),
                    Increment = loop.Increment is null ? null : RewriteExpression(loop.Increment),
                    Body = RewriteExecutionStatement(loop.Body),
                };
            case ForOfStatementSyntax loop:
                return loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteExecutionStatement(loop.Body) };
            case StaticForStatementSyntax loop:
                return loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteExecutionStatement(loop.Body) };
            case StaticIfStatementSyntax conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    ThenStatement = RewriteExecutionStatement(conditional.ThenStatement),
                    ElseStatement = conditional.ElseStatement is null ? null : RewriteExecutionStatement(conditional.ElseStatement),
                };
            default:
                return RewriteStatement(statement);
        }
    }

    private IReadOnlyList<StatementSyntax> RewriteExecutionList(IReadOnlyList<StatementSyntax> statements)
    {
        var rewritten = new List<StatementSyntax>();
        bool ended = false;
        foreach (StatementSyntax statement in statements)
        {
            if (ended)
            {
                Error("COPE-FLOW-EXEC-0006", "Statements after an unconditional flow control are unreachable.", anchor);
                break;
            }
            rewritten.Add(RewriteExecutionStatement(statement));
            ended = EndsStep(statement);
        }
        return rewritten;
    }

    private StatementSyntax RewriteControl(FlowControlStatementSyntax control)
    {
        anchor = control.Keyword;
        switch (control.Keyword.Text)
        {
            case "transition":
                {
                    if (!current.States.Any(state => state.Identifier.Text == control.SuccessTarget!.Text))
                    {
                        Error("COPE-FLOW-0015", "Transition target is not a state in this flow.", control.SuccessTarget!);
                    }
                    return FrameReturn("Active", state: control.SuccessTarget!.Text);
                }
            case "finish":
                return FrameReturn("Completed", control.Value is null ? null : RewriteExpression(control.Value));
            case "fail":
                if (current.ResultType is not ResultTypeSyntax)
                {
                    Error("COPE-FLOW-0026", "Fail requires a declared flow error type.", control.Keyword);
                }
                return FrameReturn("Failed", control.Value is null ? null : RewriteExpression(control.Value));
            case "push":
                Error("COPE-FLOW-EXEC-0008", "Typed child frames require closed frame-union lowering; push cannot be lowered as an ordinary function call.", control.Keyword);
                return Return(Value("__frame"));
            default:
                throw new InvalidOperationException("Unknown flow control: " + control.Keyword.Text);
        }
    }

    private StatementSyntax FrameReturn(string outcome, ExpressionSyntax? payload = null, string? state = null)
    {
        string flowName = current.Identifier.Text;
        ExpressionSyntax result = Member(Value(Name(flowName, "Outcome")), outcome);
        if (payload is not null) result = Invoke(result, [payload]);
        var replacements = new List<(string, ExpressionSyntax)>
        {
            ("board", Value("board")),
            ("outcome", result),
        };
        if (state is not null) replacements.Add(("state", Member(Value(Name(flowName, "State")), state)));
        return Return(With(Value("__frame"), replacements.ToArray()));
    }

    private static bool EndsStep(StatementSyntax statement)
    {
        return statement switch
        {
            FlowControlStatementSyntax or YieldStatementSyntax => true,
            BlockStatementSyntax block => block.Statements.Count > 0 && EndsStep(block.Statements[^1]),
            IfStatementSyntax { ElseStatement: not null } conditional => EndsStep(conditional.ThenStatement) && EndsStep(conditional.ElseStatement),
            _ => false,
        };
    }

    private MemberSyntax RewriteMember(MemberSyntax member)
    {
        if (member is FlowDeclarationSyntax flow && flows.ContainsKey(flow.Identifier.Text))
        {
            return flow with { IsExecution = true, TapePlan = tapePlans.GetValueOrDefault(flow.Identifier.Text) };
        }
        if (member is FunctionDeclarationSyntax function)
        {
            return function with { Body = (BlockStatementSyntax)RewriteStatement(function.Body) };
        }
        if (member is GlobalStatementMemberSyntax global)
        {
            return global with { Statement = RewriteStatement(global.Statement) };
        }
        return member;
    }

    private StatementSyntax RewriteStatement(StatementSyntax statement)
    {
        return statement switch
        {
            BlockStatementSyntax block => block with { Statements = block.Statements.Select(RewriteStatement).ToArray() },
            VariableDeclarationStatementSyntax local => local with { Initializer = RewriteExpression(local.Initializer) },
            ExpressionStatementSyntax expression => expression with { Expression = RewriteExpression(expression.Expression) },
            ReturnStatementSyntax returned => returned with { Expression = returned.Expression is null ? null : RewriteExpression(returned.Expression) },
            IfStatementSyntax conditional => conditional with
            {
                Condition = RewriteExpression(conditional.Condition),
                ThenStatement = RewriteStatement(conditional.ThenStatement),
                ElseStatement = conditional.ElseStatement is null ? null : RewriteStatement(conditional.ElseStatement),
            },
            WhileStatementSyntax loop => loop with { Condition = RewriteExpression(loop.Condition), Body = RewriteStatement(loop.Body) },
            ForStatementSyntax loop => loop with
            {
                Initializer = loop.Initializer is StatementSyntax initializer ? RewriteStatement(initializer) : loop.Initializer,
                Condition = loop.Condition is null ? null : RewriteExpression(loop.Condition),
                Increment = loop.Increment is null ? null : RewriteExpression(loop.Increment),
                Body = RewriteStatement(loop.Body),
            },
            ForOfStatementSyntax loop => loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteStatement(loop.Body) },
            StaticForStatementSyntax loop => loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteStatement(loop.Body) },
            StaticIfStatementSyntax conditional => conditional with
            {
                Condition = RewriteExpression(conditional.Condition),
                ThenStatement = RewriteStatement(conditional.ThenStatement),
                ElseStatement = conditional.ElseStatement is null ? null : RewriteStatement(conditional.ElseStatement),
            },
            _ => statement,
        };
    }

    private ExpressionSyntax RewriteExpression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case CallExpressionSyntax call:
                {
                    ExpressionSyntax target = call.Target;
                    if (target is MemberAccessExpressionSyntax { Target: NameExpressionSyntax qualifier } member
                        && flows.ContainsKey(qualifier.IdentifierToken.Text)
                        && member.NameToken.Text is "start" or "step" or "position")
                    {
                        string part = member.NameToken.Text switch
                        {
                            "start" => "Start",
                            "step" => "Step",
                            _ => "Position",
                        };
                        target = new NameExpressionSyntax(member.NameToken with { Text = Name(qualifier.IdentifierToken.Text, part) });
                    }
                    return call with { Target = RewriteExpression(target), Arguments = call.Arguments.Select(RewriteExpression).ToArray() };
                }
            case MemberAccessExpressionSyntax member:
                return member with { Target = RewriteExpression(member.Target) };
            case GenericCallExpressionSyntax call:
                return call with { Target = RewriteExpression(call.Target), Arguments = call.Arguments.Select(RewriteExpression).ToArray() };
            case GenericFunctionReferenceExpressionSyntax reference:
                return reference with { Target = RewriteExpression(reference.Target) };
            case ArrayLiteralExpressionSyntax array:
                return array with { Elements = array.Elements.Select(RewriteExpression).ToArray() };
            case IndexExpressionSyntax index:
                return index with { Target = RewriteExpression(index.Target), Index = RewriteExpression(index.Index) };
            case IfExpressionSyntax conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    ThenExpression = RewriteExpression(conditional.ThenExpression),
                    ElseExpression = RewriteExpression(conditional.ElseExpression),
                };
            case CoalesceExpressionSyntax coalesce:
                return coalesce with { Left = RewriteExpression(coalesce.Left), Right = RewriteExpression(coalesce.Right) };
            case OptionalMemberAccessExpressionSyntax optional:
                return optional with { Target = RewriteExpression(optional.Target) };
            case StaticExpressionSyntax evaluated:
                return evaluated with { Expression = RewriteExpression(evaluated.Expression) };
            case PropagateExpressionSyntax propagated:
                return propagated with { Operand = RewriteExpression(propagated.Operand) };
            case AwaitExpressionSyntax awaited:
                return awaited with { Operand = RewriteExpression(awaited.Operand) };
            case ArrowExpressionSyntax arrow:
                return arrow with
                {
                    ExpressionBody = arrow.ExpressionBody is null ? null : RewriteExpression(arrow.ExpressionBody),
                    BlockBody = arrow.BlockBody is null ? null : (BlockStatementSyntax)RewriteStatement(arrow.BlockBody),
                };
            case BinaryExpressionSyntax binary:
                return binary with { Left = RewriteExpression(binary.Left), Right = RewriteExpression(binary.Right) };
            case UnaryExpressionSyntax unary:
                return unary with { Operand = RewriteExpression(unary.Operand) };
            case AssignmentExpressionSyntax assignment:
                return assignment with { Left = RewriteExpression(assignment.Left), Right = RewriteExpression(assignment.Right) };
            case ParenthesizedExpressionSyntax parentheses:
                return parentheses with { Expression = RewriteExpression(parentheses.Expression) };
            case WithExpressionSyntax updated:
                return updated with { Source = RewriteExpression(updated.Source), Replacements = (ObjectLiteralExpressionSyntax)RewriteExpression(updated.Replacements) };
            case ObjectLiteralExpressionSyntax record:
                return record with { Properties = record.Properties.Select(property => property with { ValueExpression = RewriteExpression(property.ValueExpression) }).ToArray() };
            case MatchExpressionSyntax match:
                return match with { Expression = RewriteExpression(match.Expression), Arms = match.Arms.Select(arm => arm with { Expression = RewriteExpression(arm.Expression) }).ToArray() };
            default:
                return expression;
        }
    }

    private void Error(string id, string message, SyntaxToken token)
    {
        diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length)));
    }

    private SyntaxToken Token(SyntaxKind kind, string text, object? value = null)
    {
        return new SyntaxToken(kind, anchor.Position, text, value);
    }

    private SyntaxToken Identifier(string name) => Token(SyntaxKind.IdentifierToken, name);
    private TypeSyntax Type(string name)
    {
        SyntaxKind keyword = SyntaxFacts.GetKeywordKind(name);
        return keyword is SyntaxKind.IntKeyword or SyntaxKind.FloatKeyword or SyntaxKind.NumberKeyword
            or SyntaxKind.BooleanKeyword or SyntaxKind.StringKeyword or SyntaxKind.VoidKeyword
            ? new PredefinedTypeSyntax(Token(keyword, name))
            : new IdentifierTypeSyntax(Identifier(name));
    }
    private NameExpressionSyntax Value(string name) => new(Identifier(name));
    private ExpressionSyntax Integer(int value) => new LiteralExpressionSyntax(Token(SyntaxKind.NumberToken, value.ToString(System.Globalization.CultureInfo.InvariantCulture), value));
    private ExpressionSyntax Boolean(bool value) => new LiteralExpressionSyntax(Token(value ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword, value ? "true" : "false", value));
    private ExpressionSyntax Member(ExpressionSyntax target, string name) => new MemberAccessExpressionSyntax(target, Token(SyntaxKind.DotToken, "."), Identifier(name));
    private ParameterSyntax Parameter(string name, TypeSyntax type) => new(Identifier(name), Token(SyntaxKind.ColonToken, ":"), type);
    private StatementSyntax Return(ExpressionSyntax value) => new ReturnStatementSyntax(Token(SyntaxKind.ReturnKeyword, "return"), value, Token(SyntaxKind.SemicolonToken, ";"));

    private StatementSyntax If(ExpressionSyntax condition, StatementSyntax body)
    {
        return new IfStatementSyntax(Token(SyntaxKind.IfKeyword, "if"), Token(SyntaxKind.OpenParenToken, "("), condition,
            Token(SyntaxKind.CloseParenToken, ")"), body, null, null);
    }

    private StatementSyntax Local(string name, TypeSyntax type, ExpressionSyntax initializer, bool mutable = false)
    {
        SyntaxKind keyword = mutable ? gpu ? SyntaxKind.VarKeyword : SyntaxKind.LetKeyword : SyntaxKind.ConstKeyword;
        return new VariableDeclarationStatementSyntax(Token(keyword, SyntaxFacts.GetText(keyword)!),
            Identifier(name), Token(SyntaxKind.ColonToken, ":"), type, Token(SyntaxKind.EqualsToken, "="), initializer, Token(SyntaxKind.SemicolonToken, ";"));
    }

    private RecordFieldSyntax Field(string name, TypeSyntax type)
    {
        return new RecordFieldSyntax(Identifier(name), null, Token(SyntaxKind.ColonToken, ":"), type, [], Token(SyntaxKind.SemicolonToken, ";"), true, true);
    }

    private MemberSyntax Record(string name, IReadOnlyList<RecordFieldSyntax> fields)
    {
        return new RecordDeclarationSyntax(null, Token(SyntaxKind.RecordKeyword, "record"), Identifier(name), Token(SyntaxKind.OpenBraceToken, "{"), fields, Token(SyntaxKind.CloseBraceToken, "}"));
    }

    private EnumCaseSyntax Case(string name, params (string Name, TypeSyntax Type)[] fields)
    {
        return new EnumCaseSyntax(Identifier(name), fields.Length == 0 ? null : Token(SyntaxKind.OpenParenToken, "("),
            fields.Select(field => new EnumPayloadFieldSyntax(Identifier(field.Name), Token(SyntaxKind.ColonToken, ":"), field.Type, Token(SyntaxKind.CommaToken, ","))).ToArray(),
            fields.Length == 0 ? null : Token(SyntaxKind.CloseParenToken, ")"), Token(SyntaxKind.CommaToken, ","));
    }

    private MemberSyntax Enum(string name, IReadOnlyList<EnumCaseSyntax> cases)
    {
        return new EnumDeclarationSyntax(Token(SyntaxKind.EnumKeyword, "enum"), Identifier(name), Token(SyntaxKind.OpenBraceToken, "{"), cases, Token(SyntaxKind.CloseBraceToken, "}"));
    }

    private FunctionDeclarationSyntax Function(string name, IReadOnlyList<ParameterSyntax> parameters, TypeSyntax result, IReadOnlyList<StatementSyntax> body)
    {
        return new FunctionDeclarationSyntax(null, null, Token(SyntaxKind.FunctionKeyword, "function"), null, Identifier(name), null, [], [], null,
            Token(SyntaxKind.OpenParenToken, "("), parameters, Commas(parameters.Count), Token(SyntaxKind.CloseParenToken, ")"), Token(SyntaxKind.ColonToken, ":"), result,
            new BlockStatementSyntax(Token(SyntaxKind.OpenBraceToken, "{"), body, Token(SyntaxKind.CloseBraceToken, "}")));
    }

    private IReadOnlyList<SyntaxToken> Commas(int count) => Enumerable.Range(0, Math.Max(0, count - 1)).Select(_ => Token(SyntaxKind.CommaToken, ",")).ToArray();
    private ExpressionSyntax Call(string name, params ExpressionSyntax[] arguments) => Invoke(Value(name), arguments);
    private ExpressionSyntax Invoke(ExpressionSyntax target, IReadOnlyList<ExpressionSyntax> arguments) => new CallExpressionSyntax(target, Token(SyntaxKind.OpenParenToken, "("), arguments, Commas(arguments.Count), Token(SyntaxKind.CloseParenToken, ")"));

    private ObjectLiteralExpressionSyntax Object(IReadOnlyList<(string Name, ExpressionSyntax Value)> fields)
    {
        return new ObjectLiteralExpressionSyntax(Token(SyntaxKind.OpenBraceToken, "{"),
            fields.Select(field => new ObjectPropertySyntax(Identifier(field.Name), Token(SyntaxKind.ColonToken, ":"), field.Value)).ToArray(),
            Commas(fields.Count), Token(SyntaxKind.CloseBraceToken, "}"));
    }

    private ExpressionSyntax With(ExpressionSyntax source, params (string Name, ExpressionSyntax Value)[] fields) => new WithExpressionSyntax(source, Token(SyntaxKind.WithKeyword, "with"), Object(fields));

    private MatchArmSyntax Arm(string enumeration, string variant, IReadOnlyList<string> payload, ExpressionSyntax expression)
    {
        var pattern = new MatchPatternSyntax(Identifier(variant), payload.Count == 0 ? null : Token(SyntaxKind.OpenParenToken, "("),
            payload.Select(Identifier).ToArray(), Commas(payload.Count), payload.Count == 0 ? null : Token(SyntaxKind.CloseParenToken, ")"),
            Identifier(enumeration), Token(SyntaxKind.DotToken, "."));
        return new MatchArmSyntax(pattern, Token(SyntaxKind.ArrowToken, "=>"), expression, Token(SyntaxKind.CommaToken, ","));
    }

    private ExpressionSyntax Match(ExpressionSyntax subject, IReadOnlyList<MatchArmSyntax> arms)
    {
        return new MatchExpressionSyntax(Token(SyntaxKind.MatchKeyword, "match"), subject, Token(SyntaxKind.OpenBraceToken, "{"), arms, Token(SyntaxKind.CloseBraceToken, "}"));
    }
}
