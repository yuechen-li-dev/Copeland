using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using System.Security.Cryptography;
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

    public static IEnumerable<object[]> ReachableUnionDiagnostics()
    {
        yield return DiagnosticCase(
            "COPE-UNION-0003",
            """
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
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0004",
            """
            record Circle { radius: number; }
            type Shape = Circle | Circle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0005",
            """
            record Shape { value: number; }
            record Circle { radius: number; }
            record Rectangle { width: number; }
            type Shape = Circle | Rectangle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0006",
            """
            record Circle { radius: number; }
            record Rectangle { width: number; }
            type Round = Circle;
            type Shape = Round | Rectangle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0007",
            """
            record Circle { radius: number; }
            enum Existing { Value, }
            type Shape = Existing | Circle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0007",
            """
            record Circle { radius: number; }
            interface Required { value: number; }
            type Shape = Required | Circle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0007",
            """
            record Circle { radius: number; }
            record table Samples { value: [1]; }
            type Shape = Samples | Circle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0007",
            """
            record Circle { radius: number; }
            record Rectangle { width: number; }
            type Inner = Circle | Rectangle;
            type Outer = Inner | Circle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0008",
            """
            record Circle { radius: number; }
            type Shape = Missing | Circle;
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0009",
            """
            record Circle { radius: number; }
            record Rectangle { width: number; }
            record Triangle { side: number; }
            type Shape = Circle | Rectangle;
            function bad(): Shape {
                const triangle: Triangle = { side: 3 };
                return triangle;
            }
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0010",
            """
            record Circle { radius: number; }
            record Rectangle { width: number; }
            type Shape = Circle | Rectangle;
            function bad(shape: Shape): Circle {
                return shape;
            }
            """);
        yield return DiagnosticCase(
            "COPE-UNION-0011",
            """
            record Circle { radius: number; }
            record Rectangle { width: number; }
            type Shape = Circle | Rectangle;
            type OtherShape = Circle | Rectangle;
            function bad(shape: Shape): OtherShape {
                return shape;
            }
            """);
    }

    [Theory]
    [MemberData(nameof(ReachableUnionDiagnostics))]
    public void Reachable_union_diagnostics_have_focused_coverage_and_nonempty_spans(string diagnosticId, string source)
    {
        var compilation = CompileToBound(source);

        var diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == diagnosticId);
        Assert.True(diagnostic.Length > 0);
        Assert.True(diagnostic.Position >= 0);
        Assert.DoesNotContain("C:\\", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Primitive_alternatives_are_rejected_at_the_parser_boundary_with_union_owned_syntax_diagnostics()
    {
        var tree = SyntaxTree.Parse("""
            record Circle { radius: number; }
            type Shape = number | Circle;
            """);

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "COPE-UNION-0001");
        Assert.All(tree.Diagnostics, diagnostic => Assert.True(diagnostic.Length > 0));
    }

    [Fact]
    public void Nominal_union_lowers_as_a_declaration_ordered_payload_enum_with_fixed_value_payload_names()
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
        MirProgram program = Assert.IsType<MirProgram>(compilation.MirCompilation!.Program);
        MirEnum union = Assert.Single(program.Enums);
        Assert.Equal("Shape", union.Name);
        Assert.Equal(["Circle", "Rectangle"], union.Cases.Select(@case => @case.Name));
        Assert.All(union.Cases, @case =>
        {
            MirEnumPayloadField payload = Assert.Single(@case.PayloadFields);
            Assert.Equal("value", payload.Name);
        });
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
    public void Nominal_union_reuses_existing_record_cycle_diagnostics_for_direct_and_indirect_containment()
    {
        const string direct = """
record Node { child: Tree; }
record Leaf { value: number; }
type Tree = Node | Leaf;
""";
        const string indirect = """
record Branch { left: Tree; }
record TreeLeaf { node: Node; }
type Tree = Branch | TreeLeaf;
record Node { child: Tree; }
""";

        var directCompilation = CopelandCompiler.CompileToMir(direct);
        var indirectCompilation = CopelandCompiler.CompileToMir(indirect);

        Assert.Contains(directCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REC-0004");
        Assert.Contains(indirectCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REC-0004");
        Assert.Null(directCompilation.MirText);
        Assert.Null(indirectCompilation.MirText);
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
    public void Nominal_union_and_equivalent_payload_enum_lower_to_equivalent_mir()
    {
        const string unionSource = """
record Circle { radius: number; }
record Rectangle { width: number; height: number; }
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
        const string enumSource = """
record Circle { radius: number; }
record Rectangle { width: number; height: number; }
enum Shape {
    Circle(value: Circle),
    Rectangle(value: Rectangle),
}
function area(): number {
    const circle: Circle = { radius: 4 };
    const shape: Shape = Shape.Circle(circle);
    return match shape {
        Circle(value) => value.radius * value.radius,
        Rectangle(value) => value.width * value.height,
    };
}
""";

        var unionCompilation = CopelandCompiler.CompileToMir(unionSource);
        var enumCompilation = CopelandCompiler.CompileToMir(enumSource);

        Assert.True(unionCompilation.Success, Describe(unionCompilation));
        Assert.True(enumCompilation.Success, Describe(enumCompilation));
        Assert.Equal(enumCompilation.MirText, unionCompilation.MirText);
    }

    [Fact]
    public void Nominal_union_reuses_enum_tson_identity_and_payload_identities()
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

        var tsonCompilation = CopelandCompiler.CompileToMir(tsonSource);

        Assert.True(tsonCompilation.Success, Describe(tsonCompilation));
        MirTsonEncodingPlan plan = Assert.Single(tsonCompilation.MirCompilation!.Program!.TsonEncodingPlans);
        Assert.Equal("copeland://union-proof/v1", plan.SchemaIdentity);
        MirTsonEnumPlan enumPlan = Assert.IsType<MirTsonEnumPlan>(Assert.Single(plan.Definitions.OfType<MirTsonEnumPlan>()));
        Assert.Equal("Shape", enumPlan.Name);
        Assert.Equal("copeland://union-proof/v1#Shape", enumPlan.StableIdentity);
        Assert.Equal(["Circle", "Rectangle"], enumPlan.Cases.Select(@case => @case.Name));
        Assert.Equal(
            ["copeland://union-proof/v1#Shape.Circle", "copeland://union-proof/v1#Shape.Rectangle"],
            enumPlan.Cases.Select(@case => @case.StableIdentity));
        Assert.Equal(
            ["copeland://union-proof/v1#Shape.Circle.value", "copeland://union-proof/v1#Shape.Rectangle.value"],
            enumPlan.Cases.Select(@case => Assert.Single(@case.Payloads).StableIdentity));
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

    [Fact]
    public void Nominal_union_corpus_artifacts_have_stable_bytes_and_hashes()
    {
        string corpusRoot = Path.Combine(
            Corpus.CorpusFile.GetRepoRoot(),
            "tests",
            "Copeland",
            "Copeland.TS.Tests",
            "TestData",
            "Corpus",
            "cts-union-m0b");
        var expected = new Dictionary<string, (int Length, string Sha256)>(StringComparer.Ordinal)
        {
            ["nominal-union.ts"] = (357, "FE2E63779FDC9C6B2497C1E43B79446212F6CCDB5B3A8D49571F263B02361296"),
            ["nominal-union.cope"] = (608, "69CEDD1030B756AFC481942309E7BF85D4E5AAEB7E636B5C0645EC364C051033"),
            ["nominal-union.g.cs"] = (1268, "56EDE4777585B3886F37F86B48332556AFE78CEEA86AEF5EBDC2BA43AF2BC34C"),
            ["nominal-union.g.js"] = (6272, "BBAAA7FA856306904D74F64947A072BFA80958A46DA8C8E274660E7ABB37AAEC"),
        };

        foreach ((string fileName, (int expectedLength, string expectedHash)) in expected)
        {
            string path = Path.Combine(corpusRoot, fileName);
            byte[] bytes = File.ReadAllBytes(path);

            Assert.Equal(expectedLength, bytes.Length);
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
        }
    }

    private static object[] DiagnosticCase(string diagnosticId, string source)
    {
        return [diagnosticId, source];
    }

    private static CopelandCompilation CompileToBound(string source)
    {
        return CopelandCompiler.Compile(
            source,
            new CopelandCompilationOptions
            {
                TargetStage = CopelandCompilationStage.Bound,
            });
    }

    private static string Describe(CopelandCompilation compilation)
        => string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
}
