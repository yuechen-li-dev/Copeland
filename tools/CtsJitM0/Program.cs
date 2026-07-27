using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;

namespace CtsJitM0;

public static class Program
{
    private const int DefaultColdRuns = 10;
    private const int DefaultWarmupRounds = 10;
    private const int DefaultMeasuredRounds = 30;

    private static readonly IReadOnlyList<WorkloadDefinition> Workloads = CtsJitM0Workloads.All;

    public static int Main(string[] args)
    {
        try
        {
            BenchmarkOptions options = BenchmarkOptions.Parse(args);
            string repositoryRoot = FindRepositoryRoot();
            string outputDirectory = Path.GetFullPath(Path.Combine(repositoryRoot, options.OutputDirectory));
            Directory.CreateDirectory(outputDirectory);

            var results = new List<WorkloadResult>();
            foreach (WorkloadDefinition workload in Workloads)
            {
                Console.WriteLine($"Preparing {workload.Name}.");
                WorkloadResult result = RunWorkload(repositoryRoot, outputDirectory, workload, options);
                results.Add(result);
                Console.WriteLine($"Completed {workload.Name}: checksum {result.Checksum}.");
            }

            var document = new BenchmarkDocument(
                CreateEnvironment(),
                new BenchmarkProtocol(options.ColdRuns, options.WarmupRounds, options.MeasuredRounds, options.JavaScriptProfile.ToString()),
                results);
            WriteJson(Path.Combine(outputDirectory, "environment.json"), document.Environment);
            WriteJson(Path.Combine(outputDirectory, "results.json"), document);
            Console.WriteLine($"Wrote {outputDirectory}.");
            return 0;
        }
        catch (BenchmarkUsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(BenchmarkOptions.Usage);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static WorkloadResult RunWorkload(
        string repositoryRoot,
        string outputDirectory,
        WorkloadDefinition workload,
        BenchmarkOptions options)
    {
        string workloadDirectory = Path.Combine(outputDirectory, "generated", workload.Name);
        Directory.CreateDirectory(workloadDirectory);
        string sourcePath = Path.Combine(repositoryRoot, "tools", "CtsJitM0", "Workloads", workload.SourceFileName);
        string sourceText = File.ReadAllText(sourcePath);
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(sourceText, new CopelandCompilationOptions
        {
            SourcePath = sourcePath,
            ProjectRoot = Path.GetDirectoryName(sourcePath),
        });
        EnsureCompilationSucceeded(workload, compilation);

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        if (csharp.Diagnostics.Count > 0 || csharp.SourceText is null)
        {
            throw new InvalidOperationException($"C# emission failed for {workload.Name}: {string.Join(Environment.NewLine, csharp.Diagnostics.Select(diagnostic => diagnostic.Message))}");
        }

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(
            compilation.MirCompilation.Program!,
            new JavaScriptEmissionOptions { Profile = options.JavaScriptProfile });
        if (!javaScript.Success || javaScript.SourceText is null)
        {
            throw new InvalidOperationException($"JavaScript emission failed for {workload.Name}: {string.Join(Environment.NewLine, javaScript.Diagnostics.Select(diagnostic => diagnostic.Message))}");
        }

        string generatedCSharpPath = Path.Combine(workloadDirectory, workload.Name + ".g.cs");
        string generatedJavaScriptPath = Path.Combine(workloadDirectory, workload.Name + "." + options.JavaScriptProfile.ToString().ToLowerInvariant() + ".g.js");
        File.WriteAllText(generatedCSharpPath, csharp.SourceText);
        File.WriteAllText(generatedJavaScriptPath, javaScript.SourceText);

        string csharpHost = CreateCSharpHost(workloadDirectory, workload.Name, csharp.SourceText);
        string javaScriptHost = CreateJavaScriptHost(workloadDirectory, javaScript.SourceText);
        string csharpAssembly = BuildCSharpHost(csharpHost, workload.Name);

        string csharpChecksum = RunCommand("dotnet", Quote(csharpAssembly) + " --checksum", csharpHost);
        string javaScriptChecksum = RunCommand("node", Quote(javaScriptHost) + " --checksum", workloadDirectory);
        int checksum = ParseChecksum(csharpChecksum, "C#", workload.Name);
        int javaScriptValue = ParseChecksum(javaScriptChecksum, "JavaScript", workload.Name);
        if (checksum != javaScriptValue)
        {
            throw new InvalidOperationException($"Semantic mismatch for {workload.Name}: generated C# returned {checksum}, generated JavaScript returned {javaScriptValue}.");
        }

        IReadOnlyList<double> csharpCold = MeasureColdRuns("dotnet", Quote(csharpAssembly) + " --checksum", csharpHost, options.ColdRuns);
        IReadOnlyList<double> javaScriptCold = MeasureColdRuns("node", Quote(javaScriptHost) + " --checksum", workloadDirectory, options.ColdRuns);
        WarmMeasurement csharpWarm = ParseWarmMeasurement(RunCommand(
            "dotnet",
            Quote(csharpAssembly) + $" --warm --warmup {options.WarmupRounds} --rounds {options.MeasuredRounds} --iterations {workload.Iterations}",
            csharpHost),
            "C#",
            workload.Name);
        WarmMeasurement javaScriptWarm = ParseWarmMeasurement(RunCommand(
            "node",
            Quote(javaScriptHost) + $" --warm --warmup {options.WarmupRounds} --rounds {options.MeasuredRounds} --iterations {workload.Iterations}",
            workloadDirectory),
            "JavaScript",
            workload.Name);

        if (csharpWarm.Checksum != javaScriptWarm.Checksum)
        {
            throw new InvalidOperationException($"Warm semantic mismatch for {workload.Name}: generated C# returned {csharpWarm.Checksum}, generated JavaScript returned {javaScriptWarm.Checksum}.");
        }

        return new WorkloadResult(
            workload.Name,
            workload.SourceFileName,
            workload.Iterations,
            csharpWarm.Checksum,
            new ColdMeasurement(csharpCold),
            new ColdMeasurement(javaScriptCold),
            csharpWarm,
            javaScriptWarm,
            new ArtifactSizes(
                new FileInfo(sourcePath).Length,
                new FileInfo(generatedCSharpPath).Length,
                new FileInfo(generatedJavaScriptPath).Length,
                new FileInfo(csharpAssembly).Length,
                new FileInfo(javaScriptHost).Length));
    }

    private static void EnsureCompilationSucceeded(WorkloadDefinition workload, CopelandCompilation compilation)
    {
        if (!compilation.Success)
        {
            throw new InvalidOperationException($"Copeland compilation failed for {workload.Name}: {string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message))}");
        }
    }

    private static string CreateCSharpHost(string workloadDirectory, string workloadName, string generatedSource)
    {
        string directory = Path.Combine(workloadDirectory, "csharp-host");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Generated.cs"), generatedSource);
        File.WriteAllText(Path.Combine(directory, "CtsJitM0Host.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <Optimize>true</Optimize>
                <DebugType>none</DebugType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "Program.cs"), CSharpHostSource);
        return directory;
    }

    private static string CreateJavaScriptHost(string workloadDirectory, string generatedSource)
    {
        string path = Path.Combine(workloadDirectory, "javascript-host.js");
        File.WriteAllText(path, generatedSource + Environment.NewLine + JavaScriptHostSource);
        return path;
    }

    private static string BuildCSharpHost(string hostDirectory, string workloadName)
    {
        RunCommand("dotnet", "build CtsJitM0Host.csproj --configuration Release --nologo", hostDirectory);
        string assemblyPath = Path.Combine(hostDirectory, "bin", "Release", "net10.0", "CtsJitM0Host.dll");
        if (!File.Exists(assemblyPath))
        {
            throw new InvalidOperationException($"Generated C# host did not produce an assembly for {workloadName}.");
        }

        return assemblyPath;
    }

    private static IReadOnlyList<double> MeasureColdRuns(string fileName, string arguments, string workingDirectory, int count)
    {
        var values = new List<double>(count);
        for (int index = 0; index < count; index += 1)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            RunCommand(fileName, arguments, workingDirectory);
            stopwatch.Stop();
            values.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return values;
    }

    private static string RunCommand(string fileName, string arguments, string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }

        return standardOutput.Trim();
    }

    private static int ParseChecksum(string text, string runtime, string workload)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("checksum").GetInt32();
    }

