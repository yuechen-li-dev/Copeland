namespace Copeland.TS.Syntax;

internal sealed partial class FlowExecutionProjection
{
    private sealed class TapeInstance(
        int id,
        string path,
        FlowDeclarationSyntax flow,
        TapeInstance? parent,
        FlowControlStatementSyntax? callSite)
    {
        public int Id { get; } = id;
        public string Path { get; } = path;
        public FlowDeclarationSyntax Flow { get; } = flow;
        public TapeInstance? Parent { get; } = parent;
        public FlowControlStatementSyntax? CallSite { get; } = callSite;
        public Dictionary<FlowControlStatementSyntax, TapeInstance> Children { get; } = [];
    }

    private FlowDeclarationSyntax tapeRoot = null!;
    private List<TapeInstance> tapeInstances = [];
    private readonly Dictionary<string, FlowTapePlan> tapePlans = new(StringComparer.Ordinal);
    private string TapeName(string part) => Name(tapeRoot.Identifier.Text, part);
    private string TapeBoard(TapeInstance instance) => instance.Id == 0
        ? TapeName("Board") : TapeName("Run" + instance.Id + "_Board");
    private static string TapeField(TapeInstance instance, string name) => instance.Id == 0
        ? name : "__run" + instance.Id + "_" + name;
    private static string TapeState(TapeInstance instance, string state) => instance.Id == 0
        ? state : instance.Path + "_" + state;
    private ExpressionSyntax TapeStateValue(TapeInstance instance, string state) =>
        Member(Value(TapeName("State")), TapeState(instance, state));

