using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableDiagnosticInventoryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return Case("COPE-TABLE-0001", "function f(): void { record table Nested { x: [1]; } }");
        yield return Case("COPE-TABLE-0002", "record table T { x: [1]; } record table T { x: [2]; }");
        yield return Case("COPE-TABLE-0003", "record table Empty { }");
        yield return Case("COPE-TABLE-0004", "record table T { x: [1]; x: [2]; }");
        yield return Case("COPE-TABLE-0005", "record table T { x: []; }");
        yield return Case("COPE-TABLE-0006", "record table T { x: [1, \"two\"]; }");
        yield return Case("COPE-TABLE-0007", "record table T { x: number = [\"wrong\"]; }");
        yield return Case("COPE-TABLE-0008", "record table T { x: [1, 2]; y: [3]; }");
        yield return Case("COPE-TABLE-0009", "record table T { x: [[1]]; }");
        yield return Case("COPE-TABLE-0010", "record Node { next: Node; } record table T { node: Node = []; }");
        yield return Case("COPE-TABLE-0011", "function f(): void { const value: int = 1; value[0]; }");
        yield return Case("COPE-TABLE-0012", "record table T { x: [1]; } function f(): column number { return T.missing; }");
        yield return Case("COPE-TABLE-0013", "record table T { x: [1]; } function f(index: string): number ! TableBoundsError { return T.x[index]; }");
        yield return Case("COPE-TABLE-0014", "record table T { x: [1]; } function f(): void { T = T; }");
        yield return Case("COPE-TABLE-0015", "record table T { x: [1]; } function f(): void { T.x[0] = 2; }");
        yield return Case("COPE-TABLE-0016", "record table T { x: [1]; } function f(): void { const row: T.Row = { x: 1 }; }");
        yield return Case("COPE-TABLE-0017", "record table T { x: [1]; } function f(): boolean { return T.x == T.x; }");
        yield return Case("COPE-TABLE-0018", "record table First { x: [1]; } record table Second { x: [2]; } function use(value: First.Row): void { return; } function f(): void { const row: Second.Row ! TableBoundsError = Second[0]; use(row!); }");
        yield return Case("COPE-TABLE-0019", "function f(value: Missing.Row): void { return; }");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_table_diagnostic_has_a_focused_source_test_and_meaningful_span(string diagnosticId, string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        var diagnostic = compilation.Diagnostics.First(candidate => candidate.Id == diagnosticId);
        Assert.True(diagnostic.Position >= 0);
        Assert.True(diagnostic.Length > 0);
    }

    private static object[] Case(string diagnosticId, string source) => [diagnosticId, source];
}
