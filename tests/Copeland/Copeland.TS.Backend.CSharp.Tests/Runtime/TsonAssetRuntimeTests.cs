using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class TsonAssetRuntimeTests
{
    private const string UnionEncodingSource = """
        const $schema: string = "copeland://tests/runtime-shape";
        record Circle { radius: number; }
        record Rectangle { width: number; height: number; }
        type Shape = Circle | Rectangle;
        function load(): Shape {
            const circle: Circle = { radius: 4 };
            return circle;
        }
        function encodeLoaded(): string ! TsonEncodeError {
            return tsonEncode(load());
        }
        function encodeRectangle(): string ! TsonEncodeError {
            const rectangle: Rectangle = { width: 3, height: 2 };
            const shape: Shape = rectangle;
            return tsonEncode(shape);
        }
        """;

    private const string ArrayParitySource = """
        const $schema: string = "copeland://tests/runtime-array-assets";
        record Entry { label: string; }
        record OtherEntry { label: string; }
        enum Signal { Idle, Number(value: number), Values(values: number[]), }
        enum OtherSignal { Idle, Number(value: number), }
        record Batch {
            empty: number[];
            flags: boolean[];
            numbers: number[];
            texts: string[];
            entries: Entry[];
            signals: Signal[];
            rows: number[][];
            groups: Entry[][];
        }
        record Envelope { batch: Batch; payload: Signal; }
        function load(): Envelope {
            const value: Envelope = tsonAsset("./arrays.obj.ts");
            return value;
        }
        """;

    private const string ArrayParityAuthoringAsset = """
        // Authoring formatting and comments deliberately differ from canonical TSON.
        const $schema: string = "copeland://tests/runtime-array-assets";
        record Entry { label: string; }
        record OtherEntry { label: string; }
        enum Signal { Idle, Number(value: number), Values(values: number[]), }
        enum OtherSignal { Idle, Number(value: number), }
        record Batch {
            empty: number[];
            flags: boolean[];
            numbers: number[];
            texts: string[];
            entries: Entry[];
            signals: Signal[];
            rows: number[][];
            groups: Entry[][];
        }
        record Envelope { batch: Batch; payload: Signal; }
        const $value: Envelope = {
            batch: {
                groups: [[{ label: "nested" }], [{ label: "again" }]],
                rows: [[], [1, 2]],
                signals: [Signal.Idle, Signal.Number($number("3FF8000000000000")), Signal.Values([1, 2])],
                entries: [{ label: "first" }, { label: "second" }],
                texts: ["quote \" slash \\ newline\n", "雪", "😀"],
                numbers: [$number("0000000000000000"), $number("8000000000000000"), $number("3FF8000000000000"), $number("7FF8000000000000"), $number("7FF0000000000000"), $number("FFF0000000000000")],
                flags: [true, false],
                empty: [],
            },
            payload: Signal.Values([3, 4]),
        };
        """;

    private const string Source = """
        const $schema: string = "copeland://tests/runtime-assets";
        record Detail { value: number; }
        enum Choice { None, Detail(value: Detail), }
        record Data {
            zero: number;
            negativeZero: number;
            finite: number;
            large: number;
            small: number;
            nan: number;
            positiveInfinity: number;
            negativeInfinity: number;
            text: string;
            enabled: boolean;
            choice: Choice;
        }
        function zero(): number { const data: Data = tsonAsset("./data.tson"); return data.zero; }
        function negativeZero(): number { const data: Data = tsonAsset("./data.tson"); return data.negativeZero; }
        function finite(): number { const data: Data = tsonAsset("./data.tson"); return data.finite; }
        function large(): number { const data: Data = tsonAsset("./data.tson"); return data.large; }
        function small(): number { const data: Data = tsonAsset("./data.tson"); return data.small; }
        function nan(): number { const data: Data = tsonAsset("./data.tson"); return data.nan; }
        function positiveInfinity(): number { const data: Data = tsonAsset("./data.tson"); return data.positiveInfinity; }
        function negativeInfinity(): number { const data: Data = tsonAsset("./data.tson"); return data.negativeInfinity; }
        function text(): string { const data: Data = tsonAsset("./data.tson"); return data.text; }
        function enabled(): boolean { const data: Data = tsonAsset("./data.tson"); return data.enabled; }
        function nested(): number {
            const data: Data = tsonAsset("./data.tson");
            return match data.choice { None => 0, Detail(detail) => detail.value, };
        }
        """;

    private const string AuthoringAsset = """
        const $schema: string = "copeland://tests/runtime-assets";
        record Detail { value: number; }
        enum Choice { None, Detail(value: Detail), }
        record Data {
            zero: number;
            negativeZero: number;
            finite: number;
            large: number;
            small: number;
            nan: number;
            positiveInfinity: number;
            negativeInfinity: number;
            text: string;
            enabled: boolean;
            choice: Choice;
        }
        const $value: Data = {
            zero: $number("0000000000000000"),
            negativeZero: $number("8000000000000000"),
            finite: $number("3FF8000000000000"),
            large: $number("7FEFFFFFFFFFFFFF"),
            small: $number("0000000000000001"),
            nan: $number("7FF8000000000000"),
            positiveInfinity: $number("7FF0000000000000"),
            negativeInfinity: $number("FFF0000000000000"),
            text: "quote \" slash \\ newline\n雪 😀",
            enabled: true,
            choice: Choice.Detail({ value: 42 }),
        };
        """;

    [Fact]
    public async Task Nominal_union_encoding_reuses_payload_enum_runtime_contract_with_csharp_node_parity()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            UnionEncodingSource,
            new CopelandCompilationOptions
            {
                SourcePath = "C:/project/main.ts",
                ProjectRoot = "C:/project",
            });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirProgram program = compilation.MirCompilation!.Program!;
        CSharpCompilation csharp = CSharpBackend.Emit(program);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(program);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        Assert.DoesNotContain("union", csharp.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("union", javascript.SourceText, StringComparison.OrdinalIgnoreCase);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        object loaded = Invoke(generated, "load")!;
        Assert.Equal("Circle", loaded.GetType().Name);
        object payload = ReadProperties(loaded)[0]!;
        Assert.Equal(4d, Assert.IsType<double>(ReadProperties(payload)[0]));

        string loadedEncoded = ResultValue(Invoke(generated, "encodeLoaded"));
        string rectangleEncoded = ResultValue(Invoke(generated, "encodeRectangle"));
        Copeland.TS.Tson.TsonReadResult loadedRead = Copeland.TS.Tson.TsonDocumentReader.ReadSelfDescribed(
            loadedEncoded,
            Copeland.TS.Tson.TsonDocumentProfile.CanonicalTson);
        Copeland.TS.Tson.TsonReadResult rectangleRead = Copeland.TS.Tson.TsonDocumentReader.ReadSelfDescribed(
            rectangleEncoded,
            Copeland.TS.Tson.TsonDocumentProfile.CanonicalTson);
        Assert.True(loadedRead.Success, string.Join(Environment.NewLine, loadedRead.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(rectangleRead.Success, string.Join(Environment.NewLine, rectangleRead.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(loadedEncoded, Copeland.TS.Tson.TsonCanonicalPrinter.Print(loadedRead.Document!));
        Assert.Equal(rectangleEncoded, Copeland.TS.Tson.TsonCanonicalPrinter.Print(rectangleRead.Document!));

        Copeland.TS.Tson.TsonEnum loadedValue = Assert.IsType<Copeland.TS.Tson.TsonEnum>(loadedRead.Document!.Root);
        Copeland.TS.Tson.TsonEnum rectangleValue = Assert.IsType<Copeland.TS.Tson.TsonEnum>(rectangleRead.Document!.Root);
        Assert.Equal("copeland://tests/runtime-shape#Shape", loadedValue.EnumIdentity);
        Assert.Equal("copeland://tests/runtime-shape#Shape.Circle", loadedValue.CaseIdentity);
        Assert.Equal("copeland://tests/runtime-shape#Shape.Circle.value", Assert.Single(loadedValue.Payloads).Identity);
        Assert.Equal("copeland://tests/runtime-shape#Shape", rectangleValue.EnumIdentity);
        Assert.Equal("copeland://tests/runtime-shape#Shape.Rectangle", rectangleValue.CaseIdentity);
        Assert.Equal("copeland://tests/runtime-shape#Shape.Rectangle.value", Assert.Single(rectangleValue.Payloads).Identity);

        ProcessResult node = await RunNodeAsync(javascript.SourceText + """
            const shape = load();
            const payload = shape.$payload[0];
            console.log(JSON.stringify({
              tag: shape.$tag,
              radius: payload[Object.getOwnPropertySymbols(payload)[1]],
              loaded: encodeLoaded().$payload[0],
              rectangle: encodeRectangle().$payload[0],
            }));
            """);
        using JsonDocument output = JsonDocument.Parse(node.StdOut);
        JsonElement root = output.RootElement;
        Assert.Equal("Circle", root.GetProperty("tag").GetString());
        Assert.Equal(4d, root.GetProperty("radius").GetDouble());
        Assert.Equal(loadedEncoded, root.GetProperty("loaded").GetString());
        Assert.Equal(rectangleEncoded, root.GetProperty("rectangle").GetString());
    }

    [Fact]
    public async Task Both_backends_execute_compiled_asset_with_exact_repeated_parity()
    {
        string canonical = Copeland.TS.Tson.TsonCanonicalPrinter.Print(
            Copeland.TS.Tson.TsonDocumentReader.ReadSelfDescribed(
                AuthoringAsset,
                Copeland.TS.Tson.TsonDocumentProfile.ObjectTypeScript).Document!);
        var source = new InMemoryAssetSource("C:/project/data.tson", canonical);
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            Source,
            new CopelandCompilationOptions
            {
                SourcePath = "C:/project/main.ts",
                ProjectRoot = "C:/project",
                AssetSource = source,
            });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Single(compilation.AssetDependencies);

        CSharpCompilation firstCSharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        CSharpCompilation secondCSharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        JavaScriptCompilation firstJavaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        JavaScriptCompilation secondJavaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(firstCSharp.Diagnostics);
        Assert.True(firstJavaScript.Success, string.Join(Environment.NewLine, firstJavaScript.Diagnostics));
        Assert.Equal(firstCSharp.SourceText, secondCSharp.SourceText);
        Assert.Equal(firstJavaScript.SourceText, secondJavaScript.SourceText);

        Assert.DoesNotContain("tsonAsset", firstCSharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("tsonAsset", firstJavaScript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("data.tson", firstCSharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("data.tson", firstJavaScript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Tson", firstCSharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Tson", firstJavaScript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", firstCSharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("require(", firstJavaScript.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(firstCSharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        for (int iteration = 0; iteration < 3; iteration++)
        {
            Assert.Equal(0UL, Bits(Invoke(generated, "zero")));
            Assert.Equal(0x8000000000000000UL, Bits(Invoke(generated, "negativeZero")));
            Assert.Equal(0x3FF8000000000000UL, Bits(Invoke(generated, "finite")));
            Assert.Equal(0x7FEFFFFFFFFFFFFFUL, Bits(Invoke(generated, "large")));
            Assert.Equal(1UL, Bits(Invoke(generated, "small")));
            Assert.Equal(0x7FF8000000000000UL, Bits(Invoke(generated, "nan")));
            Assert.Equal(0x7FF0000000000000UL, Bits(Invoke(generated, "positiveInfinity")));
            Assert.Equal(0xFFF0000000000000UL, Bits(Invoke(generated, "negativeInfinity")));
            Assert.Equal("quote \" slash \\ newline\n雪 😀", Assert.IsType<string>(Invoke(generated, "text")));
            Assert.True(Assert.IsType<bool>(Invoke(generated, "enabled")));
            Assert.Equal(42d, Assert.IsType<double>(Invoke(generated, "nested")));
        }

        string script = firstJavaScript.SourceText + """
            const bits = value => {
              const bytes = new ArrayBuffer(8);
              const view = new DataView(bytes);
              view.setFloat64(0, value, false);
              return view.getBigUint64(0, false).toString(16).toUpperCase().padStart(16, "0");
            };
            console.log([
              bits(zero()), bits(negativeZero()), bits(finite()), bits(large()), bits(small()),
              bits(nan()), bits(positiveInfinity()), bits(negativeInfinity()),
              JSON.stringify(text()), enabled(), nested()
            ].join("|"));
            """;
        ProcessResult firstNode = await RunNodeAsync(script);
        ProcessResult secondNode = await RunNodeAsync(script);
        const string expected = "0000000000000000|8000000000000000|3FF8000000000000|7FEFFFFFFFFFFFFF|0000000000000001|7FF8000000000000|7FF0000000000000|FFF0000000000000|\"quote \\\" slash \\\\ newline\\n雪 😀\"|true|42\n";
        Assert.Equal(expected, firstNode.StdOut);
        Assert.Equal(firstNode, secondNode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CSharp_and_node_expose_the_same_compiled_array_asset_values(bool canonicalProfile)
    {
        Copeland.TS.Tson.TsonReadResult authored = Copeland.TS.Tson.TsonDocumentReader.ReadSelfDescribed(
            ArrayParityAuthoringAsset,
            Copeland.TS.Tson.TsonDocumentProfile.ObjectTypeScript);
        Assert.True(
            authored.Success,
            string.Join(
                Environment.NewLine,
                authored.SyntaxDiagnostics.Select(diagnostic => diagnostic.Message)
                    .Concat(authored.Diagnostics.Select(diagnostic => diagnostic.Message))));
        string asset = canonicalProfile
            ? Copeland.TS.Tson.TsonCanonicalPrinter.Print(authored.Document!)
            : ArrayParityAuthoringAsset;
        string extension = canonicalProfile ? ".tson" : ".obj.ts";
        string source = ArrayParitySource.Replace("arrays.obj.ts", "arrays" + extension, StringComparison.Ordinal);
        var assets = new InMemoryAssetSource("C:/project/arrays" + extension, asset);
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            source,
            new CopelandCompilationOptions
            {
                SourcePath = "C:/project/main.ts",
                ProjectRoot = "C:/project",
                AssetSource = assets,
            });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.MirCompilation!.Program!;
        CSharpCompilation csharp = CSharpBackend.Emit(program);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(program);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        Assert.Equal(csharp.SourceText, CSharpBackend.Emit(program).SourceText);
        Assert.Equal(javascript.SourceText, JavaScriptBackend.Emit(program).SourceText);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        AssertArrayAssetCSharpShape(Invoke(generated, "load"));

        string script = javascript.SourceText + """
            const symbols = value => Object.getOwnPropertySymbols(value);
            const fields = value => symbols(value).slice(1).map(symbol => value[symbol]);
            const bits = value => {
              const bytes = new ArrayBuffer(8);
              const view = new DataView(bytes);
              view.setFloat64(0, value, false);
              return view.getBigUint64(0, false).toString(16).toUpperCase().padStart(16, "0");
            };
            const [batch, payload] = fields(load());
            const [empty, flags, numbers, texts, entries, signals, rows, groups] = fields(batch);
            const signal = value => [
              value.$tag,
              value.$payload.length === 0 ? [] : value.$payload[0],
            ];
            console.log(JSON.stringify({
              empty: empty.length,
              flags,
              bits: numbers.map(bits),
              texts,
              entryLabels: entries.map(entry => fields(entry)[0]),
              sameEntryIdentity: symbols(entries[0])[0] === symbols(entries[1])[0]
                && symbols(entries[0])[0] === symbols(groups[0][0])[0],
              signalIdentity: symbols(signals[0])[0] === symbols(signals[1])[0]
                && symbols(signals[0])[0] === symbols(signals[2])[0]
                && symbols(signals[0])[0] === symbols(payload)[0],
              signals: signals.map(signal),
              payload: signal(payload),
              rows,
              groups: groups.map(group => group.map(entry => fields(entry)[0])),
              arraysAreMutableCarriers: !Object.isFrozen(numbers) && !Object.isFrozen(rows),
            }));
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal(string.Empty, node.StdErr);
        using JsonDocument output = JsonDocument.Parse(node.StdOut);
        JsonElement root = output.RootElement;
        Assert.Equal(0, root.GetProperty("empty").GetInt32());
        Assert.Equal([true, false], root.GetProperty("flags").EnumerateArray().Select(value => value.GetBoolean()));
        Assert.Equal(
            ["0000000000000000", "8000000000000000", "3FF8000000000000", "7FF8000000000000", "7FF0000000000000", "FFF0000000000000"],
            root.GetProperty("bits").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            ["quote \" slash \\ newline\n", "雪", "😀"],
            root.GetProperty("texts").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["first", "second"], root.GetProperty("entryLabels").EnumerateArray().Select(value => value.GetString()));
        Assert.True(root.GetProperty("sameEntryIdentity").GetBoolean());
        Assert.True(root.GetProperty("signalIdentity").GetBoolean());
        Assert.Equal("Idle", root.GetProperty("signals")[0][0].GetString());
        Assert.Equal("Number", root.GetProperty("signals")[1][0].GetString());
        Assert.Equal(1.5d, root.GetProperty("signals")[1][1].GetDouble());
        Assert.Equal([1, 2], root.GetProperty("signals")[2][1].EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal([3, 4], root.GetProperty("payload")[1].EnumerateArray().Select(value => value.GetInt32()));
        Assert.True(root.GetProperty("arraysAreMutableCarriers").GetBoolean());
    }

    [Fact]
    public void General_CSharp_number_lowering_supports_all_binary64_categories()
    {
        var number = new Copeland.TS.Mir.MirType("number");
        var program = new Copeland.TS.Mir.MirProgram(
            [],
            [
                Function("nan", double.NaN, number),
                Function("positiveInfinity", double.PositiveInfinity, number),
                Function("negativeInfinity", double.NegativeInfinity, number),
                Function("negativeZero", -0d, number),
                Function("large", double.MaxValue, number),
                Function("small", double.Epsilon, number),
            ]);

        CSharpCompilation emitted = CSharpBackend.Emit(program);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(BitConverter.DoubleToUInt64Bits(double.NaN), Bits(Invoke(generated, "nan")));
        Assert.Equal(0x7FF0000000000000UL, Bits(Invoke(generated, "positiveInfinity")));
        Assert.Equal(0xFFF0000000000000UL, Bits(Invoke(generated, "negativeInfinity")));
        Assert.Equal(0x8000000000000000UL, Bits(Invoke(generated, "negativeZero")));
        Assert.Equal(0x7FEFFFFFFFFFFFFFUL, Bits(Invoke(generated, "large")));
        Assert.Equal(1UL, Bits(Invoke(generated, "small")));
    }

    [Fact]
    public void Representative_asset_corpus_is_byte_stable_and_has_no_forbidden_runtime_tokens()
    {
        string corpus = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Copeland",
            "Copeland.TS.Tests",
            "TsonAssets",
            "Corpus",
            "record");
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
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(javascript.Success);

        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.cope"))), Normalize(compilation.MirText!));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.g.cs"))), Normalize(csharp.SourceText!));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.g.js"))), Normalize(javascript.SourceText!));
        foreach (string artifact in new[] { compilation.MirText!, csharp.SourceText!, javascript.SourceText! })
        {
            Assert.DoesNotContain("tsonAsset", artifact, StringComparison.Ordinal);
            Assert.DoesNotContain("settings.obj.ts", artifact, StringComparison.Ordinal);
            Assert.DoesNotContain("TsonDocument", artifact, StringComparison.Ordinal);
            Assert.DoesNotContain("TsonValue", artifact, StringComparison.Ordinal);
        }

        var expectedHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main.cope"] = "9e6bd14910ebbc0862138cd15a000f836b047fedf5e86ab24102e117d09c8d95",
            ["main.g.cs"] = "bbb11645f4f3c00ef6e5c82023d0831b268fe4df791ea063924d32a472f8185d",
            ["main.g.js"] = "647c473707eab2665fc931732b33357ab0c7779f9d55d88958e76ffe5aa7dc7b",
            ["main.ts"] = "662e4abf48cb939fae86ab9a28ce2377f84e917c3440be2bc9d45140f1d3e63f",
            ["settings.obj.ts"] = "d2565ff75f6199ee14444ee607eacabd6d2f0e35c0dc2b3df60436ba05655310",
        };
        foreach ((string fileName, string expectedHash) in expectedHashes)
        {
            string actualHash = Convert.ToHexString(SHA256.HashData(CanonicalFileBytes(Path.Combine(corpus, fileName)))).ToLowerInvariant();
            Assert.Equal(expectedHash, actualHash);
        }
    }

    [Fact]
    public void Array_asset_corpus_is_byte_stable_and_pins_every_artifact_hash()
    {
        string corpus = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Copeland",
            "Copeland.TS.Tests",
            "TsonAssets",
            "Corpus",
            "arrays");
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
        var program = compilation.MirCompilation!.Program!;
        CSharpCompilation csharp = CSharpBackend.Emit(program);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(program);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        Assert.Equal(csharp.SourceText, CSharpBackend.Emit(program).SourceText);
        Assert.Equal(javascript.SourceText, JavaScriptBackend.Emit(program).SourceText);
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.cope"))), Normalize(compilation.MirText!));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.g.cs"))), Normalize(csharp.SourceText));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(corpus, "main.g.js"))), Normalize(javascript.SourceText!));
        foreach (string artifact in new[] { compilation.MirText!, csharp.SourceText, javascript.SourceText! })
        {
            Assert.DoesNotContain("tsonAsset", artifact, StringComparison.Ordinal);
            Assert.DoesNotContain("batch.obj.ts", artifact, StringComparison.Ordinal);
            Assert.DoesNotContain("Tson", artifact, StringComparison.Ordinal);
        }

        var expectedHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["batch.obj.ts"] = "32c551b037fa503a646b4fcc30c983aea8b94f3235bde0b67bae64f963871ede",
            ["main.cope"] = "840a285a4238f341f34aa89348d00e5cdf5677422009192e0150d2c1c7a4b12e",
            ["main.g.cs"] = "a3b97b999c7bc529fd40ac7b38bc860a89a664d6cf5639952f8123f562777015",
            ["main.g.js"] = "de884da8fdaacd96ba8ac92e75076df0df268298f29da65df0c74b7af56f5873",
            ["main.ts"] = "d95366df1041d079075628c8132c44b1325835b4bfbd9ada8a71a0dc033f5e03",
        };
        foreach ((string fileName, string expectedHash) in expectedHashes)
        {
            string actualHash = Convert.ToHexString(SHA256.HashData(CanonicalFileBytes(Path.Combine(corpus, fileName)))).ToLowerInvariant();
            Assert.Equal(expectedHash, actualHash);
        }
    }

    private static Copeland.TS.Mir.MirFunction Function(string name, double value, Copeland.TS.Mir.MirType type)
    {
        return new Copeland.TS.Mir.MirFunction(
            name,
            [],
            type,
            [],
            [new Copeland.TS.Mir.MirReturnStatement(new Copeland.TS.Mir.MirLiteralExpression(value, type))]);
    }

    private static object? Invoke(RoslynCompileResult generated, string name)
    {
        return GeneratedModuleInvoker.Invoke(generated.Assembly!, name);
    }

    private static string ResultValue(object? result)
    {
        object value = result!;
        return Assert.IsType<string>(
            value.GetType()
                .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(value));
    }

    private static ulong Bits(object? value)
    {
        return BitConverter.DoubleToUInt64Bits(Assert.IsType<double>(value));
    }

    private static void AssertArrayAssetCSharpShape(object? loaded)
    {
        object[] envelope = ReadProperties(loaded!);
        Assert.Equal(2, envelope.Length);
        object[] batch = ReadProperties(envelope[0]);
        Assert.Equal(8, batch.Length);

        Assert.Empty(Assert.IsType<double[]>(batch[0]));
        Assert.Equal([true, false], Assert.IsType<bool[]>(batch[1]));
        double[] numbers = Assert.IsType<double[]>(batch[2]);
        Assert.Equal(
            [0UL, 0x8000000000000000UL, 0x3FF8000000000000UL, 0x7FF8000000000000UL, 0x7FF0000000000000UL, 0xFFF0000000000000UL],
            numbers.Select(BitConverter.DoubleToUInt64Bits));
        Assert.Equal(["quote \" slash \\ newline\n", "雪", "😀"], Assert.IsType<string[]>(batch[3]));

        Array entries = Assert.IsAssignableFrom<Array>(batch[4]);
        Assert.Equal("first", ReadProperties(entries.GetValue(0)!)[0]);
        Assert.Equal("second", ReadProperties(entries.GetValue(1)!)[0]);
        Assert.Equal(entries.GetValue(0)!.GetType(), entries.GetValue(1)!.GetType());

        Array signals = Assert.IsAssignableFrom<Array>(batch[5]);
        Assert.Equal("Idle", signals.GetValue(0)!.GetType().Name);
        Assert.Equal("Number", signals.GetValue(1)!.GetType().Name);
        Assert.Equal(1.5d, ReadProperties(signals.GetValue(1)!)[0]);
        Assert.Equal("Values", signals.GetValue(2)!.GetType().Name);
        Assert.Equal([1d, 2d], Assert.IsType<double[]>(ReadProperties(signals.GetValue(2)!)[0]));
        Assert.Equal(signals.GetValue(0)!.GetType().DeclaringType, signals.GetValue(1)!.GetType().DeclaringType);

        double[][] rows = Assert.IsType<double[][]>(batch[6]);
        Assert.Empty(rows[0]);
        Assert.Equal([1d, 2d], rows[1]);
        Array groups = Assert.IsAssignableFrom<Array>(batch[7]);
        Array firstGroup = Assert.IsAssignableFrom<Array>(groups.GetValue(0));
        Assert.Equal("nested", ReadProperties(firstGroup.GetValue(0)!)[0]);
        Assert.Equal(entries.GetValue(0)!.GetType(), firstGroup.GetValue(0)!.GetType());

        object payload = envelope[1];
        Assert.Equal("Values", payload.GetType().Name);
        Assert.Equal([3d, 4d], Assert.IsType<double[]>(ReadProperties(payload)[0]));
        Assert.Equal(signals.GetValue(0)!.GetType().DeclaringType, payload.GetType().DeclaringType);
    }

    private static object[] ReadProperties(object value)
    {
        return value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.DeclaringType == value.GetType() && property.Name != "EqualityContract")
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.GetValue(value))
            .ToArray()!;
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static byte[] CanonicalFileBytes(string path)
    {
        return System.Text.Encoding.UTF8.GetBytes(Normalize(File.ReadAllText(path)));
    }

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
        string path = Path.Combine(Path.GetTempPath(), $"copeland-tson-asset-{Guid.NewGuid():N}.js");
        try
        {
            await File.WriteAllTextAsync(path, source, new UTF8Encoding(false));
            var start = new ProcessStartInfo("node", path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process process = Process.Start(start)!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, stderr);
            return new ProcessResult(stdout.Replace("\r\n", "\n", StringComparison.Ordinal), stderr, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class InMemoryAssetSource(string path, string sourceText) : ICopelandAssetSource
    {
        private readonly string _path = Path.GetFullPath(path);

        public bool TryRead(string normalizedPath, out string? source)
        {
            if (string.Equals(Path.GetFullPath(normalizedPath), _path, StringComparison.OrdinalIgnoreCase))
            {
                source = sourceText;
                return true;
            }

            source = null;
            return false;
        }
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

    private sealed record ProcessResult(string StdOut, string StdErr, int ExitCode);
}
