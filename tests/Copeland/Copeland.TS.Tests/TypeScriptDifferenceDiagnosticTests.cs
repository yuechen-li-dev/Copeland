using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TypeScriptDifferenceDiagnosticTests
{
    [Theory]
    [InlineData("function main(): number { return try { 1 } catch (error) { 0 }; }", "COPE-PROFILE-0010")]
    [InlineData("function greet(name: string = \"World\"): string { return name; }", "COPE-PROFILE-0011")]
    [InlineData("record Person { nickname?: string; }", "COPE-PROFILE-0012")]
    [InlineData("function main(): string { const value: string = \"x\"; return value ?? \"y\"; }", "COPE-PROFILE-0013")]
    [InlineData("function count(values: readonly string[]): int { return 0; }", "COPE-PROFILE-0014")]
    [InlineData("function first(value: [int, string]): int { return 0; }", "COPE-PROFILE-0015")]
    [InlineData("function describe(value: string | int): string { return \"x\"; }", "COPE-UNION-0012")]
    public void Familiar_typescript_syntax_has_a_focused_diagnostic(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId && diagnostic.Length > 0);
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }
}
