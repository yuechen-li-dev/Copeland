using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class ModuleValueTests
{
    [Fact]
    public void Typed_module_storage_emits_and_executes_as_a_readonly_field()
    {
        MirType integer = new MirNamedType("int");
        var initializer = new MirFunction("initialize", [], integer, [],
            [new MirReturnStatement(new MirLiteralExpression(5, integer))]);
        var main = new MirFunction("main", [], integer, [],
            [new MirReturnStatement(new MirBinaryExpression("+", new MirVariableExpression("stored", integer), new MirLiteralExpression(1, integer), integer))]);
        var program = new MirProgram([], [initializer, main])
        {
            ModuleValues = [new MirModuleValue("stored", integer, "initialize")],
        };
        CSharpCompilation emitted = CSharpBackend.Emit(program);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(6, GeneratedModuleInvoker.Invoke(generated.Assembly!, "main"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    [InlineData("duplicate")]
    [InlineData("self-read")]
    [InlineData("write")]
    public void Malformed_module_storage_is_rejected_before_either_backend_emits(string defect)
    {
        MirType integer = new MirNamedType("int");
        MirType number = new MirNamedType("number");
        MirExpression expression = defect == "self-read"
            ? new MirVariableExpression("stored", integer)
            : new MirLiteralExpression(5, integer);
        var initializer = new MirFunction("initialize", [], integer, [], [new MirReturnStatement(expression)]);
        var value = new MirModuleValue("stored", defect == "wrong-type" ? number : integer,
            defect == "missing" ? "missing" : "initialize");
        var functions = new List<MirFunction> { initializer };
        if (defect == "write")
        {
            functions.Add(new MirFunction("main", [], integer, [],
                [new MirReturnStatement(new MirAssignmentExpression("stored", new MirLiteralExpression(9, integer), integer))]));
        }
        var program = new MirProgram([], functions)
        {
            ModuleValues = defect == "duplicate" ? [value, value] : [value],
        };
        CSharpCompilation csharp = CSharpBackend.Emit(program);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(program);
        Assert.Empty(csharp.SourceText);
        Assert.NotEmpty(csharp.Diagnostics);
        Assert.Null(javascript.SourceText);
        Assert.NotEmpty(javascript.Diagnostics);
    }
}
