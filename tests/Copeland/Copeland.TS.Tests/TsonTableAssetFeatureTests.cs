using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsonTableAssetFeatureTests
{
    private const string Source = """
        const $schema: string = "copeland://tests/table-assets";
        record Point { x: number; label: string; }
        enum State { Missing, Named(label: string), }
        record table Samples from tsonAsset("./data/../samples.obj.ts") {
            active: boolean;
            score: number;
            label: string;
            point: Point;
            state: State;
            values: number[][];
        }
        function getTable(): Samples { return Samples; }
        """;

    private const string AuthoringAsset = """
        // This comment participates in dependency evidence only.
        const $schema: string = "copeland://tests/table-assets";
        record Point { x: number; label: string; }
        enum State { Missing, Named(label: string), }
        record table Samples {
            active: boolean = [true, false];
            score: number = [$number("8000000000000000"), $number("7FF8000000000001")];
            label: string = ["雪", "😀"];
            point: Point = [{ x: 1, label: "first" }, { x: 2, label: "second" }];
            state: State = [State.Missing, State.Named("ready")];
            values: number[][] = [[[], [1, 2]], [[3], []]];
        }
        const $value = Samples;
        """;

    [Fact]
    public void Parser_preserves_contextual_asset_clause_and_source_schema_spans()
    {
        SyntaxTree tree = SyntaxTree.Parse(Source);

        Assert.Empty(tree.Diagnostics);
        TableDeclarationSyntax declaration = Assert.IsType<TableDeclarationSyntax>(tree.Root.Members[3]);
        TableAssetClauseSyntax clause = Assert.IsType<TableAssetClauseSyntax>(declaration.AssetClause);
        Assert.Equal("from", clause.FromToken.Text);
        Assert.Equal("tsonAsset", Assert.IsType<NameExpressionSyntax>(clause.AssetCall.Target).IdentifierToken.Text);
        Assert.Equal("./data/../samples.obj.ts", Assert.IsType<LiteralExpressionSyntax>(Assert.Single(clause.AssetCall.Arguments)).LiteralToken.Value);
        Assert.All(declaration.Columns, column =>
        {
            Assert.NotNull(column.ExplicitType);
            Assert.False(column.HasInlineData);
            Assert.True(column.Identifier.Text.Length > 0);
            Assert.True(column.ExplicitType!.GetChildren().OfType<SyntaxToken>().Any());
        });

        SyntaxTree contextual = SyntaxTree.Parse("function use(from: number): number { return from; }");
        Assert.Empty(contextual.Diagnostics);
    }

    [Fact]
    public void Declaration_owned_asset_projects_to_one_closed_table_definition()
    {
        var assets = new InMemoryAssetSource(("C:/project/samples.obj.ts", AuthoringAsset));

        CopelandCompilation compilation = Compile(Source, assets);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Equal(1, assets.ReadCount);
        Assert.Single(compilation.AssetDependencies);
        BoundTableDefinition bound = Assert.Single(compilation.BoundCompilation!.Program.Tables);
        Assert.Equal(2, bound.RowCount);
        Assert.Equal(6, bound.Columns.Count);
        Assert.IsType<BoundTableArrayConstant>(bound.Columns[5].Cells[0]);
        MirTableDefinition mir = Assert.Single(compilation.MirCompilation!.Program!.Tables);
        Assert.Equal("t1", mir.Id.Value);
        Assert.Equal("t1.row", mir.RowTypeId);
        Assert.Equal(["t1.c0", "t1.c1", "t1.c2", "t1.c3", "t1.c4", "t1.c5"], mir.Columns.Select(column => column.Id.Value));
        Assert.IsType<MirTableArrayConstant>(mir.Columns[5].Constants[0]);
        Assert.DoesNotContain("tsonAsset", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("samples.obj.ts", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("copeland://", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_and_authoring_profiles_produce_identical_mir_and_comment_only_changes_change_dependency_hash()
    {
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(
            AuthoringAsset,
            TsonDocumentProfile.ObjectTypeScript);
        Assert.True(read.Success);
        string canonical = TsonCanonicalPrinter.Print(read.Document!);
        string canonicalSource = Source.Replace("samples.obj.ts", "samples.tson", StringComparison.Ordinal);

        CopelandCompilation authored = Compile(
            Source,
            new InMemoryAssetSource(("C:/project/samples.obj.ts", AuthoringAsset)));
        CopelandCompilation canonicalCompilation = Compile(
            canonicalSource,
            new InMemoryAssetSource(("C:/project/samples.tson", canonical)));
        CopelandCompilation commentChanged = Compile(
            Source,
            new InMemoryAssetSource((
                "C:/project/samples.obj.ts",
                AuthoringAsset.Replace("This comment", "A changed comment", StringComparison.Ordinal))));

        Assert.True(authored.Success, Describe(authored));
        Assert.True(canonicalCompilation.Success, Describe(canonicalCompilation));
        Assert.True(commentChanged.Success, Describe(commentChanged));
        Assert.Equal(authored.MirText, canonicalCompilation.MirText);
        Assert.Equal(authored.MirText, commentChanged.MirText);
        Assert.NotEqual(
            Assert.Single(authored.AssetDependencies).Sha256,
            Assert.Single(commentChanged.AssetDependencies).Sha256);
    }

    [Fact]
    public void Asset_clause_rejects_inline_data_and_exact_schema_mismatches()
    {
        string inline = Source.Replace("active: boolean;", "active: boolean = [true];", StringComparison.Ordinal);
        CopelandCompilation inlineCompilation = Compile(
            inline,
            new InMemoryAssetSource(("C:/project/samples.obj.ts", AuthoringAsset)));
        Assert.Contains(inlineCompilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ASSET-0001");

        string reordered = Source.Replace(
            "active: boolean;\n    score: number;",
            "score: number;\n    active: boolean;",
            StringComparison.Ordinal);
        CopelandCompilation mismatch = Compile(
            reordered,
            new InMemoryAssetSource(("C:/project/samples.obj.ts", AuthoringAsset)));
        Assert.Contains(mismatch.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ASSET-0003");
    }

    [Fact]
    public void Ordinary_expression_tsonAsset_does_not_construct_a_table()
    {
        string expressionUse = Source.Replace(
            "function getTable(): Samples { return Samples; }",
            "function getTable(): Samples { const other: Samples = tsonAsset(\"./samples.obj.ts\"); return other; }",
            StringComparison.Ordinal);

        CopelandCompilation compilation = Compile(
            expressionUse,
            new InMemoryAssetSource(("C:/project/samples.obj.ts", AuthoringAsset)));

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ASSET-0001");
    }

    [Fact]
    public void Closed_array_constants_defensively_copy_element_storage()
    {
        var elements = new List<MirTableConstant>
        {
            new MirTableLiteralConstant(1d, new MirNamedType("number")),
        };
        var constant = new MirTableArrayConstant(new MirArrayType(new MirNamedType("number")), elements);

        elements.Clear();

        Assert.Single(constant.Elements);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<MirTableConstant>)constant.Elements).Add(
                new MirTableLiteralConstant(2d, new MirNamedType("number"))));
    }

    [Fact]
    public void Filesystem_language_fixtures_have_expected_acceptance()
    {
        string root = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Copeland",
            "Copeland.TS.Tests",
            "TsonTableAssets");
        string[] valid = Directory.GetFiles(
            Path.Combine(root, "Valid"),
            "*.asset-valid.ts",
            SearchOption.AllDirectories);
        string[] invalid = Directory.GetFiles(
            Path.Combine(root, "Invalid"),
            "*.asset-invalid.ts",
            SearchOption.AllDirectories);
        Assert.NotEmpty(valid);
        Assert.NotEmpty(invalid);

        foreach (string path in valid)
        {
            CopelandCompilation compilation = CompileFile(path);
            Assert.True(compilation.Success, path + Environment.NewLine + Describe(compilation));
        }
        foreach (string path in invalid)
        {
            CopelandCompilation compilation = CompileFile(path);
            Assert.False(compilation.Success, path);
            Assert.Contains(
                compilation.Diagnostics,
                diagnostic => diagnostic.Id.StartsWith("COPE-TSON-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Representative_corpus_is_pinned_and_recompiles_to_identical_mir()
    {
        string root = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Copeland",
            "Copeland.TS.Tests",
            "TsonTableAssets",
            "Corpus",
            "representative");
        var expected = new Dictionary<string, (int Length, string Sha256)>(StringComparer.Ordinal)
        {
            ["empty.tson"] = (130, "83290D5672AA58BF14F8F23E8B6F54BB2883C8B47C16418D39971A881D6D173B"),
            ["main.cope"] = (1638, "FBEDC9F2C1B2DF59A159EC2449444154EE262689EA4ECB19E3A7C4FB94916351"),
            ["main.g.cs"] = (14916, "E44594FF253DF2210366616E808F34135D84AE7091FC120A9F3735403F2C1B9F"),
            ["main.g.js"] = (38279, "F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A"),
            ["main.ts"] = (971, "FF124D4067C5BE4A2F8C7242902A04EDDB243F0419E1ABAEA100228EBE8E4CEF"),
            ["samples.obj.ts"] = (660, "0D42F52BABBAC35E584B5D8ECD7B60B9B8DD69ECA58C82D10E065905AAA28761"),
        };
        foreach ((string name, (int length, string sha256)) in expected)
        {
            byte[] bytes = CanonicalFileBytes(Path.Combine(root, name));
            Assert.Equal(length, bytes.Length);
            Assert.Equal(sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        }

        CopelandCompilation first = CompileFile(Path.Combine(root, "main.ts"));
        CopelandCompilation second = CompileFile(Path.Combine(root, "main.ts"));
        Assert.True(first.Success, Describe(first));
        Assert.Equal(first.MirText, second.MirText);
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(root, "main.cope"))), Normalize(first.MirText!));
    }

    private static CopelandCompilation Compile(string source, ICopelandAssetSource assets)
    {
        return CopelandCompiler.CompileToMir(
            source,
            new CopelandCompilationOptions
            {
                SourcePath = "C:/project/main.ts",
                ProjectRoot = "C:/project",
                AssetSource = assets,
            });
    }

    private static byte[] CanonicalFileBytes(string path)
    {
        return System.Text.Encoding.UTF8.GetBytes(Normalize(File.ReadAllText(path)));
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static CopelandCompilation CompileFile(string path)
    {
        return CopelandCompiler.CompileToMir(
            File.ReadAllText(path),
            new CopelandCompilationOptions
            {
                SourcePath = path,
                ProjectRoot = Path.GetDirectoryName(path),
                AssetSource = FileAssetSource.Instance,
            });
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

    private static string Describe(CopelandCompilation compilation)
    {
        return string.Join(
            Environment.NewLine,
            compilation.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Id}|{diagnostic.Position}|{diagnostic.Length}|{diagnostic.Message}"));
    }

    private sealed class InMemoryAssetSource(params (string Path, string Text)[] files) : ICopelandAssetSource
    {
        private readonly Dictionary<string, string> _files = files.ToDictionary(
            file => Path.GetFullPath(file.Path),
            file => file.Text,
            StringComparer.OrdinalIgnoreCase);

        public int ReadCount { get; private set; }

        public bool TryRead(string normalizedPath, out string? sourceText)
        {
            ReadCount++;
            return _files.TryGetValue(Path.GetFullPath(normalizedPath), out sourceText);
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
            catch (IOException)
            {
                sourceText = null;
                return false;
            }
        }
    }
}
