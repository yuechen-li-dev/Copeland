using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;
using CopelandSyntaxTree = Copeland.TS.Syntax.SyntaxTree;
using RoslynCSharpCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation;

namespace Copeland.TS.Tests;

public sealed class ClrInteropTests
{
    [Fact]
    public void Parser_Distinguishes_ClrUsing_And_ResourceUsing()
    {
        var tree = CopelandSyntaxTree.Parse("""
using System.IO;
function read(path: string): string {
    using reader = new StreamReader(path);
    return reader.ReadToEnd();
}
""", "sample.ts");

        Assert.Empty(tree.Diagnostics);
        Assert.IsType<ClrUsingDirectiveSyntax>(tree.Root.Members[0]);
        var function = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[1]);
        Assert.IsType<ResourceUsingDeclarationStatementSyntax>(function.Body.Statements[0]);
    }

    [Fact]
    public void Emits_And_Executes_Direct_Framework_Clr_Calls()
    {
        const string source = """
using System.IO;
using System.Text.Json;

record Person {
    name: string;
}

function run(path: string, person: Person): string {
    using reader = new StreamReader(path);
    const prefix: string = reader.ReadLine();
    const json: string = JsonSerializer.Serialize(person);
    return prefix + json;
}
""";

        var compilation = CopelandCompiler.CompileToMir(source, new CopelandCompilationOptions { SourcePath = "sample.ts" });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("using var reader = new global::System.IO.StreamReader(path);", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("global::System.Text.Json.JsonSerializer.Serialize<", emitted.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("dynamic", emitted.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetMethod", emitted.SourceText, StringComparison.Ordinal);

        using var assemblyStream = new MemoryStream();
        RoslynCSharpCompilation csharp = RoslynCSharpCompilation.Create(
            "CopelandClrInteropProof",
            [CSharpSyntaxTree.ParseText(emitted.SourceText)],
            GetRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        EmitResult result = csharp.Emit(assemblyStream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(assemblyStream.ToArray());
        Type module = assembly.GetType("Copeland.Generated.CopelandModule")!;
        Type person = assembly.GetTypes().Single(type => type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal));
        object value = Activator.CreateInstance(person, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, binder: null, ["Ada"], culture: null)!;
        string filePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filePath, "prefix:");
            string output = (string)module.GetMethod("run")!.Invoke(null, [filePath, value])!;
            Assert.Equal("prefix:{\"name\":\"Ada\"}", output);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Selects_Number_To_Double_Overload_Deterministically()
    {
        var compilation = CopelandCompiler.CompileToMir("""
using System;
function rounded(value: number): number { return Math.Round(value); }
""", new CopelandCompilationOptions { SourcePath = "sample.ts" });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("global::System.Math.Round(value)", emitted.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_Clr_Specific_Failures_Without_Npm_Fallback()
    {
        var missing = CopelandCompiler.CompileToMir("using System.DoesNotExist;", new CopelandCompilationOptions { SourcePath = "sample.ts" });
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "COPE-CLR-0001");

        var unsupported = CopelandCompiler.CompileToMir("""
using System;
function value(): number { return Math.Round("not a number"); }
""", new CopelandCompilationOptions { SourcePath = "sample.ts" });
        Assert.Contains(unsupported.Diagnostics, diagnostic => diagnostic.Id == "COPE-CLR-0005");
    }

    [Fact]
    public void AwaitUsing_Is_Parsed_And_Explicitly_Deferred()
    {
        var compilation = CopelandCompiler.CompileToMir("""
using System.IO;
async function read(path: string): Async<string> {
    await using reader = new StreamReader(path);
    return reader.ReadToEnd();
}
""", new CopelandCompilationOptions { SourcePath = "sample.ts" });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CLR-0008");
    }

    private static IEnumerable<MetadataReference> GetRuntimeReferences()
    {
        string trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}
