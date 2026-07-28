using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class TsonEncodeRuntimeTests
{
    [Fact]
    public async Task Table_singleton_encodes_through_authoritative_columns_with_csharp_node_parity()
    {
        const string source = """
            const $schema: string = "copeland://tests/runtime-table";
            record Point { x: number; }
            enum State { Ready, Named(label: string), }
            record table Samples {
                active: boolean = [true, false];
                score: number = [0, -0];
                point: Point = [{ x: 1 }, { x: 2 }];
                state: State = [State.Ready, State.Named("雪")];
                values: number[][] = [[[1, 2], []], [[], []]];
            }
            function encode(): string ! TsonEncodeError { return tsonEncode(Samples); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirTsonEncodingPlan plan = Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
        MirTsonTablePlan tablePlan = Assert.IsType<MirTsonTablePlan>(plan.TablePlan);
        Assert.Equal("copeland://tests/runtime-table#Samples", tablePlan.StableIdentity);
        Assert.Equal(2, tablePlan.ExpectedRowCount);
        Assert.Equal(5, tablePlan.Columns.Count);

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(compilation.MirCompilation.Program);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        Assert.DoesNotContain("Object.keys", javascript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Object.getOwnPropertySymbols", javascript.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        string csharpText = ResultValue(Invoke(generated, "encode"));
        ProcessResult node = await RunNodeAsync(javascript.SourceText + "process.stdout.write(encode().$payload[0]);\n");
        Assert.Equal(csharpText, node.StdOut);
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(csharpText, TsonDocumentProfile.CanonicalTson);
        Assert.True(read.Success, csharpText + Environment.NewLine + string.Join(Environment.NewLine, read.Diagnostics));
        Assert.Equal(csharpText, TsonCanonicalPrinter.Print(read.Document!));
        Assert.Equal(csharpText, ResultValue(Invoke(generated, "encode")));
    }

    [Fact]
    public async Task Asset_backed_and_zero_row_tables_encode_with_csharp_node_parity()
    {
        const string source = """
            const $schema: string = "copeland://tests/runtime-table-assets";
            record Point { name: string; }
            enum State { Off, Named(label: string), }
            record table Empty from tsonAsset("./empty.obj.ts") {
                active: boolean;
                note: string;
            }
            record table Samples from tsonAsset("./samples.tson") {
                active: boolean;
                score: number;
                point: Point;
                state: State;
                values: number[][];
            }
            function encode(): string ! TsonEncodeError { return tsonEncode(Samples); }
            function encodeEmpty(): string ! TsonEncodeError { return tsonEncode(Empty); }
            """;
        const string emptyAuthoring = """
            const $schema: string = "copeland://tests/runtime-table-assets";

            record table Empty {
                active: boolean = [];
                note: string = [];
            }

            const $value = Empty;
            """;
        TsonReadResult emptyRead = TsonDocumentReader.ReadSelfDescribed(emptyAuthoring, TsonDocumentProfile.ObjectTypeScript);
        Assert.True(emptyRead.Success, string.Join(Environment.NewLine, emptyRead.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string emptyCanonical = TsonCanonicalPrinter.Print(emptyRead.Document!);
        const string samplesAuthoring = """
            const $schema: string = "copeland://tests/runtime-table-assets";
            record Point { name: string; }
            enum State { Off, Named(label: string), }
            record table Samples {
                active: boolean = [true, false];
                score: number = [$number("0000000000000000"), $number("FFF0000000000000")];
                point: Point = [{ name: "first" }, { name: "雪😀" }];
                state: State = [State.Off, State.Named("payload")];
                values: number[][] = [[[1, 2], []], [[], [3]]];
            }
            const $value = Samples;
            """;

        TsonReadResult samplesRead = TsonDocumentReader.ReadSelfDescribed(samplesAuthoring, TsonDocumentProfile.ObjectTypeScript);
        Assert.True(samplesRead.Success, string.Join(Environment.NewLine, samplesRead.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string samplesCanonical = TsonCanonicalPrinter.Print(samplesRead.Document!);
        var options = new CopelandCompilationOptions
        {
            SourcePath = "C:/project/main.ts",
            ProjectRoot = "C:/project",
            AssetSource = new InMemoryAssetSource(
                ("C:/project/empty.obj.ts", emptyAuthoring),
                ("C:/project/samples.tson", samplesCanonical)),
        };
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source, options);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.DoesNotContain("samples.tson", csharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("samples.tson", javaScript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("empty.obj.ts", csharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("empty.obj.ts", javaScript.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        string samples = ResultValue(Invoke(generated, "encode"));
        string empty = ResultValue(Invoke(generated, "encodeEmpty"));
        Assert.Equal(samplesCanonical, samples);
        Assert.Equal(emptyCanonical, empty);
        Assert.Contains("record table Empty", empty, StringComparison.Ordinal);
        Assert.Contains("active: boolean = [];", empty, StringComparison.Ordinal);
        Assert.True(TsonDocumentReader.ReadSelfDescribed(samples, TsonDocumentProfile.CanonicalTson).Success);
        Assert.True(TsonDocumentReader.ReadSelfDescribed(empty, TsonDocumentProfile.CanonicalTson).Success);

        ProcessResult node = await RunNodeAsync(javaScript.SourceText + """
            process.stdout.write(encode().$payload[0] + "---\n" + encodeEmpty().$payload[0]);
            """);
        Assert.Equal(samples + "---\n" + empty, node.StdOut);
    }

    [Fact]
    public async Task Table_encoding_preserves_result_flow_and_terminal_invariants_bypass_except()
    {
        const string source = """
            const $schema: string = "copeland://tests/runtime-table-errors";
            record table Samples { text: string = ["ok"]; }
            function forwarded(): string ! TsonEncodeError { return tsonEncode(Samples); }
            function propagated(): string ! TsonEncodeError {
                const text: string = tsonEncode(Samples)?;
                return text;
            }
            function handled(): string {
                return try {
                    const text: string = tsonEncode(Samples)?;
                    "ok"
                } except (error) {
                    match error {
                        InvalidUnicode => "InvalidUnicode",
                        OutputLimitExceeded => "OutputLimitExceeded",
                    }
                };
            }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation invariantCSharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation invariantJavaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(invariantCSharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Type module = generated.Assembly!.GetType("Copeland.Generated.CopelandModule")!;
        object singleton = module
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(field => field.FieldType.Name.StartsWith("__CopeTable_", StringComparison.Ordinal))
            .GetValue(null)!;
        FieldInfo storage = singleton.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(string[]));
        string[] original = (string[])storage.GetValue(singleton)!;

        storage.SetValue(singleton, new[] { "\uD800" });
        Assert.Equal("InvalidUnicode", ResultErrorName(Invoke(generated, "forwarded")));
        Assert.Equal("InvalidUnicode", ResultErrorName(Invoke(generated, "propagated")));
        Assert.Equal("InvalidUnicode", Assert.IsType<string>(Invoke(generated, "handled")));

        storage.SetValue(singleton, new[] { new string('a', 262_145) });
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "forwarded")));
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "propagated")));
        Assert.Equal("OutputLimitExceeded", Assert.IsType<string>(Invoke(generated, "handled")));

        storage.SetValue(singleton, new[] { "ok", "extra" });
        TargetInvocationException csharpBypass = Assert.Throws<TargetInvocationException>(() => Invoke(generated, "handled"));
        Assert.IsType<InvalidOperationException>(csharpBypass.InnerException);
        storage.SetValue(singleton, original);
        Assert.Equal("ok", Assert.IsType<string>(Invoke(generated, "handled")));

        string javaScriptSingleton = Regex.Match(
            invariantJavaScript.SourceText!,
            @"__cope_m3_table_value_[A-Za-z0-9_]+",
            RegexOptions.Singleline).Value;
        string javaScriptEncoder = Regex.Match(
            invariantJavaScript.SourceText!,
            @"__cope_m3_tson_[A-Za-z0-9_]+",
            RegexOptions.Singleline).Value;
        string javaScriptColumnInstances = Regex.Match(
            invariantJavaScript.SourceText!,
            @"__cope_m3_column_instances_[A-Za-z0-9_]+",
            RegexOptions.Singleline).Value;
        string javaScriptTableInstances = Regex.Match(
            invariantJavaScript.SourceText!,
            @"__cope_m3_table_instances_[A-Za-z0-9_]+",
            RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrWhiteSpace(javaScriptSingleton));
        Assert.False(string.IsNullOrWhiteSpace(javaScriptEncoder));
        Assert.False(string.IsNullOrWhiteSpace(javaScriptColumnInstances));
        Assert.False(string.IsNullOrWhiteSpace(javaScriptTableInstances));
        string script = invariantJavaScript.SourceText + """
            const tableValue =
            """ + javaScriptSingleton + """
            ;
            const encodeTable =
            """ + javaScriptEncoder + """
            ["tson0"];
            const columnInstances =
            """ + javaScriptColumnInstances + """
            ;
            const tableInstances =
            """ + javaScriptTableInstances + """
            ;
            const tableSymbols = Object.getOwnPropertySymbols(tableValue);
            const columnSlot = tableSymbols.find(symbol => typeof tableValue[symbol] === "object" && tableValue[symbol] !== null);
            const columnValue = tableValue[columnSlot];
            const columnSymbols = Object.getOwnPropertySymbols(columnValue);
            const valuesSlot = columnSymbols.find(symbol => Array.isArray(columnValue[symbol]));
            function makeColumn(cells) {
                const fake = Object.create(null);
                for (const symbol of columnSymbols) {
                    const value = symbol === valuesSlot ? Object.freeze(cells.slice()) : columnValue[symbol];
                    Object.defineProperty(fake, symbol, { value, writable: false, enumerable: false, configurable: false });
                }
                Object.freeze(fake);
                columnInstances.add(fake);
                return fake;
            }
            function makeTable(column) {
                const fake = Object.create(null);
                for (const symbol of tableSymbols) {
                    const value = symbol === columnSlot ? column : tableValue[symbol];
                    Object.defineProperty(fake, symbol, { value, writable: false, enumerable: false, configurable: false });
                }
                Object.freeze(fake);
                tableInstances.add(fake);
                return fake;
            }
            console.log(encodeTable(makeTable(makeColumn(["\uD800"]))).$payload[0].$tag);
            console.log(encodeTable(makeTable(makeColumn(["a".repeat(262145)]))).$payload[0].$tag);
            let bypass = "typed";
            try { encodeTable(Object.freeze(Object.create(null))); } catch (error) { bypass = error.message; }
            console.log(bypass);
            console.log(handled());
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal(
            "InvalidUnicode\n" +
            "OutputLimitExceeded\n" +
            "Copeland JavaScript backend invariant failure.\n" +
            "ok\n",
            node.StdOut);
    }

    [Fact]
    public async Task Table_m2_corpus_has_pinned_artifacts_and_repeated_canonical_fixed_point()
    {
        string root = GetRepositoryRoot();
        string corpus = Path.Combine(root, "tests", "Copeland", "Copeland.TS.Tests", "TsonEncoding", "Corpus", "tables-m2");
        string sourcePath = Path.Combine(corpus, "main.ts");
        CopelandCompilation firstCompilation = CopelandCompiler.CompileToMir(
            File.ReadAllText(sourcePath),
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = corpus,
                AssetSource = FileAssetSource.Instance,
            });
        Assert.True(firstCompilation.Success, string.Join(Environment.NewLine, firstCompilation.Diagnostics));

        CSharpCompilation firstCSharp = CSharpBackend.Emit(firstCompilation.MirCompilation!.Program!);
        JavaScriptCompilation firstJavaScript = JavaScriptBackend.Emit(firstCompilation.MirCompilation.Program!);
        CSharpCompilation repeatedCSharp = CSharpBackend.Emit(firstCompilation.MirCompilation.Program!);
        JavaScriptCompilation repeatedJavaScript = JavaScriptBackend.Emit(firstCompilation.MirCompilation.Program!);
        Assert.Equal(firstCSharp.SourceText, repeatedCSharp.SourceText);
        Assert.Equal(firstJavaScript.SourceText, repeatedJavaScript.SourceText);
        Assert.Equal(File.ReadAllText(Path.Combine(corpus, "main.cope")), firstCompilation.MirText);
        Assert.Equal(File.ReadAllText(Path.Combine(corpus, "main.g.cs")), firstCSharp.SourceText);
        Assert.Equal(File.ReadAllText(Path.Combine(corpus, "main.g.js")), firstJavaScript.SourceText);

        byte[] expectedBytes = File.ReadAllBytes(Path.Combine(corpus, "expected.tson"));
        string expected = Encoding.UTF8.GetString(expectedBytes);
        Assert.NotEmpty(expectedBytes);
        Assert.False(expectedBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal((byte)'\n', expectedBytes[^1]);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(firstCSharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        string csharpText = ResultValue(Invoke(generated, "encode"));
        string emptyText = ResultValue(Invoke(generated, "encodeEmpty"));
        ProcessResult node = await RunNodeAsync(firstJavaScript.SourceText + """
            process.stdout.write(encode().$payload[0] + "---\n" + encodeEmpty().$payload[0]);
            """);
        Assert.Equal(expected, csharpText);
        Assert.Equal(expected + "---\n" + emptyText, node.StdOut);

        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(expected, TsonDocumentProfile.CanonicalTson);
        Assert.True(read.Success, string.Join(Environment.NewLine, read.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(expected, TsonCanonicalPrinter.Print(read.Document!));
        Assert.True(TsonDocumentReader.ReadSelfDescribed(emptyText, TsonDocumentProfile.CanonicalTson).Success);

        const string generationTwoSource = """
            const $schema: string = "copeland://corpus/runtime-table-encoding";

            record Point {
                name: string;
            }

            enum State {
                Off,
                Named(label: string),
            }

            record table Samples from tsonAsset("./generation-1.tson") {
                active: boolean;
                score: number;
                point: Point;
                state: State;
                values: number[][];
            }

            function encode(): string ! TsonEncodeError {
                return tsonEncode(Samples);
            }
            """;
        var generationTwoOptions = new CopelandCompilationOptions
        {
            SourcePath = "C:/generation-two/main.ts",
            ProjectRoot = "C:/generation-two",
            AssetSource = new InMemoryAssetSource(("C:/generation-two/generation-1.tson", expected)),
        };
        CopelandCompilation generationTwoCompilation = CopelandCompiler.CompileToMir(
            generationTwoSource,
            generationTwoOptions);
        Assert.True(generationTwoCompilation.Success, string.Join(Environment.NewLine, generationTwoCompilation.Diagnostics));
        CSharpCompilation generationTwoCSharp = CSharpBackend.Emit(generationTwoCompilation.MirCompilation!.Program!);
        JavaScriptCompilation generationTwoJavaScript = JavaScriptBackend.Emit(generationTwoCompilation.MirCompilation.Program!);
        RoslynCompileResult generationTwoGenerated = RoslynCompileHelper.CompileGeneratedSource(generationTwoCSharp.SourceText);
        Assert.True(generationTwoGenerated.Success, string.Join(Environment.NewLine, generationTwoGenerated.Diagnostics));
        string generationTwoCsharpText = ResultValue(Invoke(generationTwoGenerated, "encode"));
        ProcessResult generationTwoNode = await RunNodeAsync(
            generationTwoJavaScript.SourceText + "process.stdout.write(encode().$payload[0]);\n");
        Assert.Equal(expected, generationTwoCsharpText);
        Assert.Equal(expected, generationTwoNode.StdOut);
        foreach (string forbidden in new[] { "generation-1.tson", "C:/generation-two", "authoring comment" })
        {
            Assert.DoesNotContain(forbidden, generationTwoCompilation.MirText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, generationTwoCSharp.SourceText, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, generationTwoJavaScript.SourceText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, generationTwoCsharpText, StringComparison.Ordinal);
        }

        foreach (string forbidden in new[] { "empty.obj.ts", "authoring comment", "TsonDocument", "TsonValue", "System.IO", "Object.keys", "Object.getOwnPropertySymbols" })
        {
            Assert.DoesNotContain(forbidden, firstCompilation.MirText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, firstCSharp.SourceText, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, firstJavaScript.SourceText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, expected, StringComparison.Ordinal);
        }

        var expectedArtifacts = new Dictionary<string, (int Length, string Sha256)>(StringComparer.Ordinal)
        {
            ["empty.obj.ts"] = (164, "A3E967D07DF6730E703718EC84EF42CEE5360682022751AB2FF65B683220088E"),
            ["expected.tson"] = (1619, "77DB4113560183DD4F052F16E8656C0B2B1673FD39373FA6B720E58225F78666"),
            ["main.cope"] = (2154, "5CF1FC80EFAE33F77807298E7EE9F9A10C57565E09715B14515549D35AC78A4A"),
            ["main.g.cs"] = (35232, "3F63C6211F98E5F177C432AF2DFC66FA8EFF72FA0430D8F1963F4750757A1929"),
            ["main.g.js"] = (62425, "D7363BCD7050B8A255E290CDCEF7CC633A6250EF887731DD78539BFC4BA19EF9"),
            ["main.ts"] = (577, "563EA53F2241964E9E43749B008131301C2F883D6B7ABA01827B40E6ED619064"),
            ["samples.obj.ts"] = (1054, "684FE68C20A7EC25BD24A853198C3C5274CF5BDDC30B19F3468067FC154D55D0"),
        };
        foreach ((string fileName, (int expectedLength, string expectedHash)) in expectedArtifacts)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(corpus, fileName));
            Assert.Equal(expectedLength, bytes.Length);
            string actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.Equal(expectedHash, actualHash);
        }
    }

    private const string Source = """
        const $schema: string = "copeland://tests/runtime-encode";

        enum Mode {
            Off,
            Named(label: string),
        }

        record Inner {
            amount: number;
        }

        record Data {
            enabled: boolean;
            text: string;
            inner: Inner;
            mode: Mode;
        }

        function encode(text: string, amount: number): string ! TsonEncodeError {
            const value: Data = {
                enabled: true,
                text: text,
                inner: { amount: amount },
                mode: Mode.Named("payload"),
            };
            return tsonEncode(value);
        }
        function makeData(text: string, amount: number): Data {
            return {
                enabled: true,
                text: text,
                inner: { amount: amount },
                mode: Mode.Named("payload"),
            };
        }
        function encodeValue(value: Data): string ! TsonEncodeError { return tsonEncode(value); }
        function encodeMade(text: string, amount: number): string ! TsonEncodeError {
            return tsonEncode(makeData(text, amount));
        }
        """;

    [Fact]
    public void Writer_helpers_are_demand_emitted_and_forbidden_runtime_apis_are_absent()
    {
        CopelandCompilation unusedCompilation = CopelandCompiler.CompileToMir(
            "record Value { text: string; } function read(value: Value): string { return value.text; }");
        CSharpCompilation unusedCSharp = CSharpBackend.Emit(unusedCompilation.MirCompilation!.Program!);
        JavaScriptCompilation unusedJavaScript = JavaScriptBackend.Emit(unusedCompilation.MirCompilation.Program!);
        Assert.DoesNotContain("TsonWriter", unusedCSharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("tson", unusedJavaScript.SourceText, StringComparison.OrdinalIgnoreCase);

        CopelandCompilation usedCompilation = CopelandCompiler.CompileToMir(Source);
        CSharpCompilation usedCSharp = CSharpBackend.Emit(usedCompilation.MirCompilation!.Program!);
        JavaScriptCompilation usedJavaScript = JavaScriptBackend.Emit(usedCompilation.MirCompilation.Program!);
        Assert.Equal(1, Count(usedCSharp.SourceText, "private sealed class __TsonWriter"));
        Assert.Equal(1, Count(usedJavaScript.SourceText!, "function makeWriter("));
        Assert.Equal(1, Count(usedCSharp.SourceText, "makeData(text, amount)"));
        Assert.Equal(2, Count(usedJavaScript.SourceText!, "makeData(text, amount)"));
        foreach (string forbidden in new[]
        {
            "System.Text.Json",
            "JSON.stringify",
            "System.Reflection",
            "Object.keys",
            "Object.getOwnPropertySymbols",
            "for...in",
            "System.IO",
        })
        {
            Assert.DoesNotContain(forbidden, usedCSharp.SourceText, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, usedJavaScript.SourceText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Corpus_artifacts_are_byte_stable_and_pin_expected_runtime_output()
    {
        string root = GetRepositoryRoot();
        string corpus = Path.Combine(root, "tests", "Copeland", "Copeland.TS.Tests", "TsonEncoding", "Corpus", "record");
        string sourcePath = Path.Combine(corpus, "main.ts");
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            File.ReadAllText(sourcePath),
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = corpus,
                AssetSource = FileAssetSource.Instance,
            });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.cope"))), Normalize(compilation.MirText!));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.g.cs"))), Normalize(csharp.SourceText));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.g.js"))), Normalize(javaScript.SourceText!));

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        string expected = Normalize(File.ReadAllText(Path.Combine(corpus, "expected.tson")));
        Assert.Equal(expected, Normalize(ResultValue(Invoke(generated, "encode"))));

        var expectedHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main.ts"] = "72F3E1EDF2CEA75029722BA16C8DDEA9F8F9DFB7891D1477A973F4C3BF66214F",
            ["settings.obj.ts"] = "FCF039A91E0157C47AC3B2B3578101001FBE0F65ABBC4F85ED0C0D009AED0C5F",
            ["expected.tson"] = "F7754A6EBDFF2D2429EAF8AF06479F855043EF70911DBF2AF964CDA1815D5647",
            ["main.cope"] = "CD02076AB1D5D53860643FBBB11235AAE1087E506E9372D78A733F291F787119",
            ["main.g.cs"] = "F0C0A6BF3B9546C2D575E762B7E3AA8C90F1153A96C25DCB81ACF3288F78AB37",
            ["main.g.js"] = "0E88DFEBF8D3588F44CB0B1AD59A3554BC81BD6343DAB47546750926F5E66553",
        };
        foreach ((string fileName, string expectedHash) in expectedHashes)
        {
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(corpus, fileName))));
            Assert.Equal(expectedHash, actualHash);
        }
    }

    [Fact]
    public async Task Array_corpus_has_two_generation_csharp_node_fixed_point_and_pinned_artifacts()
    {
        string root = GetRepositoryRoot();
        string corpus = Path.Combine(root, "tests", "Copeland", "Copeland.TS.Tests", "TsonEncoding", "Corpus", "arrays");
        string sourcePath = Path.Combine(corpus, "main.ts");
        CopelandCompilation firstCompilation = CopelandCompiler.CompileToMir(
            File.ReadAllText(sourcePath),
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = corpus,
                AssetSource = FileAssetSource.Instance,
            });
        Assert.True(firstCompilation.Success, string.Join(Environment.NewLine, firstCompilation.Diagnostics));

        CSharpCompilation firstCSharp = CSharpBackend.Emit(firstCompilation.MirCompilation!.Program!);
        JavaScriptCompilation firstJavaScript = JavaScriptBackend.Emit(firstCompilation.MirCompilation.Program!);
        Assert.Empty(firstCSharp.Diagnostics);
        Assert.True(firstJavaScript.Success, string.Join(Environment.NewLine, firstJavaScript.Diagnostics));
        Assert.Equal(File.ReadAllText(Path.Combine(corpus, "main.cope")), firstCompilation.MirText);
        Assert.Equal(File.ReadAllText(Path.Combine(corpus, "main.g.cs")), firstCSharp.SourceText);
        Assert.Equal(File.ReadAllText(Path.Combine(corpus, "main.g.js")), firstJavaScript.SourceText);

        CSharpCompilation repeatedCSharp = CSharpBackend.Emit(firstCompilation.MirCompilation.Program!);
        JavaScriptCompilation repeatedJavaScript = JavaScriptBackend.Emit(firstCompilation.MirCompilation.Program!);
        Assert.Equal(firstCSharp.SourceText, repeatedCSharp.SourceText);
        Assert.Equal(firstJavaScript.SourceText, repeatedJavaScript.SourceText);

        byte[] expectedBytes = File.ReadAllBytes(Path.Combine(corpus, "expected.tson"));
        Assert.NotEmpty(expectedBytes);
        Assert.False(expectedBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal((byte)'\n', expectedBytes[^1]);
        string expected = Encoding.UTF8.GetString(expectedBytes);

        RoslynCompileResult firstGenerated = RoslynCompileHelper.CompileGeneratedSource(firstCSharp.SourceText);
        Assert.True(firstGenerated.Success, string.Join(Environment.NewLine, firstGenerated.Diagnostics));
        string firstCSharpText = ResultValue(Invoke(firstGenerated, "encode"));
        ProcessResult firstNode = await RunNodeAsync(firstJavaScript.SourceText + "process.stdout.write(encode().$payload[0]);\n");
        Assert.Equal(expected, firstCSharpText);
        Assert.Equal(expected, firstNode.StdOut);
        TsonReadResult reparsed = TsonDocumentReader.ReadSelfDescribed(expected, TsonDocumentProfile.CanonicalTson);
        Assert.True(reparsed.Success, string.Join(Environment.NewLine, reparsed.Diagnostics));
        Assert.Equal(expected, TsonCanonicalPrinter.Print(reparsed.Document!));

        const string secondSource = """
            const $schema: string = "copeland://corpus/runtime-array-encoding";
            record Detail { label: string; }
            enum Signal { Idle, Text(value: string), DetailValue(detail: Detail), }
            record Packet {
                emptyNumbers: number[];
                booleans: boolean[];
                numbers: number[];
                texts: string[];
                nested: number[][];
                details: Detail[];
                signals: Signal[];
                emptyDetails: Detail[];
            }
            function encode(): string ! TsonEncodeError {
                const loaded: Packet = tsonAsset("./canonical.tson");
                return tsonEncode(loaded);
            }
            """;
        var options = new CopelandCompilationOptions
        {
            SourcePath = "C:/array-m1/main.ts",
            ProjectRoot = "C:/array-m1",
            AssetSource = new InMemoryAssetSource(("C:/array-m1/canonical.tson", expected)),
        };
        CopelandCompilation secondCompilation = CopelandCompiler.CompileToMir(secondSource, options);
        Assert.True(secondCompilation.Success, string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        CSharpCompilation secondCSharp = CSharpBackend.Emit(secondCompilation.MirCompilation!.Program!);
        JavaScriptCompilation secondJavaScript = JavaScriptBackend.Emit(secondCompilation.MirCompilation.Program!);
        RoslynCompileResult secondGenerated = RoslynCompileHelper.CompileGeneratedSource(secondCSharp.SourceText);
        Assert.True(secondGenerated.Success, string.Join(Environment.NewLine, secondGenerated.Diagnostics));
        ProcessResult secondNode = await RunNodeAsync(secondJavaScript.SourceText + "process.stdout.write(encode().$payload[0]);\n");
        Assert.Equal(expected, ResultValue(Invoke(secondGenerated, "encode")));
        Assert.Equal(expected, secondNode.StdOut);

        foreach (string forbidden in new[] { "packet.obj.ts", "canonical.tson", "ARRAY-M1 authoring", "declaration and element order" })
        {
            Assert.DoesNotContain(forbidden, firstCompilation.MirText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, firstCSharp.SourceText, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, firstJavaScript.SourceText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, secondCompilation.MirText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, secondCSharp.SourceText, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, secondJavaScript.SourceText!, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, expected, StringComparison.Ordinal);
        }

        var expectedHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main.ts"] = "5F7506BE9A496A8B6970E48553D7AF8656A3EA1A28FFDF3BAD8C39AFBF2D4342",
            ["packet.obj.ts"] = "8BDA38AB1B62167C8794F5864777312BA674EA08D47C73042F3634FB4D1FFB8C",
            ["expected.tson"] = "3E9DC91E15DA05DEE0F41556225914C7AD375A0DE1AD928FE423EC8AA3E94E51",
            ["main.cope"] = "CCC4064D7FAFCD393FDD4FB0DD4F4E229EE20087F19AD739BE8EC990900AFB37",
            ["main.g.cs"] = "9D4EFAF8827733808FF4A560B85CA64BC204898C3C547A2BDFA0F432856566F0",
            ["main.g.js"] = "1335FE7939F9CB535DCD0E8116F5F9B4F227FA2B82B37E4EEB6E8CE5DE817E15",
        };
        foreach ((string fileName, string expectedHash) in expectedHashes)
        {
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(corpus, fileName))));
            Assert.Equal(expectedHash, actualHash);
        }
    }

    [Fact]
    public async Task Both_backends_emit_identical_canonical_text_and_unicode_errors()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
        Assert.Contains("tson-encode [tson0]", compilation.MirText, StringComparison.Ordinal);

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, csharp.SourceText + Environment.NewLine + string.Join(Environment.NewLine, generated.Diagnostics));
        object result = Invoke(generated, "encode", "snow 雪 😀", -0.0);
        Assert.True((bool)result.GetType().GetProperty("IsOk")!.GetValue(result)!);
        string canonical = (string)result.GetType().GetProperty("Value")!.GetValue(result)!;
        TsonReadResult reparsed = TsonDocumentReader.ReadSelfDescribed(canonical, TsonDocumentProfile.CanonicalTson);
        Assert.True(reparsed.Success, string.Join(Environment.NewLine, reparsed.Diagnostics));
        Assert.Equal(canonical, TsonCanonicalPrinter.Print(reparsed.Document!));
        for (int repetition = 0; repetition < 3; repetition++)
        {
            Assert.Equal(canonical, ResultValue(Invoke(generated, "encode", "snow 雪 😀", -0.0)));
        }

        object invalid = Invoke(generated, "encode", "\uD800", 0.0);
        Assert.False((bool)invalid.GetType().GetProperty("IsOk")!.GetValue(invalid)!);
        object error = invalid.GetType().GetProperty("Error")!.GetValue(invalid)!;
        Assert.Equal("InvalidUnicode", error.GetType().Name);

        string script = javaScript.SourceText + """
            const first = encode("snow 雪 😀", -0);
            const repeated = [first, encode("snow 雪 😀", -0), encode("snow 雪 😀", -0)];
            const bad = encode("\uD800", 0);
            console.log(repeated.every(item => item.$tag === "ok" && item.$payload[0] === first.$payload[0]));
            console.log(first.$payload[0]);
            console.log(bad.$payload[0].$tag);
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal("true\n" + canonical + "\nInvalidUnicode\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    [Fact]
    public async Task Both_backends_encode_nested_arrays_with_canonical_schema_evidence()
    {
        const string source = """
            const $schema: string = "copeland://tests/runtime-arrays";
            record Item { name: string; score: number; }
            enum Choice { None, Some(item: Item), }
            record Batch {
                flags: boolean[];
                names: string[];
                items: Item[];
                choices: Choice[];
                matrix: number[][];
                empty: Item[];
            }
            function encode(): string ! TsonEncodeError {
                const value: Batch = {
                    flags: [true, false],
                    names: ["Ada", "雪😀"],
                    items: [{ name: "one", score: 0 }],
                    choices: [Choice.None],
                    matrix: [[1, 0], []],
                    empty: [],
                };
                return tsonEncode(value);
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("number[][]", compilation.MirText, StringComparison.Ordinal);

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        Assert.Contains("var length = array.Length;", csharp.SourceText, StringComparison.Ordinal);
        Assert.Contains("Array.isArray(array)", javaScript.SourceText, StringComparison.Ordinal);
        Assert.Contains("const length = array.length;", javaScript.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        string csharpText = ResultValue(Invoke(generated, "encode"));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "process.stdout.write(encode().$payload[0]);\n");
        Assert.Equal(csharpText, node.StdOut);

        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(csharpText, TsonDocumentProfile.CanonicalTson);
        Assert.True(read.Success, string.Join(Environment.NewLine, read.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(csharpText, TsonCanonicalPrinter.Print(read.Document!));
        Assert.Contains("matrix: number[][];", csharpText, StringComparison.Ordinal);
        Assert.Contains("empty: Item[];", csharpText, StringComparison.Ordinal);
        Assert.Contains("        [],", csharpText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Array_runtime_carriers_have_exact_boundaries_and_javascript_terminal_invariants()
    {
        const string source = """
            const $schema: string = "copeland://tests/runtime-array-carriers";
            record Root { values: number[]; }
            function make(values: number[]): Root { return { values: values }; }
            function encodeValues(values: number[]): string ! TsonEncodeError { return tsonEncode(make(values)); }
            function encodeValue(value: Root): string ! TsonEncodeError { return tsonEncode(value); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        foreach (int length in new[] { 99_999, 100_000, 100_001 })
        {
            object result = Invoke(generated, "encodeValues", new object[] { new double[length] });
            Assert.Equal("OutputLimitExceeded", ResultErrorName(result));
        }

        string script = javaScript.SourceText + """
            const outcomes = [99999, 100000, 100001].map(length => encodeValues(new Array(length).fill(0)).$payload[0].$tag);
            let holes = "accepted";
            const sparse = [];
            sparse.length = 1;
            try { encodeValue(make(sparse)); } catch (error) { holes = error.message; }
            let counterfeit = "accepted";
            try { encodeValue(make({ length: 1, 0: 0 })); } catch (error) { counterfeit = error.message; }
            let reads = 0;
            const observed = [0];
            Object.defineProperty(observed, 0, { get() { reads += 1; return 0; } });
            const observedResult = encodeValue(make(observed));
            console.log(outcomes.join(","));
            console.log(holes);
            console.log(counterfeit);
            console.log(observedResult.$tag + ":" + reads);
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal(
            "OutputLimitExceeded,OutputLimitExceeded,OutputLimitExceeded\n" +
            "Copeland JavaScript backend invariant failure.\n" +
            "Copeland JavaScript backend invariant failure.\n" +
            "ok:1\n",
            node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    [Fact]
    public async Task JavaScript_encoder_rejects_counterfeits_and_observes_frozen_nominal_values()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Source);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        string script = javaScript.SourceText + """
            const legitimate = makeData("stable", 2);
            const before = encodeValue(legitimate).$payload[0];
            try { legitimate.text = "changed"; } catch (error) {}
            let counterfeit = "accepted";
            try { encodeValue(Object.freeze(Object.create(null))); } catch (error) { counterfeit = error.message; }
            const copied = Object.create(null);
            for (const symbol of Object.getOwnPropertySymbols(legitimate)) { copied[symbol] = legitimate[symbol]; }
            Object.freeze(copied);
            let copiedCounterfeit = "accepted";
            try { encodeValue(copied); } catch (error) { copiedCounterfeit = error.message; }
            const after = encodeValue(legitimate).$payload[0];
            console.log(before === after);
            console.log(counterfeit);
            console.log(copiedCounterfeit);
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal("true\nCopeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\n", node.StdOut);
    }

    [Fact]
    public async Task Utf8_output_and_per_string_limits_have_exact_shared_boundaries()
    {
        const string source = """
            const $schema: string = "copeland://tests/runtime-encode-limits";
            record Limit { a: string; b: string; c: string; d: string; }
            function encodeLimit(a: string, b: string, c: string, d: string): string ! TsonEncodeError {
                const value: Limit = { a: a, b: b, c: c, d: d };
                return tsonEncode(value);
            }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        object emptyResult = Invoke(generated, "encodeLimit", "", "", "", "");
        string emptyText = ResultValue(emptyResult);
        int remaining = 1_048_576 - Encoding.UTF8.GetByteCount(emptyText);
        string[] exactValues = DistributeAscii(remaining);
        object exactResult = Invoke(generated, "encodeLimit", exactValues);
        string exactText = ResultValue(exactResult);
        Assert.Equal(1_048_576, Encoding.UTF8.GetByteCount(exactText));

        Assert.NotEmpty(ResultValue(Invoke(generated, "encodeLimit", new string('a', 262_143), "", "", "")));
        Assert.NotEmpty(ResultValue(Invoke(generated, "encodeLimit", new string('a', 262_144), "", "", "")));
        Assert.NotEmpty(ResultValue(Invoke(generated, "encodeLimit", new string('a', 262_142) + "😀", "", "", "")));
        string[] overValues = exactValues.ToArray();
        overValues[3] += "a";
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "encodeLimit", overValues)));
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "encodeLimit", new string('a', 262_145), "", "", "")));
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "encodeLimit", new string('a', 262_144) + "\uD800", "", "", "")));
        Assert.Equal("InvalidUnicode", ResultErrorName(Invoke(generated, "encodeLimit", "\uD800", "", "", "")));
        Assert.Equal("InvalidUnicode", ResultErrorName(Invoke(generated, "encodeLimit", "\uDC00", "", "", "")));
        string[] invalidOverLimit = overValues.ToArray();
        invalidOverLimit[0] = invalidOverLimit[0][..^1] + "\uD800";
        Assert.Equal("InvalidUnicode", ResultErrorName(Invoke(generated, "encodeLimit", invalidOverLimit)));

        string script = javaScript.SourceText + $$"""
            const overhead = new TextEncoder().encode(encodeLimit("", "", "", "").$payload[0]).length;
            const remaining = 1048576 - overhead;
            const lengths = [Math.min(remaining, 262144), 0, 0, 0];
            let left = remaining - lengths[0];
            for (let index = 1; index < 4; index += 1) {
                lengths[index] = Math.min(left, 262144);
                left -= lengths[index];
            }
            const values = lengths.map(length => "a".repeat(length));
            const exact = encodeLimit(...values);
            values[3] += "a";
            console.log(exact.$tag + ":" + new TextEncoder().encode(exact.$payload[0]).length);
            console.log(encodeLimit(...values).$payload[0].$tag);
            console.log(encodeLimit("a".repeat(262145), "", "", "").$payload[0].$tag);
            console.log(encodeLimit("\uD800", "", "", "").$payload[0].$tag);
            console.log(encodeLimit("\uDC00", "", "", "").$payload[0].$tag);
            values[0] = values[0].slice(0, -1) + "\uD800";
            console.log(encodeLimit(...values).$payload[0].$tag);
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal("ok:1048576\nOutputLimitExceeded\nOutputLimitExceeded\nInvalidUnicode\nInvalidUnicode\nInvalidUnicode\n", node.StdOut);
    }

    [Fact]
    public async Task Binary64_categories_use_exact_uppercase_bits_and_normalized_nan()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Source);
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        (double Value, string Bits)[] cases =
        [
            (0.0, "0000000000000000"),
            (-0.0, "8000000000000000"),
            (1.5, "3FF8000000000000"),
            (double.Epsilon, "0000000000000001"),
            (double.MaxValue, "7FEFFFFFFFFFFFFF"),
            (BitConverter.UInt64BitsToDouble(0xFFF0000000000001UL), "7FF8000000000000"),
            (double.PositiveInfinity, "7FF0000000000000"),
            (double.NegativeInfinity, "FFF0000000000000"),
        ];
        foreach ((double value, string bits) in cases)
        {
            string text = ResultValue(Invoke(generated, "encode", "", value));
            Assert.Contains($"$number(\"{bits}\")", text, StringComparison.Ordinal);
        }

        string script = javaScript.SourceText + """
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer);
            view.setBigUint64(0, 0xFFF0000000000001n, false);
            const values = [0, -0, 1.5, Number.MIN_VALUE, Number.MAX_VALUE, view.getFloat64(0, false), Infinity, -Infinity];
            for (const value of values) {
                const text = encode("", value).$payload[0];
                console.log(text.match(/\$number\("([0-9A-F]{16})"\)/)[1]);
            }
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal(string.Join("\n", cases.Select(item => item.Bits)) + "\n", node.StdOut);
    }

    [Fact]
    public async Task Asset_loaded_value_encodes_without_asset_text_or_path_at_runtime()
    {
        const string source = """
            const $schema: string = "copeland://tests/asset-round-trip";
            record Settings { title: string; enabled: boolean; }
            function encodeLoaded(): string ! TsonEncodeError {
                const loaded: Settings = tsonAsset("./settings.obj.ts");
                return tsonEncode(loaded);
            }
            """;
        const string asset = """
            const $schema: string = "copeland://tests/asset-round-trip";
            // This comment and authored layout must not survive compilation.
            record Settings { title: string; enabled: boolean; }
            const $value: Settings = { enabled: true, title: "loaded" };
            """;
        TsonDocument document = TsonDocumentReader.ReadSelfDescribed(asset, TsonDocumentProfile.ObjectTypeScript).Document!;
        string expected = TsonCanonicalPrinter.Print(document);
        var options = new CopelandCompilationOptions
        {
            SourcePath = "C:/project/main.ts",
            ProjectRoot = "C:/project",
            AssetSource = new InMemoryAssetSource(("C:/project/settings.obj.ts", asset)),
        };
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source, options);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.DoesNotContain("settings.obj.ts", csharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.obj.ts", javaScript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("This comment", csharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("This comment", javaScript.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.Equal(expected, ResultValue(Invoke(generated, "encodeLoaded")));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "process.stdout.write(encodeLoaded().$payload[0]);\n");
        Assert.Equal(expected, node.StdOut);
    }

    [Fact]
    public async Task Empty_record_and_zero_payload_enum_roots_encode_as_closed_documents()
    {
        const string source = """
            const $schema: string = "copeland://tests/empty-roots";
            record Empty {}
            enum State { Ready, }
            function encodeEmpty(): string ! TsonEncodeError {
                const value: Empty = {};
                return tsonEncode(value);
            }
            function encodeState(): string ! TsonEncodeError { return tsonEncode(State.Ready); }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(2, compilation.MirCompilation!.Program!.TsonEncodingPlans.Count);
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        string empty = ResultValue(Invoke(generated, "encodeEmpty"));
        string state = ResultValue(Invoke(generated, "encodeState"));
        Assert.Contains("record Empty {\n}", empty, StringComparison.Ordinal);
        Assert.Contains("$record.Empty({})", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("enum State", empty, StringComparison.Ordinal);
        Assert.Contains("enum State {\n    Ready,\n}", state, StringComparison.Ordinal);
        Assert.Contains("State.Ready", state, StringComparison.Ordinal);
        Assert.DoesNotContain("record Empty", state, StringComparison.Ordinal);
        Assert.True(TsonDocumentReader.ReadSelfDescribed(empty, TsonDocumentProfile.CanonicalTson).Success);
        Assert.True(TsonDocumentReader.ReadSelfDescribed(state, TsonDocumentProfile.CanonicalTson).Success);

        string script = javaScript.SourceText + "process.stdout.write(encodeEmpty().$payload[0] + \"---\\n\" + encodeState().$payload[0]);\n";
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal(empty + "---\n" + state, node.StdOut);
    }

    [Fact]
    public async Task Fixed_point_matrix_preserves_nominal_identity_and_erases_authoring_trivia()
    {
        const string source = """
            const $schema: string = "copeland://tests/m2c-fixed-point";

            // Declaration layout is authoring-only; canonical order is ordinal by name.
            record Zed { text: string; }
            enum Second { Same, Wrap(value: Zed), }
            record Alpha { value: Zed; }
            enum First { Same, Wrap(value: Zed), }
            record Root {
                enabled: boolean;
                amount: number;
                text: string;
                alpha: Alpha;
                first: First;
                second: Second;
            }

            function encodeRoot(text: string, amount: number): string ! TsonEncodeError {
                const value: Root = {
                    enabled: true,
                    amount: amount,
                    text: text,
                    alpha: { value: { text: "nested" } },
                    first: First.Wrap({ text: "first" }),
                    second: Second.Wrap({ text: "second" }),
                };
                return tsonEncode(value);
            }
            function encodeFirst(): string ! TsonEncodeError { return tsonEncode(First.Same); }
            function encodeSecond(): string ! TsonEncodeError { return tsonEncode(Second.Same); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(3, compilation.MirCompilation!.Program!.TsonEncodingPlans.Count);
        Assert.Equal(new[] { "Alpha", "First", "Root", "Second", "Zed" },
            compilation.MirCompilation.Program.TsonEncodingPlans[0].Definitions.Select(definition => definition.Name));

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        string[] csharpTexts =
        [
            ResultValue(Invoke(generated, "encodeRoot", "snow 雪 😀\\\"\\n", -0.0)),
            ResultValue(Invoke(generated, "encodeFirst")),
            ResultValue(Invoke(generated, "encodeSecond")),
        ];
        foreach (string text in csharpTexts)
        {
            TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(text, TsonDocumentProfile.CanonicalTson);
            Assert.True(read.Success, string.Join(Environment.NewLine, read.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.Equal(text, TsonCanonicalPrinter.Print(read.Document!));
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
        }

        string script = javaScript.SourceText + """
            for (const text of [encodeRoot("snow 雪 😀\\\"\\n", -0).$payload[0], encodeFirst().$payload[0], encodeSecond().$payload[0]]) {
                process.stdout.write("@" + Buffer.from(text, "utf8").toString("base64") + "\n");
            }
            """;
        ProcessResult node = await RunNodeAsync(script);
        string[] javaScriptTexts = node.StdOut.Split('\n')
            .Where(line => line.StartsWith('@'))
            .Select(line => line[1..])
            .Select(Convert.FromBase64String)
            .Select(Encoding.UTF8.GetString)
            .ToArray();
        Assert.Equal(csharpTexts, javaScriptTexts);

        Assert.Contains("First.Same", csharpTexts[1], StringComparison.Ordinal);
        Assert.DoesNotContain("Second", csharpTexts[1], StringComparison.Ordinal);
        Assert.Contains("Second.Same", csharpTexts[2], StringComparison.Ordinal);
        Assert.DoesNotContain("First", csharpTexts[2], StringComparison.Ordinal);
        Assert.DoesNotContain("Declaration layout", csharpTexts[0], StringComparison.Ordinal);
        Assert.DoesNotContain("authoring-only", csharpTexts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Encoding_uses_existing_staging_for_once_order_and_result_flow()
    {
        const string source = """
            const $schema: string = "copeland://tests/m2c-evaluation";
            record Root { value: number; }

            function make(value: number): Root { return { value: value }; }
            function inspect(value: string ! TsonEncodeError): number {
                return match value { ok(text) => 1, err(error) => 0, };
            }
            function select(first: number, encoded: string ! TsonEncodeError): number {
                return match encoded { ok(text) => first, err(error) => 0, };
            }
            function operandOnce(): number {
                let trace: number = 0;
                const encoded: string ! TsonEncodeError = tsonEncode(make(trace = trace + 1));
                return match encoded { ok(text) => trace, err(error) => 0, };
            }
            function argumentOrder(): number {
                let trace: number = 0;
                return select(trace = trace * 10 + 1, tsonEncode(make(trace = trace * 10 + 2))) + trace;
            }
            function conditionalOrder(): number {
                let trace: number = 0;
                const encoded: string ! TsonEncodeError = if ((trace = trace + 1) == 1) {
                    tsonEncode(make(trace = trace + 1))
                } else {
                    tsonEncode(make(trace = trace + 100))
                };
                return match encoded { ok(text) => trace, err(error) => 0, };
            }
            function logicalOrder(): number {
                let trace: number = 0;
                const selected: boolean = false && (match tsonEncode(make(trace = trace + 1)) { ok(text) => true, err(error) => false, });
                return if selected { 100 } else { trace };
            }
            function matchOnce(): number {
                let trace: number = 0;
                return match tsonEncode(make(trace = trace + 1)) { ok(text) => trace, err(error) => 0, };
            }
            function forwarded(): string ! TsonEncodeError { return tsonEncode(make(4)); }
            function propagated(): number ! TsonEncodeError {
                const text: string = forwarded()?;
                return 7;
            }
            function handled(): number {
                let trace: number = 0;
                return try {
                    const text: string = tsonEncode(make(trace = trace + 1))?;
                    trace
                } except (error) {
                    0
                };
            }
            function repeated(): number {
                return inspect(tsonEncode(make(1))) + inspect(tsonEncode(make(2)));
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program);
        Assert.DoesNotMatch(@"\btry\s*\{", csharp.SourceText);
        Assert.DoesNotMatch(@"\bcatch\s*\(", csharp.SourceText);
        Assert.DoesNotContain("catch", javaScript.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        string[] methods = ["operandOnce", "argumentOrder", "conditionalOrder", "logicalOrder", "matchOnce", "handled", "repeated"];
        double[] expected = [1, 13, 2, 0, 1, 1, 2];
        Assert.Equal(expected, methods.Select(method => Assert.IsType<double>(Invoke(generated, method))));
        object propagated = Invoke(generated, "propagated");
        Assert.True((bool)propagated.GetType().GetProperty("IsOk")!.GetValue(propagated)!);
        Assert.Equal(7d, Assert.IsType<double>(propagated.GetType().GetProperty("Value")!.GetValue(propagated)));

        string script = javaScript.SourceText + """
            console.log([operandOnce(), argumentOrder(), conditionalOrder(), logicalOrder(), matchOnce(), handled(), repeated()].join(","));
            console.log(propagated().$payload[0]);
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal("1,13,2,0,1,1,2\n7\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    [Fact]
    public async Task Runtime_canonical_output_recompiles_as_a_canonical_asset_without_byte_changes()
    {
        const string firstSource = """
            const $schema: string = "copeland://tests/m2c-recompile";
            enum State { Ready, Named(text: string), }
            record Root { title: string; state: State; }
            function encode(): string ! TsonEncodeError {
                const value: Root = { title: "round trip 雪", state: State.Named("payload") };
                return tsonEncode(value);
            }
            """;
        const string secondSource = """
            const $schema: string = "copeland://tests/m2c-recompile";
            enum State { Ready, Named(text: string), }
            record Root { title: string; state: State; }
            function encode(): string ! TsonEncodeError {
                const value: Root = tsonAsset("./canonical.tson");
                return tsonEncode(value);
            }
            """;

        CopelandCompilation firstCompilation = CopelandCompiler.CompileToMir(firstSource);
        Assert.True(firstCompilation.Success, string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        CSharpCompilation firstCSharp = CSharpBackend.Emit(firstCompilation.MirCompilation!.Program!);
        JavaScriptCompilation firstJavaScript = JavaScriptBackend.Emit(firstCompilation.MirCompilation.Program!);
        RoslynCompileResult firstGenerated = RoslynCompileHelper.CompileGeneratedSource(firstCSharp.SourceText);
        Assert.True(firstGenerated.Success, string.Join(Environment.NewLine, firstGenerated.Diagnostics));

        string canonical = ResultValue(Invoke(firstGenerated, "encode"));
        ProcessResult firstNode = await RunNodeAsync(firstJavaScript.SourceText + "process.stdout.write(encode().$payload[0]);\n");
        Assert.Equal(canonical, firstNode.StdOut);
        Assert.True(TsonDocumentReader.ReadSelfDescribed(canonical, TsonDocumentProfile.CanonicalTson).Success);

        var options = new CopelandCompilationOptions
        {
            SourcePath = "C:/project/main.ts",
            ProjectRoot = "C:/project",
            AssetSource = new InMemoryAssetSource(("C:/project/canonical.tson", canonical)),
        };
        CopelandCompilation secondCompilation = CopelandCompiler.CompileToMir(secondSource, options);
        Assert.True(secondCompilation.Success, string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        CSharpCompilation secondCSharp = CSharpBackend.Emit(secondCompilation.MirCompilation!.Program!);
        JavaScriptCompilation secondJavaScript = JavaScriptBackend.Emit(secondCompilation.MirCompilation.Program!);
        Assert.DoesNotContain("canonical.tson", secondCSharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("canonical.tson", secondJavaScript.SourceText, StringComparison.Ordinal);

        RoslynCompileResult secondGenerated = RoslynCompileHelper.CompileGeneratedSource(secondCSharp.SourceText);
        Assert.True(secondGenerated.Success, string.Join(Environment.NewLine, secondGenerated.Diagnostics));
        Assert.Equal(canonical, ResultValue(Invoke(secondGenerated, "encode")));
        ProcessResult secondNode = await RunNodeAsync(secondJavaScript.SourceText + "process.stdout.write(encode().$payload[0]);\n");
        Assert.Equal(canonical, secondNode.StdOut);
        Assert.Equal(firstNode, secondNode);
    }

    private static object Invoke(RoslynCompileResult generated, string name, params object[] arguments)
        => generated.Assembly!.GetType("Copeland.Generated.CopelandModule")!
            .GetMethod(name)!
            .Invoke(null, arguments)!;

    private static string ResultValue(object result)
    {
        Assert.True((bool)result.GetType().GetProperty("IsOk")!.GetValue(result)!);
        return (string)result.GetType().GetProperty("Value")!.GetValue(result)!;
    }

    private static string ResultErrorName(object result)
    {
        Assert.False((bool)result.GetType().GetProperty("IsOk")!.GetValue(result)!);
        object error = result.GetType().GetProperty("Error")!.GetValue(result)!;
        return error.GetType().Name;
    }

    private static string[] DistributeAscii(int count)
    {
        var values = new string[4];
        for (int index = 0; index < values.Length; index++)
        {
            int length = Math.Min(count, 262_144);
            values[index] = new string('a', length);
            count -= length;
        }
        Assert.Equal(0, count);
        return values;
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static async Task<ProcessResult> RunNodeAsync(string source)
    {
        string node = Environment.GetEnvironmentVariable("COPELAND_NODE") ?? "node";
        var startInfo = new ProcessStartInfo(node, "-")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Node.js.");
        await process.StandardInput.WriteAsync(source);
        process.StandardInput.Close();
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        return new ProcessResult(stdout.Replace("\r\n", "\n", StringComparison.Ordinal), stderr.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private sealed record ProcessResult(string StdOut, string StdErr);

    private sealed class InMemoryAssetSource(params (string Path, string Text)[] files) : ICopelandAssetSource
    {
        private readonly IReadOnlyDictionary<string, string> files = files.ToDictionary(
            item => Path.GetFullPath(item.Path),
            item => item.Text,
            StringComparer.OrdinalIgnoreCase);

        public bool TryRead(string normalizedPath, out string? sourceText)
            => files.TryGetValue(Path.GetFullPath(normalizedPath), out sourceText);
    }

    private sealed class FileAssetSource : ICopelandAssetSource
    {
        public static FileAssetSource Instance { get; } = new();

        public bool TryRead(string normalizedPath, out string? sourceText)
        {
            try
            {
                sourceText = File.ReadAllText(normalizedPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sourceText = null;
                return false;
            }
        }
    }
}
