using Copeland.TS.Syntax;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsonFeatureTests
{
    private const string SchemaIdentity = "copeland://tests/tson";

    private const string AuthoringDocument = """
        // Authoring comments and field order are intentionally noncanonical.
        enum Role {
            Guest,
            Named(label: string),
        }

        record User {
            name: string;
            metadata: $object;
            role: Role;
        }

        const $value: User = {
            role: Role.Named("maintainer"),
            metadata: { "second": -0, first: true },
            name: "Ada 😀\nLovelace",
        };
        """;

    [Fact]
    public void Both_profiles_share_parser_and_round_trip_semantics_and_bytes()
    {
        var authored = TsonDocumentReader.DecodeAuthoringValue(AuthoringDocument, SchemaIdentity);

        Assert.True(authored.Success, Describe(authored));
        var canonicalText = TsonCanonicalPrinter.Print(authored.Document!);
        var canonical = TsonDocumentReader.ReadSelfDescribed(
            canonicalText,
            TsonDocumentProfile.CanonicalTson);

        Assert.True(canonical.Success, Describe(canonical));
        AssertEquivalent(authored.Document!, canonical.Document!);
        Assert.Equal(canonicalText, TsonCanonicalPrinter.Print(canonical.Document!));
        Assert.Equal(
            TsonCanonicalPrinter.PrintUtf8(authored.Document!),
            TsonCanonicalPrinter.PrintUtf8(canonical.Document!));
        var utf8 = TsonCanonicalPrinter.PrintUtf8(canonical.Document!);
        Assert.False(utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF);
        Assert.Contains("const $schema: string = \"copeland://tests/tson\";", canonicalText, StringComparison.Ordinal);
        Assert.Contains("$record.User", canonicalText, StringComparison.Ordinal);
        Assert.Contains("$number(\"8000000000000000\")", canonicalText, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_binary64_form_preserves_complete_categories_and_normalizes_nan()
    {
        var source = Envelope("""
            const $value = {
                finite: $number("400921FB54442D18"),
                negative: $number("C00921FB54442D18"),
                negativeZero: $number("8000000000000000"),
                nan: $number("7FF0000000000001"),
                positiveInfinity: $number("7FF0000000000000"),
                negativeInfinity: $number("FFF0000000000000"),
            };
            """);

        var authoring = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript);

        Assert.True(authoring.Success, Describe(authoring));
        var value = Assert.IsType<TsonObject>(authoring.Document!.Root);
        Assert.Equal(0x8000000000000000UL, Assert.IsType<TsonNumber>(value.Fields[2].Value).Bits);
        Assert.Equal(0x7FF8000000000000UL, Assert.IsType<TsonNumber>(value.Fields[3].Value).Bits);
        Assert.True(double.IsPositiveInfinity(Assert.IsType<TsonNumber>(value.Fields[4].Value).Value));
        Assert.True(double.IsNegativeInfinity(Assert.IsType<TsonNumber>(value.Fields[5].Value).Value));

        var canonical = TsonCanonicalPrinter.Print(authoring.Document);
        Assert.Contains("7FF8000000000000", canonical, StringComparison.Ordinal);
        Assert.True(TsonDocumentReader.ReadSelfDescribed(canonical, TsonDocumentProfile.CanonicalTson).Success);
    }

    [Fact]
    public void Same_shaped_nominals_have_stable_distinct_identities()
    {
        var source = Envelope("""
            record Left { value: number; }
            record Right { value: number; }
            enum First { Same, }
            enum Second { Same, }
            const $value = {
                left: $record.Left({ value: 1 }),
                right: $record.Right({ value: 1 }),
                first: First.Same,
                second: Second.Same,
            };
            """);

        var result = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript);

        Assert.True(result.Success, Describe(result));
        var root = Assert.IsType<TsonObject>(result.Document!.Root);
        var left = Assert.IsType<TsonRecord>(root.Fields[0].Value);
        var right = Assert.IsType<TsonRecord>(root.Fields[1].Value);
        var first = Assert.IsType<TsonEnum>(root.Fields[2].Value);
        var second = Assert.IsType<TsonEnum>(root.Fields[3].Value);
        Assert.Equal("copeland://tests/tson#Left", left.Identity);
        Assert.Equal("copeland://tests/tson#Left.value", left.Fields[0].Identity);
        Assert.NotEqual(left.Identity, right.Identity);
        Assert.NotEqual(first.EnumIdentity, second.EnumIdentity);
        Assert.NotEqual(first.CaseIdentity, second.CaseIdentity);
    }

    [Theory]
    [InlineData("function run(): number { return 1; } const $value = 1;", "COPE-TSON-0002")]
    [InlineData("const $value = run();", "COPE-TSON-0002")]
    [InlineData("let $value = 1;", "COPE-TSON-0002")]
    [InlineData("const other = 1; const $value = 1;", "COPE-TSON-0001")]
    [InlineData("const $value = 1; const $value = 2;", "COPE-TSON-0001")]
    [InlineData("const $value = value;", "COPE-TSON-0002")]
    [InlineData("const $value = null;", "COPE-TSON-0004")]
    [InlineData("const $value = undefined;", "COPE-TSON-0002")]
    [InlineData("const $value = [1];", "COPE-TSON-0004")]
    [InlineData("const $value: number ! string = 1;", "COPE-TSON-0003")]
    [InlineData("record table Values { items: [1]; } const $value = 1;", "COPE-TSON-0002")]
    [InlineData("const $value = 1 + 2;", "COPE-TSON-0002")]
    [InlineData("const $value = { x: 1, x: 2 };", "COPE-TSON-0004")]
    public void Restriction_pass_rejects_non_data_syntax(string body, string expectedCode)
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            Envelope(body),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode && diagnostic.Length > 0);
    }

    [Theory]
    [InlineData("while (true) { break; } const $value = true;")]
    [InlineData("for (;;) { continue; } const $value = true;")]
    [InlineData("if (true) { return; } const $value = true;")]
    [InlineData("const $value = target = 1;")]
    [InlineData("record Bad { method(): number; } const $value = true;")]
    [InlineData("record Bad { get value(): number; } const $value = true;")]
    [InlineData("const $value = { [name]: 1 };")]
    [InlineData("const $value = { ...other };")]
    [InlineData("import value; const $value = true;")]
    [InlineData("class Value {} const $value = true;")]
    public void Executable_or_foreign_forms_never_become_tson(string body)
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            Envelope(body),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.False(result.Success);
        Assert.True(result.SyntaxDiagnostics.Count > 0 || result.Diagnostics.Count > 0);
        Assert.All(result.Diagnostics, diagnostic => Assert.True(diagnostic.Length > 0));
    }

    [Theory]
    [InlineData(
        "record A { value: number; } record B { value: number; } const $value: B = $record.A({ value: 1 });",
        "Record 'A' does not match")]
    [InlineData(
        "enum A { Same, } enum B { Same, } const $value: B = A.Same;",
        "Enum 'A' does not match")]
    public void Same_shape_does_not_cross_nominal_boundaries(string body, string messagePart)
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            Envelope(body),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "COPE-TSON-0004"
                && diagnostic.Message.Contains(messagePart, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("record User { name: string; } const $value: User = {};", "Missing field")]
    [InlineData("record User { name: string; } const $value: User = { name: \"A\", extra: 1 };", "Unknown field")]
    [InlineData("record User { name: string; } const $value: User = { name: 1 };", "does not match")]
    [InlineData("record User { name: string; name: string; } const $value = 1;", "Duplicate field")]
    [InlineData("enum Role { Named(label: string), } const $value = Role.Missing;", "Unknown enum case")]
    [InlineData("enum Role { Named(label: string), } const $value = Role.Named();", "requires 1 payload")]
    [InlineData("enum Role { Named(label: string), } const $value = Role.Named(1);", "does not match")]
    [InlineData("record A { next: A; } const $value = 1;", "Recursive TSON schema")]
    public void Catalog_and_value_validation_are_deterministic(string body, string messagePart)
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            Envelope(body),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code is "COPE-TSON-0003" or "COPE-TSON-0004"
                && diagnostic.Message.Contains(messagePart, StringComparison.Ordinal)
                && diagnostic.Length > 0);
    }

    [Fact]
    public void Ordinary_parser_diagnostics_are_retained_without_tson_translation()
    {
        var source = Envelope("const $value = { broken: ; };");
        var ordinary = SyntaxTree.Parse(source);
        var tson = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript);

        Assert.NotEmpty(ordinary.Diagnostics);
        Assert.Equal(ordinary.Diagnostics, tson.SyntaxDiagnostics);
        Assert.Empty(tson.Diagnostics);
    }

    [Fact]
    public void Profiles_enforce_identity_and_canonicality_laws()
    {
        const string withoutIdentity = "const $value = true;";
        var missingAuthoringIdentity = TsonDocumentReader.ReadSelfDescribed(
            withoutIdentity,
            TsonDocumentProfile.ObjectTypeScript);
        var suppliedAuthoringIdentity = TsonDocumentReader.DecodeAuthoringValue(
            withoutIdentity,
            SchemaIdentity);
        var missingCanonicalIdentity = TsonDocumentReader.ReadSelfDescribed(
            withoutIdentity,
            TsonDocumentProfile.CanonicalTson);

        Assert.Contains(missingAuthoringIdentity.Diagnostics, item => item.Code == "COPE-TSON-0003");
        Assert.True(suppliedAuthoringIdentity.Success, Describe(suppliedAuthoringIdentity));
        Assert.Contains(missingCanonicalIdentity.Diagnostics, item => item.Code == "COPE-TSON-0001");

        var noncanonical = Envelope("const $value=true;");
        var canonicalResult = TsonDocumentReader.ReadSelfDescribed(
            noncanonical,
            TsonDocumentProfile.CanonicalTson);
        Assert.Contains(canonicalResult.Diagnostics, item => item.Code == "COPE-TSON-0005");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("nesting")]
    [InlineData("declarations")]
    [InlineData("fields")]
    [InlineData("cases")]
    [InlineData("payloads")]
    [InlineData("nodes")]
    [InlineData("string")]
    public void Every_resource_limit_has_a_tson_diagnostic(string limit)
    {
        var source = Envelope("const $value = true;");
        var limits = limit switch
        {
            "source" => new TsonLimits(maximumSourceLength: 10),
            "nesting" => new TsonLimits(maximumNestingDepth: 1),
            "declarations" => new TsonLimits(maximumDeclarationCount: 1),
            "fields" => new TsonLimits(maximumFieldsPerAggregate: 1),
            "cases" => new TsonLimits(maximumEnumCases: 1),
            "payloads" => new TsonLimits(maximumPayloadsPerCase: 1),
            "nodes" => new TsonLimits(maximumValueNodeCount: 1),
            "string" => new TsonLimits(maximumStringLength: 1),
            _ => throw new InvalidOperationException(),
        };

        source = limit switch
        {
            "nesting" => Envelope("const $value = { x: true };"),
            "declarations" => Envelope("record A {} record B {} const $value = true;"),
            "fields" => Envelope("const $value = { a: 1, b: 2 };"),
            "cases" => Envelope("enum E { A, B, } const $value = E.A;"),
            "payloads" => Envelope("enum E { A(x: number, y: number), } const $value = E.A(1, 2);"),
            "nodes" => Envelope("const $value = { x: true };"),
            "string" => Envelope("const $value = \"ab\";"),
            _ => source,
        };

        var result = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript,
            limits: limits);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "COPE-TSON-0005" && diagnostic.Length > 0);
    }

    [Fact]
    public void Semantic_constructors_defensively_copy_and_do_not_install_value_equality()
    {
        var mutable = new List<TsonField> { new("one", TsonBoolean.True) };
        var value = new TsonObject(mutable);
        mutable.Add(new TsonField("two", TsonBoolean.False));

        Assert.Single(value.Fields);
        Assert.NotEqual(new TsonString("same"), new TsonString("same"));
        Assert.Throws<ArgumentException>(() => new TsonObject([
            new TsonField("duplicate", TsonBoolean.True),
            new TsonField("duplicate", TsonBoolean.False),
        ]));
        Assert.Throws<ArgumentException>(() => new TsonArray(
            new TsonArraySchema(TsonTypeReference.Number),
            [TsonBoolean.True]));
        Assert.Throws<ArgumentException>(() => new TsonString("\uD800"));
    }

    [Fact]
    public void Unicode_surrogate_escape_pair_projects_to_one_scalar_and_prints_stably()
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            Envelope("const $value = \"\\uD83D\\uDE00\";"),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.True(result.Success, Describe(result));
        Assert.Equal("😀", Assert.IsType<TsonString>(result.Document!.Root).Value);
        var canonical = TsonCanonicalPrinter.Print(result.Document);
        Assert.True(TsonDocumentReader.ReadSelfDescribed(canonical, TsonDocumentProfile.CanonicalTson).Success);
    }

    [Fact]
    public void Arrays_are_homogeneous_contextual_and_canonical()
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            Envelope("""
                record Item { label: string; }
                record Batch { names: string[]; rows: number[][]; items: Item[]; }
                const $value: Batch = {
                    names: [],
                    rows: [[], [1, 2]],
                    items: [{ label: "first" }],
                };
                """),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.True(result.Success, Describe(result));
        var batch = Assert.IsType<TsonRecord>(result.Document!.Root);
        var names = Assert.IsType<TsonArray>(batch.Fields[0].Value);
        var rows = Assert.IsType<TsonArray>(batch.Fields[1].Value);
        Assert.Empty(names.Elements);
        Assert.Equal(TsonTypeKind.String, names.Schema.ElementType.Kind);
        Assert.Equal(2, rows.Elements.Count);
        Assert.Equal(TsonTypeKind.Array, rows.Schema.ElementType.Kind);

        string canonical = TsonCanonicalPrinter.Print(result.Document);
        var reparsed = TsonDocumentReader.ReadSelfDescribed(canonical, TsonDocumentProfile.CanonicalTson);
        Assert.True(reparsed.Success, Describe(reparsed));
        Assert.Equal(canonical, TsonCanonicalPrinter.Print(reparsed.Document!));
    }

    [Theory]
    [InlineData(99_999, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    public void Array_length_boundary_is_exact_and_reports_the_array_span(int length, bool succeeds)
    {
        string source = CreatePrimitiveArrayDocument(length);
        var limits = new TsonLimits(maximumValueNodeCount: length + 2);

        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript,
            limits: limits);

        Assert.Equal(succeeds, result.Success);
        if (!succeeds)
        {
            TsonDiagnostic diagnostic = Assert.Single(result.Diagnostics, item =>
                item.Message.Contains("Array length exceeds", StringComparison.Ordinal));
            Assert.True(diagnostic.Length > 0);
            Assert.InRange(diagnostic.Position, 0, source.Length - 1);
        }
    }

    [Fact]
    public void Array_depth_and_total_node_boundaries_are_bounded_without_stack_overflow()
    {
        var depthLimits = new TsonLimits(maximumNestingDepth: 64);
        TsonReadResult deepestAccepted = TsonDocumentReader.ReadSelfDescribed(
            CreateNestedPrimitiveArrayDocument(62),
            TsonDocumentProfile.ObjectTypeScript,
            limits: depthLimits);
        TsonReadResult firstRejected = TsonDocumentReader.ReadSelfDescribed(
            CreateNestedPrimitiveArrayDocument(63),
            TsonDocumentProfile.ObjectTypeScript,
            limits: depthLimits);

        Assert.True(deepestAccepted.Success, Describe(deepestAccepted));
        Assert.False(firstRejected.Success);
        Assert.Contains(firstRejected.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Semantic nesting", StringComparison.Ordinal)
            && diagnostic.Length > 0);

        var exactNodeLimit = new TsonLimits(maximumValueNodeCount: 12);
        TsonReadResult exactNodeCount = TsonDocumentReader.ReadSelfDescribed(
            CreatePrimitiveArrayDocument(10),
            TsonDocumentProfile.ObjectTypeScript,
            limits: exactNodeLimit);
        TsonReadResult overNodeCount = TsonDocumentReader.ReadSelfDescribed(
            CreatePrimitiveArrayDocument(11),
            TsonDocumentProfile.ObjectTypeScript,
            limits: exactNodeLimit);
        TsonReadResult emptyArrayCountsAsNode = TsonDocumentReader.ReadSelfDescribed(
            CreatePrimitiveArrayDocument(0),
            TsonDocumentProfile.ObjectTypeScript,
            limits: new TsonLimits(maximumValueNodeCount: 1));

        Assert.True(exactNodeCount.Success, Describe(exactNodeCount));
        Assert.False(overNodeCount.Success);
        Assert.Contains(overNodeCount.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Value-node count", StringComparison.Ordinal));
        Assert.False(emptyArrayCountsAsNode.Success);
        Assert.Contains(emptyArrayCountsAsNode.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Value-node count", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicitly_typed_array_root_is_rejected_by_the_root_law()
    {
        var result = TsonDocumentReader.ReadSelfDescribed(
            Envelope("const $value: number[] = [1, 2];"),
            TsonDocumentProfile.ObjectTypeScript);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "COPE-TSON-0004"
            && diagnostic.Message.Contains("cannot be the document root", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("record Batch { names: string[]; } const $value: Batch = { names: [\"Ada\", 1] };", "does not match")]
    [InlineData("const $value = [];", "requires an authoritative")]
    [InlineData("record Item { label: string; } record Other { label: string; } record Batch { items: Item[]; } const $value: Batch = { items: [{ label: \"ok\" }, $record.Other({ label: \"wrong\" })] };", "does not match")]
    [InlineData("record Batch { rows: number[][]; } const $value: Batch = { rows: [[1], [\"wrong\"]] };", "does not match")]
    public void Array_validation_preserves_exact_schema_evidence(string body, string message)
    {
        var result = TsonDocumentReader.ReadSelfDescribed(Envelope(body), TsonDocumentProfile.ObjectTypeScript);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "COPE-TSON-0004"
            && diagnostic.Message.Contains(message, StringComparison.Ordinal)
            && diagnostic.Length > 0);
    }

    private static string Envelope(string body)
    {
        return $"const $schema: string = \"{SchemaIdentity}\";{Environment.NewLine}{body}";
    }

    private static string CreatePrimitiveArrayDocument(int length)
    {
        return Envelope($"record Batch {{ values: number[]; }} const $value: Batch = {{ values: [{string.Join(',', Enumerable.Repeat("1", length))}], }};");
    }

    private static string CreateNestedPrimitiveArrayDocument(int arrayDepth)
    {
        string type = "number" + string.Concat(Enumerable.Repeat("[]", arrayDepth));
        string value = string.Concat(Enumerable.Repeat("[", arrayDepth))
            + "1"
            + string.Concat(Enumerable.Repeat("]", arrayDepth));
        return Envelope($"record Batch {{ values: {type}; }} const $value: Batch = {{ values: {value}, }};");
    }

    private static string Describe(TsonReadResult result)
    {
        return string.Join(
            Environment.NewLine,
            result.SyntaxDiagnostics.Select(item => $"{item.Id}: {item.Message}")
                .Concat(result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
    }

    private static void AssertEquivalent(TsonDocument expected, TsonDocument actual)
    {
        Assert.Equal(expected.Catalog.SchemaIdentity, actual.Catalog.SchemaIdentity);
        Assert.Equal(
            expected.Catalog.Definitions.Select(item => (item.Name, item.Identity)),
            actual.Catalog.Definitions.Select(item => (item.Name, item.Identity)));
        AssertEquivalent(expected.Root, actual.Root);
    }

    private static void AssertEquivalent(TsonValue expected, TsonValue actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case TsonBoolean boolean:
                Assert.Equal(boolean.Value, Assert.IsType<TsonBoolean>(actual).Value);
                break;
            case TsonNumber number:
                Assert.Equal(number.Bits, Assert.IsType<TsonNumber>(actual).Bits);
                break;
            case TsonString text:
                Assert.Equal(text.Value, Assert.IsType<TsonString>(actual).Value);
                break;
            case TsonArray array:
                var actualArray = Assert.IsType<TsonArray>(actual);
                Assert.Equal(array.Schema.ElementType.Kind, actualArray.Schema.ElementType.Kind);
                Assert.Equal(array.Elements.Count, actualArray.Elements.Count);
                for (var index = 0; index < array.Elements.Count; index++)
                {
                    AssertEquivalent(array.Elements[index], actualArray.Elements[index]);
                }

                break;
            case TsonObject @object:
                AssertFieldsEquivalent(@object.Fields, Assert.IsType<TsonObject>(actual).Fields);
                break;
            case TsonRecord record:
                var actualRecord = Assert.IsType<TsonRecord>(actual);
                Assert.Equal(record.Identity, actualRecord.Identity);
                AssertFieldsEquivalent(record.Fields, actualRecord.Fields);
                break;
            case TsonEnum @enum:
                var actualEnum = Assert.IsType<TsonEnum>(actual);
                Assert.Equal(@enum.EnumIdentity, actualEnum.EnumIdentity);
                Assert.Equal(@enum.CaseIdentity, actualEnum.CaseIdentity);
                Assert.Equal(@enum.CaseName, actualEnum.CaseName);
                AssertFieldsEquivalent(@enum.Payloads, actualEnum.Payloads);
                break;
        }
    }

    private static void AssertFieldsEquivalent(
        IReadOnlyList<TsonField> expected,
        IReadOnlyList<TsonField> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].Identity, actual[index].Identity);
            AssertEquivalent(expected[index].Value, actual[index].Value);
        }
    }
}
