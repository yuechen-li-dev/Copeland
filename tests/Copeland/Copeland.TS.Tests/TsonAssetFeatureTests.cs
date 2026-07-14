using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsonAssetFeatureTests
{
    private const string ProgramSource = """
        const $schema: string = "copeland://tests/assets";

        record Inner {
            label: string;
        }

        enum Mode {
            Off,
            On(score: number),
            Nested(inner: Inner),
        }

        record Settings {
            title: string;
            enabled: boolean;
            value: number;
            inner: Inner;
            mode: Mode;
        }

        function load(): Settings {
            const settings: Settings = tsonAsset("./data/../settings.obj.ts");
            return settings;
        }
        """;

    private const string AuthoringAsset = """
        const $schema: string = "copeland://tests/assets";

        // Object TypeScript assets are data even when comments and layout are noncanonical.
        record Inner { label: string; }
        enum Mode {
            Off,
            On(score: number),
            Nested(inner: Inner),
        }
        record Settings {
            title: string;
            enabled: boolean;
            value: number;
            inner: Inner;
            mode: Mode;
        }

        const $value: Settings = {
            mode: Mode.Nested({ label: "nested 😀" }),
            inner: { label: "line\nquote\"" },
            value: $number("8000000000000000"),
            enabled: true,
            title: "settings",
        };
        """;

    private const string EnumProgramSource = """
        const $schema: string = "copeland://tests/enum-assets";
        record Detail { text: string; }
        enum Choice { None, Count(value: number), Detail(value: Detail), }
        function load(): Choice {
            const choice: Choice = tsonAsset("./choice.obj.ts");
            return choice;
        }
        """;

    private const string EnumAuthoringAsset = """
        const $schema: string = "copeland://tests/enum-assets";
        record Detail { text: string; }
        enum Choice { None, Count(value: number), Detail(value: Detail), }
        const $value: Choice = Choice.Detail({ text: "payload" });
        """;

    [Fact]
    public void ObjectTypeScript_asset_expands_to_existing_bound_and_mir_nodes()
    {
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", AuthoringAsset));

        CopelandCompilation compilation = Compile(ProgramSource, source);

        Assert.True(compilation.Success, Describe(compilation));
        CopelandAssetDependency dependency = Assert.Single(compilation.AssetDependencies);
        Assert.Equal("settings.obj.ts", dependency.NormalizedPath);
        Assert.Equal(64, dependency.Sha256.Length);
        Assert.DoesNotContain("tsonAsset", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.obj.ts", compilation.MirText, StringComparison.Ordinal);
        var function = Assert.Single(compilation.BoundCompilation!.Program.Functions);
        var declaration = Assert.IsType<BoundVariableDeclaration>(function.Body.Statements[0]);
        Assert.IsType<BoundRecordConstructionExpression>(declaration.Initializer);
    }

    [Fact]
    public void Filesystem_owned_record_and_enum_profile_fixtures_compile()
    {
        string fixtureRoot = Path.Combine(GetRepositoryRoot(), "tests", "Copeland", "Copeland.TS.Tests", "TsonAssets", "Valid");
        string[] sources = Directory.GetFiles(fixtureRoot, "*.asset-valid.ts", SearchOption.AllDirectories);
        Assert.Equal(2, sources.Length);

        foreach (string sourcePath in sources.Order(StringComparer.Ordinal))
        {
            string directory = Path.GetDirectoryName(sourcePath)!;
            CopelandCompilation compilation = CopelandCompiler.CompileToMir(
                File.ReadAllText(sourcePath),
                new CopelandCompilationOptions
                {
                    SourcePath = sourcePath,
                    ProjectRoot = directory,
                    AssetSource = FileAssetSource.Instance,
                });

            Assert.True(compilation.Success, sourcePath + Environment.NewLine + Describe(compilation));
            Assert.Single(compilation.AssetDependencies);
        }
    }

    [Fact]
    public void Canonical_asset_uses_the_same_expansion_and_is_deterministic()
    {
        TsonReadResult authored = TsonDocumentReader.ReadSelfDescribed(
            AuthoringAsset,
            TsonDocumentProfile.ObjectTypeScript);
        Assert.True(authored.Success);
        string canonical = TsonCanonicalPrinter.Print(authored.Document!);
        string sourceText = ProgramSource.Replace("settings.obj.ts", "settings.tson", StringComparison.Ordinal);
        var source = new InMemoryAssetSource(("C:/project/settings.tson", canonical));

        CopelandCompilation first = Compile(sourceText, source);
        CopelandCompilation second = Compile(sourceText, source);

        Assert.True(first.Success, Describe(first));
        Assert.Equal(first.MirText, second.MirText);
        Assert.Equal(first.AssetDependencies, second.AssetDependencies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_enum_root_expands_for_both_profiles(bool canonicalProfile)
    {
        TsonReadResult authored = TsonDocumentReader.ReadSelfDescribed(
            EnumAuthoringAsset,
            TsonDocumentProfile.ObjectTypeScript);
        Assert.True(authored.Success);
        string assetText = canonicalProfile
            ? TsonCanonicalPrinter.Print(authored.Document!)
            : EnumAuthoringAsset;
        string extension = canonicalProfile ? ".tson" : ".obj.ts";
        string sourceText = EnumProgramSource.Replace(".obj.ts", extension, StringComparison.Ordinal);
        var source = new InMemoryAssetSource(("C:/project/choice" + extension, assetText));

        CopelandCompilation compilation = Compile(sourceText, source);

        Assert.True(compilation.Success, Describe(compilation));
        var declaration = Assert.IsType<BoundVariableDeclaration>(compilation.BoundCompilation!.Program.Functions[0].Body.Statements[0]);
        var enumValue = Assert.IsType<BoundEnumValueExpression>(declaration.Initializer);
        Assert.Equal("Detail", enumValue.Case.Name);
        Assert.IsType<BoundRecordConstructionExpression>(Assert.Single(enumValue.Arguments));
    }

    [Fact]
    public void Repeated_loads_have_one_dependency_and_two_ordinary_constructions()
    {
        string sourceText = ProgramSource.Replace(
            "return settings;",
            "const again: Settings = tsonAsset(\"./settings.obj.ts\");\n    return again;",
            StringComparison.Ordinal);
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", AuthoringAsset));

        CopelandCompilation compilation = Compile(sourceText, source);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Single(compilation.AssetDependencies);
        Assert.Equal(2, compilation.BoundCompilation!.Program.Functions[0].Body.Statements
            .OfType<BoundVariableDeclaration>()
            .Count(statement => statement.Initializer is BoundRecordConstructionExpression));
    }

    [Fact]
    public void Nested_asset_arrays_lower_to_existing_bound_arrays_with_explicit_types()
    {
        const string program = """
            const $schema: string = "copeland://tests/array-assets";
            record Item { label: string; }
            enum State { Off, On(value: number), }
            record Batch { names: string[]; items: Item[]; states: State[]; rows: number[][]; }
            function load(): Batch {
                const batch: Batch = tsonAsset("./batch.obj.ts");
                return batch;
            }
            """;
        const string asset = """
            const $schema: string = "copeland://tests/array-assets";
            record Item { label: string; }
            enum State { Off, On(value: number), }
            record Batch { names: string[]; items: Item[]; states: State[]; rows: number[][]; }
            const $value: Batch = {
                names: [],
                items: [{ label: "first" }],
                states: [State.Off, State.On(2)],
                rows: [[], [1, 2]],
            };
            """;
        var source = new InMemoryAssetSource(("C:/project/batch.obj.ts", asset));

        CopelandCompilation compilation = Compile(program, source);

        Assert.True(compilation.Success, Describe(compilation));
        var declaration = Assert.IsType<BoundVariableDeclaration>(compilation.BoundCompilation!.Program.Functions[0].Body.Statements[0]);
        var batch = Assert.IsType<BoundRecordConstructionExpression>(declaration.Initializer);
        Assert.All(batch.Initializers, initializer => Assert.IsType<BoundArrayExpression>(initializer.Value));
        Assert.Contains("[]", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Array_asset_lowering_flows_to_runtime_tson_encoding_plan()
    {
        const string sourceText = """
            const $schema: string = "copeland://tests/array-encoding-deferral";
            record Batch { values: number[]; }
            function encode(): string ! TsonEncodeError {
                const batch: Batch = tsonAsset("./batch.obj.ts");
                return tsonEncode(batch);
            }
            """;
        const string asset = """
            const $schema: string = "copeland://tests/array-encoding-deferral";
            record Batch { values: number[]; }
            const $value: Batch = { values: [1, 2], };
            """;
        var source = new InMemoryAssetSource(("C:/project/batch.obj.ts", asset));

        CopelandCompilation compilation = Compile(sourceText, source);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
        Assert.Contains("number[]", compilation.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("function load(): Settings { const x = tsonAsset(\"./settings.obj.ts\"); return x; }", "COPE-TSON-ASSET-0001")]
    [InlineData("function load(): number { const x: number = tsonAsset(\"./settings.obj.ts\"); return x; }", "COPE-TSON-ASSET-0001")]
    [InlineData("function load(): Settings { const p: string = \"./settings.obj.ts\"; const x: Settings = tsonAsset(p); return x; }", "COPE-TSON-ASSET-0001")]
    [InlineData("function load(): Settings { const x: Settings = tsonAsset(\"C:/settings.obj.ts\"); return x; }", "COPE-TSON-ASSET-0002")]
    [InlineData("function load(): Settings { const x: Settings = tsonAsset(\"../settings.obj.ts\"); return x; }", "COPE-TSON-ASSET-0002")]
    [InlineData("function load(): Settings { const x: Settings = tsonAsset(\"./settings.json\"); return x; }", "COPE-TSON-ASSET-0002")]
    public void Intrinsic_and_path_misuse_is_diagnostic(string replacementFunction, string diagnosticId)
    {
        string sourceText = ProgramSource[..ProgramSource.IndexOf("function load", StringComparison.Ordinal)] + replacementFunction;
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", AuthoringAsset));

        CopelandCompilation compilation = Compile(sourceText, source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.Null(compilation.MirCompilation);
    }

    [Fact]
    public void Asset_diagnostics_retain_asset_path_and_asset_span()
    {
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", "const $value = ;"));

        CopelandCompilation compilation = Compile(ProgramSource, source);

        Diagnostic diagnostic = Assert.Single(compilation.Diagnostics, item => item.SourcePath == "settings.obj.ts");
        Assert.StartsWith("COPE-PARSE-", diagnostic.Id, StringComparison.Ordinal);
        Assert.True(diagnostic.Length > 0);
        Assert.Contains("settings.obj.ts", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_shaped_wrong_identity_is_rejected()
    {
        string wrong = AuthoringAsset.Replace("copeland://tests/assets", "copeland://tests/other", StringComparison.Ordinal);
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", wrong));

        CopelandCompilation compilation = Compile(ProgramSource, source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic =>
            diagnostic.Id == "COPE-TSON-ASSET-0003"
            && diagnostic.Message.Contains("copeland://tests/assets#Settings", StringComparison.Ordinal));
    }

    [Fact]
    public void Structural_object_root_is_rejected_as_an_unsupported_compiled_value()
    {
        const string structuralAsset = """
            const $schema: string = "copeland://tests/assets";
            const $value = { title: "settings", enabled: true };
            """;
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", structuralAsset));

        CopelandCompilation compilation = Compile(ProgramSource, source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ASSET-0005");
    }

    [Fact]
    public void Noncanonical_canonical_asset_is_rejected_by_existing_diagnostic()
    {
        var source = new InMemoryAssetSource(("C:/project/settings.tson", AuthoringAsset));
        string sourceText = ProgramSource.Replace("settings.obj.ts", "settings.tson", StringComparison.Ordinal);

        CopelandCompilation compilation = Compile(sourceText, source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-0005");
    }

    [Fact]
    public void Schema_metadata_is_not_emitted_and_duplicate_or_malformed_forms_fail()
    {
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", AuthoringAsset));
        CopelandCompilation valid = Compile(ProgramSource, source);
        CopelandCompilation duplicate = Compile(
            ProgramSource.Replace(
                "const $schema: string = \"copeland://tests/assets\";",
                "const $schema: string = \"copeland://tests/assets\";\nconst $schema: string = \"copeland://tests/other\";",
                StringComparison.Ordinal),
            source);
        CopelandCompilation malformed = Compile(
            ProgramSource.Replace("copeland://tests/assets", "not a schema", StringComparison.Ordinal),
            source);

        Assert.DoesNotContain("$schema", valid.MirText, StringComparison.Ordinal);
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ASSET-0004");
        Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ASSET-0004");
    }

    [Fact]
    public void Asset_resource_limit_failure_retains_existing_Tson_diagnostic()
    {
        string oversized = new(' ', 1_048_577);
        var source = new InMemoryAssetSource(("C:/project/settings.obj.ts", oversized));

        CopelandCompilation compilation = Compile(ProgramSource, source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic =>
            diagnostic.Id == "COPE-TSON-0005"
            && diagnostic.SourcePath == "settings.obj.ts");
    }

    [Theory]
    [InlineData("function tsonAsset(): number { return 1; }")]
    [InlineData("record tsonAsset { value: number; }")]
    [InlineData("enum tsonAsset { Value, }")]
    [InlineData("function load(): number { const tsonAsset: number = 1; return tsonAsset; }")]
    public void Intrinsic_cannot_be_redefined_or_shadowed(string declaration)
    {
        CopelandCompilation compilation = Compile(declaration, new InMemoryAssetSource());

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ASSET-0001");
    }

    private static CopelandCompilation Compile(string sourceText, ICopelandAssetSource source)
    {
        return CopelandCompiler.CompileToMir(
            sourceText,
            new CopelandCompilationOptions
            {
                SourcePath = "C:/project/main.ts",
                ProjectRoot = "C:/project",
                AssetSource = source,
            });
    }

    private static string Describe(CopelandCompilation compilation)
    {
        return string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
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

        throw new InvalidOperationException("Could not locate the repository root.");
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

    private sealed class InMemoryAssetSource : ICopelandAssetSource
    {
        private readonly IReadOnlyDictionary<string, string> _files;

        public InMemoryAssetSource(params (string Path, string Text)[] files)
        {
            _files = files.ToDictionary(
                file => Path.GetFullPath(file.Path),
                file => file.Text,
                StringComparer.OrdinalIgnoreCase);
        }

        public bool TryRead(string normalizedPath, out string? sourceText)
        {
            return _files.TryGetValue(Path.GetFullPath(normalizedPath), out sourceText);
        }
    }
}
