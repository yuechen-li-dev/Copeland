using System.Text;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TypeAliasTests
{
    [Fact]
    public void Parser_recognizes_contextual_compilation_unit_alias_declaration()
    {
        var tree = SyntaxTree.Parse("type UserId = number;");

        var alias = Assert.IsType<TypeAliasDeclarationSyntax>(Assert.Single(tree.Root.Members));
        Assert.Equal("type", alias.TypeKeyword.Text);
        Assert.Equal("UserId", alias.Identifier.Text);
        Assert.IsType<PredefinedTypeSyntax>(alias.TargetType);
        Assert.Empty(tree.Diagnostics);
    }

    [Theory]
    [InlineData("type = number;", "COPE-ALIAS-0001")]
    [InlineData("type Name number;", "COPE-ALIAS-0001")]
    [InlineData("type Name = ;", "COPE-ALIAS-0001")]
    [InlineData("type Name = number", "COPE-ALIAS-0001")]
    [InlineData("type Box<T> = T[];", "COPE-ALIAS-0002")]
    [InlineData("type Name = keyof number;", "COPE-ALIAS-0001")]
    public void Parser_recovers_malformed_and_unsupported_aliases_without_general_parse_cascades(
        string source,
        string diagnosticId)
    {
        var tree = SyntaxTree.Parse(source);

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.DoesNotContain(tree.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        Assert.All(tree.Diagnostics, diagnostic => Assert.True(diagnostic.Length > 0));
    }

    [Fact]
    public void Aliases_are_transparent_across_expected_types_functions_records_enums_arrays_results_and_equality()
    {
        const string source = """
type UserId = NumericId;
type NumericId = number;
type UserAlias = User;
type Users = User[];
type ParseResult = User ! string;

record User {
    id: UserId;
    name: string;
}

enum Event {
    Created(user: UserAlias),
}

function identity(id: UserId): number {
    const raw: number = id;
    return raw;
}

function make(): UserAlias {
    return { id: 42, name: "Ada" };
}

function parse(): ParseResult {
    return ok({ id: 42, name: "Ada" });
}

function same(left: UserId, right: NumericId): boolean {
    return left == right;
}

function all(): Users {
    return [make()];
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Empty(compilation.Diagnostics);
        Assert.DoesNotContain("UserId", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("NumericId", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("UserAlias", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("Users", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("ParseResult", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Alias_and_direct_programs_have_identical_canonical_mir_and_tson_nominal_identity()
    {
        const string aliasSource = """
const $schema: string = "copeland://alias-proof/v1";
type SettingsAlias = Settings;
record Settings {
    enabled: boolean;
}
function make(): SettingsAlias {
    return { enabled: true };
}
function encode(settings: SettingsAlias): string ! TsonEncodeError {
    return tsonEncode(settings);
}
""";
        const string directSource = """
const $schema: string = "copeland://alias-proof/v1";
record Settings {
    enabled: boolean;
}
function make(): Settings {
    return { enabled: true };
}
function encode(settings: Settings): string ! TsonEncodeError {
    return tsonEncode(settings);
}
""";

        var aliasCompilation = CopelandCompiler.CompileToMir(aliasSource);
        var directCompilation = CopelandCompiler.CompileToMir(directSource);

        Assert.True(aliasCompilation.Success, Describe(aliasCompilation));
        Assert.True(directCompilation.Success, Describe(directCompilation));
        Assert.Equal(directCompilation.MirText, aliasCompilation.MirText);
        Assert.Contains("copeland://alias-proof/v1#Settings", aliasCompilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsAlias", aliasCompilation.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type Name = number; type Name = string;")]
    [InlineData("record Name { value: number; } type Name = number;")]
    [InlineData("enum Name { Value, } type Name = number;")]
    [InlineData("record table Name { value: [1]; } type Name = number;")]
    [InlineData("type Name = number; record Name { value: number; }")]
    public void Alias_collisions_share_the_compilation_unit_type_namespace(string source)
    {
        var compilation = CopelandCompiler.Compile(source, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
        });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-ALIAS-0003");
    }

    [Fact]
    public void Alias_names_are_case_sensitive_and_do_not_enter_value_scope()
    {
        const string valid = "type Value = number; type value = string; function Value(): number { return 1; } function main(): number { return Value(); }";
        const string invalid = "type Value = number; function main(): number { return Value; }";

        Assert.True(CopelandCompiler.CompileToMir(valid).Success);
        var invalidCompilation = CopelandCompiler.CompileToMir(invalid);
        Assert.Contains(invalidCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-ALIAS-0006");
        Assert.DoesNotContain(invalidCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-BIND-0001");
    }

    [Fact]
    public void Unknown_alias_target_reports_once_and_suppresses_dependent_cascades()
    {
        const string source = "type A = Missing; type B = A[]; function value(): B { return []; }";

        var compilation = CopelandCompiler.Compile(source, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
        });

        var diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == "COPE-ALIAS-0004");
        Assert.Contains("Missing", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(diagnostic.Length > 0);
    }

    [Theory]
    [InlineData("type A = A;", "A -> A")]
    [InlineData("type A = B; type B = A;", "A -> B -> A")]
    [InlineData("type A = B; type B = C; type C = A;", "A -> B -> C -> A")]
    public void Alias_cycles_report_one_deterministic_declaration_order_path(string source, string path)
    {
        var first = CopelandCompiler.Compile(source, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
        });
        var second = CopelandCompiler.Compile(source, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
        });

        var diagnostic = Assert.Single(first.Diagnostics, item => item.Id == "COPE-ALIAS-0005");
        Assert.Contains(path, diagnostic.Message, StringComparison.Ordinal);
        Assert.True(diagnostic.Length > 0);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public void Long_alias_chain_resolves_without_recursive_graph_traversal()
    {
        const int aliasCount = 5000;
        var source = new StringBuilder();
        for (var index = 0; index < aliasCount - 1; index++)
        {
            source.Append("type A").Append(index).Append(" = A").Append(index + 1).AppendLine(";");
        }

        source.Append("type A").Append(aliasCount - 1).AppendLine(" = number;");
        source.AppendLine("function value(input: A0): number { return input; }");

        var compilation = CopelandCompiler.CompileToMir(source.ToString());

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Contains("input: number", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_alias_cycle_is_non_recursive_and_has_a_bounded_path()
    {
        const int aliasCount = 5000;
        var source = new StringBuilder();
        for (var index = 0; index < aliasCount - 1; index++)
        {
            source.Append("type C").Append(index).Append(" = C").Append(index + 1).AppendLine(";");
        }

        source.Append("type C").Append(aliasCount - 1).AppendLine(" = C0;");

        var compilation = CopelandCompiler.Compile(source.ToString(), new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
        });

        var diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == "COPE-ALIAS-0005");
        Assert.Contains("C0 -> C1", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("-> ... -> C0", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(diagnostic.Message.Length < 256);
    }

    [Fact]
    public void Void_alias_obeys_canonical_position_legality()
    {
        const string source = "type Nothing = void; function invalid(value: Nothing): number { return 1; }";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0020");
        Assert.Null(compilation.MirText);
    }

    [Fact]
    public void Type_mismatch_preserves_direct_authored_alias_provenance()
    {
        const string source = "type UserId = number; function invalid(): number { const id: UserId = \"bad\"; return 1; }";

        var compilation = CopelandCompiler.CompileToMir(source);

        var diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == "COPE-TYPE-0001");
        Assert.Contains("'UserId' (alias of 'number')", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_mir_assembly_has_no_alias_node_or_declaration_type()
    {
        Type[] mirTypes = typeof(MirProgram).Assembly.GetTypes();

        Assert.DoesNotContain(mirTypes, type => type.Name.Contains("Alias", StringComparison.Ordinal));
    }

    private static string Describe(CopelandCompilation compilation)
    {
        return string.Join(
            Environment.NewLine,
            compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
    }
}