    private void GenerateTapeFlow()
    {
        tapeRoot = current;
        tapeInstances = [];
        int previousErrors = diagnostics.Count;
        BuildTapeInstance(tapeRoot, null, null, new HashSet<string>(StringComparer.Ordinal));
        if (diagnostics.Count != previousErrors)
        {
            return;
        }

        int position = 0;
        tapePlans[tapeRoot.Identifier.Text] = new FlowTapePlan(TapeName("Frame"), TapeName("State"),
            tapeInstances.SelectMany(instance => instance.Flow.States.Select(state => new FlowTapeState(
                position++, TapeState(instance, state.Identifier.Text), instance.Flow.Identifier.Text,
                instance.Path, instance.Parent?.Path, state.Identifier, instance.CallSite?.Keyword))).ToArray());

        var states = new List<EnumCaseSyntax>();
        var frameFields = new List<RecordFieldSyntax>();
        foreach (TapeInstance instance in tapeInstances)
        {
            current = instance.Flow;
            anchor = current.Identifier;
            var boardFields = (current.Board?.Fields ?? [])
                .Select(field => Field(field.Identifier.Text, field.Type!)).ToList();
            if (boardFields.Count == 0)
            {
                boardFields.Add(Field("__empty", Type("bool")));
            }
            generated.Add(Record(TapeBoard(instance), boardFields));
            frameFields.Add(Field(TapeField(instance, "board"), Type(TapeBoard(instance))));
            frameFields.AddRange(current.Parameters.Select(parameter =>
                Field(TapeField(instance, "parameter_" + parameter.Identifier.Text), parameter.Type!)));
            foreach (FlowStateSyntax state in current.States)
            {
                // The path distinguishes reused fragments; the source remains the authored state.
                anchor = state.Identifier;
                states.Add(Case(TapeState(instance, state.Identifier.Text)));
                frameFields.AddRange(state.Parameters.Select(parameter =>
                    Field(TapeField(instance, "entry_" + state.Identifier.Text), parameter.Type!)));
            }
        }
        generated.Add(Enum(TapeName("State"), states));
        anchor = tapeRoot.Identifier;
        var outcomes = new List<EnumCaseSyntax> { Case("Active") };
        outcomes.Add(IsVoid(SuccessType(tapeRoot)) ? Case("Completed") : Case("Completed", ("value", SuccessType(tapeRoot))));
        if (FailureType(tapeRoot) is { } failure) outcomes.Add(Case("Failed", ("error", failure)));
        outcomes.Add(Case("Fault", ("code", CounterType())));
        generated.Add(Enum(TapeName("Outcome"), outcomes));
        generated.Add(Enum(TapeName("StepKind"), new[]
        {
            "Started", "Transitioned", "Yielded", "Entered", "Completed", "Failed", "Fault"
        }.Select(name => Case(name)).ToArray()));
        frameFields.InsertRange(0,
        [
            Field("state", Type(TapeName("State"))),
            Field("entryState", Type(TapeName("State"))),
            Field("outcome", Type(TapeName("Outcome"))),
            Field("stepKind", Type(TapeName("StepKind"))),
        ]);
        generated.Add(Record(TapeName("Frame"), frameFields));

        TapeInstance root = tapeInstances[0];
        string initial = tapeRoot.States.Single(state => state.InitialKeyword is not null).Identifier.Text;
        var initialValues = frameFields.Select(field =>
            (field.Identifier.Text, Value: (ExpressionSyntax)new GeneratedZeroExpressionSyntax(field.Type))).ToList();
        Set("state", TapeStateValue(root, initial));
        Set("entryState", TapeStateValue(root, initial));
        Set("outcome", Member(Value(TapeName("Outcome")), "Active"));
        Set("stepKind", Member(Value(TapeName("StepKind")), "Started"));
        Set("board", TapeBoardInitializer(root));
        foreach (ParameterSyntax parameter in tapeRoot.Parameters)
        {
            Set("parameter_" + parameter.Identifier.Text, Value(parameter.Identifier.Text));
        }
        generated.Add(Function(TapeName("Start"), tapeRoot.Parameters, Type(TapeName("Frame")),
            [Return(Object(initialValues))]));

        var arms = new List<MatchArmSyntax>();
        foreach (TapeInstance instance in tapeInstances)
        {
            current = instance.Flow;
            foreach (FlowStateSyntax state in current.States)
            {
                anchor = state.Identifier;
                string helper = TapeName("State_" + TapeState(instance, state.Identifier.Text));
                var statements = new List<StatementSyntax>();
                if (state.Parameters.Count > 0)
                {
                    statements.Add(If(new BinaryExpressionSyntax(new GeneratedEnumTagExpressionSyntax(Member(Value("__frame"), "entryState")),
                        Token(SyntaxKind.BangEqualsToken, "!="), new GeneratedEnumTagExpressionSyntax(Member(Value("__frame"), "state"))),
                        TapeFault(3)));
                }
                statements.Add(Local("board", Type(TapeBoard(instance)),
                    Member(Value("__frame"), TapeField(instance, "board")), mutable: true));
                statements.AddRange(current.Parameters.Select(parameter => Local(parameter.Identifier.Text, parameter.Type!,
                    Member(Value("__frame"), TapeField(instance, "parameter_" + parameter.Identifier.Text)))));
                statements.AddRange(state.Parameters.Select(parameter => Local(parameter.Identifier.Text, parameter.Type!,
                    Member(Value("__frame"), TapeField(instance, "entry_" + state.Identifier.Text)))));
                statements.AddRange(RewriteTapeList(instance, StateBody(state).Statements));
                generated.Add(Function(helper, [Parameter("__frame", Type(TapeName("Frame")))],
                    Type(TapeName("Frame")), statements));
                arms.Add(Arm(TapeName("State"), TapeState(instance, state.Identifier.Text), [], Call(helper, Value("__frame"))));
            }
            if (instance.Parent is not null)
            {
                GenerateTapeEntry(instance);
            }
        }
        current = tapeRoot;
        anchor = tapeRoot.Identifier;
        var statusArms = outcomes.Select(outcome => Arm(TapeName("Outcome"), outcome.Identifier.Text,
            outcome.PayloadFields.Count == 0 ? [] : ["__ignored"], Boolean(outcome.Identifier.Text != "Active"))).ToArray();
        generated.Add(Function(TapeName("Step"), [Parameter("__frame", Type(TapeName("Frame")))], Type(TapeName("Frame")),
        [
            If(Match(Member(Value("__frame"), "outcome"), statusArms), Return(Value("__frame"))),
            Return(Match(Member(Value("__frame"), "state"), arms)),
        ]));
        generated.Add(Function(TapeName("Position"), [Parameter("__frame", Type(TapeName("Frame")))], CounterType(),
            [Return(new GeneratedEnumTagExpressionSyntax(Member(Value("__frame"), "state")))]));

        void Set(string name, ExpressionSyntax value)
        {
            int index = initialValues.FindIndex(field => field.Text == name);
            initialValues[index] = (name, value);
        }
    }

