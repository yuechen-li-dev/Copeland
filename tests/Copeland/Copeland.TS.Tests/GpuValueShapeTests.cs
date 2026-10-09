using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuValueShapeTests
{
    public const string Witness = """
        type Weights = Matrix<f32, 2, 2>;
        record Sample { value: f32; weights: Weights; }
        function Answer(v: f32): f32 {
            let original: Sample = { value: v, weights: [1.0, 2.0, 3.0, 4.0] };
            let sample: Sample = original with { value: v * 2.0 };
            let product: Matrix<f32, 2, 2> = MatMul(sample.weights, sample.weights);
            let a: Vector<f32, 3> = [1.0, 2.0, 3.0];
            let b: Tensor<f32, 3> = [4.0, 5.0, 6.0];
            let added: Tensor<f32, 3> = a + b;
            var grid: NDArray<f32, 2, 2> = [0.0, 1.0, 2.0, 3.0];
            grid.set(1, 0, v);
            var weights: Array<f32, 2> = [0.0, 1.0];
            weights[0] = v;
            return sample.value + product.at(0, 1) + Dot(a, b) + added[2] + grid.at(1, 0) + weights[0];
        }
        """;

    public static VdMirGraphicsModule Graphics(string declarations) => GpuGraphicsBinder.Compile(new([
        new("main.v.ts", declarations + "\n" + GpuLanguagePortTests.Graphics)
    ]));

    public static VdMirComputeModule Compute(string declarations) => GpuComputeBinder.Compile(new([
        new("main.v.ts", declarations + """
            @compute @numthreads(1, 1, 1)
            function Main(@builtin(dispatchThreadId) thread: uint3,
                @binding(0) readonly input: StorageBuffer<f32>,
                @binding(1) readwrite output: StorageBuffer<f32>): void {
                output[thread.x] = Answer(input[thread.x]);
            }
            """)
    ]));

    [Fact]
    public void Records_aliases_shapes_and_tensor_math_share_both_gpu_paths()
    {
        var graphics = Graphics(Witness);
        var compute = Compute(Witness);
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
        Assert.True(compute.Success, string.Join("\n", compute.Diagnostics));
        Assert.Equal(graphics.ValueTypes!.Select(type => (type.Kind, type.Name)), compute.ValueTypes!.Select(type => (type.Kind, type.Name)));
        Assert.Contains(compute.ValueTypes!, type => type.Kind == "tensor" && type.Shape.SequenceEqual(new[] { 2, 2 }));
        Assert.DoesNotContain(compute.Functions.SelectMany(function => function.Statements), statement => statement.Kind is "while" or "for");
        var wgsl = WgslGraphicsBackend.Lower(graphics);
        Assert.True(wgsl.Success, string.Join("\n", wgsl.Diagnostics));
    }

    [Theory]
    [InlineData("Array<f32, 0>", "[1.0]", "COPE-GPU-SHAPE-0001")]
    [InlineData("Matrix<f32, 2>", "[1.0, 2.0]", "COPE-GPU-SHAPE-0001")]
    [InlineData("Tensor<u32, 2>", "[1, 2]", "COPE-GPU-SHAPE-0003")]
    [InlineData("Tensor<f32, 17, 17>", "[1.0]", "COPE-GPU-SHAPE-0002")]
    [InlineData("NDArray<f32, 2, 2>", "[1.0, 2.0]", "COPE-GPU-SHAPE-0004")]
    public void Invalid_shapes_fail_before_backend_emission(string type, string initializer, string code)
    {
        string source = "function Answer(v: f32): f32 { let data: " + type + " = " + initializer + "; return v; }";
        Assert.Contains(Graphics(source).Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Contains(Compute(source).Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("let", false)]
    [InlineData("var", true)]
    public void Array_and_ndarray_writes_follow_binding_mutability(string keyword, bool success)
    {
        string source = $$"""
            function Answer(v: f32): f32 {
                {{keyword}} grid: NDArray<f32, 2, 2> = [0.0, 1.0, 2.0, 3.0];
                grid.set(1, 0, v);
                {{keyword}} data: Array<f32, 2> = [0.0, 1.0];
                data[0] = v;
                return grid.at(1, 0) + data[0];
            }
            """;
        Assert.Equal(success, Graphics(source).Success);
        Assert.Equal(success, Compute(source).Success);
    }

    [Theory]
    [InlineData("data.at(2, 0)", "COPE-GPU-INDEX-0003")]
    [InlineData("data.at(0)", "COPE-GPU-INDEX-0002")]
    [InlineData("data.at(0.0, 0)", "COPE-GPU-INDEX-0002")]
    public void Index_rank_type_and_constant_bounds_are_checked(string read, string code)
    {
        string source = "function Answer(v: f32): f32 { let data: NDArray<f32, 2, 2> = [0.0, 1.0, 2.0, 3.0]; return " + read + "; }";
        Assert.Contains(Graphics(source).Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Contains(Compute(source).Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Recursive_records_and_aliases_report_diagnostics_instead_of_throwing()
    {
        foreach (string type in new[] { "record Cycle { next: Cycle; }", "type Cycle = Cycle;" })
        {
            string source = type + "function Answer(v: f32): f32 { let value: Cycle = {}; return v; }";
            Assert.Contains(Graphics(source).Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-VALUE-0001");
            Assert.Contains(Compute(source).Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-VALUE-0001");
        }
    }

    [Fact]
    public void Literals_use_parameter_return_and_parenthesis_type_context()
    {
        const string source = """
            record Sample { data: Array<f32, 2>; }
            function Build(v: f32): Sample { return { data: [v, 2.0] }; }
            function Read(sample: Sample): f32 { return sample.data[0]; }
            function Answer(v: f32): f32 {
                var sample: Sample = ({ data: [1.0, 2.0] });
                sample = { data: [v, 2.0] };
                return Read({ data: [v, 2.0] }) + Build(v).data[0] + sample.data[0];
            }
            """;
        var graphics = Graphics(source);
        var compute = Compute(source);
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
        Assert.True(compute.Success, string.Join("\n", compute.Diagnostics));
    }

    [Fact]
    public void Ndarray_element_spaces_are_preserved_without_typing_tensor_axes()
    {
        const string source = """
            @space(world.position) type Position = float3;
            function Answer(v: f32): f32 {
                let positions: NDArray<Position, 2> = [float3(v, 0.0, 0.0), float3(0.0, 0.0, 0.0)];
                return positions[0].x;
            }
            """;
        var graphics = Graphics(source);
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
        var array = Assert.Single(graphics.ValueTypes!);
        Assert.Equal("Position", array.ElementType);
        Assert.All(array.Fields, field => Assert.Equal("float3", field.PhysicalType));
        var wgsl = WgslGraphicsBackend.Lower(graphics);
        Assert.True(wgsl.Success, string.Join("\n", wgsl.Diagnostics));
    }

    [Theory]
    [InlineData("MatMul(a, b)")]
    [InlineData("a + b")]
    public void Incompatible_tensor_shapes_are_rejected(string expression)
    {
        string source = $$"""
            function Answer(v: f32): f32 {
                let a: Tensor<f32, 2> = [1.0, 2.0];
                let b: Tensor<f32, 3> = [1.0, 2.0, 3.0];
                let result: Tensor<f32, 2> = {{expression}};
                return v;
            }
            """;
        Assert.Contains(Graphics(source).Diagnostics, item => item.Code == "COPE-GPU-TENSOR-0001");
        Assert.Contains(Compute(source).Diagnostics, item => item.Code == "COPE-GPU-TENSOR-0001");
    }

    [Fact]
    public void Record_initializers_and_indexed_writes_preserve_authored_argument_order()
    {
        const string source = """
            record Pair { first: f32; second: f32; }
            function First(v: f32): f32 { return v; }
            function Second(v: f32): f32 { return v; }
            function Index(): u32 { return 0; }
            function Answer(v: f32): f32 {
                let pair: Pair = { second: Second(v), first: First(v) };
                var data: Array<f32, 2> = [0.0, 1.0];
                data.set(Index(), First(v));
                return pair.first + data[0];
            }
            """;
        var graphics = Graphics(source);
        var compute = Compute(source);
        Assert.True(graphics.Success, string.Join("\n", graphics.Diagnostics));
        Assert.True(compute.Success, string.Join("\n", compute.Diagnostics));
        foreach (var functions in new[] { graphics.Functions, compute.Functions })
        {
            var answer = Assert.Single(functions, function => function.Name == "Answer");
            var constructor = answer.Statements[0].Expression!;
            Assert.Equal(new[] { "Second", "First" }, constructor.Operands!.Select(argument => argument.Value));
            var write = answer.Statements[2].Expression!;
            if (write.Kind == "assignment")
            {
                write = write.Operands![1];
            }
            Assert.Equal("Index", write.Operands![1].Value);
            Assert.Equal("First", write.Operands[2].Value);
        }
    }

    [Fact]
    public void Shape_queries_preserve_evaluation_of_their_subject()
    {
        var module = Graphics("""
            function Build(v: f32): Array<f32, 2> { return [v, 1.0]; }
            function Answer(v: f32): f32 { return Convert<f32>(Build(v).length); }
            """);
        Assert.True(module.Success, string.Join("\n", module.Diagnostics));
        Assert.Contains(module.Functions, function => function.Name == "Build");
        Assert.Contains(module.Functions, function => function.Name.StartsWith("VtsQuery", StringComparison.Ordinal));
    }
}
