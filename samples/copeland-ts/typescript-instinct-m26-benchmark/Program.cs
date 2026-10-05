using System.Diagnostics;
using System.Text.Json;
using M = global::CopeBench.Copeland.CopelandProject;

bool quick = args.Length > 0 && args[0] == "quick";
string only = args.Length > 1 ? args[1] : "";

void Run(string name, Func<string> body)
{
    if (only != "" && only != name) return;
    var trials = new List<object>();
    for (int attempt = 0; attempt < 3; attempt++)
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        var stopwatch = Stopwatch.StartNew();
        string result = body();
        stopwatch.Stop();
        trials.Add(new
        {
            result,
            milliseconds = stopwatch.Elapsed.TotalMilliseconds,
            allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated,
            gcCollections = new[] { GC.CollectionCount(0) - collections[0], GC.CollectionCount(1) - collections[1], GC.CollectionCount(2) - collections[2] },
        });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { name, quick, trials, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 }));
}

Run("fib", () => M.fibBench(0).ToString());
Run("nbody", () => M.nbodyBench(quick ? 1000 : 10_000_000).ToString("F9"));
Run("trees", () => M.treesBench(quick ? 8 : 18).ToString());
Run("sieve", () => M.sieveBench(quick ? 1000 : 20_000_000).ToString());
Run("arrays", () => M.arraysBench(quick ? 1 : 10, quick ? 1000 : 1_000_000).ToString("R"));
Run("strings", () => M.stringsBench(quick ? 1 : 20, quick ? 1000 : 100_000).ToString());
Run("objects", () => M.objectsBench(quick ? 1 : 50, quick ? 1000 : 200_000).ToString("R"));
Run("closures", () => M.closuresBench(quick ? 1000 : 100_000_000).ToString());

Run("native-strings", () => M.nativeStringsBench(quick ? 1 : 20, quick ? 1000 : 100_000).ToString());

Run("maps", () => M.mapsBench(quick ? 1 : 10, quick ? 1000 : 100_000).ToString());
