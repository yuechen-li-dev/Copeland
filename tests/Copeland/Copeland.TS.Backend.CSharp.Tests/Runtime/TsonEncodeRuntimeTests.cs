using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class TsonEncodeRuntimeTests
{
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
            ["main.g.js"] = "6FE85C34DE3FDBAD1C4917AE08AE94D8F752F0828EEB0642BD535EB7D25E69D9",
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
            const after = encodeValue(legitimate).$payload[0];
            console.log(before === after);
            console.log(counterfeit);
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal("true\nCopeland JavaScript backend invariant failure.\n", node.StdOut);
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

        string[] overValues = exactValues.ToArray();
        overValues[3] += "a";
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "encodeLimit", overValues)));
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "encodeLimit", new string('a', 262_145), "", "", "")));
        Assert.Equal("OutputLimitExceeded", ResultErrorName(Invoke(generated, "encodeLimit", new string('a', 262_144) + "\uD800", "", "", "")));
        Assert.Equal("InvalidUnicode", ResultErrorName(Invoke(generated, "encodeLimit", "\uD800", "", "", "")));

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
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal("ok:1048576\nOutputLimitExceeded\nOutputLimitExceeded\nInvalidUnicode\n", node.StdOut);
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
