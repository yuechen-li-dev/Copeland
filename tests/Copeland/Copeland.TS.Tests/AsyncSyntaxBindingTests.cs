using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class AsyncSyntaxBindingTests
{
    [Fact]
    public void AsyncFunctionCall_BindsAsCompilerOwnedAsync_AndAwaitRestoresEventualType()
    {
        const string source = """
async function read(): number {
    return 7;
}

async function load(): number {
    const pending: Async<number> = read();
    return await pending;
}
""";

        CopelandCompilation compilation = CopelandCompiler.Compile(source, new CopelandCompilationOptions { TargetStage = CopelandCompilationStage.Bound });

        Assert.Empty(compilation.Diagnostics);
        BoundFunctionDeclaration load = Assert.Single(compilation.BoundCompilation!.Program.Functions, function => function.Symbol.Name == "load");
        var declaration = Assert.IsType<BoundVariableDeclaration>(load.Body.Statements[0]);
        Assert.IsType<AsyncTypeSymbol>(declaration.Variable.Type);
        Assert.IsType<AsyncTypeSymbol>(declaration.Initializer.Type);
        var returned = Assert.IsType<BoundReturnStatement>(load.Body.Statements[1]);
        var awaited = Assert.IsType<BoundAwaitExpression>(returned.Expression);
        Assert.Equal("number", awaited.Type.Name);
    }

    [Fact]
    public void AwaitOutsideAsyncFunction_HasFocusedDiagnostic()
    {
        const string source = """
function load(): number {
    return await 1;
}
""";

        CopelandCompilation compilation = CopelandCompiler.Compile(source, new CopelandCompilationOptions { TargetStage = CopelandCompilationStage.Bound });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-ASYNC-0001");
    }

    [Fact]
    public void AwaitOfNonAsyncValue_HasFocusedDiagnostic()
    {
        const string source = """
async function load(): number {
    return await 1;
}
""";

        CopelandCompilation compilation = CopelandCompiler.Compile(source, new CopelandCompilationOptions { TargetStage = CopelandCompilationStage.Bound });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-ASYNC-0002");
    }

    [Fact]
    public void AwaitQuestion_ParsesAsPropagationAfterAwait()
    {
        SyntaxTree tree = SyntaxTree.Parse("async function load(): number ! Error { return await fetch()?; }");

        var function = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(tree.Root.Members));
        var returned = Assert.IsType<ReturnStatementSyntax>(Assert.Single(function.Body.Statements));
        var propagate = Assert.IsType<PropagateExpressionSyntax>(returned.Expression);
        Assert.IsType<AwaitExpressionSyntax>(propagate.Operand);
    }

    [Fact]
    public void AsyncSource_LowersToStructuredMirBeforeAutomatonSplitting()
    {
        const string source = """
async function read(): number {
    return 7;
}

async function load(): number {
    return await read();
}
""";

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("async func load() -> number", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("await call read()", compilation.MirText, StringComparison.Ordinal);
        Assert.True(compilation.MirCompilation!.Program!.Functions.All(function => function.IsAsync));
    }

    [Fact]
    public void AsyncSource_LowersAValidatedAutomatonWithSuspensionFrameSlots()
    {
        const string source = """
async function read(value: number): number {
    return value;
}

async function load(value: number): number {
    const pending: Async<number> = read(value);
    return await pending;
}
""";

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Empty(compilation.Diagnostics);
        MirFunction load = Assert.Single(compilation.MirCompilation!.Program!.Functions, function => function.Name == "load");
        MirSuspensionAutomaton automaton = Assert.IsType<MirSuspensionAutomaton>(load.SuspensionAutomaton);
        Assert.Contains(automaton.FrameSlots, slot => slot.Id.Value == "parameter_value");
        Assert.Contains(automaton.FrameSlots, slot => slot.Id.Value == "local_pending");
        Assert.Contains(automaton.States, state => state is MirAwaitSuspensionAutomatonState);
        MirAsyncExecutionPlan executionPlan = Assert.IsType<MirAsyncExecutionPlan>(automaton.ExecutionPlan);
        Assert.Contains(executionPlan.States, state => state is MirAsyncStatementExecutionState);
        Assert.Empty(MirValidator.Validate(compilation.MirCompilation.Program));
    }

    [Fact]
    public void NestedAwaitExpression_LowersToExplicitAwaitExecutionStates()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            async function read(value: number): number { return value + 1; }
            async function load(value: number): number {
                return (await read(value)) + 1;
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirFunction load = Assert.Single(compilation.MirCompilation!.Program!.Functions, function => function.Name == "load");
        MirAsyncExecutionPlan plan = Assert.IsType<MirAsyncExecutionPlan>(load.SuspensionAutomaton!.ExecutionPlan);
        MirAsyncAwaitExecutionState awaitState = Assert.Single(plan.States.OfType<MirAsyncAwaitExecutionState>());

        Assert.Contains(load.SuspensionAutomaton.FrameSlots, slot => slot.Id == awaitState.AwaitedComputationSlot && slot.Type is MirAsyncType);
        Assert.Contains(load.SuspensionAutomaton.FrameSlots, slot => slot.Id == awaitState.ResumedValueSlot && slot.Type.Name == "number");
        Assert.Contains(plan.States, state => state is MirAsyncReturnExecutionState { Statement.Expression: MirBinaryExpression });
    }

    [Fact]
    public void AwaitedResultInsideTry_LowersToAnExplicitLexicalHandlerTransfer()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            async function parse(value: number): number ! string {
                if (value < 0) { return err("negative"); }
                return value + 1;
            }
            async function load(value: number): number {
                return try {
                    const parsed: number = await parse(value)?;
                    parsed + 1
                } except (error) {
                    0
                };
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirFunction load = Assert.Single(compilation.MirCompilation!.Program!.Functions, function => function.Name == "load");
        MirAsyncExecutionPlan plan = Assert.IsType<MirAsyncExecutionPlan>(load.SuspensionAutomaton!.ExecutionPlan);
        MirAsyncPropagateExecutionState propagation = Assert.Single(plan.States.OfType<MirAsyncPropagateExecutionState>());

        Assert.IsType<MirPropagationTarget.LexicalExcept>(propagation.Target);
        Assert.NotNull(propagation.HandlerStateId);
        Assert.NotNull(propagation.HandlerErrorSlot);
        Assert.Contains(load.SuspensionAutomaton.FrameSlots, slot => slot.Id == propagation.HandlerErrorSlot);
        Assert.Empty(MirValidator.Validate(compilation.MirCompilation.Program));
    }
}
