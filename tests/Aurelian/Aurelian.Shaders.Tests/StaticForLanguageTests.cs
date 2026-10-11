using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class StaticForLanguageTests
{
    [Fact]
    public void Compute_expands_nested_lists_and_discards_unselected_branches_before_dxc()
    {
        var module = Compute("""
            var sum: u32 = 0;
            static for (let x of [1, 2]) {
                static for (const y of [3, 4]) {
                    const weight: u32 = static Square(x + y);
                    static if (x == 1) {
                        sum = sum + weight;
                    } else {
                        sum = sum + y;
                    }
                }
            }
            Output[0] = Convert<f32>(sum);
            """, "function Square(value: u32): u32 { return value * value; }");
        Assert.True(module.Success, Diagnostics(module.Diagnostics));
        string hlsl = VdMirComputeHlslEmitter.Emit(module);
        Assert.DoesNotContain("for (", hlsl);
        Assert.DoesNotContain("if (", hlsl);
        Assert.DoesNotContain("Square(", hlsl);
        Assert.Contains("16", hlsl);
        Assert.Contains("25", hlsl);
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
    }

    [Fact]
    public void Finite_array_results_use_the_existing_static_evaluator()
    {
        var module = Compute("""
            var sum: u32 = 0;
            static for (const value of Offsets()) {
                sum = sum + value;
            }
            Output[0] = Convert<f32>(sum);
            """, "function Offsets(): Array<u32, 3> { return [2, 4, 8]; }");
        Assert.True(module.Success, Diagnostics(module.Diagnostics));
        string hlsl = VdMirComputeHlslEmitter.Emit(module);
        Assert.DoesNotContain("Offsets(", hlsl);
        Assert.DoesNotContain("for (", hlsl);
    }

    [Fact]
    public void Named_compile_time_arrays_expand_without_a_runtime_container()
    {
        var module = Compute("""
            var sum: u32 = 0;
            static for (const value of Offsets) { sum = sum + value; }
            Output[0] = Convert<f32>(sum);
            """, "const Offsets: Array<u32, 3> = [2, 4, 8];");
        Assert.True(module.Success, Diagnostics(module.Diagnostics));
        Assert.DoesNotContain("VtsConstruct", VdMirComputeHlslEmitter.Emit(module));
    }

    [Fact]
    public void Static_payload_enum_results_embed_through_the_certified_layout()
    {
        var module = Compute("""
            const choice: Choice = static Choice.Value(7);
            const value: u32 = match choice {
                case Choice.Empty => 0,
                case Choice.Value(payload) => payload.value,
            };
            Output[0] = Convert<f32>(value);
            """, "enum Choice { Empty, Value(value: u32) }");
        Assert.True(module.Success, Diagnostics(module.Diagnostics));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput);
    }

    [Fact]
    public void Closed_child_flow_is_evaluated_before_hlsl_emission()
    {
        var module = Compute("Output[0] = Convert<f32>(static Resolve());", """
            flow Child(count: u32) -> u32 {
                state Work initial { finish count + 1; }
            }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child(count) -> Accept; }
                state Accept(value: u32) { finish value + 10; }
            }
            function Resolve(): u32 {
                var frame = Parent.start(2);
                static for (let tick of [0, 1, 2]) { frame = Parent.step(frame); }
                return match frame.outcome {
                    case ParentOutcome.Active => 1000,
                    case ParentOutcome.Completed(payload) => payload.value,
                    case ParentOutcome.Fault(payload) => payload.code,
                };
            }
            """);
        Assert.True(module.Success, Diagnostics(module.Diagnostics));
        string hlsl = VdMirComputeHlslEmitter.Emit(module);
        Assert.Contains("float(13)", hlsl);
        Assert.DoesNotContain("__flow_Parent_Step(", hlsl);
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput);
    }

    [Theory]
    [InlineData("static for (const x of [Output[0]]) { Output[0] = x; }")]
    [InlineData("static for (const x of [1]) { x = 2; }")]
    [InlineData("var x: u32 = 0; static for (const x of [1]) { Output[0] = 0.0; }")]
    [InlineData("static for (const x of [1, 2.0]) { Output[0] = 0.0; }")]
    [InlineData("static for (const x in [1, 2]) { Output[0] = 0.0; }")]
    public void Runtime_values_mutation_shadowing_and_mixed_lists_are_diagnosed(string body)
    {
        var module = Compute(body);
        Assert.False(module.Success);
        Assert.NotEmpty(module.Diagnostics);
    }

    [Fact]
    public void Nested_expansion_is_bounded()
    {
        string values = string.Join(", ", Enumerable.Range(0, 65));
        var module = Compute($$"""
            static for (const x of [{{values}}]) {
                static for (const y of [{{values}}]) {
                    Output[0] = 0.0;
                }
            }
            """);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Message.Contains("budget", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_list_emits_no_runtime_work()
    {
        var module = Compute("""
            static for (const x of []) { UnknownFunction(); }
            Output[0] = 7.0;
            """);
        Assert.True(module.Success, Diagnostics(module.Diagnostics));
        Assert.DoesNotContain("UnknownFunction", VdMirComputeHlslEmitter.Emit(module));
    }

    [Fact]
    public void Iteration_values_do_not_leak_into_called_helpers()
    {
        var module = Compute("""
            static for (const x of [1]) { Output[0] = Convert<f32>(Read()); }
            """, "function Read(): u32 { return x; }");
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-NAME-0001");
    }

    [Fact]
    public void Bloom_resolves_its_nine_tap_kernel_before_dxc()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Bloom3D.v.ts");
        var module = GpuGraphicsBinder.Compile(new([new("Bloom3D.v.ts", File.ReadAllText(path))]));
        Assert.True(module.Success, Diagnostics(module.Diagnostics));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput);
        Assert.DoesNotContain("for (", backend.Hlsl);
        Assert.DoesNotContain("TentWeight(", backend.Hlsl);
    }

    private static Copeland.TS.Gpu.VdMir.VdMirComputeModule Compute(string body, string declarations = "")
    {
        string source = declarations + $$"""
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite Output: StorageBuffer<f32>): void {
                {{body}}
            }
            """;
        return GpuComputeBinder.Compile(new([new("static-for.v.ts", source)]));
    }

    private static string Diagnostics(IEnumerable<Copeland.TS.Gpu.VdMir.VdMirDiagnostic> diagnostics)
    {
        return string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message));
    }
}
