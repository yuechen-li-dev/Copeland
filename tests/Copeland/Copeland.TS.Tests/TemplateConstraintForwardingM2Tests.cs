using Copeland.TS.Compiler;
using Copeland.TS.Templates;
using System.Text;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TemplateConstraintForwardingM2Tests
{
    [Fact]
    public void Equivalent_constraint_evidence_survives_nested_forwarding_and_reflection()
    {
        const string source = """
            interface Named { name: string; }
            record Person { name: string; }

            template<type T extends Named> Inner: ProjectTree {
                static for (const field of reflect fieldsOf<T>()) {
                    emit(textFile(`${reflect nameOf<T>()}-${field.name}.txt`, field.typeName));
                }
            }
            template<type T extends Named> Middle: ProjectTree {
                return instantiate Inner<T>;
            }
            template<type T extends Named = Person> Outer: ProjectTree {
                return instantiate Middle<T>;
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Outer");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        TextFileArtifact file = Assert.IsType<TextFileArtifact>(Assert.Single(result.Project!.Files));
        Assert.Equal("Person-name.txt", file.Path);
        Assert.Equal("string", Encoding.UTF8.GetString(file.Bytes));
        string deepestChain = Assert.Single(
            result.InstantiationChain,
            chain => chain.Contains("Inner", StringComparison.Ordinal));
        Assert.Contains("Outer", deepestChain, StringComparison.Ordinal);
        Assert.Contains("Middle", deepestChain, StringComparison.Ordinal);
    }

    [Fact]
    public void Stronger_constraint_evidence_entails_a_weaker_inner_requirement()
    {
        const string source = """
            interface Named { name: string; }
            interface Versioned { version: int; }
            record Package { name: string; version: int; }

            template<type T extends Named> Inner: ProjectTree {
                emit(textFile("accepted.txt", reflect nameOf<T>()));
            }
            template<type T extends Named & Versioned = Package> Outer: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Outer");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("Package", Encoding.UTF8.GetString(Assert.Single(result.Project!.Files).Bytes));
    }

    [Fact]
    public void Weaker_constraint_evidence_does_not_entail_a_stronger_inner_requirement()
    {
        const string source = """
            interface Named { name: string; }
            interface Versioned { version: int; }
            record Person { name: string; }

            template<type T extends Named & Versioned> Inner: ProjectTree {
                emit(textFile("invalid.txt", "invalid"));
            }
            template<type T extends Named = Person> Outer: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-REQUIREMENT-0011", diagnostic.Id);
        Assert.Equal(source.IndexOf("Inner<T>", StringComparison.Ordinal) + "Inner".Length, diagnostic.Position);
        Assert.Equal(1, diagnostic.Length);
        Assert.Equal(
            "Template argument 'T' does not satisfy constraint 'Named & Versioned' required by 'Inner': missing version: int. Known constraints for 'T': Named.",
            diagnostic.Message);
    }

    [Fact]
    public void Unconstrained_type_parameter_cannot_cross_a_constrained_boundary()
    {
        const string source = """
            interface Named { name: string; }
            record Person { name: string; }

            template<type T extends Named> Inner: ProjectTree {
                emit(textFile("invalid.txt", "invalid"));
            }
            template<type T = Person> Outer: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-REQUIREMENT-0011", diagnostic.Id);
        Assert.Contains("Known constraints for 'T': none", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Concrete_primitive_still_fails_the_outer_structural_constraint()
    {
        const string source = """
            interface Named { name: string; }

            template<type T extends Named> Inner: ProjectTree {
                emit(textFile("invalid.txt", "invalid"));
            }
            template<type T extends Named = int> Outer: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REQUIREMENT-0005");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0008");
    }

    [Theory]
    [InlineData("record Person { name: string; }", "Person")]
    [InlineData(
        "class Person { name: string; constructor(name: string): Person { return { name }; } }",
        "Person")]
    [InlineData("type Person = { name: string; };", "Person")]
    [InlineData("record table People { name: [\"Ada\"]; }", "People.Row")]
    public void Supported_structural_carriers_forward_through_the_same_constraint_path(
        string declaration,
        string typeName)
    {
        string source = $$"""
            interface Named { name: string; }
            {{declaration}}

            template<type T extends Named> Inner: ProjectTree {
                emit(textFile("carrier.txt", reflect nameOf<T>()));
            }
            template<type T extends Named = {{typeName}}> Outer: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Outer");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Single(result.Project!.Files);
    }

    [Fact]
    public void Equivalent_structural_alias_constraints_forward_by_normalized_fields()
    {
        const string source = """
            type NamedShape = { name: string; };
            type NamedAlias = NamedShape;
            type EquivalentNamedShape = { name: string; };
            record Person { name: string; }

            template<type T extends EquivalentNamedShape> Inner: ProjectTree {
                emit(textFile("alias.txt", reflect nameOf<T>()));
            }
            template<type T extends NamedAlias = Person> Outer: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Outer");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Single(result.Project!.Files);
    }

    [Fact]
    public void Forwarded_concrete_type_identity_preserves_template_memoization()
    {
        const string source = """
            interface Named { name: string; }
            record Person { name: string; }

            template<type T extends Named> Inner: string {
                return reflect nameOf<T>();
            }
            template<type T extends Named = Person> Outer: ProjectTree {
                emit(textFile("first.txt", instantiate Inner<T>));
                emit(textFile("second.txt", instantiate Inner<T>));
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Outer");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(1, result.InstantiationChain.Count(chain => chain.Contains("Inner", StringComparison.Ordinal)));
        Assert.Equal(
            result.Project!.Files.Single(file => file.Path == "first.txt").Bytes,
            result.Project.Files.Single(file => file.Path == "second.txt").Bytes);
    }
}
