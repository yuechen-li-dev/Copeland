namespace Copeland.TS.Backend.CSharp;

internal static class BatchRuntime
{
    internal static void Emit(CSharpTextWriter writer)
    {
        const string source = """
// M26 measurements favor parallel expensive work at 128 items; cheap work still favors sequential.
// This count-only default is a conservative compromise, not a universal crossover.
private const int __cope_batch_parallel_threshold = 128;
private static int __cope_batch_parallel_threshold_for_testing = 0;

private static TResult[] __cope_batch_map<TSource, TResult>(TSource[] input, global::System.Func<TSource, TResult> body)
{
    var output = new TResult[input.Length];
    global::System.Collections.Concurrent.ConcurrentDictionary<int, global::System.Exception>? failures = null;
    void RunItem(int index)
    {
        try
        {
            __cope_batch_item_entered_for_testing?.Invoke();
            output[index] = body(input[index]);
        }
        catch (global::System.Exception exception)
        {
            var storage = global::System.Threading.LazyInitializer.EnsureInitialized(ref failures);
            storage.TryAdd(index, exception);
        }
    }

    int threshold = __cope_batch_parallel_threshold_for_testing > 0
        ? __cope_batch_parallel_threshold_for_testing
        : __cope_batch_parallel_threshold;
    if (input.Length < threshold && __cope_batch_max_degree_for_testing <= 0)
    {
        for (int index = 0; index < input.Length; index++)
        {
            RunItem(index);
            if (failures is not null)
            {
                break;
            }
        }
    }
    else
    {
        var options = new global::System.Threading.Tasks.ParallelOptions();
        if (__cope_batch_max_degree_for_testing > 0)
        {
            options.MaxDegreeOfParallelism = __cope_batch_max_degree_for_testing;
        }
        global::System.Threading.Tasks.Parallel.For(0, input.Length, options, RunItem);
    }

    if (failures is not null)
    {
        int firstFailure = int.MaxValue;
        foreach (int index in failures.Keys)
        {
            if (index < firstFailure)
            {
                firstFailure = index;
            }
        }
        throw new global::System.InvalidOperationException($"COPE-BATCH-FAILURE index {firstFailure}", failures[firstFailure]);
    }
    return output;
}
""";
        foreach (string line in source.Split('\n'))
        {
            writer.WriteLine(line);
        }
    }
}