    private TapeInstance? BuildTapeInstance(FlowDeclarationSyntax flow, TapeInstance? parent,
        FlowControlStatementSyntax? callSite, HashSet<string> active)
    {
        if (active.Contains(flow.Identifier.Text))
        {
            Error("COPE-GPU-FLOW-0003", "Recursive run composition is not finite. Use a state transition to repeat work.", callSite!.Keyword);
            return null;
        }
        if (active.Count >= 16 || tapeInstances.Count >= 32
            || tapeInstances.Sum(instance => instance.Flow.States.Count) + flow.States.Count > 64)
        {
            Error("COPE-GPU-FLOW-0004", "Static FLOW composition exceeds 16 levels, 32 sites or 64 states per tape.", flow.Identifier);
            return null;
        }
        if (flow.StackCapacityToken is not null)
        {
            Error("COPE-GPU-FLOW-0005", "Static run composition has fixed storage; remove the stack clause.", flow.StackCapacityToken);
        }
        if (flow.Events.Count > 0 || flow.States.Any(state => state.Transitions.Count > 0))
        {
            Error("COPE-FLOW-EXEC-0001", "Static run composition requires event-free execution states.", flow.Identifier);
        }
        FlowStateSyntax[] initial = flow.States.Where(state => state.InitialKeyword is not null).ToArray();
        if (initial.Length != 1 || initial[0].Parameters.Count != 0
            || flow.States.Select(state => state.Identifier.Text).Distinct(StringComparer.Ordinal).Count() != flow.States.Count)
        {
            Error("COPE-GPU-FLOW-0006", "A composed fragment requires unique states and one payload-free initial state.", flow.Identifier);
        }
        foreach (FlowBoardFieldSyntax field in flow.Board?.Fields ?? [])
        {
            if (field.Type is null || field.Initializer is null)
            {
                Error("COPE-FLOW-EXEC-0002", "Board fields require an explicit type and initializer.", field.Identifier);
            }
        }
        foreach (ParameterSyntax parameter in flow.Parameters)
        {
            ValidateTapeParameter(parameter);
        }
        foreach (FlowStateSyntax state in flow.States)
        {
            foreach (ParameterSyntax parameter in state.Parameters)
            {
                ValidateTapeParameter(parameter);
            }
            if (state.Parameters.Count > 1)
            {
                Error("COPE-FLOW-EXEC-0015", "A continuation admits one typed entry payload.", state.Identifier);
            }
            if (!EndsStep(StateBody(state)))
            {
                Error("COPE-FLOW-EXEC-0003", "Every state path must end with transition, yield, finish, fail, or run.", state.Identifier);
            }
        }
        int id = tapeInstances.Count;
        string path = parent is null ? "" : (parent.Path.Length == 0 ? "" : parent.Path + "_") + "Run" + id + "_" + flow.Identifier.Text;
        var instance = new TapeInstance(id, path, flow, parent, callSite);
        tapeInstances.Add(instance);
        active.Add(flow.Identifier.Text);
        foreach (FlowControlStatementSyntax control in flow.States.SelectMany(state => Controls(StateBody(state))))
        {
            if (control.Keyword.Text == "push")
            {
                Error("COPE-GPU-FLOW-0002", "GPU FLOW uses static composition: replace push with run.", control.Keyword);
                continue;
            }
            if (control.Keyword.Text != "run")
            {
                continue;
            }
            if (control.Value is not CallExpressionSyntax { Target: NameExpressionSyntax child }
                || !declarations.TryGetValue(child.IdentifierToken.Text, out var childFlow))
            {
                Error("COPE-GPU-FLOW-0007", "Run requires a statically named flow declared in this module.", control.Keyword);
                continue;
            }
            ValidatePush(flow, control);
            TapeInstance? childInstance = BuildTapeInstance(childFlow, instance, control, active);
            if (childInstance is not null)
            {
                instance.Children.Add(control, childInstance);
            }
        }
        active.Remove(flow.Identifier.Text);
        return instance;
    }

