using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Mir;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class InitializerRepetitionTests
{
    private const string RuntimeSource = """
        flow Done(input: int) -> int { board { value: int = input; } state Complete initial { finish board.value; } }
        function flowStarts(): int { return [Done.start(4) ... 2].length; }
        record table Marks { valid: boolean = [true ...]; weight: int = [4 ... 3]; }
        function tableRead(): int[] {
            const revised: Marks = Marks with { weight: [9, 0 ...] };
            const original: Marks.Row = Marks[2]!;
            const changed: Marks.Row = revised[2]!;
            return [original.weight, changed.weight];
        }
        function next(storage: MutableArray<int>): int {
            storage[0] = storage[0] + 1;
            return storage[0];
        }
        function run(): int[] {
            const storage: MutableArray<int> = MutableArray<int>(1);
            storage[0] = 0;
            const values: int[] = [next(storage) ... 3, 99, next(storage) ... 0, next(storage) ... 2];
            const nested: int[][] = [[next(storage) ... 2] ... 2];
            return [values[0], values[1], values[2], values[3], values[4], values[5], nested[0][0], nested[1][1], storage[0]];
        }
        function typedEmpty(): float[] { return [1.5 ... 0]; }
        function inference(): int { return [1.5...0].length; }
        function staticValues(): int[] { return static [1 ... 2, 2 ... 3, 9]; }
        function zeros<static N: int>(): int[] { return [0 ... N]; }
        function generic(): int { return zeros<4>().length; }
        function checked(value: int): int ! string {
            return if value < 0 { err("negative") } else { ok(value) };
        }
        function failures(): int {
            const empty: int[] = [checked(-1)! ... 0];
            return try { const values: int[] = [checked(-1)? ... 2]; values.length } except (error) { 7 };
        }
        """;

    [Fact]
    public async Task CSharp_and_JavaScript_evaluate_each_element_and_preserve_zero_nested_generic_and_failure_semantics()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(RuntimeSource);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal([1, 2, 3, 99, 4, 5, 6, 9, 9], (int[])module.GetMethod("run")!.Invoke(null, null)!);
        Assert.Empty((double[])module.GetMethod("typedEmpty")!.Invoke(null, null)!);
        Assert.Equal(4, module.GetMethod("generic")!.Invoke(null, null));
        Assert.Equal(7, module.GetMethod("failures")!.Invoke(null, null));
        Assert.Equal(0, module.GetMethod("inference")!.Invoke(null, null));
        Assert.Equal([1, 1, 2, 2, 2, 9], (int[])module.GetMethod("staticValues")!.Invoke(null, null)!);
        Assert.Equal([4, 0], (int[])module.GetMethod("tableRead")!.Invoke(null, null)!);
        Assert.Equal(2, module.GetMethod("flowStarts")!.Invoke(null, null));

        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var javascript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Empty(javascript.Diagnostics);
            string output = await CorpusRunner.RunNode(javascript.SourceText + "\nconsole.log(JSON.stringify([run(), typedEmpty(), generic(), failures(), inference(), staticValues(), tableRead(), flowStarts()]));\n");
            Assert.Equal("[[1,2,3,99,4,5,6,9,9],[],4,7,0,[1,1,2,2,2,9],[4,0],2]", output.Trim());
        }
    }

    [Theory]
    [InlineData("function f(): int[] { const n: int = 3; return [0 ... n]; }", "COPE-INIT-0002")]
    [InlineData("function count(): int { return 3; } function f(): int[] { return [0 ... count()]; }", "COPE-INIT-0002")]
    [InlineData("function f(): int[] { return [0 ... -1]; }", "COPE-INIT-0003")]
    [InlineData("function f(): int[] { return [0 ... 1.5]; }", "COPE-INIT-0001")]
    [InlineData("function f(n: int): int[] { return [0 ... n]; }", "COPE-INIT-0002")]
    [InlineData("function f(): int[] { return [0 ... 65537]; }", "COPE-INIT-0004")]
    [InlineData("function f(): int[] { return [0 ... 32768, 1 ... 32769]; }", "COPE-INIT-0004")]
    [InlineData("function f(): int[] { return [0 ... (2147483647 + 1)]; }", "COPE-INIT-0003")]
    [InlineData("function f(): int[] { return [0 ...]; }", "COPE-INIT-0005")]
    [InlineData("function f(): int[] { return [0 ..., 1]; }", "COPE-INIT-0006")]
    [InlineData("function f(): int[] { return [...[1, 2]]; }", "COPE-INIT-0008")]
    [InlineData("record table T { value: int = [0 ...]; }", "COPE-INIT-0005")]
    [InlineData("record table T { value: int = [1, 2, 3, 0 ...]; anchor: int = [0, 1]; }", "COPE-INIT-0007")]
    [InlineData("record table T { key id: int = [1 ... 2]; }", "COPE-TABLE-0037")]
    [InlineData("record table T { value: int = [1, 2]; } function f<static N: int>(): T { return T with { value: [0 ... N] }; } function closed(): T { return f<2>(); }", "COPE-INIT-0002")]
    public void Invalid_initializers_have_directed_diagnostics(string source, string expected)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }

    [Fact]
    public async Task Async_repetition_reenters_each_await_and_skips_zero_counts()
    {
        const string source = """
            async function next(): int { return 4; }
            async function run(): int[] { return [await next() ... 3, await next() ... 0]; }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        object computation = module.GetMethod("run")!.Invoke(null, null)!;
        Assert.Equal([4, 4, 4], (int[])computation.GetType().GetProperty("Value")!.GetValue(computation)!);
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var javascript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
            const string suffix = """
                const pending = [];
                next = () => {
                    const computation = __cope_async_pending();
                    pending.push(computation);
                    return computation;
                };
                const output = run();
                if (output.completed) throw new Error("Expected suspension");
                pending[0].resolve(1);
                pending[1].resolve(2);
                pending[2].resolve(3);
                if (!output.completed || pending.length !== 3) throw new Error("Wrong repeat count");
                console.log(JSON.stringify(output.value));
                """;
            Assert.Equal("[1,2,3]", (await CorpusRunner.RunNode(javascript.SourceText + suffix)).Trim());
        }
    }

    [Fact]
    public void Tables_fill_from_later_anchors_and_keep_logical_cells_and_compact_plans()
    {
        const string source = """
            record table Marks {
                valid: boolean = [true ...];
                key id: int = [10, 20, 30];
                weight: int = [4 ... (1 + 2)];
            }
            record table Empty { valid: boolean = [true ...]; id: int = []; }
            function cleared(): Marks { return Marks with { weight: [9, 0 ...] }; }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundTableDefinition marks = compilation.BoundCompilation!.Program.Tables[0];
        Assert.Equal(3, marks.RowCount);
        var values = Assert.IsType<InitializerSequence<BoundTableConstant>>(marks.Columns[0].Cells);
        Assert.Single(values.Values);
        Assert.Equal(3, values.Count);
        Assert.All(values, value => Assert.Equal(true, Assert.IsType<BoundTableLiteralConstant>(value).Value));
        Assert.Equal(0, compilation.BoundCompilation.Program.Tables[1].RowCount);
        Assert.Contains("true ... 3", compilation.MirText);
    }

    [Fact]
    public void Repetition_does_not_relax_the_existing_authored_table_array_cell_boundary()
    {
        const string source = """
            record table Nested { cells: int[] = [[7 ... 3] ... 2]; }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TABLE-0009");
    }

    [Fact]
    public async Task Largest_admitted_array_and_table_remain_compact_deterministic_and_execute()
    {
        const string source = "record table Bulk { value: int = [7 ... 65536]; } function run(): int { const values: int[] = [4 ... 65536]; return values.length; }";
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.True(compilation.MirText!.Length < 2000);
        string csharpSource = CSharpBackend.Emit(compilation.MirCompilation!.Program!).SourceText;
        var javascript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        string javascriptSource = javascript.SourceText!;
        Assert.True(csharpSource.Length < 20000);
        Assert.True(javascriptSource.Length < 30000);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            CopelandCompilation repeated = CopelandCompiler.CompileToMir(source);
            Assert.Equal(compilation.MirText, repeated.MirText);
            Assert.Equal(csharpSource, CSharpBackend.Emit(repeated.MirCompilation!.Program!).SourceText);
            Assert.Equal(javascriptSource, JavaScriptBackend.Emit(repeated.MirCompilation.Program!).SourceText);
        }
        Type module = CorpusRunner.CompileCSharp(csharpSource).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal(65536, module.GetMethod("run")!.Invoke(null, null));
        Assert.Equal("65536", (await CorpusRunner.RunNode(javascriptSource + "\nconsole.log(run());")).Trim());
    }
}
