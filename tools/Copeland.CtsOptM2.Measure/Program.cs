using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;

string repositoryRoot = FindRepositoryRoot();
string artifactRoot = Path.Combine(repositoryRoot, "artifacts", "cts-opt-m2");
string temporaryRoot = Path.Combine(repositoryRoot, ".tmp", "cts-opt-m2-measure");
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
};
Directory.CreateDirectory(artifactRoot);
Directory.CreateDirectory(temporaryRoot);

var programs = new List<ProgramMeasurement>();
foreach (string name in new[] { "Application", "Tables", "Flow", "AsyncBatchGenerator" })
{
    string sourcePath = Path.Combine(
        repositoryRoot,
        "tests",
        "Copeland",
        "Copeland.TS.Tests",
        "TestData",
        "BurnIn",
        name + ".ts");
    CopelandCompilation compilation = CopelandCompiler.CompileToMir(
        File.ReadAllText(sourcePath),
        new CopelandCompilationOptions { SourcePath = sourcePath });
    if (!compilation.Success || compilation.MirCompilation?.Program is null)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    var baselineOptions = new JavaScriptEmissionOptions
    {
        Profile = JavaScriptEmissionProfile.Production,
        EnableGeneratedDefinitionReachability = false,
    };
    var optimizedOptions = baselineOptions with { EnableGeneratedDefinitionReachability = true };
    JavaScriptCompilation baseline = JavaScriptBackend.Emit(compilation.MirCompilation.Program, baselineOptions);
    JavaScriptCompilation optimized = JavaScriptBackend.Emit(compilation.MirCompilation.Program, optimizedOptions);
    RequireSuccess(baseline);
    RequireSuccess(optimized);

    string baselinePath = Path.Combine(temporaryRoot, name + "-baseline.js");
    string optimizedPath = Path.Combine(temporaryRoot, name + "-optimized.js");
    File.WriteAllText(baselinePath, baseline.SourceText, new UTF8Encoding(false));
    File.WriteAllText(optimizedPath, optimized.SourceText, new UTF8Encoding(false));

    string baselineOutput = ExecuteNode(baselinePath, invokeMain: true).StandardOutput;
    string optimizedOutput = ExecuteNode(optimizedPath, invokeMain: true).StandardOutput;
    if (!string.Equals(baselineOutput, optimizedOutput, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Runtime parity failed for {name}.");
    }

    double baselineCompileMs = MeasureEmission(compilation.MirCompilation.Program, baselineOptions);
    double optimizedCompileMs = MeasureEmission(compilation.MirCompilation.Program, optimizedOptions);
    double baselineStartupMs = Median(Enumerable.Range(0, 9).Select(_ => ExecuteNode(baselinePath, invokeMain: false).ElapsedMilliseconds));
    double optimizedStartupMs = Median(Enumerable.Range(0, 9).Select(_ => ExecuteNode(optimizedPath, invokeMain: false).ElapsedMilliseconds));
    int baselineBytes = Encoding.UTF8.GetByteCount(baseline.SourceText!);
    int optimizedBytes = Encoding.UTF8.GetByteCount(optimized.SourceText!);
    CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program);
    if (csharp.Diagnostics.Count > 0)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, csharp.Diagnostics));
    }
    int csharpBytes = Encoding.UTF8.GetByteCount(csharp.SourceText!);

    programs.Add(new ProgramMeasurement(
        name,
        baselineBytes,
        optimizedBytes,
        baselineBytes - optimizedBytes,
        baseline.Reachability!,
        optimized.Reachability!,
        baselineStartupMs,
        optimizedStartupMs,
        baselineCompileMs,
        optimizedCompileMs,
        csharpBytes,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baselineOutput)))));
}

WriteJson("baseline-reachability.json", new
{
    milestone = "CTS-OPT-M2",
    profile = "Production",
    reachabilityEnabled = false,
    programs = programs.Select(program => new { program.Name, program.BaselineBytes, report = program.BaselineReport }),
});
WriteJson("optimized-reachability.json", new
{
    milestone = "CTS-OPT-M2",
    profile = "Production",
    reachabilityEnabled = true,
    programs = programs.Select(program => new { program.Name, program.OptimizedBytes, report = program.OptimizedReport }),
});
WriteJson("removed-definitions.json", new
{
    milestone = "CTS-OPT-M2",
    definitions = programs.SelectMany(program => program.OptimizedReport.Definitions
        .Where(definition => !definition.IsReachable)
        .Select(definition => new
        {
            program = program.Name,
            definition.StableId,
            definition.Kind,
            definition.EmittedBytes,
        })),
});
WriteJson("corpus-size-comparison.json", new
{
    milestone = "CTS-OPT-M2",
    m0OldDeadByteEstimate = 6037,
    m1TablesProductionBaselineBytes = 36450,
    programs = programs.Select(program => new
    {
        program.Name,
        program.BaselineBytes,
        program.OptimizedBytes,
        program.RemovedBytes,
        definitionsBefore = program.OptimizedReport.DefinitionCount,
        definitionsRetained = program.OptimizedReport.RetainedCount,
        definitionsRemoved = program.OptimizedReport.RemovedCount,
        csharpBytes = program.CSharpBytes,
        runtimeOutputSha256 = program.OutputHash,
    }),
    totalBaselineBytes = programs.Sum(program => program.BaselineBytes),
    totalOptimizedBytes = programs.Sum(program => program.OptimizedBytes),
    totalRemovedBytes = programs.Sum(program => program.RemovedBytes),
    metaprogrammingRuntimeBytes = 0,
});
WriteJson("startup-comparison.json", new
{
    milestone = "CTS-OPT-M2",
    samplesPerProgram = 9,
    programs = programs.Select(program => new
    {
        program.Name,
        baselineMedianMs = program.BaselineStartupMs,
        optimizedMedianMs = program.OptimizedStartupMs,
        changeMs = program.OptimizedStartupMs - program.BaselineStartupMs,
        baselineEmissionMedianMs = program.BaselineCompileMs,
        optimizedEmissionMedianMs = program.OptimizedCompileMs,
        emissionOverheadMs = program.OptimizedCompileMs - program.BaselineCompileMs,
    }),
});

