using Copeland.TS.Compiler;
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
}
