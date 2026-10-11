namespace Copeland.TS.Syntax;

internal sealed partial class FlowExecutionProjection
{
    private FlowDeclarationSyntax stackRoot = null!;
    private IReadOnlyList<FlowDeclarationSyntax> stackFlows = [];
    private int stackCapacity;

    private string StackName(string part) => Name(stackRoot.Identifier.Text, part);
    private string ActivationName(FlowDeclarationSyntax flow, string part) => StackName("Activation_" + flow.Identifier.Text + "_" + part);
    private ExpressionSyntax StackVariant(string variant, params ExpressionSyntax[] arguments)
    {
        ExpressionSyntax target = Member(Value(StackName("Slot")), variant);
        return arguments.Length == 0 ? target : Invoke(target, arguments);
    }

    internal static IEnumerable<FlowControlStatementSyntax> Controls(StatementSyntax statement)
    {
        switch (statement)
        {
            case FlowControlStatementSyntax control:
                yield return control;
                break;
            case BlockStatementSyntax block:
                foreach (var nested in block.Statements.SelectMany(Controls)) yield return nested;
                break;
            case IfStatementSyntax conditional:
                foreach (var nested in Controls(conditional.ThenStatement)) yield return nested;
                if (conditional.ElseStatement is not null)
                {
                    foreach (var nested in Controls(conditional.ElseStatement)) yield return nested;
                }
                break;
            case WhileStatementSyntax loop:
                foreach (var nested in Controls(loop.Body)) yield return nested;
                break;
            case ForStatementSyntax loop:
                foreach (var nested in Controls(loop.Body)) yield return nested;
                break;
            case ForOfStatementSyntax loop:
                foreach (var nested in Controls(loop.Body)) yield return nested;
                break;
            case StaticForStatementSyntax loop:
                foreach (var nested in Controls(loop.Body)) yield return nested;
                break;
            case StaticIfStatementSyntax conditional:
                foreach (var nested in Controls(conditional.ThenStatement)) yield return nested;
                if (conditional.ElseStatement is not null)
                {
                    foreach (var nested in Controls(conditional.ElseStatement)) yield return nested;
                }
                break;
        }
    }

