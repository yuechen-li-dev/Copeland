using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;
using RoslynCSharpCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation;
using CopelandSyntaxTree = Copeland.TS.Syntax.SyntaxTree;

namespace Copeland.TS.Tests;

public sealed class InlineCSharpBlockTests
{
    [Fact]
    public void Inline_CSharp_Uses_Typed_Captures_And_Emits_Direct_Code()
    {
        var compilation = CopelandCompiler.CompileToMir("""
using System;
function Format(name: string, count: number): string {
    csharp {
        var punctuation = "}";
        return $"{name}:{count}" + punctuation;
    }
}
""", new CopelandCompilationOptions { SourcePath = "inline.ts" });

        Assert.True(compilation.Success, FormatDiagnostics(compilation));
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("var punctuation = \"}\";", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("return $\"{name}:{count}\" + punctuation;", emitted.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("dynamic", emitted.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CSharpScript", emitted.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetMethod", emitted.SourceText, StringComparison.Ordinal);

        using var assembly = new MemoryStream();
        EmitResult result = RoslynCSharpCompilation.Create(
            "InlineCSharpProof",
            [CSharpSyntaxTree.ParseText(emitted.SourceText)],
            GetRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(assembly);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void Inline_CSharp_Rejects_Captured_Assignment_And_Async()
    {
        var assignment = CopelandCompiler.CompileToMir("""
function Change(value: number): number {
    csharp { value = 2; return value; }
}
""", new CopelandCompilationOptions { SourcePath = "inline.ts" });
        Assert.Contains(assignment.Diagnostics, diagnostic => diagnostic.Id == "COPE-CSHARP-0005");

        var async = CopelandCompiler.CompileToMir("""
function Value(value: number): number {
    csharp { await Task.Yield(); return value; }
}
""", new CopelandCompilationOptions { SourcePath = "inline.ts" });
        Assert.Contains(async.Diagnostics, diagnostic => diagnostic.Id == "COPE-CSHARP-0003");
    }

    [Fact]
    public void Inline_CSharp_Is_Unavailable_To_JavaScript()
    {
        var compilation = CopelandCompiler.CompileToMir("""
function Value(value: number): number {
    csharp { return value + 1; }
}
""");

        Assert.True(compilation.Success, FormatDiagnostics(compilation));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Contains(emitted.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-CSHARP-0001");
    }

    [Fact]
    public void Parser_Preserves_Braces_In_Comments_And_Raw_CSharp_Strings()
    {
        var tree = CopelandSyntaxTree.Parse(
            "function Value(value: string): string {\n"
            + "    csharp {\n"
            + "        // } ignored\n"
            + "        var json = \"\"\"{ \\\"value\\\": \\\"}\\\" }\"\"\";\n"
            + "        return value + json;\n"
            + "    }\n"
            + "}\n",
            "inline.ts");

        Assert.Empty(tree.Diagnostics);
        var function = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[0]);
        var block = Assert.IsType<CSharpBlockStatementSyntax>(Assert.Single(function.Body.Statements));
        Assert.Contains("var json", block.BodyText, StringComparison.Ordinal);
    }

    private static string FormatDiagnostics(CopelandCompilation compilation)
        => string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.ToString()));

    private static IEnumerable<MetadataReference> GetRuntimeReferences()
    {
        string trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trustedPlatformAssemblies.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
    }
}
