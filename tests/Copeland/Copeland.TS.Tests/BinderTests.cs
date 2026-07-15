using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using System.Text.RegularExpressions;
using Xunit;
using SemanticBinder = Copeland.TS.Semantics.Binder;

namespace Copeland.TS.Tests;

public sealed class BinderTests
{
    [Fact]
    public void Specializes_Generic_Bodies_Into_Concrete_Mir_Without_Open_Generic_Operations()
    {
        const string source = """
interface Positioned { x: number; y: number; }
record Point { x: number; y: number; }
function sum<T extends Positioned>(value: T): number { return value.x + value.y; }
const point: Point = { x: 20, y: 22 };
const answer: number = sum<Point>(point);
""";
        var compilation = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Matches(@"func sum__record_[A-Za-z0-9_]+__[0-9A-F]{16}\(value: Point\) -> number", compilation.MirText);
        Assert.Contains("record-get [r1]", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("Positioned", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeParameter", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Reuses_A_Closed_Generic_Instantiation_And_Rejects_Inference()
    {
        const string valid = "function identity<T>(value: T): T { return value; } const one: number = identity<number>(1); const two: number = identity<number>(2);";
        var specialized = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(valid);
        var inferred = SemanticBinder.Bind(SyntaxTree.Parse("function identity<T>(value: T): T { return value; } const value: number = identity(1);"));

        Assert.True(specialized.Success, string.Join(Environment.NewLine, specialized.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Single(Regex.Matches(specialized.MirText!, @"func identity__primitive_number__[0-9A-F]{16}\(").Cast<Match>());
        Assert.Contains(inferred.Diagnostics, diagnostic => diagnostic.Id == "COPE-GENERIC-0003");
    }

    [Fact]
    public void Closed_generic_instantiation_identity_is_stable_when_unrelated_declarations_are_inserted()
    {
        const string shared = """
interface Positioned { x: number; y: number; }
record Point { x: number; y: number; }
function sum<T extends Positioned>(value: T): number { return value.x + value.y; }
const point: Point = { x: 20, y: 22 };
const answer: number = sum<Point>(point);
""";
        const string withEarlierDeclaration = """
record Earlier { marker: number; }
""" + shared;

        var baseline = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(shared);
        var shifted = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(withEarlierDeclaration);

        Assert.True(baseline.Success, string.Join(Environment.NewLine, baseline.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(shifted.Success, string.Join(Environment.NewLine, shifted.Diagnostics.Select(diagnostic => diagnostic.Message)));

        string firstName = Assert.Single(Regex.Matches(baseline.MirText!, @"func (sum__[A-Za-z0-9_]+)\(").Cast<Match>()).Groups[1].Value;
        string secondName = Assert.Single(Regex.Matches(shifted.MirText!, @"func (sum__[A-Za-z0-9_]+)\(").Cast<Match>()).Groups[1].Value;
        Assert.Equal(firstName, secondName);
        Assert.DoesNotMatch(@"__r\d", firstName);
    }

    [Fact]
    public void Different_closed_type_arguments_produce_distinct_specializations()
    {
        const string source = """
function identity<T>(value: T): T { return value; }
const a: number = identity<number>(1);
const b: string = identity<string>("x");
""";

        var compilation = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(2, Regex.Matches(compilation.MirText!, @"func identity__[A-Za-z0-9_]+__[0-9A-F]{16}\(").Count);
    }

    [Fact]
    public void Nested_closed_type_arguments_get_deterministic_specialization_names()
    {
        const string source = """
function identity<T>(value: T): T { return value; }
const a: (number ! string)[] = identity<(number ! string)[]>([ok(1)]);
const b: (number ! string)[] = identity<(number ! string)[]>([ok(2)]);
""";

        var first = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(source);
        var second = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(source);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(second.Success, string.Join(Environment.NewLine, second.Diagnostics.Select(diagnostic => diagnostic.Message)));

        string firstName = Assert.Single(Regex.Matches(first.MirText!, @"func (identity__[A-Za-z0-9_]+)\(").Cast<Match>()).Groups[1].Value;
        string secondName = Assert.Single(Regex.Matches(second.MirText!, @"func (identity__[A-Za-z0-9_]+)\(").Cast<Match>()).Groups[1].Value;
        Assert.Equal(firstName, secondName);
    }

    [Fact]
    public void Generic_bodies_bind_once_and_requirements_do_not_expand_from_concrete_candidates()
    {
        const string source = """
interface Positioned { x: number; }
record RichPoint { x: number; name: string; }
function invalid<T extends Positioned>(value: T): string { return value.name; }
const value: string = invalid<RichPoint>({ x: 1, name: "ok" });
""";

        var bound = SemanticBinder.Bind(SyntaxTree.Parse(source));

        Assert.Contains(bound.Diagnostics, diagnostic => diagnostic.Id == "COPE-REQUIREMENT-0004");
    }

    [Fact]
    public void Open_generic_body_uses_requirement_field_access_before_closed_rewrite()
    {
        const string source = """
interface Positioned { x: number; }
function valid<T extends Positioned>(value: T): number { return value.x; }
""";

        BoundCompilation compilation = SemanticBinder.Bind(SyntaxTree.Parse(source));
        var genericBodies = SemanticBinder.BindOpenGenericBodiesForTesting(SyntaxTree.Parse(source));
        BoundFunctionDeclaration openBody = Assert.Single(genericBodies.Values);
        string dump = BoundTreeDumper.Dump(new BoundProgram([openBody], [], [], [], [], []));

        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REQUIREMENT-0004");
        Assert.Contains("RequirementFieldAccess T.x : number", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Closed_instantiation_rewrites_requirement_access_to_existing_table_row_access()
    {
        const string source = """
interface Positioned { x: number; }
record table Samples { x: [20]; y: [22]; }
function read<T extends Positioned>(value: T): number { return value.x; }
function main(): number { return read<Samples.Row>(Samples[0]!); }
""";

        var compilation = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("table-row-field [t1.row]", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("RequirementFieldAccess", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_resource_limits_are_frontend_diagnostics()
    {
        string tooManyTypeParameters = "function pack<T0, T1, T2, T3, T4, T5, T6, T7, T8>(value: T0): T0 { return value; }";
        string tooManyRequirements = """
interface I0 { x0: number; }
interface I1 { x1: number; }
interface I2 { x2: number; }
interface I3 { x3: number; }
interface I4 { x4: number; }
interface I5 { x5: number; }
interface I6 { x6: number; }
interface I7 { x7: number; }
interface I8 { x8: number; }
function use<T extends I0 & I1 & I2 & I3 & I4 & I5 & I6 & I7 & I8>(value: T): number { return value.x0; }
""";

        var typeParameterCompilation = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(tooManyTypeParameters);
        var requirementCompilation = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(tooManyRequirements);

        Assert.Contains(typeParameterCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-GENERIC-0011");
        Assert.Contains(requirementCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REQUIREMENT-0009");
        Assert.Null(typeParameterCompilation.MirCompilation);
        Assert.Null(requirementCompilation.MirCompilation);
    }

    [Fact]
    public void Excessive_closed_type_nesting_is_bounded_before_specialization()
    {
        string nested = "number";
        for (var index = 0; index < 17; index++)
        {
            nested = "(" + nested + " ! string)";
        }

        string source = $"function identity<T>(value: T): T {{ return value; }} const value: {nested} = identity<{nested}>(err(\"bad\"));";
        var compilation = Copeland.TS.Compiler.CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-GENERIC-0015");
        Assert.Null(compilation.MirCompilation);
    }

    [Fact]
    public void Binds_Result_Unwrap_Without_A_Fallible_Return_Target()
    {
        var bound = SemanticBinder.Bind(SyntaxTree.Parse("function unwrap(value: number ! string): number { return value!; }"));

        Assert.DoesNotContain(bound.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-", StringComparison.Ordinal));
        Assert.Contains("UnwrapExpression ! : number", BoundTreeDumper.Dump(bound.Program), StringComparison.Ordinal);
    }

    [Fact]
    public void Binds_Try_Except_With_Inferred_Handler_Error_And_Lexical_Target()
    {
        var bound = SemanticBinder.Bind(SyntaxTree.Parse("function read(): number ! string { return err(\"bad\"); } function main(): number { return try { read()? } except (error) { 42 }; }"));

        Assert.DoesNotContain(bound.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-", StringComparison.Ordinal));
        var dump = BoundTreeDumper.Dump(bound.Program);
        Assert.Contains("TryExceptExpression h1 error string : number", dump, StringComparison.Ordinal);
        Assert.Contains("LexicalExcept", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_Except_Requires_A_Targeted_Propagation_And_Agrees_On_Error_Type()
    {
        var empty = SemanticBinder.Bind(SyntaxTree.Parse("function main(): number { return try { 1 } except (error) { 2 }; }"));
        var mismatch = SemanticBinder.Bind(SyntaxTree.Parse("function one(): number ! string { return err(\"x\"); } function two(): number ! number { return err(2); } function main(): number { return try { one()?; two()? } except (error) { 0 }; }"));

        Assert.Contains(empty.Diagnostics, diagnostic => diagnostic.Id == "COPE-TRY-0004");
        Assert.Contains(mismatch.Diagnostics, diagnostic => diagnostic.Id == "COPE-TRY-0003");
    }

    [Fact]
    public void Rejects_Result_Unwrap_On_Non_Result()
    {
        var bound = SemanticBinder.Bind(SyntaxTree.Parse("function invalid(): number { return 1!; }"));

        Assert.Contains(bound.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0019");
    }

    [Fact]
    public void Binds_Typed_Variable_And_Assignment()
    {
        var tree = SyntaxTree.Parse("let x: number = 1; x = 2;");
        var bound = SemanticBinder.Bind(tree);
        Assert.DoesNotContain(bound.Diagnostics, d => d.Id.StartsWith("COPE-BIND") || d.Id.StartsWith("COPE-TYPE") || d.Id.StartsWith("COPE-PROFILE"));
        var dump = BoundTreeDumper.Dump(bound.Program);
        Assert.Contains("VariableDeclaration let x: number", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_Assignment_To_Const()
    {
        var tree = SyntaxTree.Parse("const x: number = 1; x = 2;");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-BIND-0003");
    }

    [Fact]
    public void Reports_Missing_Type_Annotation()
    {
        var tree = SyntaxTree.Parse("let x = 1;");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-TYPE-0002");
    }

    [Fact]
    public void Profile_Rejects_Parsed_Var_Declaration()
    {
        var tree = SyntaxTree.Parse("var value: number = 1;");
        var bound = SemanticBinder.Bind(tree);

        Assert.DoesNotContain(tree.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        Assert.Contains(bound.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0001");
    }

    [Theory]
    [InlineData("===")]
    [InlineData("!==")]
    public void Profile_Rejects_Strict_Equality_Spellings(string equalityOperator)
    {
        var tree = SyntaxTree.Parse($"function equal(left: number, right: number): boolean {{ return left {equalityOperator} right; }}");
        var bound = SemanticBinder.Bind(tree);

        Assert.DoesNotContain(tree.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        Assert.Contains(bound.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0009");
    }

    [Theory]
    [InlineData("function equal(): boolean { return [1] == [1]; }")]
    [InlineData("""
enum Choice { Yes, No, }
function equal(): boolean { return Choice.Yes == Choice.No; }
""")]
    public void Equality_Does_Not_Accidentally_Admit_Unsupported_Value_Families(string source)
    {
        var bound = SemanticBinder.Bind(SyntaxTree.Parse(source));

        Assert.Contains(bound.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0007");
    }

    [Fact]
    public void Match_Duplicate_Arm_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Choice { A, B, }
function value(choice: Choice): number {
  return match choice {
    A => 1,
    A => 2,
    B => 3,
  };
}
""");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0003");
    }

    [Fact]
    public void Match_Non_Exhaustive_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Choice { A, B, C, }
function value(choice: Choice): number {
  return match choice {
    A => 1,
    B => 2,
  };
}
""");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0004");
    }

    [Fact]
    public void Match_Payload_Arity_And_Duplicate_Name_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Shape { Rect(width: number, height: number), }
function value(shape: Shape): number {
  return match shape {
    Rect(x, x) => x,
  };
}
""");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0006");
    }

    [Fact]
    public void Match_Payload_Arity_Mismatch_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Shape { Rect(width: number, height: number), }
function value(shape: Shape): number {
  return match shape {
    Rect(width) => width,
  };
}
""");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0005");
    }

    [Fact]
    public void Match_Arm_Type_Mismatch_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Choice { A, B, }
function value(choice: Choice): number {
  return match choice {
    A => 1,
    B => "bad",
  };
}
""");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0007");
    }

    [Fact]
    public void Match_Payload_Variable_Does_Not_Leak()
    {
        var tree = SyntaxTree.Parse("""
enum Shape { Circle(radius: number), }
function value(shape: Shape): number {
  const x: number = match shape {
    Circle(radius) => radius,
  };
  return radius;
}
""");
        var bound = SemanticBinder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-BIND-0001");
    }

    [Fact]
    public void If_Expression_Invalid_Cases_Report()
    {
        var nonBool = SemanticBinder.Bind(SyntaxTree.Parse("function value(x: number): number { return if x { 1 } else { 2 }; }"));
        Assert.Contains(nonBool.Diagnostics, d => d.Id == "COPE-TYPE-0017");

        var mismatch = SemanticBinder.Bind(SyntaxTree.Parse("function value(flag: boolean): number { return if flag { 1 } else { \"bad\" }; }"));
        Assert.Contains(mismatch.Diagnostics, d => d.Id == "COPE-TYPE-0018");
    }

    [Fact]
    public void Profile_Bans_For_Ternary_And_Optional_Chaining_Report()
    {
        var ternary = SemanticBinder.Bind(SyntaxTree.Parse("function value(flag: boolean): number { return flag ? 1 : 2; }"));
        Assert.Contains(ternary.Diagnostics, d => d.Id == "COPE-PROFILE-0007");

        var optional = SemanticBinder.Bind(SyntaxTree.Parse("function value(x: number): number { return x?.toString(); }"));
        Assert.Contains(optional.Diagnostics, d => d.Id == "COPE-PROFILE-0008");
    }

}