    private void GenerateStackFlow()
    {
        stackRoot = current;
        stackCapacity = 8;
        if (stackRoot.StackCapacityToken is { } capacity && !int.TryParse(capacity.Text, out stackCapacity))
        {
            Error("COPE-FLOW-EXEC-0009", "Stack capacity must be an integer from 1 through 64.", capacity);
            return;
        }
        if (stackCapacity is < 1 or > 64)
        {
            Error("COPE-FLOW-EXEC-0009", "Stack capacity must be from 1 through 64.", stackRoot.StackCapacityToken!);
            return;
        }
        var reachable = new Dictionary<string, FlowDeclarationSyntax>(StringComparer.Ordinal);
        var pending = new Queue<FlowDeclarationSyntax>();
        pending.Enqueue(stackRoot);
        while (pending.TryDequeue(out FlowDeclarationSyntax? flow))
        {
            if (!reachable.TryAdd(flow.Identifier.Text, flow)) continue;
            foreach (FlowControlStatementSyntax control in flow.States.SelectMany(state => Controls(StateBody(state))))
            {
                if (control.Keyword.Text != "push") continue;
                if (control.Value is not CallExpressionSyntax { Target: NameExpressionSyntax child }
                    || !declarations.TryGetValue(child.IdentifierToken.Text, out FlowDeclarationSyntax? childFlow))
                {
                    Error("COPE-FLOW-EXEC-0011", "Push requires a statically named declared flow call.", control.Keyword);
                    continue;
                }
                pending.Enqueue(childFlow);
            }
        }
        stackFlows = reachable.Values.ToArray();
        if (stackFlows.Count > 16)
        {
            Error("COPE-FLOW-EXEC-0012", "The closed frame union admits at most 16 flow declarations.", stackRoot.Identifier);
            return;
        }
        foreach (FlowDeclarationSyntax flow in stackFlows)
        {
            if (flow.Events.Count > 0 || flow.States.Any(state => state.Transitions.Count > 0))
            {
                Error("COPE-FLOW-EXEC-0001", "Child execution frames must be event-free.", flow.Identifier);
            }
            if (flow.States.Count(state => state.InitialKeyword is not null) != 1)
            {
                Error("COPE-FLOW-0012", "A child flow requires exactly one initial state.", flow.Identifier);
                return;
            }
            foreach (FlowControlStatementSyntax control in flow.States.SelectMany(state => Controls(StateBody(state))))
            {
                if (control.Keyword.Text == "push") ValidatePush(flow, control);
            }
        }

        foreach (FlowDeclarationSyntax flow in stackFlows) GenerateActivation(flow);
        generated.Add(Enum(StackName("Slot"),
            new[] { Case("Empty") }.Concat(stackFlows.Select(flow => Case(flow.Identifier.Text,
                ("data", Type(ActivationName(flow, "Frame")))))).ToArray()));
        TypeSyntax result = SuccessType(stackRoot);
        TypeSyntax? failure = FailureType(stackRoot);
        var outcomes = new List<EnumCaseSyntax> { Case("Active"), IsVoid(result) ? Case("Completed") : Case("Completed", ("value", result)), Case("Fault", ("code", CounterType())) };
        if (failure is not null) outcomes.Add(Case("Failed", ("error", failure)));
        generated.Add(Enum(StackName("Outcome"), outcomes));
        generated.Add(Enum(StackName("StepKind"), new[] { "Started", "Transitioned", "Yielded", "Pushed", "Completed", "Failed", "Fault" }.Select(name => Case(name)).ToArray()));
        var fields = new List<RecordFieldSyntax>
        {
            Field("depth", CounterType()),
            Field("outcome", Type(StackName("Outcome"))),
            Field("stepKind", Type(StackName("StepKind"))),
        };
        fields.AddRange(Enumerable.Range(0, stackCapacity).Select(index => Field("slot" + index, Type(StackName("Slot")))));
        generated.Add(Record(StackName("Frame"), fields));
        GenerateStackHelpers();

        foreach (FlowDeclarationSyntax flow in stackFlows)
        {
            GenerateActivationStep(flow);
            GenerateCompletion(flow, false);
            if (FailureType(flow) is not null) GenerateCompletion(flow, true);
        }
        var rootValues = new List<(string, ExpressionSyntax)>
        {
            ("depth", Integer(1)),
            ("outcome", Member(Value(StackName("Outcome")), "Active")),
            ("stepKind", Member(Value(StackName("StepKind")), "Started")),
        };
        rootValues.AddRange(Enumerable.Range(0, stackCapacity).Select(index => ("slot" + index,
            index == 0 ? StackVariant(stackRoot.Identifier.Text,
                Call(ActivationName(stackRoot, "Start"), stackRoot.Parameters.Select(parameter => (ExpressionSyntax)Value(parameter.Identifier.Text)).ToArray())) : StackVariant("Empty"))));
        generated.Add(Function(StackName("Start"), stackRoot.Parameters, Type(StackName("Frame")), [Return(Object(rootValues))]));
        var statusArms = outcomes.Select(variant => Arm(StackName("Outcome"), variant.Identifier.Text,
            variant.PayloadFields.Count == 0 ? [] : ["__ignored"], Boolean(variant.Identifier.Text != "Active"))).ToArray();
        var slotArms = new List<MatchArmSyntax> { Arm(StackName("Slot"), "Empty", [], FaultExpression(Value("__machine"), 2)) };
        slotArms.AddRange(stackFlows.Select(flow => Arm(StackName("Slot"), flow.Identifier.Text, ["__slot"],
            Call(ActivationName(flow, "Step"), Value("__machine"), PayloadValue("__slot", "data")))));
        generated.Add(Function(StackName("Step"), [Parameter("__machine", Type(StackName("Frame")))], Type(StackName("Frame")),
        [
            If(Match(Member(Value("__machine"), "outcome"), statusArms), Return(Value("__machine"))),
            Return(Match(Call(StackName("Peek"), Value("__machine")), slotArms)),
        ]));
        current = stackRoot;
    }