    private static WarmMeasurement ParseWarmMeasurement(string text, string runtime, string workload)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        return new WarmMeasurement(
            root.GetProperty("checksum").GetInt32(),
            root.GetProperty("milliseconds").EnumerateArray().Select(value => value.GetDouble()).ToArray(),
            root.TryGetProperty("allocatedBytes", out JsonElement allocatedBytes) ? allocatedBytes.GetInt64() : null,
            root.TryGetProperty("gcCollections", out JsonElement gcCollections) ? gcCollections.EnumerateArray().Select(value => value.GetInt32()).ToArray() : null,
            root.TryGetProperty("heapDeltaBytes", out JsonElement heapDeltaBytes) ? heapDeltaBytes.GetInt64() : null);
    }

    private static BenchmarkEnvironment CreateEnvironment()
    {
        return new BenchmarkEnvironment(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            RunCommand("dotnet", "--version", Environment.CurrentDirectory),
            RunCommand("dotnet", "--info", Environment.CurrentDirectory),
            RunCommand("node", "--version", Environment.CurrentDirectory),
            RunCommand("node", "-p \"process.versions.v8\"", Environment.CurrentDirectory),
            "Release, framework-dependent, default tiered compilation and dynamic PGO; no ReadyToRun publish, profiler, or debugger.",
            "Default Node.js flags; no inspector or source maps. Heap delta is a coarse process-memory signal, not allocation equivalence.");
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Copeland.slnx from the current directory.");
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    private const string CSharpHostSource = """
        using System.Diagnostics;
        using System.Text.Json;
        using Copeland.Generated;

        internal static class Program
        {
            private static int Main(string[] args)
            {
                try
                {
                    HostOptions options = HostOptions.Parse(args);
                    if (options.ChecksumOnly)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(new { checksum = CopelandModule.Run(17) }));
                        return 0;
                    }

                    for (int index = 0; index < options.WarmupRounds; index += 1)
                    {
                        CopelandModule.Run(options.Iterations);
                    }

                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    int[] gcBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
                    var milliseconds = new double[options.MeasuredRounds];
                    int checksum = 0;
                    for (int index = 0; index < options.MeasuredRounds; index += 1)
                    {
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        checksum = CopelandModule.Run(options.Iterations);
                        stopwatch.Stop();
                        milliseconds[index] = stopwatch.Elapsed.TotalMilliseconds;
                    }

                    int[] gcAfter = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        checksum,
                        milliseconds,
                        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                        gcCollections = new[] { gcAfter[0] - gcBefore[0], gcAfter[1] - gcBefore[1], gcAfter[2] - gcBefore[2] },
                    }));
                    return 0;
                }
                catch (ArgumentException exception)
                {
                    Console.Error.WriteLine(exception.Message);
                    return 2;
                }
            }

            private sealed record HostOptions(bool ChecksumOnly, int WarmupRounds, int MeasuredRounds, int Iterations)
            {
                public static HostOptions Parse(string[] args)
                {
                    if (args.Length == 1 && args[0] == "--checksum")
                    {
                        return new HostOptions(true, 0, 0, 0);
                    }

                    if (args.Length != 7 || args[0] != "--warm" || args[1] != "--warmup" || args[3] != "--rounds" || args[5] != "--iterations")
                    {
                        throw new ArgumentException("Use --checksum or --warm --warmup <count> --rounds <count> --iterations <count>.");
                    }

                    return new HostOptions(false, Positive(args[2], "warmup"), Positive(args[4], "rounds"), Positive(args[6], "iterations"));
                }

                private static int Positive(string text, string name)
                {
                    if (!int.TryParse(text, out int value) || value <= 0)
                    {
                        throw new ArgumentException($"{name} must be a positive integer.");
                    }

                    return value;
                }
            }
        }
        """;

    private const string JavaScriptHostSource = """
        function parseOptions(argumentsIn) {
          if (argumentsIn.length === 1 && argumentsIn[0] === "--checksum") return { checksumOnly: true };
          if (argumentsIn.length !== 7 || argumentsIn[0] !== "--warm" || argumentsIn[1] !== "--warmup" || argumentsIn[3] !== "--rounds" || argumentsIn[5] !== "--iterations") {
            throw new Error("Use --checksum or --warm --warmup <count> --rounds <count> --iterations <count>.");
          }
          const warmup = Number(argumentsIn[2]);
          const rounds = Number(argumentsIn[4]);
          const iterations = Number(argumentsIn[6]);
          if (!Number.isInteger(warmup) || !Number.isInteger(rounds) || !Number.isInteger(iterations) || warmup <= 0 || rounds <= 0 || iterations <= 0) {
            throw new Error("warmup, rounds, and iterations must be positive integers.");
          }
          return { checksumOnly: false, warmup, rounds, iterations };
        }

        try {
          const options = parseOptions(process.argv.slice(2));
          if (options.checksumOnly) {
            console.log(JSON.stringify({ checksum: Run(17) }));
          } else {
            for (let index = 0; index < options.warmup; index += 1) Run(options.iterations);
            const heapBefore = process.memoryUsage().heapUsed;
            const milliseconds = [];
            let checksum = 0;
            for (let index = 0; index < options.rounds; index += 1) {
              const start = process.hrtime.bigint();
              checksum = Run(options.iterations);
              milliseconds.push(Number(process.hrtime.bigint() - start) / 1000000);
            }
            console.log(JSON.stringify({ checksum, milliseconds, heapDeltaBytes: process.memoryUsage().heapUsed - heapBefore }));
          }
        } catch (error) {
          console.error(error.message);
          process.exitCode = 2;
        }
        """;
}

