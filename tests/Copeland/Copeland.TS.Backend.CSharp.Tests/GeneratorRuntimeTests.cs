using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class GeneratorRuntimeTests
{
    [Fact]
    public void Generated_clr_iterator_preserves_local_state_and_yield_aliases()
    {
        const string source = """
            function* countTo(limit: number): Iterable<number> {
                let current: number = 0;
                while (current < limit) {
                    yield current;
                    current = current + 1;
                }
                yield break;
            }

            function consume(): void {
                for (const value of countTo(1)) {
                }
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("IEnumerable<double>", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("yield return", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("foreach (double value", emitted.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        var sequence = Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "countTo", 3d));
        Assert.Equal([0d, 1d, 2d], sequence.Cast<double>().ToArray());
    }

    [Fact]
    public void Generated_clr_iterators_are_pull_driven_stable_and_independent()
    {
        const string source = """
            function* values(): Iterable<number> {
                yield 1;
                yield return 2;
            }

            function* delegated(): Iterable<number> {
                yield 0;
                yield* values();
            }

            function* emptyReturn(): Iterable<number> {
                return;
            }

            function* emptyBreak(): Iterable<number> {
                yield break;
            }
            """;

        Assembly assembly = Compile(source);
        var first = Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(assembly, "values"));
        var second = Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(assembly, "values"));

        IEnumerator firstIterator = first.GetEnumerator();
        IEnumerator secondIterator = second.GetEnumerator();
        Assert.True(firstIterator.MoveNext());
        Assert.Equal(1d, firstIterator.Current);
        Assert.True(secondIterator.MoveNext());
        Assert.Equal(1d, secondIterator.Current);
        Assert.True(firstIterator.MoveNext());
        Assert.Equal(2d, firstIterator.Current);
        Assert.False(firstIterator.MoveNext());
        Assert.False(firstIterator.MoveNext());

        var delegated = Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(assembly, "delegated"));
        Assert.Equal([0d, 1d, 2d], delegated.Cast<double>().ToArray());
        Assert.False(Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(assembly, "emptyReturn")).GetEnumerator().MoveNext());
        Assert.False(Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(assembly, "emptyBreak")).GetEnumerator().MoveNext());
    }

    [Fact]
    public void Disposing_a_clr_generator_early_releases_its_resource()
    {
        const string source = """
            using System.IO;

            function* read(path: string): Iterable<number> {
                using reader = new StreamReader(path);
                yield 1;
                yield 2;
            }
            """;

        Assembly assembly = Compile(source);
        string path = Path.GetTempFileName();
        try
        {
            var sequence = Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(assembly, "read", path));
            AssertCanOpenExclusively(path);
            IEnumerator iterator = sequence.GetEnumerator();
            Assert.True(iterator.MoveNext());
            Assert.Equal(1d, iterator.Current);
            (iterator as IDisposable)!.Dispose();

            AssertCanOpenExclusively(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Generated_clr_generator_surfaces_failure_on_the_advancement_that_reaches_it()
    {
        const string source = """
            function failed(): number ! string {
                return err("broken");
            }

            function* faulty(): Iterable<number> {
                yield 1;
                const ignored: number = failed()!;
            }
            """;

        Assembly assembly = Compile(source);
        IEnumerator iterator = Assert.IsAssignableFrom<IEnumerable>(GeneratedModuleInvoker.Invoke(assembly, "faulty")).GetEnumerator();
        Assert.True(iterator.MoveNext());
        Assert.Equal(1d, iterator.Current);
        Assert.ThrowsAny<Exception>(() => iterator.MoveNext());
    }

    [Fact]
    public void Generated_clr_generator_wrapper_rejects_reentrant_advancement()
    {
        Assembly assembly = Compile("function* one(): Iterable<number> { yield 1; }");
        Type wrapperDefinition = assembly.GetType("Copeland.Generated.CopeGeneratorEnumerable`1", throwOnError: true)!;
        Type wrapperType = wrapperDefinition.MakeGenericType(typeof(double));
        IEnumerator<double>? outer = null;
        Func<IEnumerator<double>> factory = () => new ReentrantEnumerator(() => outer!.MoveNext());
        var sequence = Assert.IsAssignableFrom<IEnumerable<double>>(Activator.CreateInstance(wrapperType, factory));
        outer = sequence.GetEnumerator();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => outer.MoveNext());
        Assert.Contains("cannot be resumed", error.Message, StringComparison.Ordinal);
    }

    private static Assembly Compile(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        return generated.Assembly!;
    }

    private static void AssertCanOpenExclusively(string path)
    {
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private sealed class ReentrantEnumerator(Func<bool> reenter) : IEnumerator<double>
    {
        private bool invoked;

        public double Current => 0;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (invoked)
            {
                return false;
            }

            invoked = true;
            return reenter();
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