    private TypeSyntax CounterType() => Type(gpu ? "u32" : "int");
    private static TypeSyntax SuccessType(FlowDeclarationSyntax flow) => flow.ResultType is ResultTypeSyntax result
        ? result.SuccessType : flow.ResultType ?? new IdentifierTypeSyntax(new SyntaxToken(SyntaxKind.IdentifierToken, flow.Identifier.Position, "void", null));
    private static TypeSyntax? FailureType(FlowDeclarationSyntax flow) => (flow.ResultType as ResultTypeSyntax)?.ErrorType;
    private static bool IsVoid(TypeSyntax type) => type is IdentifierTypeSyntax { Identifier.Text: "void" } or PredefinedTypeSyntax { Keyword.Kind: SyntaxKind.VoidKeyword };
    private ExpressionSyntax PayloadValue(string variable, string field) => gpu ? Member(Value(variable), field) : Value(variable);

    private void ValidatePush(FlowDeclarationSyntax parent, FlowControlStatementSyntax control)
    {
        if (control.Value is not CallExpressionSyntax { Target: NameExpressionSyntax child }
            || !declarations.TryGetValue(child.IdentifierToken.Text, out var childFlow)) return;
        FlowStateSyntax? success = parent.States.FirstOrDefault(state => state.Identifier.Text == control.SuccessTarget?.Text);
        FlowStateSyntax? failure = parent.States.FirstOrDefault(state => state.Identifier.Text == control.FailureTarget?.Text);
        int successCount = IsVoid(SuccessType(childFlow)) ? 0 : 1;
        if (success is null || success.Parameters.Count != successCount)
        {
            Error("COPE-FLOW-EXEC-0013", "The success continuation must exist and declare the child's result payload, or no payload for void.", control.Keyword);
        }
        bool fallible = FailureType(childFlow) is not null;
        if (fallible && (failure is null || failure.Parameters.Count != 1)
            || !fallible && control.FailureTarget is not null)
        {
            Error("COPE-FLOW-EXEC-0014", "A fallible child requires a one-payload failure continuation; an infallible child has none.", control.Keyword);
        }
    }

    private void GenerateActivation(FlowDeclarationSyntax flow)
    {
        current = flow;
        anchor = flow.Identifier;
        string boardName = flow == stackRoot ? StackName("Board") : ActivationName(flow, "Board");
        var fields = new List<RecordFieldSyntax>();
        foreach (FlowBoardFieldSyntax field in flow.Board?.Fields ?? [])
        {
            if (field.Type is null || field.Initializer is null)
            {
                Error("COPE-FLOW-EXEC-0002", "Execution board fields require an explicit type and initializer.", field.Identifier);
                return;
            }
            fields.Add(Field(field.Identifier.Text, field.Type));
        }
        if (fields.Count == 0) fields.Add(Field("__empty", Type(gpu ? "bool" : "boolean")));
        generated.Add(Record(boardName, fields));
        generated.Add(Enum(ActivationName(flow, "State"), flow.States.Select(state => Case(state.Identifier.Text)).ToArray()));
        var entries = new List<EnumCaseSyntax> { Case("None") };
        foreach (FlowStateSyntax state in flow.States.Where(state => state.Parameters.Count > 0))
        {
            if (state.Parameters.Count != 1)
            {
                Error("COPE-FLOW-EXEC-0015", "A child continuation admits exactly one typed entry payload.", state.Identifier);
            }
            entries.Add(Case(state.Identifier.Text, state.Parameters.Select(parameter => (parameter.Identifier.Text, parameter.Type!)).ToArray()));
        }
        generated.Add(Enum(ActivationName(flow, "Entry"), entries));
        var frameFields = new List<RecordFieldSyntax>
        {
            Field("state", Type(ActivationName(flow, "State"))),
            Field("board", Type(boardName)),
            Field("entry", Type(ActivationName(flow, "Entry"))),
            Field("resumeSuccess", Type(ActivationName(flow, "State"))),
            Field("resumeFailure", Type(ActivationName(flow, "State"))),
        };
        frameFields.AddRange(flow.Parameters.Select(parameter => Field("parameter_" + parameter.Identifier.Text, parameter.Type!)));
        generated.Add(Record(ActivationName(flow, "Frame"), frameFields));
        var boardValues = (flow.Board?.Fields ?? []).Select(field => (field.Identifier.Text, RewriteExpression(field.Initializer!))).ToList();
        if (boardValues.Count == 0) boardValues.Add(("__empty", Boolean(false)));
        string initial = flow.States.Single(state => state.InitialKeyword is not null).Identifier.Text;
        if (flow.States.Single(state => state.InitialKeyword is not null).Parameters.Count > 0)
        {
            Error("COPE-FLOW-EXEC-0016", "An initial state cannot require a child result.", flow.Identifier);
        }
        var values = new List<(string, ExpressionSyntax)>
        {
            ("state", Member(Value(ActivationName(flow, "State")), initial)),
            ("board", Object(boardValues)),
            ("entry", Member(Value(ActivationName(flow, "Entry")), "None")),
            ("resumeSuccess", Member(Value(ActivationName(flow, "State")), initial)),
            ("resumeFailure", Member(Value(ActivationName(flow, "State")), initial)),
        };
        values.AddRange(flow.Parameters.Select(parameter => ("parameter_" + parameter.Identifier.Text, (ExpressionSyntax)Value(parameter.Identifier.Text))));
        generated.Add(Function(ActivationName(flow, "Start"), flow.Parameters, Type(ActivationName(flow, "Frame")), [Return(Object(values))]));
    }

