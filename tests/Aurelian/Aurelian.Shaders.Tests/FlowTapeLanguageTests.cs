using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class FlowTapeLanguageTests
{
    [Fact]
    public void Reused_fragments_have_distinct_storage_and_authored_source_maps()
    {
        var module = Compile("""
            flow Child(count: u32) -> u32 {
                board { remaining: u32 = count; }
                state Work initial { finish count + 1; }
            }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child(count) -> First; }
                state First(value: u32) { run Child(value) -> Last; }
                state Last(value: u32) { finish value; }
            }
            """, "var frame = Parent.start(U32(Input[0])); frame = Parent.step(frame); Output[0] = Convert<f32>(Parent.position(frame));");
        Assert.True(module.Success, Diagnostics(module));
        VdMirFlowTape tape = Assert.Single(module.FlowTapes!);
        Assert.Equal(Enumerable.Range(0, 5), tape.States.Select(state => state.Position));
        Assert.Equal(2, tape.States.Where(state => state.Fragment == "Child").Select(state => state.Path).Distinct().Count());
        Assert.All(tape.States.Where(state => state.Fragment == "Child"), state =>
        {
            Assert.NotNull(state.CompositionSite);
            Assert.Equal("", state.ParentPath);
            Assert.Equal("tape.v.ts", state.Source.File);
            Assert.Equal(4, state.Source.Length);
        });
        VdMirValueType frame = module.ValueTypes!.Single(type => type.Name == tape.FrameType);
        Assert.DoesNotContain(frame.Fields, field => field.Name is "depth" or "slot0" or "slot1");
        Assert.Equal(2, frame.Fields.Count(field => field.Name.EndsWith("parameter_count", StringComparison.Ordinal) && field.Name.StartsWith("__run", StringComparison.Ordinal)));
        ValidateSpirv(module);
    }

    [Fact]
    public void Finite_record_array_and_enum_storage_initializes_through_existing_layout_authority()
    {
        var module = Compile("""
            enum Token { Empty, Value(value: u32) }
            record Payload { token: Token; values: Array<u32, 2>; }
            flow Child(count: u32) -> Payload {
                state Work initial { finish { token: Token.Value(count), values: [count, count + 1] }; }
            }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child(count) -> Accept; }
                state Accept(value: Payload) { finish value.values[1]; }
            }
            """, "var frame = Parent.start(U32(Input[0])); frame = Parent.step(frame); frame = Parent.step(frame); frame = Parent.step(frame); Output[0] = Convert<f32>(Parent.position(frame));");
        Assert.True(module.Success, Diagnostics(module));
        ValidateSpirv(module);
    }

    [Theory]
    [InlineData("push Child(count) -> Accept;", "COPE-GPU-FLOW-0002")]
    [InlineData("run Missing(count) -> Accept;", "COPE-GPU-FLOW-0007")]
    [InlineData("run Parent(count) -> Accept;", "COPE-GPU-FLOW-0003")]
    [InlineData("run Child(count) -> Missing;", "COPE-FLOW-EXEC-0013")]
    public void Illegal_composition_has_a_focused_diagnostic(string control, string code)
    {
        var module = Compile($$"""
            flow Child(count: u32) -> u32 { state Work initial { finish count; } }
            flow Parent(count: u32) -> u32 {
                state Begin initial { {{control}} }
                state Accept(value: u32) { finish value; }
            }
            """);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("stack 2;", "COPE-GPU-FLOW-0005")]
    [InlineData("", "COPE-GPU-TYPE-0002")]
    public void Stack_clauses_and_mismatched_entry_types_are_rejected(string declaration, string code)
    {
        var module = Compile($$"""
            flow Child(count: u32) -> u32 { state Work initial { finish count; } }
            flow Parent(count: u32) -> u32 {
                {{declaration}}
                state Begin initial { run Child(count) -> Accept; }
                state Accept(value: f32) { finish 1; }
            }
            """);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Transition_cannot_forge_a_typed_continuation()
    {
        var module = Compile("""
            flow Child(count: u32) -> u32 { state Work initial { finish count; } }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child(count) -> Accept; }
                state Accept(value: u32) { transition Accept; }
            }
            """);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-FLOW-0008");
    }

    [Fact]
    public void Mutual_recursion_is_rejected_before_frame_expansion()
    {
        var module = Compile("""
            flow Child(count: u32) -> u32 {
                state Work initial { run Parent(count) -> Accept; }
                state Accept(value: u32) { finish value; }
            }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child(count) -> Accept; }
                state Accept(value: u32) { finish value; }
            }
            """);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-FLOW-0003");
    }

    [Fact]
    public void Failure_payload_is_checked_even_when_step_is_not_reachable()
    {
        var module = Compile("""
            flow Child(count: u32) -> u32 ! u32 {
                state Work initial { fail count; }
            }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child(count) -> Accept ! Reject; }
                state Accept(value: u32) { finish value; }
                state Reject(error: f32) { finish 0; }
            }
            """);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-TYPE-0002");
    }

    [Fact]
    public void Duplicate_flow_names_produce_diagnostics_instead_of_throwing()
    {
        var module = Compile("""
            flow Child(count: u32) -> u32 { state Work initial { finish count; } }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child(count) -> Accept; }
                state Accept(value: u32) { finish value; }
            }
            flow Parent(count: u32) -> u32 { state Work initial { finish count; } }
            """);
        Assert.False(module.Success);
        Assert.NotEmpty(module.Diagnostics);
    }

    [Fact]
    public void Void_child_returns_to_a_payload_free_continuation()
    {
        var module = Compile("""
            flow Child() { state Work initial { finish; } }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child() -> Accept; }
                state Accept { finish count; }
            }
            """, "var frame = Parent.start(U32(Input[0])); frame = Parent.step(frame); frame = Parent.step(frame); frame = Parent.step(frame); Output[0] = Convert<f32>(Parent.position(frame));");
        Assert.True(module.Success, Diagnostics(module));
        ValidateSpirv(module);
    }

    [Fact]
    public void Tape_expansion_has_a_finite_state_budget()
    {
        string additional = string.Join("\n", Enumerable.Range(0, 62).Select(index => $"state Extra{index} {{ finish 0; }}"));
        var module = Compile($$"""
            flow Child() { state Work initial { finish; } }
            flow Parent(count: u32) -> u32 {
                state Begin initial { run Child() -> End; }
                state End { finish 0; }
                {{additional}}
            }
            """);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-FLOW-0004");
    }

    private static VdMirComputeModule Compile(string declarations,
        string body = "var frame = Parent.start(2); Output[0] = 0.0;")
    {
        return GpuComputeBinder.Compile(new([new("tape.v.ts", declarations + $$"""
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readonly Input: StorageBuffer<f32>,
                @binding(1) readwrite Output: StorageBuffer<f32>): void {
                {{body}}
            }
            """)]));
    }

    private static void ValidateSpirv(VdMirComputeModule module)
    {
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
    }

    private static string Diagnostics(VdMirComputeModule module)
    {
        return string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message));
    }
}
