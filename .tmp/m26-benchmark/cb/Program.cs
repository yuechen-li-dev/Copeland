using System.Diagnostics;
using M = global::CopeBench.Copeland.CopelandProject;

bool quick = args.Length > 0 && args[0] == "quick";
string only = args.Length > 1 ? args[1] : "";

void Run(string name, Func<string> body)
{
    if (only != "" && only != name) return;
    long best = long.MaxValue;
    string result = "";
    for (int attempt = 0; attempt < 3; attempt++)
    {
        var sw = Stopwatch.StartNew();
        result = body();
        sw.Stop();
        best = Math.Min(best, sw.ElapsedMilliseconds);
    }
    Console.WriteLine($"{name}\t{result}\t{best}");
}

Run("fib", () => M.fibBench(0).ToString());
Run("nbody", () => M.nbodyBench(quick ? 1000 : 10_000_000).ToString("F9"));
Run("trees", () => M.treesBench(quick ? 8 : 18).ToString());
Run("sieve", () => M.sieveBench(quick ? 1000 : 20_000_000).ToString());
Run("arrays", () => M.arraysBench(quick ? 1 : 10, quick ? 1000 : 1_000_000).ToString("R"));
Run("strings", () => M.stringsBench(quick ? 1 : 20, quick ? 1000 : 100_000).ToString());
Run("objects", () => M.objectsBench(quick ? 1 : 50, quick ? 1000 : 200_000).ToString("R"));
Run("closures", () => M.closuresBench(quick ? 1000 : 100_000_000).ToString());
