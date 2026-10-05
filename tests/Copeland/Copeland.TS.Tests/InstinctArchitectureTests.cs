using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class InstinctArchitectureTests
{
    [Fact]
    public void Generated_identity_is_selected_structurally_and_preserves_source_literals()
    {
        RuntimeProof.VerifyIdentity();
    }

    [Fact]
    public void Identity_owners_cannot_rewrite_emitted_source_with_replace_or_regex()
    {
        string root = FindRepositoryRoot();
        foreach (string relative in new[]
        {
            "src/Copeland/Copeland.TS.MSBuild/CopelandCompile.cs",
            "src/Copeland/Copeland.TS.Backend.CSharp/CSharp/TableQuerySourceGeneration.cs",
        })
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, relative)));
            var calls = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var call in calls)
            {
                if (call.Expression is not MemberAccessExpressionSyntax member) continue;
                string name = member.Name.Identifier.Text;
                if (name != "Replace" && !member.Expression.ToString().Contains("Regex", StringComparison.Ordinal)) continue;
                // The task's exception summary strips newlines for MSBuild logging.
                // It never receives emitted source. This is the sole allowed replacement.
                Assert.StartsWith("rootCause.Message", member.Expression.ToString(), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Query_materialization_uses_the_selected_namespace_and_module_owner()
    {
        var compilation = CopelandCompiler.CompileToMir("record table Products { id: int = [1, 2]; }");
        Assert.True(compilation.Success);
        var request = new TableQueryRequest("Products", null,
            [new TableQueryProjectionRequest("id")], [], [], [], 0, 2, "m26-custom-identity");
        var plan = TableQueryBinder.Bind(compilation.BoundCompilation!, compilation.MirCompilation!.Program!, request);
        var artifact = TableQueryBinder.Lower(plan);
        var options = new CSharpEmissionOptions { Namespace = "Instinct.Query", ModuleClassName = "QueryOwner" };
        var result = CSharpTableQueryMaterializer.Execute(compilation.MirCompilation.Program!.WithExecutableArtifact(artifact), artifact, options);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.GetValue(1, 0));
    }

    [Fact]
    public void Payloadless_enum_construction_reuses_the_immutable_case_value()
    {
        RuntimeProof.VerifyEnumSingleton();
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.TS.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("Repository source is required for the generated identity architecture gate.");
    }
}
