using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Compiler;
using Copeland.TS.TestSupport;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class BatchRuntimeTests
{
    [Fact]
    public void Batch_maps_in_stable_order_with_a_local_and_immutable_capture()
    {
        const string source = """
            function main(): number[] {
                const values: number[] = [1, 2, 3];
                const increment: number = 1;
                const output: number[] = batch values as value {
                    let current: number = value + increment;
                    return current * current;
                };
                return output;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("batch values as value", compilation.MirText, StringComparison.Ordinal);

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("System.Threading.Tasks.Parallel.For", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("ConcurrentDictionary<int, global::System.Exception>", emitted.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal([4d, 9d, 16d], Assert.IsType<double[]>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public async Task Batch_allows_multiple_item_bodies_to_overlap_when_parallel_execution_is_available()
    {
        if (Environment.ProcessorCount < 2)
        {
            return;
        }

        const string source = """
            function main(): number[] {
                const values: number[] = [1, 2, 3, 4, 5, 6, 7, 8];
                return batch values as value { return value * value; };
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Type module = generated.Assembly!.GetType("Copeland.Generated.CopelandModule", throwOnError: true)!;
        FieldInfo enteredField = module.GetField("__cope_batch_item_entered_for_testing", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo degreeField = module.GetField("__cope_batch_max_degree_for_testing", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.NotNull(enteredField);
        Assert.NotNull(degreeField);

        using var gate = new Barrier(2);
        var active = 0;
        var maximumActive = 0;
        Action entered = () =>
        {
            int current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, current);
            try
            {
                if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
                {
                    throw new InvalidOperationException("The controlled batch scheduler did not admit a second item body.");
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        };

        degreeField.SetValue(null, 2);
        enteredField.SetValue(null, entered);
        try
        {
            double[] expected = [1d, 4d, 9d, 16d, 25d, 36d, 49d, 64d];
            Task<object?> invocation = Task.Run(() => GeneratedModuleInvoker.Invoke(generated.Assembly!, "main"));
            Task completed = await Task.WhenAny(invocation, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(invocation, completed);
            Assert.Equal(expected, Assert.IsType<double[]>(await invocation));
            Assert.True(Volatile.Read(ref maximumActive) > 1, "The batch runtime did not permit overlapping item bodies.");
        }
        finally
        {
            enteredField.SetValue(null, null);
            degreeField.SetValue(null, 0);
        }
    }

    [Theory]
    [InlineData("function main(): number[] { return batch 42 as value { return value; }; }", "COPE-BATCH-0002")]
    [InlineData("function main(): number[] { let total: number = 0; return batch [1] as value { total = total + value; return value; }; }", "COPE-BATCH-0007")]
    [InlineData("function main(): number[] { return batch [1] as value { return batch [value] as inner { return inner; }; }; }", "COPE-BATCH-0011")]
    public void Batch_rejects_unsupported_semantic_shapes(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            int observed = Volatile.Read(ref maximum);
            if (candidate <= observed)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
            {
                return;
            }
        }
    }
}
