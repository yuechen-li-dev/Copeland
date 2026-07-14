using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TypeAliasDiagnosticInventoryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return Case("COPE-ALIAS-0001", "type Missing = ;");
        yield return Case("COPE-ALIAS-0002", "type Box<T> = T[];");
        yield return Case("COPE-ALIAS-0003", "type Name = number; type Name = string;");
        yield return Case("COPE-ALIAS-0004", "type Name = Missing;");
        yield return Case("COPE-ALIAS-0005", "type A = B; type B = A;");
        yield return Case("COPE-ALIAS-0006", "type Name = number; function value(): number { return Name; }");
        yield return Case("COPE-TYPE-0020", "type Nothing = void; function value(input: Nothing): number { return 1; }");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_alias_diagnostic_has_a_focused_source_test_and_meaningful_span(
        string diagnosticId,
        string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        var diagnostic = compilation.Diagnostics.First(candidate => candidate.Id == diagnosticId);
        Assert.True(diagnostic.Position >= 0);
        Assert.True(diagnostic.Length > 0);
    }

    private static object[] Case(string diagnosticId, string source)
    {
        return [diagnosticId, source];
    }
}
