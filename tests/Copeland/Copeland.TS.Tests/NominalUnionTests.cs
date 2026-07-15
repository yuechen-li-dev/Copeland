using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class NominalUnionTests
{
    [Fact]
    public void Lexer_preserves_logical_or_and_emits_a_distinct_pipe_token()
    {
        var tree = SyntaxTree.ParseTokens("a | b || c");

        Assert.Equal(
        [
            SyntaxKind.IdentifierToken,
            SyntaxKind.PipeToken,
            SyntaxKind.IdentifierToken,
            SyntaxKind.PipePipeToken,
            SyntaxKind.IdentifierToken,
            SyntaxKind.EndOfFileToken,
        ],
        tree.Tokens.Select(token => token.Kind));
    }

    [Fact]
    public void Parser_preserves_nominal_union_declaration_tokens_and_order()
    {
        var tree = SyntaxTree.Parse("type Shape =\n    | Circle\n    | Rectangle;");

        var declaration = Assert.IsType<NominalUnionDeclarationSyntax>(Assert.Single(tree.Root.Members));
        Assert.Equal("Shape", declaration.Identifier.Text);
        Assert.Equal("|", declaration.LeadingPipeToken?.Text);
        Assert.Equal(["Circle", "Rectangle"], declaration.Alternatives.Select(token => token.Text));
        Assert.Single(declaration.PipeTokens);
        Assert.Empty(tree.Diagnostics);
    }

    [Theory]
    [InlineData("type Shape = Circle |;", "COPE-UNION-0001")]
    [InlineData("type Shape = |;", "COPE-UNION-0001")]
    [InlineData("type Shape = Circle || Rectangle;", "COPE-UNION-0001")]
    public void Parser_reports_deliberate_diagnostics_for_malformed_pipe_declarations(string source, string diagnosticId)
    {
        var tree = SyntaxTree.Parse(source);

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.All(tree.Diagnostics, diagnostic => Assert.True(diagnostic.Length > 0));
    }

    [Fact]
    public void Parser_rejects_inline_pipe_type_syntax_with_a_union_owned_diagnostic()
    {
        var tree = SyntaxTree.Parse("const value: number | string = 1;");

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "COPE-UNION-0012");
        Assert.DoesNotContain(tree.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Nominal_union_lowers_as_a_payload_enum_and_contextually_injects_direct_records()
    {
        const string source = """
record Circle {
    radius: number;
}

record Rectangle {
    width: number;
    height: number;
}

type Shape = Circle | Rectangle;

function area(): number {
    const circle: Circle = { radius: 4 };
    const shape: Shape = circle;
    return match shape {
        Circle(value) => value.radius * value.radius,
        Rectangle(value) => value.width * value.height,
    };
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Contains("enum Shape", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("Circle(value: Circle)", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("Rectangle(value: Rectangle)", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("Union", compilation.MirText, StringComparison.Ordinal);

    }

    [Fact]
    public void Nominal_unions_are_distinct_and_alias_alternatives_are_rejected_with_a_canonical_recommendation()
    {
        const string source = """
record Circle { radius: number; }
record Rectangle { width: number; }
type Round = Circle;
type BadShape = Round | Rectangle;
type Shape = Circle | Rectangle;
type OtherShape = Circle | Rectangle;
function invalid(value: Shape): OtherShape { return value; }
""";

        var compilation = CopelandCompiler.Compile(source, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
        });

        var aliasDiagnostic = Assert.Single(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-UNION-0006");
        Assert.Contains("'Round' is an alias of 'Circle'; use 'Circle'", aliasDiagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-UNION-0011");
    }

    [Fact]
    public void Nominal_union_rejects_duplicate_and_out_of_range_alternatives_before_mir()
    {
        const string duplicate = """
record Circle { radius: number; }
type Shape = Circle | Circle;
""";
        const string tooMany = """
record A { value: number; }
record B { value: number; }
record C { value: number; }
record D { value: number; }
record E { value: number; }
record F { value: number; }
record G { value: number; }
record H { value: number; }
record I { value: number; }
type Shape = A | B | C | D | E | F | G | H | I;
""";

        var duplicateCompilation = CopelandCompiler.CompileToMir(duplicate);
        var limitCompilation = CopelandCompiler.CompileToMir(tooMany);

        Assert.Contains(duplicateCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-UNION-0004");
        Assert.Null(duplicateCompilation.MirText);
        Assert.Contains(limitCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-UNION-0003");
        Assert.Null(limitCompilation.MirText);
    }

    [Fact]
    public void Nominal_union_reuses_enum_tson_identity_and_record_containment_validation()
    {
        const string tsonSource = """
const $schema: string = "copeland://union-proof/v1";
record Circle { radius: number; }
record Rectangle { width: number; }
type Shape = Circle | Rectangle;
function encode(): string ! TsonEncodeError {
    const circle: Circle = { radius: 4 };
    const shape: Shape = circle;
    return tsonEncode(shape);
}
""";
        const string recursiveSource = """
record Node { child: Tree; }
record Leaf { value: number; }
type Tree = Node | Leaf;
""";

        var tsonCompilation = CopelandCompiler.CompileToMir(tsonSource);
        var recursiveCompilation = CopelandCompiler.CompileToMir(recursiveSource);

        Assert.True(tsonCompilation.Success, Describe(tsonCompilation));
        Assert.Contains("copeland://union-proof/v1#Shape", tsonCompilation.MirText, StringComparison.Ordinal);
        Assert.Contains("copeland://union-proof/v1#Shape.Circle.value", tsonCompilation.MirText, StringComparison.Ordinal);
        Assert.Contains(recursiveCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REC-0004");
        Assert.Null(recursiveCompilation.MirText);
    }

    [Fact]
    public void Nominal_union_injection_uses_existing_expected_type_paths_without_generic_inference_widening()
    {
        const string source = """
record Circle { radius: number; }
record Rectangle { width: number; }
type Shape = Circle | Rectangle;
record Holder { shape: Shape; }

function identity<T>(value: T): T { return value; }
function accept(shape: Shape): Shape { return shape; }
function make(): Shape {
    const circle: Circle = { radius: 4 };
    return circle;
}
function prove(): number {
    const circle: Circle = { radius: 4 };
    const rectangle: Rectangle = { width: 3 };
    let assigned: Shape = circle;
    assigned = rectangle;
    const argument: Shape = accept(circle);
    const explicitGeneric: Shape = identity<Shape>(circle);
    const inferredGeneric: Circle = identity(circle);
    const holder: Holder = { shape: circle };
    const values: Shape[] = [circle, rectangle];
    const result: Shape ! string = ok(circle);
    const chosen: Shape = if true { circle } else { rectangle };
    return match chosen {
        Circle(value) => value.radius,
        Rectangle(value) => value.width,
    };
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Contains("enum Shape", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("identity<", compilation.MirText, StringComparison.Ordinal);
    }

    private static string Describe(CopelandCompilation compilation)
        => string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
}
