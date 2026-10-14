using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Mir;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableAuthoringTests
{
    [Fact]
    public async Task Row_authoring_keeps_columnar_identity_and_executes_on_every_authored_table_backend()
    {
        const string source = """
            type Identity = int;
            enum Status { Active, Paused, }
            record Detail { label: string; }
            record table Parents { key id: Identity; } = [{ id: 10 }, { id: 20 }];
            record table Rows {
                key id: Identity;
                reference parentId: Identity -> Parents.id;
                status: Status;
                detail: Detail;
                amount: int;
            } = [
                { detail: { label: "first" }, amount: 3, id: 2, parentId: 20, status: Status.Active },
                { "id": 1, parentId: 10, status: Status.Paused, detail: { label: "second" }, amount: 7 },
            ];
            function read(): int[] {
                const changed: Rows = Rows with { amount: [9, 11] };
                return [Rows.amount.sum(), Rows[0]!.parentId, changed[1]!.amount, Rows[1]!.id];
            }
            """;
        CopelandCompilation compilation = Compile(source);
        BoundTableDefinition rows = compilation.BoundCompilation!.Program.Tables[1];
        Assert.Equal(2, rows.RowCount);
        Assert.Equal("id", rows.TableType.KeyColumn!.Name);
        Assert.Equal("Parents", rows.Columns[1].Column.Reference!.TargetTable.Name);
        Assert.Equal([2, 1], rows.Columns[0].Cells.Cast<BoundTableLiteralConstant>().Select(cell => (int)cell.Value!).ToArray());
        var csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal([10, 20, 11, 1], (int[])module.GetMethod("read")!.Invoke(null, null)!);
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var javascript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Empty(javascript.Diagnostics);
            Assert.Equal("[10,20,11,1]", (await CorpusRunner.RunNode(javascript.SourceText + "\nconsole.log(JSON.stringify(read()));")).Trim());
        }
    }

    [Fact]
    public void Repeated_rows_and_empty_rows_bind_through_the_existing_initializer_rules()
    {
        CopelandCompilation compilation = Compile("""
            record table Values { value: int; valid: boolean; } = [
                { value: 1, valid: true } ... 0,
                { value: 4, valid: false } ... (1 + 2),
                { valid: true, value: 9 },
            ];
            record table Empty { value: int; } = [];
            """);
        BoundTableDefinition table = compilation.BoundCompilation!.Program.Tables[0];
        Assert.Equal(4, table.RowCount);
        var cells = Assert.IsType<InitializerSequence<BoundTableConstant>>(table.Columns[0].Cells);
        Assert.Equal([0, 3, 1], cells.Counts);
        Assert.Equal([4, 4, 4, 9], cells.Cast<BoundTableLiteralConstant>().Select(cell => (int)cell.Value!).ToArray());
        Assert.Equal(0, compilation.BoundCompilation.Program.Tables[1].RowCount);
    }

    [Theory]
    [InlineData("record table T { value: int; }", "COPE-ROWS-0001")]
    [InlineData("record table T { value: int = [1]; } = [{ value: 1 }];", "COPE-ROWS-0002")]
    [InlineData("record table T { value: int; } = [1];", "COPE-ROWS-0003")]
    [InlineData("record table T { value: int; } = [{ value: 1, other: 2 }];", "COPE-ROWS-0004")]
    [InlineData("record table T { value: int; } = [{ value: 1, value: 2 }];", "COPE-ROWS-0005")]
    [InlineData("record table T { value: int; } = [{}];", "COPE-ROWS-0006")]
    [InlineData("record table T { value: int; } = [{ value: true }];", "COPE-TABLE-0007")]
    [InlineData("record table T { key id: int; } = [{ id: 1 } ... 2];", "COPE-TABLE-0037")]
    [InlineData("record table T { value: int; } = [{ value: 1 } ...];", "COPE-INIT-0005")]
    [InlineData("record table T { value: int; } = [{ value: true } ... 0];", "COPE-TABLE-0007")]
    [InlineData("record table P { key id: int; } = [{ id: 1 }]; record table T { reference id: int -> P.id; } = [{ id: 2 }];", "COPE-TABLE-0038")]
    public void Invalid_row_authoring_gets_a_directed_diagnostic(string source, string expected)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }

    [Fact]
    public void Locals_execute_once_per_row_and_retain_transitive_identity_and_relationship_provenance()
    {
        const string source = """
            record table Products { key id: int = [20, 10]; cost: int = [3, 5]; }
            record table Stock {
                key reference productId: int -> Products.id = [10, 20];
                quantity: int = [4, 7];
            }
            record table Plan = derive Products as product join Stock as stock through stock.productId {
                const identity = product.id;
                const copiedIdentity = identity;
                const quantity: int = stock.quantity;
                const cost = quantity * product.cost;
                const doubled = cost + cost;
                id: int = copiedIdentity;
                units: int = quantity;
                cents: int = cost;
                doubleCents: int = doubled;
            }
            function read(): int[] { return [Plan[0]!.id, Plan[0]!.cents, Plan[1]!.doubleCents]; }
            """;
        CopelandCompilation compilation = Compile(source);
        var plan = Assert.IsType<BoundDerivedTableDefinition>(compilation.BoundCompilation!.Program.Tables[2]);
        Assert.Equal(5, plan.Locals.Count);
        Assert.Equal("id", plan.TableType.KeyColumn!.Name);
        Assert.Equal("id", plan.Projections[0].CopiedSourceColumn);
        Assert.Equal(["cost", "stock.quantity"], plan.Projections[3].SourceColumns);
        Assert.Single(plan.Projections[3].Relationships);
        Assert.Equal(5, compilation.MirCompilation!.Program!.Tables[2].DerivedPlan!.Locals.Count);
        var csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        foreach (BoundDerivedTableLocal local in plan.Locals)
        {
            Assert.Equal(1, csharp.SourceText.Split("int " + local.Variable.Name + " =", StringSplitOptions.None).Length - 1);
        }
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal([20, 21, 40], (int[])module.GetMethod("read")!.Invoke(null, null)!);
        Assert.Contains(JavaScriptBackend.Emit(compilation.MirCompilation.Program!).Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-DERIVE-0001");
    }

    [Fact]
    public void Generated_local_names_avoid_relation_aliases()
    {
        CopelandCompilation compilation = Compile("""
            record table T { value: int = [7]; }
            record table P = derive T as __derive_local_0 {
                const value = __derive_local_0.value;
                result: int = value;
            }
            function read(): int { return P[0]!.result; }
            """);
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal(7, module.GetMethod("read")!.Invoke(null, null));
    }

    [Fact]
    public void Generated_local_names_avoid_match_payload_bindings()
    {
        CopelandCompilation compilation = Compile("""
            enum Box { Value(value: int), }
            record table T { box: Box = [Box.Value(7)]; }
            record table P = derive T as row {
                const value = match row.box { Value(__derive_local_0) => __derive_local_0, };
                result: int = value;
            }
            function read(): int { return P[0]!.result; }
            """);
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal(7, module.GetMethod("read")!.Invoke(null, null));
    }

    [Fact]
    public void Locals_reuse_static_and_result_match_semantics()
    {
        CopelandCompilation compilation = Compile("""
            record table T { value: int ! string = [ok(4), err("missing")]; }
            record table P = derive T as row {
                const factor: int = static (1 + 2);
                const amount = match row.value { ok(value) => value * factor, err(error) => 0, };
                result: int = amount;
            }
            function read(): int { return P.result.sum(); }
            """);
        var plan = Assert.IsType<BoundDerivedTableDefinition>(compilation.BoundCompilation!.Program.Tables[1]);
        Assert.Equal(["value"], plan.Projections[0].SourceColumns);
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal(12, module.GetMethod("read")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("let amount = row.value; result: int = amount;", "COPE-DERIVE-0010")]
    [InlineData("var amount = row.value; result: int = amount;", "COPE-DERIVE-0010")]
    [InlineData("result: int = row.value; const amount = row.value;", "COPE-DERIVE-0011")]
    [InlineData("const row = 1; result: int = row;", "COPE-DERIVE-0012")]
    [InlineData("const amount = 1; const amount = 2; result: int = amount;", "COPE-DERIVE-0012")]
    [InlineData("const amount = later; const later = 1; result: int = amount;", "COPE-BIND-0001")]
    [InlineData("const amount = amount; result: int = amount;", "COPE-BIND-0001")]
    [InlineData("const amount: boolean = row.value; result: int = row.value;", "COPE-DERIVE-0005")]
    [InlineData("const amount = 1 + called(); result: int = amount;", "COPE-DERIVE-0007")]
    [InlineData("const amount = if row.value > 0 { called() } else { 0 }; result: int = amount;", "COPE-DERIVE-0007")]
    [InlineData("result: int = row.value + called();", "COPE-DERIVE-0007")]
    public void Invalid_locals_and_nested_effects_are_rejected(string body, string expected)
    {
        string source = "function called(): int { return 2; } record table T { value: int = [1]; } record table P = derive T as row { " + body + " }";
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }

    [Fact]
    public void Tson_does_not_silently_read_host_row_authoring_as_an_empty_table()
    {
        var read = TsonDocumentReader.ReadSelfDescribed("""
            const $schema: string = "copeland://test/rows";
            record table T { value: int; } = [{ value: 4 }];
            const $value = T;
            """, TsonDocumentProfile.ObjectTypeScript);
        Assert.False(read.Success);
        Assert.Contains(read.Diagnostics, diagnostic => diagnostic.Code == "COPE-TSON-TABLE-0001");
    }

    private static CopelandCompilation Compile(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }
}
