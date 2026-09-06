using Copeland.SpanAllocation;
using Copeland.TS.Assets;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ObjectAssetsM15Tests
{
    [Fact]
    public void Span_allocator_resolves_exact_surplus_and_underflow_without_sprite_dependencies()
    {
        SpanAllocationRequest<string>[] requests =
        [
            SpanAllocationRequest<string>.Fixed("cap", 10),
            SpanAllocationRequest<string>.Flex("rail-a", 10, 1),
            SpanAllocationRequest<string>.Flex("rail-b", 10, 2),
        ];

        SpanAllocationResult<string> exact = SpanAllocator.Resolve(30, requests);
        Assert.Equal(SpanAllocationStatus.Exact, exact.Status);
        Assert.Equal([10, 10, 10], exact.Placements.Select(placement => placement.Length));

        SpanAllocationResult<string> surplus = SpanAllocator.Resolve(60, requests);
        Assert.Equal(SpanAllocationStatus.SurplusDistributed, surplus.Status);
        Assert.Equal([10, 20, 30], surplus.Placements.Select(placement => placement.Length));
        Assert.Equal([0, 10, 30], surplus.Placements.Select(placement => placement.Offset));

        SpanAllocationResult<string> underflow = SpanAllocator.Resolve(15, requests);
        Assert.Equal(SpanAllocationStatus.Underflow, underflow.Status);
        Assert.Equal(15, underflow.DeficitLength);
        Assert.Equal([10, 5, 0], underflow.Placements.Select(placement => placement.Length));
        Assert.Single(underflow.Diagnostics, diagnostic => diagnostic.Code == "COPE-SPAN-ALLOC-0100");
    }

    [Fact]
    public void Span_allocator_is_generic_deterministic_and_rejects_invalid_requests()
    {
        var requests = new[]
        {
            SpanAllocationRequest<Payload>.Flex(new Payload("first"), 0, 1),
            SpanAllocationRequest<Payload>.Flex(new Payload("second"), 0, 1),
        };

        SpanAllocationResult<Payload> first = SpanAllocator.Resolve(3, requests);
        SpanAllocationResult<Payload> second = SpanAllocator.Resolve(3, requests);
        Assert.Equal([2, 1], first.Placements.Select(placement => placement.Length));
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Placements, second.Placements);
        Assert.Equal(first.Diagnostics, second.Diagnostics);

        SpanAllocationResult<string> invalid = SpanAllocator.Resolve(
            10,
            [SpanAllocationRequest<string>.Flex("bad", 0, 0)]);
        Assert.Equal(SpanAllocationStatus.Rejected, invalid.Status);
        Assert.Single(invalid.Diagnostics, diagnostic => diagnostic.Code == "COPE-SPAN-ALLOC-0005");
    }

    [Fact]
    public void Sunkill_obj_ts_executes_at_compile_time_and_lowers_deterministically()
    {
        string sourcePath = Path.Combine(
            RepositoryRoot(),
            "samples",
            "Integrations",
            "Aurelian.Ariadne.VnDemo",
            "Assets",
            "sunkill-dialogue-panel.obj.ts");

        ObjectAssetCompilationResult compilation = ObjectAssetCompiler.CompileFile(sourcePath);

        Assert.True(compilation.Success, Describe(compilation));
        ObjectAssetDocument document = compilation.Document!;
        ObjectAssetPanel panel = Assert.Single(document.Panels);
        Assert.Equal("sunkill.ui.atlas", document.Texture.Id);
        Assert.Equal(9, panel.Top.Segments.Count);
        Assert.Equal(3, panel.Top.Segments[4].Weight);
        Assert.Equal(44, panel.Top.Segments[4].MinimumLength);
        Assert.Equal(ObjectAssetSampling.Tile, panel.Top.Segments[2].Sampling);
        Assert.True(panel.MinimumWidth > 0);
        Assert.All(panel.Top.Segments, segment => Assert.Contains(segment.RegionId, document.Regions.Select(region => region.Id)));

        ObjectAssetBuildOutputs first = ObjectAssetCompiler.Emit(document, sourcePath);
        ObjectAssetBuildOutputs second = ObjectAssetCompiler.Emit(document, sourcePath);
        Assert.Equal(first, second);
        Assert.Contains("GENERATED from sunkill-dialogue-panel.obj.ts", first.Toml, StringComparison.Ordinal);
        Assert.Contains("[[programmable_panels.\"dialogue\".top]]", first.Toml, StringComparison.Ordinal);
        Assert.Contains("\"sourceSha256\"", first.AuditJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Object_asset_diagnostics_are_source_located()
    {
        const string source = """
            record AssetObject { schemaVersion: int; id: string; }
            function build(): AssetObject { return { schemaVersion: 1, id: "bad id" }; }
            const $asset: AssetObject = static build();
            """;
        string sourcePath = Path.Combine(Path.GetTempPath(), "bad.obj.ts");

        ObjectAssetCompilationResult result = ObjectAssetCompiler.Compile(source, sourcePath);

        Assert.False(result.Success);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(Path.GetFullPath(sourcePath), diagnostic.SourcePath));
    }

    private static string Describe(ObjectAssetCompilationResult result)
    {
        return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record Payload(string Id);
}
