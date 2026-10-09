using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuLanguagePortTests
{
    internal const string Graphics = """
        stream Input { @location(0) position: float3; }
        stream Varying { @builtin(position) position: float4; @location(0) value: f32; }
        stream PixelInput { @location(0) value: f32; }
        stream Output { @target(0) color: float4; }
        @vertex function VertexMain(input: Input): Varying {
            return { position: float4(input.position, 1.0), value: Answer(2.0) };
        }
        @pixel function PixelMain(input: PixelInput): Output {
            return { color: float4(input.value, input.value, input.value, 1.0) };
        }
        """;

    internal const string PayloadLibrary = """
        export enum SurfaceChoice { Unlit, Lit(value: f32, gain: f32) }
        export function Choose(v: f32): SurfaceChoice {
            if (v > 0.0) { return SurfaceChoice.Lit(v, 0.25); }
            return SurfaceChoice.Unlit;
        }
        export function Shade(v: f32): f32 {
            return match Choose(v) {
                SurfaceChoice.Unlit => 0.0,
                SurfaceChoice.Lit(payload) => payload.value * payload.gain,
            };
        }
        """;

    internal static VdMirGraphicsModule Compile(string declarations, params GpuSourceFile[] libraries)
        => GpuGraphicsBinder.Compile(new([new("main.v.ts", declarations + "\n" + Graphics), .. libraries]));

    [Fact]
    public void Named_import_alias_closes_over_private_helpers_without_global_visibility()
    {
        var module = Compile("import { publicValue as Answer } from \"./library\";",
            new GpuSourceFile("library.v.ts", "function hidden(value: f32): f32 { return value * 3.0; } export function publicValue(value: f32): f32 { return hidden(value); }"));
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Equal(4, module.Functions.Count);
        Assert.Contains(module.Functions, function => function.Source.File == "library.v.ts" && function.Name.EndsWith("_hidden"));
        Assert.False(Compile("function Answer(value: f32): f32 { return hidden(value); }",
            new GpuSourceFile("library.v.ts", "export function hidden(value: f32): f32 { return value; }")).Success);
    }

    [Fact]
    public void Duplicate_private_function_names_in_distinct_modules_have_distinct_identity()
    {
        var module = Compile("import { Answer } from \"./a\"; import { Extra } from \"./b\";",
            new("a.v.ts", "function helper(v: f32): f32 { return v * 2.0; } export function Answer(v: f32): f32 { return helper(v); }"),
            new("b.v.ts", "function helper(v: f32): f32 { return v * 3.0; } export function Extra(v: f32): f32 { return helper(v); }"));
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Single(module.Functions, function => function.Name.EndsWith("_helper"));
    }

    [Theory]
    [InlineData("import { missing as Answer } from \"./library\";", "export function value(v: f32): f32 { return v; }", "COPE-GPU-MODULE-0005")]
    [InlineData("import { value as Answer } from \"./absent\";", "export function value(v: f32): f32 { return v; }", "COPE-GPU-MODULE-0003")]
    [InlineData("import { value as Answer } from \"package\";", "export function value(v: f32): f32 { return v; }", "COPE-GPU-MODULE-0002")]
    [InlineData("import { value as Answer } from \"./library\";", "function value(v: f32): f32 { return v; }", "COPE-GPU-MODULE-0005")]
    [InlineData("import { value as Answer } from \"./library\";", "import { Answer } from \"./main\"; export function value(v: f32): f32 { return v; }", "COPE-GPU-MODULE-0007")]
    [InlineData("import Default, { value as Answer } from \"./library\";", "export function value(v: f32): f32 { return v; }", "COPE-GPU-MODULE-0004")]
    [InlineData("import { value as Answer value } from \"./library\";", "export function value(v: f32): f32 { return v; }", "COPE-GPU-MODULE-0004")]
    public void Invalid_imports_fail_with_specific_diagnostics(string source, string library, string code)
    {
        var module = Compile(source, new GpuSourceFile("library.v.ts", library));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Static_imported_helper_is_evaluated_and_erased_from_shader_closure()
    {
        var module = Compile("import { square } from \"./math\"; function Answer(v: f32): f32 { return static square(3.0); }",
            new GpuSourceFile("math.v.ts", "export function square(v: f32): f32 { return v * v; }"));
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Equal("9.0", module.Functions.Single(function => function.Name == "Answer").Statements[0].Expression!.Value);
        Assert.DoesNotContain(module.Functions, function => function.Name.EndsWith("_square"));
    }

    [Fact]
    public void Static_binary32_rounding_is_preserved_at_each_operation()
    {
        var module = Compile("function Answer(v: f32): f32 { return static ((16777216.0 + 1.0) - 16777216.0); }");
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Equal("0.0", module.Functions.Single(function => function.Name == "Answer").Statements[0].Expression!.Value);
    }

    [Fact]
    public void Static_cannot_capture_runtime_parameters()
    {
        var module = Compile("function Answer(v: f32): f32 { return static v * 2.0; }");
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-STATIC-0012");
    }

    [Fact]
    public void Compute_imports_and_static_constants_use_the_same_module_and_evaluator_path()
    {
        var module = GpuComputeBinder.Compile(new([
            new("compute.v.ts", """
                import { square } from "./math";
                const value: f32 = static square(3.0);
                @compute @numthreads(1, 1, 1)
                function Main(@builtin(dispatchThreadId) thread: uint3,
                    @binding(0) readwrite output: StorageBuffer<f32>): void {
                    output[thread.x] = value;
                }
                """),
            new GpuSourceFile("math.v.ts", "export function square(v: f32): f32 { return v * v; }")
        ]));
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Single(module.Functions);
    }

    [Fact]
    public void Qualified_payload_match_has_one_subject_and_scoped_record_binding()
    {
        var module = Compile("import { Shade as Answer } from \"./surface\";", new GpuSourceFile("surface.v.ts", PayloadLibrary));
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        VdMirEnum enumeration = Assert.Single(module.Enums!);
        Assert.Equal(3, enumeration.CarrierFields.Count);
        VdMirFunction match = Assert.Single(module.Functions, function => function.Name.StartsWith("VtsMatch"));
        Assert.Single(match.Parameters);
        Assert.Equal("if", match.Statements[0].Kind);
        Assert.Equal("block", match.Statements[1].Kind);
        string json = VdMirJson.Serialize(module);
        Assert.Contains("\"carrierFields\"", json);
    }

    [Theory]
    [InlineData("SurfaceChoice.Unlit => 0.0", "COPE-GPU-MATCH-0005")]
    [InlineData("SurfaceChoice.Unlit => 0.0, SurfaceChoice.Unlit => 0.0, SurfaceChoice.Lit(p) => p.value", "COPE-GPU-MATCH-0002")]
    [InlineData("SurfaceChoice.Unlit => 0.0, SurfaceChoice.Lit => 1.0", "COPE-GPU-MATCH-0003")]
    [InlineData("SurfaceChoice.Unlit => 0.0, SurfaceChoice.Lit(p) => true", "COPE-GPU-MATCH-0004")]
    [InlineData("Unlit => 0.0, Lit(p) => p.value", "COPE-GPU-MATCH-0001")]
    [InlineData("Wrong.Unlit => 0.0, SurfaceChoice.Lit(p) => p.value", "COPE-GPU-MATCH-0001")]
    public void Invalid_payload_matches_are_rejected_before_emission(string arms, string code)
    {
        string source = "enum SurfaceChoice { Unlit, Lit(value: f32) } function Answer(v: f32): f32 { return match SurfaceChoice.Lit(v) { " + arms + " }; }";
        var module = Compile(source);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Reordering_module_inputs_preserves_semantic_bytes()
    {
        GpuSourceFile[] sources = [new("main.v.ts", "import { Shade as Answer } from \"./surface\";\n" + Graphics), new("surface.v.ts", PayloadLibrary)];
        Assert.Equal(VdMirJson.Serialize(GpuGraphicsBinder.Compile(new(sources))), VdMirJson.Serialize(GpuGraphicsBinder.Compile(new(sources.Reverse().ToArray()))));
    }

    [Fact]
    public void Direct_wgsl_consumes_the_same_lowered_payload_match()
    {
        var result = WgslGraphicsBackend.Compile(new([
            new("main.v.ts", "import { Shade as Answer } from \"./surface\";\n" + Graphics),
            new("surface.v.ts", PayloadLibrary)
        ]));
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Contains("m0: u32", result.Program!.Code);
        Assert.Contains("m1: f32", result.Program.Code);
    }

    [Fact]
    public void Static_if_erases_the_unselected_host_only_branch_before_gpu_binding()
    {
        var module = Compile("function Answer(v: f32): f32 { static if (true) { return 0.25; } else { return host(v); } }");
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Equal("block", module.Functions.Single(function => function.Name == "Answer").Statements[0].Kind);
        Assert.DoesNotContain(module.Functions, function => function.Name == "host");
    }

    [Fact]
    public void Static_unsigned_arithmetic_retains_wraparound()
    {
        var module = Compile("const wrapped: u32 = static (2147483647 + 2147483647 + 2); function Answer(v: f32): f32 { return Convert<f32>(wrapped); }");
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Equal("0", module.Functions.Single(function => function.Name == "Answer").Statements[0].Expression!.Operands![0].Value);
    }

    [Fact]
    public void Source_loader_follows_only_declared_imports_and_constant_exports()
    {
        var available = new Dictionary<string, string>
        {
            ["main.v.ts"] = "import { Answer } from \"./library\";\n" + Graphics,
            ["library.v.ts"] = "import { gain } from \"./constants\"; export function Answer(v: f32): f32 { return v * gain; }",
            ["constants.v.ts"] = "export const gain: f32 = static (1.0 / 4.0);",
            ["unrelated.v.ts"] = "@compute function invalid(): void {}",
        };
        var sources = GpuSourceLoader.Load("main.v.ts", path => available.GetValueOrDefault(path));
        Assert.Equal(3, sources.Count);
        var module = GpuGraphicsBinder.Compile(new(sources));
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
    }

    [Theory]
    [InlineData("static (1.0 / 0.0)", "COPE-STATIC-0014")]
    [InlineData("static (\"hello\")", "COPE-GPU-LITERAL-0001")]
    public void Invalid_static_values_report_diagnostics_instead_of_throwing(string expression, string code)
    {
        var module = Compile("function Answer(v: f32): f32 { return " + expression + "; }");
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Generated_identity_collisions_report_diagnostics_instead_of_throwing()
    {
        const string source = """
            enum Choice { Some(value: f32) }
            function VtsMake_Choice_0(): f32 { return 1.0; }
            function Answer(v: f32): f32 {
                let unused: f32 = VtsMake_Choice_0();
                return match Choice.Some(v) { Choice.Some(payload) => payload.value };
            }
            """;
        var module = Compile(source);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-SYMBOL-0002");
        var compute = GpuComputeBinder.Compile(new([new("main.v.ts", source + """
            @compute @numthreads(1, 1, 1)
            function Main(@builtin(dispatchThreadId) thread: uint3,
                @binding(0) readwrite output: StorageBuffer<f32>): void {
                output[thread.x] = Answer(1.0);
            }
            """)]));
        Assert.False(compute.Success);
        Assert.Contains(compute.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-SYMBOL-0002");
    }

    [Theory]
    [InlineData("let", false)]
    [InlineData("const", false)]
    [InlineData("var", true)]
    public void Local_mutability_is_explicit_in_graphics_compute_and_static_helpers(string keyword, bool mutable)
    {
        string helper = $$"""
            function Adjust(v: f32): f32 {
                {{keyword}} value: f32 = v;
                value = value * 2.0;
                return value;
            }
            """;
        foreach (bool useStatic in new[] { false, true })
        {
            string call = useStatic ? "static Adjust(2.0)" : "Adjust(v)";
            var graphics = Compile(helper + "function Answer(v: f32): f32 { return " + call + "; }");
            Assert.Equal(mutable, graphics.Success);
            string computeCall = useStatic ? "static Adjust(2.0)" : "Adjust(input[thread.x])";
            var compute = GpuComputeBinder.Compile(new([new("main.v.ts", helper + $$"""
                @compute @numthreads(1, 1, 1)
                function Main(@builtin(dispatchThreadId) thread: uint3,
                    @binding(0) readonly input: StorageBuffer<f32>,
                    @binding(1) readwrite output: StorageBuffer<f32>): void {
                    output[thread.x] = {{computeCall}};
                }
                """)]));
            Assert.Equal(mutable, compute.Success);
            if (!mutable)
            {
                Assert.Contains(graphics.Diagnostics, item => item.Code == "COPE-GPU-MUTATION-0001");
                Assert.Contains(compute.Diagnostics, item => item.Code == "COPE-GPU-MUTATION-0001");
            }
            else if (useStatic)
            {
                Assert.DoesNotContain(graphics.Functions, function => function.Name == "Adjust");
                Assert.DoesNotContain(compute.Functions, function => function.Name == "Adjust");
            }
        }
    }
}
