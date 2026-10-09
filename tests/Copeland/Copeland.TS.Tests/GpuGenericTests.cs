using Copeland.TS.Gpu;
using Copeland.TS.Gpu.Wgsl;
using Xunit;

namespace Copeland.TS.Tests;


public sealed class GpuGenericTests
{
    public const string Witness = """
        interface HasValue<T> { value: T; }
        record Box<T> { value: T; }
        record Grid<T, static Rows: u32, static Columns: u32> {
            cells: NDArray<T, Rows, Columns>;
        }
        type FloatBox = Box<f32>;
        template<type T>
        function Identity(value: T): T { return value; }
        function Read<T extends HasValue<f32>>(value: T): f32 { return value.value; }
        function Forward<T extends HasValue<f32>>(value: T): f32 { return Read<T>(value); }
        function Square<static N: u32>(value: Matrix<f32, N, N>): Matrix<f32, N, N> {
            return MatMul(value, value);
        }
        function Make<T = f32, static Count: u32 = 2>(value: T): Box<T> {
            return { value: value };
        }
        function Answer(v: f32): f32 {
            let box: FloatBox = { value: v };
            let grid: Grid<f32, Rows: 2, Columns: 2> = { cells: [v, 2.0, 3.0, 4.0] };
            let matrix: Matrix<f32, 2, 2> = [v, 2.0, 3.0, 4.0];
            let squared: Matrix<f32, 2, 2> = Square(matrix);
            return Forward(Identity(box)) + Read(Make<f32>(v)) + squared.at(0, 1) + grid.cells.at(1, 0);
        }
        """;
    [Fact]
    public void Both_profiles_close_records_dimensions_and_forwarded_requirements()
    {
        var compute = GpuValueShapeTests.Compute(Witness);
        var graphics = GpuValueShapeTests.Graphics(Witness);
        Assert.True(compute.Success, string.Join("\n", compute.Diagnostics));
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
        Assert.NotEmpty(compute.GenericSpecializations!);
        Assert.All(compute.GenericSpecializations!, trace => Assert.Equal(1, trace.BodyBindings));
        Assert.DoesNotContain(compute.Functions, function => function.Name == "Identity");
        Assert.DoesNotContain(compute.ValueTypes!, type => type.Name.Contains("Plan", StringComparison.Ordinal));
        var wgsl = WgslGraphicsBackend.Lower(graphics);
        Assert.True(wgsl.Success, string.Join("\n", wgsl.Diagnostics));
    }

    [Fact]
    public void Explicit_and_inferred_calls_reuse_one_specialization()
    {
        var module = GpuValueShapeTests.Compute("""
            function Identity<T>(value: T): T { return value; }
            function Answer(v: f32): f32 { return Identity(v) + Identity<f32>(v); }
            """);
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Single(module.GenericSpecializations!);
    }

    [Fact]
    public void Specialization_budget_stops_with_a_source_owned_diagnostic()
    {
        string calls = string.Join(" + ", Enumerable.Range(0, 17).Select(index => $"Use<N: {index}>(v)"));
        string source = "function Use<static N: u32>(v: f32): f32 { return v; } function Answer(v: f32): f32 { return " + calls + "; }";
        foreach (var diagnostics in new[] { GpuValueShapeTests.Compute(source).Diagnostics, GpuValueShapeTests.Graphics(source).Diagnostics })
        {
            var diagnostic = Assert.Single(diagnostics, item => item.Code == "COPE-GPU-GENERIC-0004");
            Assert.Equal("main.v.ts", diagnostic.PrimarySpan.File);
            Assert.True(diagnostic.PrimarySpan.Start > 0);
            Assert.True(diagnostic.PrimarySpan.Length > 0);
        }
    }

    [Fact]
    public void Compute_resources_cannot_be_specialized_as_generic_values()
    {
        var module = GpuValueShapeTests.Compute("""
            function Use<T>(v: f32): f32 { return v; }
            function Answer(v: f32): f32 { return Use<StorageBuffer<f32>>(v); }
            """);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-GENERIC-0003");
    }

    [Fact]
    public void Named_arguments_and_scalar_spellings_normalize_to_schema_identity()
    {
        var module = GpuValueShapeTests.Compute("""
            function Sum<static First: f32, static Second: f32>(v: f32): f32 {
                return v + First + Second;
            }
            function Answer(v: f32): f32 {
                return Sum<First: 2.0, Second: 3.00>(v) + Sum<Second: 3.0, First: 2.00>(v);
            }
            """);
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Single(module.GenericSpecializations!);
    }