public sealed record WorkloadDefinition(string Name, string SourceFileName, int Iterations);

public static class CtsJitM0Workloads
{
    public static IReadOnlyList<WorkloadDefinition> All { get; } =
    [
        new("numeric-kernel", "NumericKernel.ts", 10_000_000),
        new("machina-layout-subset", "MachinaSubset.ts", 4_000),
        new("typed-reducer-batch", "ReducerBatch.ts", 2_000_000),
        new("record-array-transform", "RecordArrayTransform.ts", 2_000_000),
        new("string-processing", "StringProcessing.ts", 250_000),
    ];
}

public sealed record BenchmarkOptions(string OutputDirectory, int ColdRuns, int WarmupRounds, int MeasuredRounds, JavaScriptEmissionProfile JavaScriptProfile)
{
    public const string Usage = "Usage: CtsJitM0 [--output <relative-path>] [--cold-runs <count>] [--warmup-rounds <count>] [--measured-rounds <count>] [--javascript-profile symbolic|production]";

    public static BenchmarkOptions Parse(string[] args)
    {
        string outputDirectory = Path.Combine("artifacts", "cts-jit-m0");
        int coldRuns = 10;
        int warmupRounds = 10;
        int measuredRounds = 30;
        JavaScriptEmissionProfile javaScriptProfile = JavaScriptEmissionProfile.Symbolic;

        for (int index = 0; index < args.Length; index += 1)
        {
            string option = args[index];
            if (option is not "--output" and not "--cold-runs" and not "--warmup-rounds" and not "--measured-rounds" and not "--javascript-profile")
            {
                throw new BenchmarkUsageException($"Unknown option '{option}'.");
            }

            if (index + 1 >= args.Length)
            {
                throw new BenchmarkUsageException($"Option '{option}' requires a value.");
            }

            string value = args[index + 1];
            index += 1;
            if (option == "--output")
            {
                if (Path.IsPathRooted(value) || value.Contains("..", StringComparison.Ordinal))
                {
                    throw new BenchmarkUsageException("Output must be a relative path inside the repository.");
                }

                outputDirectory = value;
                continue;
            }

            if (option == "--javascript-profile")
            {
                javaScriptProfile = value switch
                {
                    "symbolic" => JavaScriptEmissionProfile.Symbolic,
                    "production" => JavaScriptEmissionProfile.Production,
                    _ => throw new BenchmarkUsageException("JavaScript profile must be 'symbolic' or 'production'."),
                };
                continue;
            }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            {
                throw new BenchmarkUsageException($"Option '{option}' requires a positive integer.");
            }

            if (option == "--cold-runs") coldRuns = parsed;
            if (option == "--warmup-rounds") warmupRounds = parsed;
            if (option == "--measured-rounds") measuredRounds = parsed;
        }

        return new BenchmarkOptions(outputDirectory, coldRuns, warmupRounds, measuredRounds, javaScriptProfile);
    }
}

