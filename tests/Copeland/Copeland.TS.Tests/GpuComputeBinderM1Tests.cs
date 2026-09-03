using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuComputeBinderM1Tests
{
    private const string ComputeSource = """
        @compute
        @numthreads(8, 1, 1)
        function ComputeNoRegression_CS(
            @builtin(dispatchThreadId) thread: uint3,
            @binding(0) readonly Input: StorageBuffer<f32>,
            @binding(1) readwrite Output: StorageBuffer<f32>
        ): void {
            const index: u32 = thread.x;
            Output[index] = Input[index] + 1.0;
            return;
        }
        """;

    [Fact]
    public void Parser_Retains_Annotation_And_Argument_Spans()
    {
        SyntaxTree tree = SyntaxTree.Parse(ComputeSource, "compute.v.ts");

        Assert.Empty(tree.Diagnostics);
        FunctionDeclarationSyntax function = Assert.Single(tree.Root.Members.OfType<FunctionDeclarationSyntax>());
        IReadOnlyList<AnnotationSyntax> annotations = Assert.IsAssignableFrom<IReadOnlyList<AnnotationSyntax>>(function.Annotations);
        Assert.Equal(["compute", "numthreads"], annotations.Select(annotation => annotation.NameToken.Text));
        AnnotationSyntax numthreads = annotations[1];
        Assert.Equal(ComputeSource.IndexOf("numthreads", StringComparison.Ordinal), numthreads.NameToken.Position);
        LiteralExpressionSyntax firstArgument = Assert.IsType<LiteralExpressionSyntax>(numthreads.Arguments[0]);
        Assert.Equal(ComputeSource.IndexOf("8, 1, 1", StringComparison.Ordinal), firstArgument.LiteralToken.Position);

        ParameterSyntax input = function.Parameters[1];
        Assert.Equal("binding", Assert.Single(input.Annotations!).NameToken.Text);
        Assert.Equal("readonly", input.AccessToken!.Text);
    }

    [Fact]
    public void Parser_Uses_The_Same_Annotation_Node_On_Declarations_And_Fields()
    {
        const string source = "@semantic record Value { @space(world.position) position: f32; }";

        SyntaxTree tree = SyntaxTree.Parse(source, "value.ts");

        Assert.Empty(tree.Diagnostics);
        RecordDeclarationSyntax record = Assert.Single(tree.Root.Members.OfType<RecordDeclarationSyntax>());
        Assert.Equal("semantic", Assert.Single(record.Annotations!).NameToken.Text);
        Assert.Equal("space", Assert.Single(Assert.Single(record.Fields).Annotations!).NameToken.Text);
    }

    [Fact]
    public void Vts_And_Ts_Have_Identical_Gpu_Semantics()
    {
        VdMirComputeModule vts = Compile(ComputeSource, "compute.v.ts");
        VdMirComputeModule ts = Compile(ComputeSource, "compute.ts");

        Assert.True(vts.Success, Diagnostics(vts));
        Assert.True(ts.Success, Diagnostics(ts));
        Assert.Equal(
            NormalizeSourcePath(VdMirJson.Serialize(vts)),
            NormalizeSourcePath(VdMirJson.Serialize(ts)));
    }

    [Fact]
    public void Compute_Profile_Binds_Canonical_M1_Facts_And_Is_Deterministic()
    {
        VdMirComputeModule first = Compile(ComputeSource, "compute.v.ts");
        VdMirComputeModule second = Compile(ComputeSource, "compute.v.ts");

        Assert.True(first.Success, Diagnostics(first));
        Assert.Equal("sdslv.conformance.v1", first.ConformanceSchema);
        Assert.Equal("compute.m1", first.FeatureLevel);
        Assert.Equal("ComputeNoRegression_CS", first.EntryPoint!.Name);
        Assert.Equal((8, 1, 1), (first.EntryPoint.NumThreadsX, first.EntryPoint.NumThreadsY, first.EntryPoint.NumThreadsZ));
        Assert.Equal("dispatch_thread_id", Assert.Single(first.EntryPoint.Builtins).Builtin);
        Assert.Collection(
            first.Resources,
            input => Assert.Equal(("Input", 0, VdMirResourceAccess.Readonly), (input.Name, input.Binding, input.Access)),
            output => Assert.Equal(("Output", 1, VdMirResourceAccess.Readwrite), (output.Name, output.Binding, output.Access)));
        Assert.Equal(Hash(VdMirJson.Serialize(first)), Hash(VdMirJson.Serialize(second)));
    }

    [Fact]
    public void Duplicate_Binding_Has_Canonical_Code_And_Related_Span()
    {
        string source = ComputeSource.Replace("@binding(1) readwrite Output", "@binding(0) readwrite Output", StringComparison.Ordinal);

        VdMirComputeModule module = Compile(source, "duplicate.v.ts");

        VdMirDiagnostic diagnostic = Assert.Single(module.Diagnostics, item => item.CanonicalCode == "SDSL-V4112");
        Assert.Equal("resource-binding", diagnostic.Category);
        Assert.Single(diagnostic.RelatedSpans);
        Assert.True(diagnostic.PrimarySpan.Start > diagnostic.RelatedSpans[0].Span.Start);
    }

    [Fact]
    public void Reachable_Host_Allocation_Is_Rejected_But_Unreachable_Host_Code_Is_Irrelevant()
    {
        const string shared = """
            export function AddOne(value: f32): f32 { return value + 1.0; }
            export function HostOnly(value: f32): f32 { return new Box(value); }
            """;
        string safeEntry = "import { AddOne } from \"./shared\";\n" +
            ComputeSource.Replace("Input[index] + 1.0", "AddOne(Input[index])", StringComparison.Ordinal);
        VdMirComputeModule safe = GpuComputeBinder.Compile(new GpuCompilationRequest([
            new GpuSourceFile("shared.ts", shared),
            new GpuSourceFile("compute.v.ts", safeEntry),
        ]));
        string unsafeEntry = safeEntry.Replace("AddOne(Input[index])", "HostOnly(Input[index])", StringComparison.Ordinal);
        VdMirComputeModule unsafeModule = GpuComputeBinder.Compile(new GpuCompilationRequest([
            new GpuSourceFile("shared.ts", shared),
            new GpuSourceFile("compute.v.ts", unsafeEntry),
        ]));

        Assert.True(safe.Success, Diagnostics(safe));
        Assert.DoesNotContain(safe.Diagnostics, diagnostic => diagnostic.Category == "host-only");
        Assert.Contains(unsafeModule.Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4200" && diagnostic.PrimarySpan.File == "shared.ts");
    }

    [Fact]
    public void Immutable_Local_And_Readonly_Resource_Mutation_Are_Rejected()
    {
        string immutable = ComputeSource.Replace("Output[index] =", "index = index + 1;\n    Output[index] =", StringComparison.Ordinal);
        string readonlyMutation = ComputeSource.Replace("Output[index] =", "Input[index] =", StringComparison.Ordinal);

        Assert.Contains(Compile(immutable, "immutable.v.ts").Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V3701");
        Assert.Contains(Compile(readonlyMutation, "readonly.v.ts").Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V3701");
    }

    [Fact]
    public void Profile_Must_Be_Selected_Explicitly()
    {
        VdMirComputeModule module = GpuComputeBinder.Compile(new GpuCompilationRequest(
            [new GpuSourceFile("compute.v.ts", ComputeSource)],
            CopelandCompilerProfile.Host));

        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-0001");
    }

    private static VdMirComputeModule Compile(string source, string path)
        => GpuComputeBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(path, source)]));

    private static string Diagnostics(VdMirComputeModule module)
        => string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string NormalizeSourcePath(string json)
        => json.Replace("compute.v.ts", "compute.ts", StringComparison.Ordinal);

    private static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