    private void ValidateTapeParameter(ParameterSyntax parameter)
    {
        if (parameter.Type is null)
        {
            Error("COPE-FLOW-EXEC-0010", "Composed parameters and continuation payloads require explicit types.", parameter.Identifier);
        }
        if (parameter.Identifier.Text is "board" or "__frame" or "__result")
        {
            Error("COPE-GPU-FLOW-0010", "board, __frame and __result are reserved within composed FLOW states.", parameter.Identifier);
        }
    }

    private ExpressionSyntax TapeBoardInitializer(TapeInstance instance)
    {
        var fields = (instance.Flow.Board?.Fields ?? [])
            .Select(field => (field.Identifier.Text, RewriteExpression(field.Initializer!))).ToList();
        if (fields.Count == 0)
        {
            fields.Add(("__empty", Boolean(false)));
        }
        return Object(fields);
    }

    private void GenerateTapeEntry(TapeInstance instance)
    {
        current = instance.Flow;
        anchor = instance.CallSite!.Keyword;
        string initial = current.States.Single(state => state.InitialKeyword is not null).Identifier.Text;
        var replacements = current.Parameters.Select(parameter =>
            (TapeField(instance, "parameter_" + parameter.Identifier.Text), (ExpressionSyntax)Value(parameter.Identifier.Text))).ToList();
        replacements.Add((TapeField(instance, "board"), TapeBoardInitializer(instance)));
        replacements.Add(("state", TapeStateValue(instance, initial)));
        replacements.Add(("entryState", TapeStateValue(instance, initial)));
        replacements.Add(("stepKind", Member(Value(TapeName("StepKind")), "Entered")));
        generated.Add(Function(TapeName("Enter_Run" + instance.Id),
            new[] { Parameter("__frame", Type(TapeName("Frame"))) }.Concat(current.Parameters).ToArray(),
            Type(TapeName("Frame")), [Return(With(Value("__frame"), replacements.ToArray()))]));
    }

    private IReadOnlyList<StatementSyntax> RewriteTapeList(TapeInstance instance, IReadOnlyList<StatementSyntax> statements)
    {
        var result = new List<StatementSyntax>();
        bool ended = false;
        foreach (StatementSyntax statement in statements)
        {
            if (ended)
            {
                Error("COPE-FLOW-EXEC-0006", "State work after a step-ending control is unreachable.", anchor);
                break;
            }
            result.Add(RewriteTapeStatement(instance, statement));
            ended = EndsStep(statement);
        }
        return result;
    }