    [Fact]
    public void Dependent_scalar_defaults_and_boolean_arguments_are_typed()
    {
        const string source = """
            function Sum<static N: u32 = 2, static M: u32 = (N + 1)>(v: f32): f32 {
                return v + Convert<f32>(N + M);
            }
            function Choose<static Enabled: bool>(v: f32): f32 {
                if (Enabled) { return v; }
                return 0.0;
            }
            function Answer(v: f32): f32 { return Sum(v) + Choose<true>(v); }
            """;
        var compute = GpuValueShapeTests.Compute(source);
        var graphics = GpuValueShapeTests.Graphics(source);
        Assert.True(compute.Success, string.Join("\n", compute.Diagnostics));
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
    }

    [Fact]
    public void Construction_templates_and_static_generic_calls_embed_aggregate_values()
    {
        const string source = """
            record Box<T> { value: T; }
            template<static Seed: f32 = 2.0> Build: Box<f32> { return { value: Seed }; }
            function Samples<static N: u32>(v: f32): Array<f32, N> { return [v, 2.0]; }
            function Answer(v: f32): f32 {
                let box: Box<f32> = instantiate Build<Seed: 3.0>;
                let samples: Array<f32, 2> = static Samples<N: 2>(4.0);
                return v + box.value + samples[0];
            }
            """;
        var compute = GpuValueShapeTests.Compute(source);
        var graphics = GpuValueShapeTests.Graphics(source);
        Assert.True(compute.Success, string.Join("\n", compute.Diagnostics));
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
        Assert.DoesNotContain(compute.Functions, function => function.Name.StartsWith("VtsGeneric", StringComparison.Ordinal));
        var wgsl = WgslGraphicsBackend.Lower(graphics);
        Assert.True(wgsl.Success, string.Join("\n", wgsl.Diagnostics));
    }

    [Fact]
    public void Non_generic_helpers_keep_their_own_lexical_scope_and_constructors()
    {
        const string source = """
            record Pair { value: f32; }
            function PairValue(v: f32): Pair { return { value: v }; }
            function Use<T>(v: T): T {
                let pair: Pair = PairValue(2.0);
                return v;
            }
            function Answer(v: f32): f32 { return Use(v); }
            """;
        var compute = GpuValueShapeTests.Compute(source);
        var graphics = GpuValueShapeTests.Graphics(source);
        Assert.True(compute.Success, string.Join("\n", compute.Diagnostics));
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
        Assert.True(WgslGraphicsBackend.Lower(graphics).Success);
    }

    [Theory]
    [InlineData("function Use<static N: u32>(v: f32): f32 { return v; } function Answer(v: f32): f32 { return Use<N: v>(v); }", "COPE-GPU-GENERIC-0003")]
    [InlineData("function Use<T>(a: T, b: T): T { return a; } function Answer(v: f32): f32 { return Use(v, 2); }", "COPE-GPU-GENERIC-0005")]
    [InlineData("function Use<static N: u32>(v: Matrix<f32, N, N>): f32 { return v.at(0, 0); } function Answer(v: f32): f32 { let a: Matrix<f32, 2, 3> = [v, v, v, v, v, v]; return Use(a); }", "COPE-GPU-GENERIC-0005")]
    [InlineData("type Bad = Array<f32, (2.0)>; function Answer(v: f32): f32 { let a: Bad = [v, v]; return a[0]; }", "COPE-GPU-SHAPE-0001")]
    [InlineData("interface HasValue { value: f32; } record Box<T extends HasValue> { value: T; } function Answer(v: f32): f32 { let b: Box<f32> = { value: v }; return v; }", "COPE-GPU-REQUIREMENT-0002")]
    public void Invalid_arguments_fail_at_the_typed_boundary(string source, string code)
    {
        foreach (var diagnostics in new[]
            {
                GpuValueShapeTests.Compute(source).Diagnostics,
                GpuValueShapeTests.Graphics(source).Diagnostics
            }
        )
        {
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code.StartsWith(code, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("interface Named { value: f32; } function Bad<T extends Named>(v: T): f32 { return v.other; }", "COPE-GPU")]
    [InlineData("function Bad<T>(v: T): T { return Bad<T>(v); }", "COPE-GPU-GENERIC-0004")]
    public void Invalid_open_bodies_fail_without_candidate_duck_typing(string definition, string code)
    {
        var module = GpuValueShapeTests.Compute(definition + " function Answer(v: f32): f32 { return Bad<f32>(v); }");
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code.StartsWith(code, StringComparison.Ordinal));
    }
}