var manifest = new
{
    milestone = "CTS-OPT-M2",
    kind = "module-local-generated-definition-reachability-dce",
    outcome = "A",
    moduleLocal = true,
    generatedDefinitionsOnly = true,
    wholeProgramLinkerAdded = false,
    ssaAdded = false,
    expressionDceAdded = false,
    semanticValidationRunsBeforeDce = true,
    exportsRooted = true,
    initializersRooted = true,
    interopRooted = true,
    materializedArtifactsRooted = true,
    identityRootsPreserved = true,
    deterministicReachability = true,
    sourceMappingPreserved = true,
    languageSemanticsChanged = false,
    productionBytesBefore = programs.Sum(program => program.BaselineBytes),
    productionBytesAfter = programs.Sum(program => program.OptimizedBytes),
    productionSavingsBytes = programs.Sum(program => program.RemovedBytes),
    removedDefinitions = programs.Sum(program => program.OptimizedReport.RemovedCount),
    runtimeParityPassed = true,
};
WriteJson("cts-opt-m2-manifest.json", manifest);
File.WriteAllText(
    Path.Combine(artifactRoot, "cts-opt-m2-manifest.txt"),
    string.Join(Environment.NewLine, new[]
    {
        "CTS-OPT-M2",
        "Outcome A",
        "module-local generated-definition reachability DCE",
        $"Production bytes: {manifest.productionBytesBefore} -> {manifest.productionBytesAfter}",
        $"Removed bytes: {manifest.productionSavingsBytes}",
        $"Removed definitions: {manifest.removedDefinitions}",
        "Runtime parity: PASS",
        "Whole-program linker: NO",
        "SSA/expression DCE: NO",
    }) + Environment.NewLine,
    new UTF8Encoding(false));

Directory.Delete(temporaryRoot, recursive: true);
Console.WriteLine(JsonSerializer.Serialize(manifest, jsonOptions));

void WriteJson(string fileName, object value)
{
    File.WriteAllText(
        Path.Combine(artifactRoot, fileName),
        JsonSerializer.Serialize(value, jsonOptions) + Environment.NewLine,
        new UTF8Encoding(false));
}

static double MeasureEmission(Copeland.TS.Mir.MirProgram program, JavaScriptEmissionOptions options)
{
    var samples = new List<double>();
    for (int index = 0; index < 21; index += 1)
    {
        long started = Stopwatch.GetTimestamp();
        RequireSuccess(JavaScriptBackend.Emit(program, options));
        if (index > 0)
        {
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
    return Median(samples);
}

static NodeResult ExecuteNode(string sourcePath, bool invokeMain)
{
    string executablePath = sourcePath;
    string? temporaryInvocationPath = null;
    if (invokeMain)
    {
        temporaryInvocationPath = Path.ChangeExtension(sourcePath, ".run.js");
        File.WriteAllText(
            temporaryInvocationPath,
            File.ReadAllText(sourcePath) + "console.log(main());\n",
            new UTF8Encoding(false));
        executablePath = temporaryInvocationPath;
    }

    var startInfo = new ProcessStartInfo("node", executablePath)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    long started = Stopwatch.GetTimestamp();
    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start Node.js.");
    string standardOutput = process.StandardOutput.ReadToEnd();
    string standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();
    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Node.js failed for '{sourcePath}': {standardError}");
    }
    return new NodeResult(standardOutput, elapsed);
}

static void RequireSuccess(JavaScriptCompilation compilation)
{
    if (!compilation.Success)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
    }
}

static double Median(IEnumerable<double> values)
{
    double[] ordered = values.OrderBy(value => value).ToArray();
    return ordered[ordered.Length / 2];
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }
    throw new InvalidOperationException("Could not locate Copeland.slnx.");
}

internal sealed record NodeResult(string StandardOutput, double ElapsedMilliseconds);

internal sealed record ProgramMeasurement(
    string Name,
    int BaselineBytes,
    int OptimizedBytes,
    int RemovedBytes,
    JavaScriptReachabilityReport BaselineReport,
    JavaScriptReachabilityReport OptimizedReport,
    double BaselineStartupMs,
    double OptimizedStartupMs,
    double BaselineCompileMs,
    double OptimizedCompileMs,
    int CSharpBytes,
    string OutputHash);
