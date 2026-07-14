using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class ArrayMirValidationTests
{
    [Theory]
    [InlineData("expression-type", "does not carry a MirArrayType")]
    [InlineData("element-type", "Array element 0 does not match")]
    [InlineData("nested-type", "Array element 0 does not match")]
    [InlineData("record-nominal", "Array element 0 does not match")]
    [InlineData("enum-nominal", "Array element 0 does not match")]
    [InlineData("empty-type", "does not carry a MirArrayType")]
    [InlineData("missing-element", "Array element 0 is missing")]
    [InlineData("local-boundary", "Array initializer for local")]
    [InlineData("return-boundary", "Array return expression")]
    public void Malformed_ordinary_arrays_are_rejected_before_both_backend_entry_points(
        string scenario,
        string expectedMessage)
    {
        MirProgram program = CreateProgram(scenario);

        CSharpCompilation csharp = CSharpBackend.Emit(program);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(program);

        Assert.Empty(csharp.SourceText);
        Assert.NotEmpty(csharp.Diagnostics);
        Assert.All(csharp.Diagnostics, diagnostic => Assert.Equal("COPE-CS-0002", diagnostic.Id));
        Assert.Contains(csharp.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal));

        Assert.Null(javascript.SourceText);
        Assert.False(javascript.Success);
        Assert.NotEmpty(javascript.Diagnostics);
        Assert.All(javascript.Diagnostics, diagnostic => Assert.Equal("COPE-JS-0002", diagnostic.Id));
        Assert.Contains(javascript.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    private static MirProgram CreateProgram(string scenario)
    {
        var number = new MirNamedType("number");
        var text = new MirNamedType("string");
        var firstRecord = new MirRecordType(new MirRecordTypeId("r1"), "First");
        var secondRecord = new MirRecordType(new MirRecordTypeId("r2"), "Second");
        var firstEnum = new MirNamedType("FirstState");
        var secondEnum = new MirNamedType("SecondState");
        MirArrayType numberArray = new(number);
        MirArrayType textArray = new(text);

        MirFunction function = scenario switch
        {
            "expression-type" => Function(
                numberArray,
                new MirArrayExpression([], number)),
            "element-type" => Function(
                numberArray,
                new MirArrayExpression([new MirLiteralExpression("wrong", text)], numberArray)),
            "nested-type" => Function(
                new MirArrayType(numberArray),
                new MirArrayExpression(
                    [new MirArrayExpression([], textArray)],
                    new MirArrayType(numberArray))),
            "record-nominal" => Function(
                new MirArrayType(firstRecord),
                new MirArrayExpression(
                    [new MirVariableExpression("second", secondRecord)],
                    new MirArrayType(firstRecord))),
            "enum-nominal" => Function(
                new MirArrayType(firstEnum),
                new MirArrayExpression(
                    [new MirVariableExpression("second", secondEnum)],
                    new MirArrayType(firstEnum))),
            "empty-type" => Function(
                numberArray,
                new MirArrayExpression([], text)),
            "missing-element" => Function(
                numberArray,
                new MirArrayExpression([null!], numberArray)),
            "local-boundary" => new MirFunction(
                "main",
                [],
                numberArray,
                [new MirLocal("values", numberArray, true)],
                [new MirVariableDeclarationStatement(
                    new MirLocal("values", numberArray, true),
                    new MirArrayExpression([], textArray))]),
            "return-boundary" => Function(
                numberArray,
                new MirArrayExpression([], textArray)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        return new MirProgram(
            [
                new MirEnum("FirstState", [new MirEnumCase("Only", [])]),
                new MirEnum("SecondState", [new MirEnumCase("Only", [])]),
            ],
            [
                new MirRecordDefinition(new MirRecordTypeId("r1"), "First", []),
                new MirRecordDefinition(new MirRecordTypeId("r2"), "Second", []),
            ],
            [function]);
    }

    private static MirFunction Function(MirType returnType, MirExpression expression)
    {
        return new MirFunction(
            "main",
            [],
            returnType,
            [],
            [new MirReturnStatement(expression)]);
    }
}
