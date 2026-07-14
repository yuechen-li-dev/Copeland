using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class TsonAssetRuntimeTests
{
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
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(corpus, fileName)))).ToLowerInvariant();
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

    private static ulong Bits(object? value)
    {
        return BitConverter.DoubleToUInt64Bits(Assert.IsType<double>(value));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
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