    private void GenerateStackHelpers()
    {
        var peek = new List<StatementSyntax>();
        var replace = new List<StatementSyntax>();
        for (int index = 0; index < stackCapacity; index++)
        {
            var depth = new BinaryExpressionSyntax(Member(Value("__machine"), "depth"), Token(SyntaxKind.EqualsEqualsToken, "=="), Integer(index + 1));
            peek.Add(If(depth, Return(Member(Value("__machine"), "slot" + index))));
            replace.Add(If(depth, Return(With(Value("__machine"), ("slot" + index, Value("__slot"))))));
        }
        peek.Add(Return(StackVariant("Empty")));
        replace.Add(Return(FaultExpression(Value("__machine"), 2)));
        generated.Add(Function(StackName("Peek"), [Parameter("__machine", Type(StackName("Frame")))], Type(StackName("Slot")), peek));
        generated.Add(Function(StackName("Replace"), [Parameter("__machine", Type(StackName("Frame"))), Parameter("__slot", Type(StackName("Slot")))], Type(StackName("Frame")), replace));
    }

    private ExpressionSyntax FaultExpression(ExpressionSyntax machine, int code)
    {
        return With(machine,
            ("outcome", Invoke(Member(Value(StackName("Outcome")), "Fault"), [Integer(code)])),
            ("stepKind", Member(Value(StackName("StepKind")), "Fault")));
    }

    private void GenerateActivationStep(FlowDeclarationSyntax flow)
    {
        current = flow;
        var stateArms = new List<MatchArmSyntax>();
        foreach (FlowStateSyntax state in flow.States)
        {
            BlockStatementSyntax body = StateBody(state);
            if (!EndsStep(body)) Error("COPE-FLOW-EXEC-0003", "Every execution-state path must end with a flow control.", state.Identifier);
            var parameters = new List<ParameterSyntax>
            {
                Parameter("__machine", Type(StackName("Frame"))),
                Parameter("__frame", Type(ActivationName(flow, "Frame"))),
            };
            parameters.AddRange(state.Parameters);
            string boardName = flow == stackRoot ? StackName("Board") : ActivationName(flow, "Board");
            var statements = new List<StatementSyntax> { Local("board", Type(boardName), Member(Value("__frame"), "board"), true) };
            statements.AddRange(flow.Parameters.Select(parameter => Local(parameter.Identifier.Text, parameter.Type!, Member(Value("__frame"), "parameter_" + parameter.Identifier.Text))));
            statements.AddRange(RewriteStackList(body.Statements));
            generated.Add(Function(ActivationName(flow, "State_" + state.Identifier.Text), parameters, Type(StackName("Frame")), statements));
            ExpressionSyntax call = Call(ActivationName(flow, "State_" + state.Identifier.Text), Value("__machine"), Value("__frame"));
            if (state.Parameters.Count > 0)
            {
                var entryArms = new List<MatchArmSyntax> { Arm(ActivationName(flow, "Entry"), "None", [], FaultExpression(Value("__machine"), 3)) };
                foreach (FlowStateSyntax entry in flow.States.Where(entry => entry.Parameters.Count > 0))
                {
                    ExpressionSyntax target = entry == state
                        ? Call(ActivationName(flow, "State_" + state.Identifier.Text), Value("__machine"), Value("__frame"), PayloadValue("__entry", state.Parameters[0].Identifier.Text))
                        : FaultExpression(Value("__machine"), 3);
                    entryArms.Add(Arm(ActivationName(flow, "Entry"), entry.Identifier.Text, ["__entry"], target));
                }
                call = Match(Member(Value("__frame"), "entry"), entryArms);
            }
            stateArms.Add(Arm(ActivationName(flow, "State"), state.Identifier.Text, [], call));
        }
        generated.Add(Function(ActivationName(flow, "Step"),
            [Parameter("__machine", Type(StackName("Frame"))), Parameter("__frame", Type(ActivationName(flow, "Frame")))],
            Type(StackName("Frame")), [Return(Match(Member(Value("__frame"), "state"), stateArms))]));
    }

