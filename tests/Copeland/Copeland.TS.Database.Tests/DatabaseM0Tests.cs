using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Copeland.TS.Database;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Xunit.Abstractions;

namespace Copeland.TS.Database.Tests;

public sealed class DatabaseM0Tests
{
    private readonly ITestOutputHelper _output;

    public DatabaseM0Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Binds_Logic_Free_TsXml_Index_To_Copeland_Record()
    {
        DatabaseDefinitionResult result = BindFixture();

        Assert.True(result.Success, Diagnostics(result));
        DatabaseSchema schema = result.Schema!;
        Assert.Equal("Events", schema.DatabaseName);
        Assert.Equal("Event", schema.RecordName);
        Assert.Equal(["tenant", "year"], schema.PartitionFields);
        Assert.Equal(["value"], schema.StoredFields.Select(field => field.Name));
        Assert.Equal(64, schema.SchemaIdentity.Length);
        Assert.Equal(64, schema.IndexIdentity.Length);
    }

    [Theory]
    [InlineData(
        """
        export default defineDatabase(
            <Database name="Events">
                <Index field="missing"><Table type={Event} /></Index>
            </Database>
        );
        """,
        "COPE-DATABASE-0006")]
    [InlineData(
        """
        export default defineDatabase(
            <Database name="Events">
                <Index field="tenant">
                    <Index field="tenant"><Table type={Event} /></Index>
                </Index>
            </Database>
        );
        """,
        "COPE-DATABASE-0009")]
    [InlineData(
        """
        export default defineDatabase(
            <Database name="Events">
                <Index field="value"><Table type={Event} /></Index>
            </Database>
        );
        """,
        "COPE-DATABASE-0007")]
    public void Reports_Profile_Diagnostics_With_Source_Spans(string definition, string expectedId)
    {
        DatabaseDefinitionResult result = DatabaseDefinitionBinder.Bind(
            File.ReadAllText(FixturePath("schema.ts")),
            definition);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == expectedId
            && diagnostic.Position >= 0
            && diagnostic.Length > 0
            && diagnostic.SourcePath == "index.tsx");
    }

    [Fact]
    public void Builds_Deterministic_Columnar_Leaves_With_Path_Owned_Partition_Keys()
    {
        DatabaseSchema schema = BindFixture().Schema!;
        DatabaseBuildResult first = DatabaseBuilder.Build(schema, FixtureRows());
        DatabaseBuildResult second = DatabaseBuilder.Build(schema, FixtureRows());

        Assert.Equal(7, first.Metrics.RowCount);
        Assert.Equal(3, first.Metrics.LeafCount);
        Assert.Equal(
            ArtifactHashes(first),
            ArtifactHashes(second));
        Assert.Equal(first.GeneratedSource, second.GeneratedSource);

        foreach (DatabaseArtifact leaf in first.Artifacts.Where(artifact =>
                     artifact.RelativePath.EndsWith(".segment", StringComparison.Ordinal)))
        {
            string text = Encoding.UTF8.GetString(leaf.Contents);
            Assert.DoesNotContain("tenant-a", text, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-b", text, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant", text, StringComparison.Ordinal);
            Assert.DoesNotContain("year", text, StringComparison.Ordinal);
            Assert.Contains("value", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Generated_Dll_Consumer_Routes_One_Leaf_And_Reads_Only_Value_Column()
    {
        using var fixture = BuiltFixture.Create();
        Assembly assembly = CompileGeneratedConsumer(fixture.Build.GeneratedSource);
        Type consumer = assembly.GetType("FixtureConsumer", throwOnError: true)!;

        double sum = (double)consumer.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [fixture.Path])!;
        object trace = consumer.GetProperty("Trace", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        Type traceType = trace.GetType();
        var leaves = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            traceType.GetProperty("OpenedLeaves")!.GetValue(trace));
        var columns = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            traceType.GetProperty("ReadColumns")!.GetValue(trace));
        long bytesRead = (long)traceType.GetProperty("BytesRead")!.GetValue(trace)!;

        Assert.Equal(7.75, sum);
        Assert.Single(leaves);
        Assert.Equal(["value"], columns);
        Assert.True(bytesRead > 24);
        Assert.True(bytesRead < fixture.Build.Metrics.BinaryBytes);

        foreach (string unrelatedPath in Directory.GetFiles(Path.Combine(fixture.Path, "leaves")))
        {
            string relativePath = Path.GetRelativePath(fixture.Path, unrelatedPath).Replace('\\', '/');
            if (!leaves.Contains(relativePath, StringComparer.Ordinal))
            {
                File.WriteAllBytes(unrelatedPath, [0x00]);
            }
        }

        double repeated = (double)consumer.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [fixture.Path])!;
        Assert.Equal(7.75, repeated);
    }

    [Fact]
    public void Generated_Query_Does_Not_Read_An_Unrequested_Stored_Column()
    {
        const string schemaSource =
            """
            const $schema: string = "copeland://experimental/events-with-note/v1";
            export record Event {
                tenant: string;
                year: int;
                value: number;
                note: string;
            }
            """;
        DatabaseDefinitionResult binding = DatabaseDefinitionBinder.Bind(
            schemaSource,
            File.ReadAllText(FixturePath("index.tsx")));
        DatabaseRow[] rows =
        [
            new(new Dictionary<string, object>
            {
                ["tenant"] = "tenant-a",
                ["year"] = 2026,
                ["value"] = 1.25,
                ["note"] = "not-read-a",
            }),
            new(new Dictionary<string, object>
            {
                ["tenant"] = "tenant-a",
                ["year"] = 2026,
                ["value"] = 2.50,
                ["note"] = "not-read-b",
            }),
            new(new Dictionary<string, object>
            {
                ["tenant"] = "tenant-a",
                ["year"] = 2026,
                ["value"] = 4.00,
                ["note"] = "not-read-c",
            }),
        ];
        string path = Path.Combine(Path.GetTempPath(), "copeland-database-columns-" + Guid.NewGuid().ToString("N"));
        try
        {
            DatabaseBuildResult build = DatabaseBuilder.Build(binding.Schema!, rows);
            build.WriteToDirectory(path);
            Assembly assembly = CompileGeneratedConsumer(build.GeneratedSource);
            string leafPath = Directory.GetFiles(Path.Combine(path, "leaves")).Single();
            byte[] leaf = File.ReadAllBytes(leafPath);
            leaf[^1] ^= 0xFF;
            File.WriteAllBytes(leafPath, leaf);

            Type consumer = assembly.GetType("FixtureConsumer", throwOnError: true)!;
            double sum = (double)consumer.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [path])!;
            object trace = consumer.GetProperty("Trace", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            var columns = Assert.IsAssignableFrom<IReadOnlyList<string>>(
                trace.GetType().GetProperty("ReadColumns")!.GetValue(trace));

            Assert.Equal(7.75, sum);
            Assert.Equal(["value"], columns);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    [Fact]
    public void Generated_Reader_Rejects_Schema_Mismatch_And_Truncation()
    {
        using var fixture = BuiltFixture.Create();
        Assembly assembly = CompileGeneratedConsumer(fixture.Build.GeneratedSource);
        Type database = assembly.GetType("Copeland.Generated.EventsDatabase", throwOnError: true)!;
        MethodInfo open = database.GetMethod("Open", BindingFlags.Public | BindingFlags.Static)!;
        string rootPath = Path.Combine(fixture.Path, "root.index");
        byte[] original = File.ReadAllBytes(rootPath);

        byte[] mismatch = original.ToArray();
        mismatch[12] ^= 0xFF;
        File.WriteAllBytes(rootPath, mismatch);
        Exception mismatchFailure = Assert.Throws<TargetInvocationException>(() => open.Invoke(null, [fixture.Path]));
        Assert.Contains("schema identity mismatch", mismatchFailure.InnerException!.Message, StringComparison.OrdinalIgnoreCase);

        File.WriteAllBytes(rootPath, original[..20]);
        Exception truncationFailure = Assert.Throws<TargetInvocationException>(() => open.Invoke(null, [fixture.Path]));
        Assert.IsType<InvalidDataException>(truncationFailure.InnerException);
        Assert.Contains("Truncated", truncationFailure.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generated_Reader_Rejects_Corrupted_Requested_Column()
    {
        using var fixture = BuiltFixture.Create();
        Assembly assembly = CompileGeneratedConsumer(fixture.Build.GeneratedSource);
        DatabaseArtifact targetLeaf = fixture.Build.Artifacts
            .Where(artifact => artifact.RelativePath.EndsWith(".segment", StringComparison.Ordinal))
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .First();
        string targetPath = Path.Combine(fixture.Path, targetLeaf.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] bytes = File.ReadAllBytes(targetPath);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(targetPath, bytes);

        Type database = assembly.GetType("Copeland.Generated.EventsDatabase", throwOnError: true)!;
        object instance = database.GetMethod("Open", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [fixture.Path])!;
        MethodInfo query = database.GetMethod("SumValue")!;

        // Identify the corrupted route from root lookup by trying the bounded fixture routes.
        bool checksumRejected = false;
        foreach ((string tenant, int year) in new[] { ("tenant-a", 2025), ("tenant-a", 2026), ("tenant-b", 2026) })
        {
            try
            {
                query.Invoke(instance, [tenant, year]);
            }
            catch (TargetInvocationException exception) when (
                exception.InnerException?.Message.Contains("checksum", StringComparison.OrdinalIgnoreCase) == true)
            {
                checksumRejected = true;
            }
        }

        Assert.True(checksumRejected);
    }

    [Fact]
    public void Records_Bounded_Baseline_Sizes_Without_Claiming_A_Microbenchmark_Win()
    {
        DatabaseSchema schema = BindFixture().Schema!;
        DatabaseRow[] rows = FixtureRows().ToArray();
        DatabaseBuildResult build = DatabaseBuilder.Build(schema, rows);
        byte[] json = File.ReadAllBytes(FixturePath("events.json"));
        int rowBinaryBytes = rows.Length * (sizeof(int) + sizeof(double))
            + rows.Sum(row => Encoding.UTF8.GetByteCount((string)row["tenant"]) + sizeof(int));

        Assert.True(json.Length > 0);
        Assert.True(rowBinaryBytes > 0);
        Assert.True(build.Metrics.BinaryBytes > 0);
        Assert.Equal(3, build.Metrics.LeafCount);
    }

    [Fact]
    public void Records_Open_And_Compiled_Query_Timing_For_The_Bounded_Fixture()
    {
        using var fixture = BuiltFixture.Create();
        Assembly assembly = CompileGeneratedConsumer(fixture.Build.GeneratedSource);
        Type consumer = assembly.GetType("FixtureConsumer", throwOnError: true)!;
        double openMicroseconds = (double)consumer.GetMethod("MeasureOpen")!
            .Invoke(null, [fixture.Path, 1_000])!;
        double queryMicroseconds = (double)consumer.GetMethod("MeasureQuery")!
            .Invoke(null, [fixture.Path, 100_000])!;
        byte[] jsonBytes = File.ReadAllBytes(FixturePath("events.json"));
        byte[] rowBinary = EncodeRowBinary(FixtureRows());
        double jsonScanMicroseconds = Measure(
            10_000,
            () => SumJsonRows(jsonBytes, "tenant-a", 2026));
        double rowScanMicroseconds = Measure(
            100_000,
            () => SumBinaryRows(rowBinary, "tenant-a", 2026));
        long rootBytes = fixture.Build.Artifacts.Single(artifact =>
            artifact.RelativePath == "root.index").Contents.Length;

        _output.WriteLine($"build_ms={fixture.Build.Metrics.BuildTime.TotalMilliseconds:F3}");
        _output.WriteLine($"open_us_per_operation={openMicroseconds:F3}");
        _output.WriteLine($"query_us_per_operation={queryMicroseconds:F3}");
        _output.WriteLine($"json_scan_us_per_operation={jsonScanMicroseconds:F3}");
        _output.WriteLine($"row_binary_scan_us_per_operation={rowScanMicroseconds:F3}");
        _output.WriteLine($"json_bytes={jsonBytes.Length}");
        _output.WriteLine($"row_binary_bytes={rowBinary.Length}");
        _output.WriteLine($"root_bytes={rootBytes}");
        _output.WriteLine($"tree_total_bytes={fixture.Build.Metrics.BinaryBytes}");

        Assert.True(openMicroseconds > 0);
        Assert.True(queryMicroseconds > 0);
        Assert.Equal(7.75, SumJsonRows(jsonBytes, "tenant-a", 2026));
        Assert.Equal(7.75, SumBinaryRows(rowBinary, "tenant-a", 2026));
    }

    private static DatabaseDefinitionResult BindFixture()
        => DatabaseDefinitionBinder.Bind(
            File.ReadAllText(FixturePath("schema.ts")),
            File.ReadAllText(FixturePath("index.tsx")),
            FixturePath("schema.ts"),
            FixturePath("index.tsx"));

    private static IEnumerable<DatabaseRow> FixtureRows()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FixturePath("events.json")));
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            yield return new DatabaseRow(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["tenant"] = item.GetProperty("tenant").GetString()!,
                ["year"] = item.GetProperty("year").GetInt32(),
                ["value"] = item.GetProperty("value").GetDouble(),
            });
        }
    }

    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixture", name);

    private static string Diagnostics(DatabaseDefinitionResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic =>
            $"{diagnostic.Id}: {diagnostic.Message}"));

    private static IReadOnlyList<string> ArtifactHashes(DatabaseBuildResult result)
        => result.Artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => artifact.RelativePath + ":" + Convert.ToHexString(SHA256.HashData(artifact.Contents)))
            .ToArray();

    private static byte[] EncodeRowBinary(IEnumerable<DatabaseRow> rows)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (DatabaseRow row in rows)
        {
            writer.Write((string)row["tenant"]);
            writer.Write((int)row["year"]);
            writer.Write((double)row["value"]);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static double SumJsonRows(byte[] json, string tenant, int year)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        double sum = 0;
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            if (row.GetProperty("tenant").GetString() == tenant
                && row.GetProperty("year").GetInt32() == year)
            {
                sum += row.GetProperty("value").GetDouble();
            }
        }

        return sum;
    }

    private static double SumBinaryRows(byte[] rows, string tenant, int year)
    {
        using var stream = new MemoryStream(rows, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        double sum = 0;
        while (stream.Position < stream.Length)
        {
            string rowTenant = reader.ReadString();
            int rowYear = reader.ReadInt32();
            double value = reader.ReadDouble();
            if (rowTenant == tenant && rowYear == year)
            {
                sum += value;
            }
        }

        return sum;
    }

    private static double Measure(int iterations, Func<double> operation)
    {
        _ = operation();
        var stopwatch = Stopwatch.StartNew();
        double result = 0;
        for (int index = 0; index < iterations; index++)
        {
            result = operation();
        }

        stopwatch.Stop();
        Assert.Equal(7.75, result);
        return stopwatch.Elapsed.TotalMicroseconds / iterations;
    }

    private static Assembly CompileGeneratedConsumer(string generatedSource)
    {
        const string consumerSource =
            """
            public static class FixtureConsumer
            {
                public static Copeland.Generated.DatabaseQueryTrace? Trace { get; private set; }

                public static double Run(string path)
                {
                    using var database = Copeland.Generated.EventsDatabase.Open(path);
                    double result = database.SumValue("tenant-a", 2026);
                    Trace = database.LastQueryTrace;
                    return result;
                }

                public static double MeasureOpen(string path, int iterations)
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    for (int index = 0; index < iterations; index++)
                    {
                        using var database = Copeland.Generated.EventsDatabase.Open(path);
                    }

                    stopwatch.Stop();
                    return stopwatch.Elapsed.TotalMicroseconds / iterations;
                }

                public static double MeasureQuery(string path, int iterations)
                {
                    using var database = Copeland.Generated.EventsDatabase.Open(path);
                    _ = database.SumValue("tenant-a", 2026);
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    double result = 0;
                    for (int index = 0; index < iterations; index++)
                    {
                        result = database.SumValue("tenant-a", 2026);
                    }

                    stopwatch.Stop();
                    if (result != 7.75)
                    {
                        throw new System.InvalidOperationException();
                    }

                    return stopwatch.Elapsed.TotalMicroseconds / iterations;
                }
            }
            """;
        SyntaxTree[] trees =
        [
            CSharpSyntaxTree.ParseText(generatedSource),
            CSharpSyntaxTree.ParseText(consumerSource),
        ];
        string[] trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        MetadataReference[] references = trustedAssemblies
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratedEventDatabase_" + Guid.NewGuid().ToString("N"),
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    private sealed class BuiltFixture : IDisposable
    {
        private BuiltFixture(string path, DatabaseBuildResult build)
        {
            Path = path;
            Build = build;
        }

        public string Path { get; }

        public DatabaseBuildResult Build { get; }

        public static BuiltFixture Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "copeland-database-m0-" + Guid.NewGuid().ToString("N"));
            DatabaseBuildResult build = DatabaseBuilder.Build(BindFixture().Schema!, FixtureRows());
            build.WriteToDirectory(path);
            return new BuiltFixture(path, build);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
