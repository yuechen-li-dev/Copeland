using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ComputeStorageTests
{
    [Theory]
    [InlineData("HumanoidSkinning.v.ts")]
    [InlineData("Cloth3D.v.ts")]
    public void Production_compute_assets_lower(string name)
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", name));
        var module = GpuComputeBinder.Compile(new([new(name, source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var strides = name == "HumanoidSkinning.v.ts"
            ? new Dictionary<string, int> { ["RestVertex"] = 96, ["DualQuaternion"] = 32, ["PoseHeader"] = 76, ["SolidVertex"] = 40 }
            : new Dictionary<string, int> { ["ClothHeader"] = 128, ["ClothParticle"] = 64, ["ClothAnchor"] = 16, ["ClothStencil"] = 48 };
        foreach (var pair in strides)
        {
            Assert.Equal(pair.Value, module.StorageLayouts!.Single(layout => layout.Type == pair.Key).Stride);
        }
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
    }

    public const string Source = """
        record Particle {
            position: float3;
            inverseMass: f32;
            id: u32;
        }
        function Move(readwrite particles: StorageBuffer<Particle>, index: u32): void {
            let value = particles[index];
            let normal = Normalize(float3(1.0, 0.0, 0.0));
            particles[index] = value with { position: value.position + normal * 2.0 };
            particles[index].inverseMass = Dot(normal, normal);
        }
        @compute @numthreads(1, 1, 1)
        function Main(@builtin(dispatchThreadId) thread: uint3,
            @binding(0) readwrite particles: StorageBuffer<Particle>): void {
            Move(particles, thread.x);
        }
        """;

    [Fact]
    public void Typed_storage_helpers_math_and_field_writes_lower_through_dxc()
    {
        var module = GpuComputeBinder.Compile(new([new("storage.v.ts", Source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var layout = Assert.Single(module.StorageLayouts!, layout => layout.Type == "Particle");
        Assert.Equal(20, layout.Stride);
        Assert.Equal(16, layout.Fields.Single(field => field.Path == "id").Offset);
        var bytes = new byte[20];
        GpuStoragePacking.Write(bytes, layout, 0, "id", 0xf1234567u);
        Assert.Equal(0xf1234567u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Contains("Write_position_x", GpuStoragePacking.EmitCSharp(layout, "Storage", "ParticlePacking"));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
    }

    [Theory]
    [InlineData("readonly", "readwrite")]
    [InlineData("readonly", "readonly")]
    public void Readonly_resources_cannot_be_mutated_or_escalated(string entryAccess, string helperAccess)
    {
        string source = Source.Replace("readwrite particles", helperAccess + " particles", StringComparison.Ordinal);
        int entry = source.IndexOf("@compute", StringComparison.Ordinal);
        source = source[..entry] + source[entry..].Replace(helperAccess + " particles", entryAccess + " particles", StringComparison.Ordinal);
        var module = GpuComputeBinder.Compile(new([new("invalid.v.ts", source)]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code is "COPE-GPU-STORAGE-0002" or "COPE-GPU-MUTATION-0001");
    }

    [Fact]
    public void Indices_uses_the_existing_static_for_expansion()
    {
        const string source = """
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite output: StorageBuffer<f32>): void {
                var total: f32 = 0.0;
                static for (let index of Indices<6>()) {
                    total = total + Convert<f32>(index);
                }
                output[0] = total;
            }
            """;
        var module = GpuComputeBinder.Compile(new([new("indices.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput);
    }

    [Theory]
    [InlineData("f32")]
    [InlineData("u32")]
    public void Scalar_word_transfers_keep_their_layout(string type)
    {
        string source = $$"""
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite data: StorageBuffer<{{type}}>): void {
                Store<{{type}}>(data, 0, Load<{{type}}>(data, 1));
            }
            """;
        var module = GpuComputeBinder.Compile(new([new("words.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        Assert.NotNull(module.StorageLayouts);
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput);
    }

    [Theory]
    [InlineData("record Bad {}", "Bad")]
    [InlineData("record Bad { flag: bool; }", "Bad")]
    [InlineData("record Bad { resource: StorageBuffer<f32>; }", "Bad")]
    [InlineData("record Bad { next: Bad; }", "Bad")]
    [InlineData("enum Bad { A, B }", "Bad")]
    public void Invalid_storage_is_rejected_before_backend(string declaration, string type)
    {
        string source = $$"""
            {{declaration}}
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readonly data: StorageBuffer<{{type}}>): void {}
            """;
        var module = GpuComputeBinder.Compile(new([new("invalid.v.ts", source)]));
        Assert.False(module.Success);
        Assert.NotEmpty(module.Diagnostics);
    }

    [Theory]
    [InlineData("257")]
    [InlineData("f32")]
    [InlineData("-1")]
    public void Indices_rejects_nonfinite_authoring_extents(string extent)
    {
        string source = $$"""
            @compute @numthreads(1, 1, 1)
            function Main(): void {
                static for (let index of Indices<{{extent}}>()) {}
            }
            """;
        Assert.False(GpuComputeBinder.Compile(new([new("invalid.v.ts", source)])).Success);
    }

    [Fact]
    public void Empty_indices_emit_no_iterations()
    {
        const string source = """
            @compute @numthreads(1, 1, 1)
            function Main(): void {
                static for (let index of Indices<0>()) {}
            }
            """;
        var module = GpuComputeBinder.Compile(new([new("empty.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        Assert.Empty(module.Functions.Single(function => function.Name == "Main").Statements);
    }

    private const string MatchSource = """
        enum Command { Move(delta: float3), Stop }
        function Apply(command: Command, readwrite output: StorageBuffer<f32>): void {
            match command {
                case Command.Move(move) => {
                    output[0] = move.delta.x;
                    return;
                }
                case Command.Stop => { return; }
            }
        }
        @compute @numthreads(1, 1, 1)
        function Main(@binding(0) readwrite output: StorageBuffer<f32>): void {
            Apply(Command.Move(float3(1.0, 2.0, 3.0)), output);
        }
        """;

    [Theory]
    [InlineData("match")]
    [InlineData("switch")]
    public void Block_match_supports_payloads_resources_and_outer_return(string keyword)
    {
        var module = GpuComputeBinder.Compile(new([new("match.v.ts", MatchSource.Replace("match command", keyword + " command"))]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput);
    }

    [Fact]
    public void Block_match_requires_every_case_exactly_once()
    {
        string arm = "case Command.Stop => { return; }";
        var missing = GpuComputeBinder.Compile(new([new("missing.v.ts", MatchSource.Replace(arm, ""))]));
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-MATCH-0005");
        var duplicate = GpuComputeBinder.Compile(new([new("duplicate.v.ts", MatchSource.Replace(arm, arm + arm))]));
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-MATCH-0002");
    }

    [Fact]
    public void Exhaustive_block_match_proves_nonvoid_return_without_a_fallback()
    {
        const string source = """
            enum Phase { Start, Stop }
            function Value(phase: Phase): f32 {
                match phase {
                    case Phase.Start => { return 1.0; }
                    case Phase.Stop => { return 2.0; }
                }
            }
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite output: StorageBuffer<f32>): void {
                output[0] = Value(Phase.Start);
            }
            """;
        var module = GpuComputeBinder.Compile(new([new("returns.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput);
        Assert.Contains("switch (", backend.Hlsl);
    }

    [Theory]
    [InlineData("let alias = data;")]
    [InlineData("data[0].position.xy = float2(1.0, 2.0);")]
    [InlineData("Store<f32>(data, 0, 1);")]
    [InlineData("let value = Store<f32>(data, 0, 1.0);")]
    public void Storage_aliases_swizzle_writes_and_invalid_transfers_are_explicit_errors(string statement)
    {
        string source = $$"""
            record Value { position: float3; }
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite data: StorageBuffer<Value>): void {
                {{statement}}
            }
            """;
        Assert.False(GpuComputeBinder.Compile(new([new("invalid.v.ts", source)])).Success);
    }

    [Fact]
    public void Generated_packing_source_compiles_and_rejects_ambiguous_names()
    {
        var module = GpuComputeBinder.Compile(new([new("storage.v.ts", Source)]));
        var layout = module.StorageLayouts!.Single(layout => layout.Type == "Particle");
        string generated = GpuStoragePacking.EmitCSharp(layout, "Storage", "ParticlePacking");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path));
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Packing",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(generated)], references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Throws<ArgumentException>(() => GpuStoragePacking.EmitCSharp(layout, "class", "Packing"));
        var ambiguous = layout with
        {
            Fields = [new("a.b", "f32", 0), new("a_b", "f32", 4)],
        };
        Assert.Throws<ArgumentException>(() => GpuStoragePacking.EmitCSharp(ambiguous, "Storage", "Packing"));
    }

    [Fact]
    public void A_readwrite_resource_can_be_borrowed_for_readonly_helper_access()
    {
        const string source = """
            record Value { position: float3; }
            function Read(readonly data: StorageBuffer<Value>): f32 {
                return data[0].position.x;
            }
            function Forward(readonly data: StorageBuffer<Value>): f32 {
                return Read(data);
            }
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite data: StorageBuffer<Value>,
                @binding(1) readonly input: StorageBuffer<Value>): void {
                data[0].position.x = Forward(data) + Forward(input);
            }
            """;
        var module = GpuComputeBinder.Compile(new([new("borrow.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput);
        Assert.Contains("Forward(RWStructuredBuffer<uint>", backend.Hlsl);
        Assert.Contains("Forward(StructuredBuffer<uint>", backend.Hlsl);
    }

    [Fact]
    public void Invalid_storage_write_reports_a_diagnostic_instead_of_throwing()
    {
        const string source = """
            record Bad { flag: bool; }
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite data: StorageBuffer<Bad>): void {
                data[0].flag = true;
            }
            """;
        var module = GpuComputeBinder.Compile(new([new("invalid.v.ts", source)]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-STORAGE-0001");
    }

    [Theory]
    [InlineData("float3(1.0, 2.0)")]
    [InlineData("float3(1, 2, 3)")]
    [InlineData("Dot(float2(1.0, 2.0), float3(1.0, 2.0, 3.0))")]
    [InlineData("Cross(float2(1.0, 2.0), float2(1.0, 2.0))")]
    [InlineData("Normalize()")]
    public void Native_vectors_require_exact_arity_and_lane_types(string expression)
    {
        string source = $$"""
            @compute @numthreads(1, 1, 1)
            function Main(): void { let invalid = {{expression}}; }
            """;
        var module = GpuComputeBinder.Compile(new([new("invalid.v.ts", source)]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-MATH-0001");
    }

    [Fact]
    public void Payload_enums_cannot_be_compared_by_discarding_their_payload()
    {
        const string source = """
            enum Command { Move(delta: f32), Stop }
            @compute @numthreads(1, 1, 1)
            function Main(): void {
                let invalid = Command.Move(1.0) == Command.Move(2.0);
            }
            """;
        Assert.False(GpuComputeBinder.Compile(new([new("invalid.v.ts", source)])).Success);
    }
}