    private StatementSyntax RewriteTapeStatement(TapeInstance instance, StatementSyntax statement)
    {
        switch (statement)
        {
            case FlowControlStatementSyntax control:
                return RewriteTapeControl(instance, control);
            case YieldStatementSyntax yielded:
                if (yielded.Expression is not null || yielded.StarToken is not null
                    || yielded.ReturnKeyword is not null || yielded.BreakKeyword is not null)
                {
                    Error("COPE-FLOW-EXEC-0004", "Execution yield must be bare.", yielded.YieldKeyword);
                }
                return TapeReturn(instance, "Yielded");
            case BlockStatementSyntax block:
                return block with { Statements = RewriteTapeList(instance, block.Statements) };
            case IfStatementSyntax conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    ThenStatement = RewriteTapeStatement(instance, conditional.ThenStatement),
                    ElseStatement = conditional.ElseStatement is null ? null : RewriteTapeStatement(instance, conditional.ElseStatement),
                };
            case StaticIfStatementSyntax conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    ThenStatement = RewriteTapeStatement(instance, conditional.ThenStatement),
                    ElseStatement = conditional.ElseStatement is null ? null : RewriteTapeStatement(instance, conditional.ElseStatement),
                };
            case StaticForStatementSyntax loop:
                return loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteTapeStatement(instance, loop.Body) };
            case ForStatementSyntax loop:
                return loop with { Body = RewriteTapeStatement(instance, loop.Body) };
            case ForOfStatementSyntax loop:
                return loop with { Iterable = RewriteExpression(loop.Iterable), Body = RewriteTapeStatement(instance, loop.Body) };
            case WhileStatementSyntax loop:
                return loop with { Condition = RewriteExpression(loop.Condition), Body = RewriteTapeStatement(instance, loop.Body) };
            default:
                return RewriteExecutionStatement(statement);
        }
    }

    private StatementSyntax RewriteTapeControl(TapeInstance instance, FlowControlStatementSyntax control)
    {
        anchor = control.Keyword;
        switch (control.Keyword.Text)
        {
            case "transition":
                FlowStateSyntax? target = instance.Flow.States.FirstOrDefault(state => state.Identifier.Text == control.SuccessTarget!.Text);
                if (target is null || target.Parameters.Count > 0)
                {
                    Error("COPE-GPU-FLOW-0008", "Transition requires a payload-free state in this fragment; typed entry states are run continuations.", control.Keyword);
                    return TapeFault(3);
                }
                return TapeReturn(instance, "Transitioned", ("state", TapeStateValue(instance, target.Identifier.Text)));
            case "run":
                TapeInstance child = instance.Children[control];
                var call = (CallExpressionSyntax)control.Value!;
                return Return(Call(TapeName("Enter_Run" + child.Id),
                    new[] { With(Value("__frame"), (TapeField(instance, "board"), Value("board"))) }
                        .Concat(call.Arguments.Select(RewriteExpression)).ToArray()));
            case "finish":
            case "fail":
                return TapeCompletion(instance, control);
            default:
                return TapeFault(3);
        }
    }

    private StatementSyntax TapeCompletion(TapeInstance instance, FlowControlStatementSyntax control)
    {
        bool failed = control.Keyword.Text == "fail";
        TypeSyntax? resultType = failed ? FailureType(instance.Flow) : SuccessType(instance.Flow);
        if (resultType is null || IsVoid(resultType) != (control.Value is null))
        {
            Error("COPE-GPU-FLOW-0009", "Completion must supply the fragment's declared result or error; void completion has no value.", control.Keyword);
            return TapeFault(3);
        }
        if (instance.Parent is null)
        {
            ExpressionSyntax outcome = Member(Value(TapeName("Outcome")), failed ? "Failed" : "Completed");
            if (control.Value is not null)
            {
                outcome = Invoke(outcome, [RewriteExpression(control.Value)]);
            }
            return TapeReturn(instance, failed ? "Failed" : "Completed", ("outcome", outcome));
        }
        TapeInstance parent = instance.Parent;
        string next = (failed ? instance.CallSite!.FailureTarget : instance.CallSite!.SuccessTarget)!.Text;
        var replacements = new List<(string, ExpressionSyntax)>
        {
            ("state", TapeStateValue(parent, next)),
            ("entryState", TapeStateValue(parent, next)),
        };
        if (control.Value is null)
        {
            return TapeReturn(instance, "Completed", replacements.ToArray());
        }
        // The declared child type and the receiving continuation type are checked separately.
        replacements.Add((TapeField(parent, "entry_" + next), Value("__result")));
        return new BlockStatementSyntax(Token(SyntaxKind.OpenBraceToken, "{"),
        [
            Local("__result", resultType, RewriteExpression(control.Value)),
            TapeReturn(instance, failed ? "Failed" : "Completed", replacements.ToArray()),
        ], Token(SyntaxKind.CloseBraceToken, "}"));
    }

    private StatementSyntax TapeReturn(TapeInstance instance, string kind, params (string, ExpressionSyntax)[] replacements)
    {
        return Return(With(Value("__frame"),
            new[]
            {
                (TapeField(instance, "board"), (ExpressionSyntax)Value("board")),
                ("stepKind", Member(Value(TapeName("StepKind")), kind)),
            }.Concat(replacements).ToArray()));
    }

    private StatementSyntax TapeFault(int code)
    {
        return Return(With(Value("__frame"),
            ("outcome", Invoke(Member(Value(TapeName("Outcome")), "Fault"), [Integer(code)])),
            ("stepKind", Member(Value(TapeName("StepKind")), "Fault"))));
    }
}
