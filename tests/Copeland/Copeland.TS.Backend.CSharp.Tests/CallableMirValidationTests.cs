using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class CallableMirValidationTests
{
    [Theory]
    [MemberData(nameof(MalformedPrograms))]
    public void Shared_callable_mir_validation_rejects_before_either_backend_emits(
        MirProgram program,
        string expectedMessage)
    {
        CSharpCompilation csharp = CSharpBackend.Emit(program);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(program);

        Assert.Empty(csharp.SourceText);
        Assert.Contains(csharp.Diagnostics, diagnostic => diagnostic.Id == "COPE-CS-0002"
            && diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
        Assert.Null(javascript.SourceText);
        Assert.Contains(javascript.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0002"
            && diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<object[]> MalformedPrograms()
    {
        MirType number = new MirNamedType("number");
        MirType boolean = new MirNamedType("boolean");
        MirType voidType = new MirNamedType("void");
        MirCallableType numberOperation = new([new MirCallableParameter("value", number)], number);
        MirFunction increment = new("increment", [new MirParameter("value", number)], number, [], []);

        yield return Case(
            Program(new MirFunctionReferenceExpression("missing", numberOperation)),
            "unknown function");
        yield return Case(
            new MirProgram([], [], [], [], [
                increment,
                new MirFunction("main", [], voidType, [], [new MirExpressionStatement(new MirFunctionReferenceExpression("increment", new MirCallableType([], number)))]),
            ]),
            "signature does not match");
        yield return Case(
            Program(new MirInvokeExpression(new MirVariableExpression("value", number), [], number)),
            "non-callable");
        yield return Case(
            Program(new MirInvokeExpression(new MirVariableExpression("operation", numberOperation), [], number)),
            "arity");
        yield return Case(
            Program(new MirInvokeExpression(new MirVariableExpression("operation", numberOperation), [new MirLiteralExpression(true, boolean)], number)),
            "argument 1");
        yield return Case(
            Program(new MirInvokeExpression(new MirVariableExpression("operation", numberOperation), [new MirLiteralExpression(1d, number)], boolean)),
            "result type");
        yield return Case(
            Program(new MirCallableConstructionExpression("missing", [new MirLiteralExpression(1d, number)], numberOperation)),
            "unknown code function");
        yield return Case(
            new MirProgram([], [], [], [], [
                new MirFunction("code", [new MirParameter("environment", number), new MirParameter("value", number)], number, [], []),
                new MirFunction("main", [], voidType, [], [new MirExpressionStatement(new MirCallableConstructionExpression("code", [new MirLiteralExpression(true, boolean)], numberOperation))]),
            ]),
            "environment value");
        yield return Case(
            new MirProgram([], [], [], [], [increment, new MirFunction("increment", [], voidType, [], [])]),
            "duplicate");
        yield return Case(
            new MirProgram([], [], [new MirTableDefinition(new MirTableId("t1"), "Values", "t1.row", [new MirTableColumnDefinition(new MirTableColumnId("t1.c0"), "operation", numberOperation, [])], 0)], [], [new MirFunction("main", [], voidType, [], [])]),
            "table column");

        static object[] Case(MirProgram program, string message) => [program, message];

        static MirProgram Program(MirExpression expression)
        {
            MirType voidType = new MirNamedType("void");
            return new MirProgram([], [], [], [], [
                new MirFunction("main", [], voidType, [], [new MirExpressionStatement(expression)]),
            ]);
        }
    }
}