    private StatementSyntax RewriteStackStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case FlowControlStatementSyntax control:
                return RewriteStackControl(control);
            case YieldStatementSyntax yield:
                if (yield.Expression is not null || yield.StarToken is not null
                    || yield.BreakKeyword is not null || yield.ReturnKeyword is not null)
                    Error("COPE-FLOW-EXEC-0004", "Execution yield must be bare.", yield.YieldKeyword);
                return Return(UpdateActivation("Yielded"));
            case BlockStatementSyntax block:
                return block with { Statements = RewriteStackList(block.Statements) };
            case IfStatementSyntax conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    ThenStatement = RewriteStackStatement(conditional.ThenStatement),
                    ElseStatement = conditional.ElseStatement is null ? null : RewriteStackStatement(conditional.ElseStatement),
                };
            case ReturnStatementSyntax returned:
                Error("COPE-FLOW-EXEC-0005", "Use finish to complete a flow.", returned.ReturnKeyword);
                return returned;
            case WhileStatementSyntax loop:
                return loop with { Condition = RewriteExpression(loop.Condition), Body = RewriteStackStatement(loop.Body) };
            case ForStatementSyntax loop:
                return loop with
                {
                    Initializer = loop.Initializer is StatementSyntax initializer ? RewriteStatement(initializer) : loop.Initializer,
                    Condition = loop.Condition is null ? null : RewriteExpression(loop.Condition),
                    Increment = loop.Increment is null ? null : RewriteExpression(loop.Increment),
                    Body = RewriteStackStatement(loop.Body),
                };
            case ForOfStatementSyntax loop:
                return loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteStackStatement(loop.Body) };
            case StaticForStatementSyntax loop:
                return loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteStackStatement(loop.Body) };
            case StaticIfStatementSyntax conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    ThenStatement = RewriteStackStatement(conditional.ThenStatement),
                    ElseStatement = conditional.ElseStatement is null ? null : RewriteStackStatement(conditional.ElseStatement),
                };
            default:
                return RewriteExecutionStatement(statement);
        }
    }

    private IReadOnlyList<StatementSyntax> RewriteStackList(IReadOnlyList<StatementSyntax> statements)
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
            rewritten.Add(RewriteStackStatement(statement));
            ended = EndsStep(statement);
        }
        return rewritten;
    }

    private ExpressionSyntax UpdateActivation(string kind, params (string Name, ExpressionSyntax Value)[] fields)
    {
        var updates = new[] { ("board", (ExpressionSyntax)Value("board")) }.Concat(fields).ToArray();
        ExpressionSyntax updated = Call(StackName("Replace"), Value("__machine"), StackVariant(current.Identifier.Text, With(Value("__frame"), updates)));
        return With(updated, ("stepKind", Member(Value(StackName("StepKind")), kind)));
    }

    private StatementSyntax RewriteStackControl(FlowControlStatementSyntax control)
    {
        anchor = control.Keyword;
        switch (control.Keyword.Text)
        {
            case "transition":
                {
                    FlowStateSyntax? target = current.States.FirstOrDefault(state => state.Identifier.Text == control.SuccessTarget?.Text);
                    if (target is null) Error("COPE-FLOW-0015", "Transition target must be a state in this flow.", control.SuccessTarget!);
                    if (target?.Parameters.Count > 0) Error("COPE-FLOW-EXEC-0017", "A typed continuation requires its child result; transition cannot fabricate that payload.", control.Keyword);
                    return Return(UpdateActivation("Transitioned",
                        ("state", Member(Value(ActivationName(current, "State")), control.SuccessTarget!.Text)),
                        ("entry", Member(Value(ActivationName(current, "Entry")), "None"))));
                }
            case "finish":
            case "fail":
                {
                    bool failed = control.Keyword.Text == "fail";
                    TypeSyntax? payloadType = failed ? FailureType(current) : SuccessType(current);
                    if (payloadType is null)
                    {
                        Error("COPE-FLOW-0026", "Fail requires a declared error type.", control.Keyword);
                        return Return(FaultExpression(Value("__machine"), 3));
                    }
                    if (IsVoid(payloadType) != (control.Value is null))
                    {
                        Error("COPE-FLOW-0029", "Completion must carry exactly the declared payload, or none for void.", control.Keyword);
                    }
                    var arguments = new List<ExpressionSyntax> { UpdateActivation(failed ? "Failed" : "Completed") };
                    if (control.Value is not null) arguments.Add(RewriteExpression(control.Value));
                    return Return(Call(ActivationName(current, failed ? "Fail" : "Complete"), arguments.ToArray()));
                }
            case "push":
                {
                    if (control.Value is not CallExpressionSyntax { Target: NameExpressionSyntax child } call
                        || !declarations.TryGetValue(child.IdentifierToken.Text, out FlowDeclarationSyntax? childFlow))
                        return Return(FaultExpression(Value("__machine"), 3));
                    ExpressionSyntax condition = new BinaryExpressionSyntax(Member(Value("__machine"), "depth"), Token(SyntaxKind.GreaterOrEqualsToken, ">="), Integer(stackCapacity));
                    string fallback = control.FailureTarget?.Text ?? control.SuccessTarget!.Text;
                    ExpressionSyntax parent = UpdateActivation("Pushed",
                        ("resumeSuccess", Member(Value(ActivationName(current, "State")), control.SuccessTarget!.Text)),
                        ("resumeFailure", Member(Value(ActivationName(current, "State")), fallback)));
                    ExpressionSyntax activated = StackVariant(childFlow.Identifier.Text,
                        Call(ActivationName(childFlow, "Start"), call.Arguments.Select(RewriteExpression).ToArray()));
                    var advanced = With(parent, ("depth", new BinaryExpressionSyntax(Member(Value("__machine"), "depth"), Token(SyntaxKind.PlusToken, "+"), Integer(1))));
                    return new BlockStatementSyntax(Token(SyntaxKind.OpenBraceToken, "{"),
                    [
                        If(condition, Return(FaultExpression(Value("__machine"), 1))),
                    Return(Call(StackName("Replace"), advanced, activated)),
                ], Token(SyntaxKind.CloseBraceToken, "}"));
                }
            default:
                throw new InvalidOperationException("Unknown flow control.");
        }
    }

    private void GenerateCompletion(FlowDeclarationSyntax child, bool failed)
    {
        current = child;
        TypeSyntax payloadType = failed ? FailureType(child)! : SuccessType(child);
        bool isVoid = IsVoid(payloadType);
        var parameters = new List<ParameterSyntax> { Parameter("__machine", Type(StackName("Frame"))) };
        if (!isVoid) parameters.Add(Parameter("__value", payloadType));
        string kind = failed ? "Failed" : "Completed";
        ExpressionSyntax rootOutcome = Member(Value(StackName("Outcome")), kind);
        if (!isVoid) rootOutcome = Invoke(rootOutcome, [Value("__value")]);
        ExpressionSyntax rootResult = child == stackRoot
            ? With(Value("__machine"), ("outcome", rootOutcome), ("stepKind", Member(Value(StackName("StepKind")), kind)), ("depth", Integer(0)))
            : FaultExpression(Value("__machine"), 3);
        ExpressionSyntax rootCondition = new BinaryExpressionSyntax(Member(Value("__machine"), "depth"), Token(SyntaxKind.EqualsEqualsToken, "=="), Integer(1));
        var statements = new List<StatementSyntax> { If(rootCondition, Return(rootResult)) };
        ExpressionSyntax cleared = Call(StackName("Replace"), Value("__machine"), StackVariant("Empty"));
        ExpressionSyntax parentMachine = With(cleared, ("depth", new BinaryExpressionSyntax(Member(Value("__machine"), "depth"), Token(SyntaxKind.MinusToken, "-"), Integer(1))));
        statements.Add(Local("__parentMachine", Type(StackName("Frame")), parentMachine));
        var parentArms = new List<MatchArmSyntax> { Arm(StackName("Slot"), "Empty", [], FaultExpression(Value("__parentMachine"), 3)) };
        foreach (FlowDeclarationSyntax parent in stackFlows)
        {
            var calls = parent.States.SelectMany(state => Controls(StateBody(state)))
                .Where(control => control is { Keyword.Text: "push", Value: CallExpressionSyntax { Target: NameExpressionSyntax name } }
                    && name.IdentifierToken.Text == child.Identifier.Text)
                .Where(control => !failed || control.FailureTarget is not null).ToArray();
            string resumeField = failed ? "resumeFailure" : "resumeSuccess";
            var helperStatements = new List<StatementSyntax>();
            foreach (string targetName in calls.Select(control => failed ? control.FailureTarget!.Text : control.SuccessTarget!.Text).Distinct(StringComparer.Ordinal))
            {
                FlowStateSyntax? target = parent.States.FirstOrDefault(state => state.Identifier.Text == targetName);
                if (target is null || target.Parameters.Count != (isVoid ? 0 : 1)) continue;
                ExpressionSyntax condition = Match(Member(Value("__parentFrame"), resumeField),
                    parent.States.Select(state => Arm(ActivationName(parent, "State"), state.Identifier.Text, [], Boolean(state.Identifier.Text == targetName))).ToArray());
                ExpressionSyntax entry = Member(Value(ActivationName(parent, "Entry")), "None");
                if (!isVoid) entry = Invoke(Member(Value(ActivationName(parent, "Entry")), targetName), [Value("__value")]);
                ExpressionSyntax updated = With(Value("__parentFrame"),
                    ("state", Member(Value(ActivationName(parent, "State")), targetName)), ("entry", entry));
                helperStatements.Add(If(condition, Return(Call(StackName("Replace"), Value("__parentMachine"), StackVariant(parent.Identifier.Text, updated)))));
            }
            helperStatements.Add(Return(FaultExpression(Value("__parentMachine"), 3)));
            string helperName = ActivationName(child, kind + "_To_" + parent.Identifier.Text);
            var helperParameters = new List<ParameterSyntax>
            {
                Parameter("__parentMachine", Type(StackName("Frame"))),
                Parameter("__parentFrame", Type(ActivationName(parent, "Frame"))),
            };
            if (!isVoid) helperParameters.Add(Parameter("__value", payloadType));
            generated.Add(Function(helperName, helperParameters, Type(StackName("Frame")), helperStatements));
            var args = new List<ExpressionSyntax> { Value("__parentMachine"), PayloadValue("__parent", "data") };
            if (!isVoid) args.Add(Value("__value"));
            parentArms.Add(Arm(StackName("Slot"), parent.Identifier.Text, ["__parent"], Call(helperName, args.ToArray())));
        }
        statements.Add(Return(With(Match(Call(StackName("Peek"), Value("__parentMachine")), parentArms),
            ("stepKind", Member(Value(StackName("StepKind")), kind)))));
        generated.Add(Function(ActivationName(child, failed ? "Fail" : "Complete"), parameters, Type(StackName("Frame")), statements));
    }
}