public sealed class BenchmarkUsageException(string message) : Exception(message);

public sealed record BenchmarkDocument(BenchmarkEnvironment Environment, BenchmarkProtocol Protocol, IReadOnlyList<WorkloadResult> Workloads);
public sealed record BenchmarkEnvironment(string OperatingSystem, string Architecture, int LogicalProcessors, long AvailableMemoryBytes, string DotnetSdkVersion, string DotnetInfo, string NodeVersion, string V8Version, string RyuJitConfiguration, string V8Configuration);
public sealed record BenchmarkProtocol(int ColdRuns, int WarmupRounds, int MeasuredRounds, string JavaScriptProfile);
public sealed record WorkloadResult(string Name, string SourceFile, int IterationsPerRound, int Checksum, ColdMeasurement CSharpCold, ColdMeasurement JavaScriptCold, WarmMeasurement CSharpWarm, WarmMeasurement JavaScriptWarm, ArtifactSizes ArtifactSizes);
public sealed record ColdMeasurement(IReadOnlyList<double> Milliseconds);
public sealed record WarmMeasurement(int Checksum, IReadOnlyList<double> Milliseconds, long? AllocatedBytes, IReadOnlyList<int>? GcCollections, long? HeapDeltaBytes);
public sealed record ArtifactSizes(long AuthoredSourceBytes, long GeneratedCSharpBytes, long GeneratedJavaScriptBytes, long CompiledCSharpAssemblyBytes, long JavaScriptHostBytes);
