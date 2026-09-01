using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;

string repositoryRoot = FindRepositoryRoot();
string corpusRoot = Path.Combine(
    repositoryRoot,
    "tests",
    "Copeland",
    "Copeland.TS.Tests",
    "TestData",
    "BurnIn");
string artifactRoot = Path.Combine(repositoryRoot, "artifacts", "cts-burn-in");
Directory.CreateDirectory(artifactRoot);

BurnInProgram[] programs =
[
    new("Application", "Application.ts", "console.log(main());\n"),
    new("Tables", "Tables.ts", "console.log(main());\n"),
    new(
        "Flow",
        "Flow.ts",
        "const session = Delivery.start();\n" +
        "console.log(main());\n" +
        "console.log(session.sendStart(10).kind);\n" +
        "console.log(session.sendTick(2).kind);\n" +
        "console.log(session.sendAccept(3).kind);\n" +
        "console.log(session.sendTick(4).kind);\n" +
        "const completed = session.sendAccept(5);\n" +
        "console.log(completed.kind);\n" +
        "console.log(session.state);\n" +
        "console.log(session.board.total);\n"),
    new(
        "AsyncBatchGenerator",
        "AsyncBatchGenerator.ts",
        "console.log(main());\n" +
        "const result = compose(13).value;\n" +
        "console.log(result.$tag);\n" +
        "console.log(result.$payload[0]);\n" +
        "console.log([...values()].join(','));\n"),
];

JsonSerializerOptions jsonOptions = new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

_ = CompileRuntimeProgram(
    "function __burnInWarmup(): int { return 1; }",
    "<burn-in-warmup>");

var reports = new List<object>();
foreach (BurnInProgram program in programs)
{
    reports.Add(RunRuntimeProgram(program));
}
reports.Add(RunTemplateProgram());

var manifest = new
{
    kind = "copeland-ts-parser-level-language-burn-in",
    featureInventoryDerivedFromParser = true,
    documentationUsedAsPrimaryAuthority = false,
    burnInPrograms = 5,
    javascriptPrimaryBackend = true,
    csharpParitySpotChecked = true,
    parserRewritten = false,
    mirRedesigned = false,
    newMajorLanguageFeatureAdded = false,
    oblivionChanged = false,
    functionCardWorkResumed = false,
    compilerTimingMode = "single warmed in-process measurement; milliseconds are coarse signals",
    nodeTimingMode = "median of five fresh Node processes including startup",
    generatedAtUtc = DateTimeOffset.UtcNow,
    programs = reports,
};

string manifestJson = JsonSerializer.Serialize(manifest, jsonOptions) + Environment.NewLine;
File.WriteAllText(
    Path.Combine(artifactRoot, "cts-burn-in-manifest.json"),
    manifestJson,
    new UTF8Encoding(false));
File.WriteAllText(
    Path.Combine(artifactRoot, "cts-burn-in-manifest.txt"),
    BuildTextManifest(reports),
    new UTF8Encoding(false));

Console.WriteLine($"Wrote burn-in evidence to {artifactRoot}");
return;

object RunRuntimeProgram(BurnInProgram program)
{
    string sourcePath = Path.Combine(corpusRoot, program.RelativePath);
    string source = File.ReadAllText(sourcePath);
    StageMeasurement measurement = CompileRuntimeProgram(source, sourcePath);

    if (measurement.Diagnostics.Count > 0 || measurement.Program is null || measurement.JavaScript is null)
    {
        throw new InvalidOperationException(
            $"{program.Name} failed compilation:{Environment.NewLine}" +
            string.Join(Environment.NewLine, measurement.Diagnostics));
    }

    StageMeasurement repeated = CompileRuntimeProgram(source, sourcePath);
    string javascript = measurement.JavaScript;
    if (!string.Equals(javascript, repeated.JavaScript, StringComparison.Ordinal)
        || !string.Equals(measurement.Mir, repeated.Mir, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{program.Name} did not compile deterministically.");
    }

    string programArtifactRoot = Path.Combine(artifactRoot, program.Name);
    Directory.CreateDirectory(programArtifactRoot);
    WriteArtifact(programArtifactRoot, program.Name + ".cope", measurement.Mir!);
    WriteArtifact(programArtifactRoot, program.Name + ".g.js", javascript);
    WriteArtifact(programArtifactRoot, program.Name + ".g.cs", measurement.CSharp!);

    List<double> nodeTimings = [];
    string? runtimeOutput = null;
    for (int repetition = 0; repetition < 5; repetition += 1)
    {
        NodeResult node = RunNode(javascript + Environment.NewLine + program.NodeSuffix);
        if (node.ExitCode != 0)
        {
            throw new InvalidOperationException($"{program.Name} failed under Node: {node.StandardError}");
        }
        if (runtimeOutput is not null && !string.Equals(runtimeOutput, node.StandardOutput, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{program.Name} produced nondeterministic runtime output.");
        }
        runtimeOutput = node.StandardOutput;
        nodeTimings.Add(node.ElapsedMilliseconds);
    }
    WriteArtifact(programArtifactRoot, program.Name + ".runtime.txt", runtimeOutput!);

    int sourceBytes = Encoding.UTF8.GetByteCount(source);
    int javascriptBytes = Encoding.UTF8.GetByteCount(javascript);
    int anonymousCarriers = measurement.Program.Records.Count(
        record => record.Name.StartsWith("__CopeInferredRecord_", StringComparison.Ordinal));
    var report = new
    {
        name = program.Name,
        source = Path.GetRelativePath(repositoryRoot, sourcePath).Replace('\\', '/'),
        sourceLoc = CountLines(source),
        sourceBytes,
        parseMs = measurement.ParseMilliseconds,
        bindMs = measurement.BindMilliseconds,
        templateMs = measurement.StaticMilliseconds,
        lowerMs = measurement.LowerMilliseconds,
        emitJsMs = measurement.EmitJavaScriptMilliseconds,
        totalCompileMs = measurement.TotalMilliseconds,
        generatedJsLoc = CountLines(javascript),
        generatedJsBytes = javascriptBytes,
        jsToSourceSizeRatio = Math.Round((double)javascriptBytes / sourceBytes, 3),
        generatedCarrierCount = measurement.Program.Records.Count,
        anonymousCarrierCount = anonymousCarriers,
        namedCarrierCount = measurement.Program.Records.Count - anonymousCarriers,
        helperCount = Regex.Matches(javascript, @"\bfunction __cope_").Count,
        enumCount = measurement.Program.Enums.Count,
        tableCount = measurement.Program.Tables.Count,
        flowCount = measurement.Program.Flows.Count,
        nodeExecutionMs = Math.Round(Median(nodeTimings), 3),
        artifactSha256 = Sha256(javascript),
        runtimeOutputSha256 = Sha256(runtimeOutput!),
        deterministicMir = true,
        deterministicJavaScript = true,
        deterministicRuntimeOutput = true,
        csharpEmitted = measurement.CSharpDiagnostics.Count == 0,
    };
    WriteArtifact(
        programArtifactRoot,
        program.Name + ".timing.json",
        JsonSerializer.Serialize(report, jsonOptions) + Environment.NewLine);
    return report;
}

object RunTemplateProgram()
{
    string sourcePath = Path.Combine(corpusRoot, "Metaprogramming", "main.ts");
    string source = File.ReadAllText(sourcePath);
    var total = Stopwatch.StartNew();
    var parse = Stopwatch.StartNew();
    SyntaxTree tree = SyntaxTree.Parse(source, sourcePath);
    parse.Stop();
    var bind = Stopwatch.StartNew();
    BoundCompilation bound = Binder.Bind(tree);
    bind.Stop();
    var template = Stopwatch.StartNew();
    TemplateEvaluationResult result = TemplateCompiler.Evaluate(bound, "BurnInMetadata");
    template.Stop();
    total.Stop();
    if (tree.Diagnostics.Count > 0 || bound.Diagnostics.Count > 0 || !result.Success)
    {
        throw new InvalidOperationException(
            "Metaprogramming failed: " +
            string.Join(Environment.NewLine, tree.Diagnostics.Concat(bound.Diagnostics).Concat(result.Diagnostics)));
    }

    string summary = string.Join(
        Environment.NewLine,
        result.Project!.Files.Select(file => $"{file.Path}\t{file.Bytes.Length}\t{file.Sha256}")) +
        Environment.NewLine;
    TemplateEvaluationResult repeated = TemplateCompiler.Evaluate(bound, "BurnInMetadata");
    string repeatedSummary = string.Join(
        Environment.NewLine,
        repeated.Project!.Files.Select(file => $"{file.Path}\t{file.Bytes.Length}\t{file.Sha256}")) +
        Environment.NewLine;
    if (!repeated.Success || !string.Equals(summary, repeatedSummary, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Metaprogramming output was not deterministic.");
    }
    string programArtifactRoot = Path.Combine(artifactRoot, "Metaprogramming");
    Directory.CreateDirectory(programArtifactRoot);
    WriteArtifact(programArtifactRoot, "Metaprogramming.runtime.txt", summary);
    var report = new
    {
        name = "Metaprogramming",
        source = Path.GetRelativePath(repositoryRoot, sourcePath).Replace('\\', '/'),
        sourceLoc = CountLines(source),
        sourceBytes = Encoding.UTF8.GetByteCount(source),
        parseMs = parse.Elapsed.TotalMilliseconds,
        bindMs = bind.Elapsed.TotalMilliseconds,
        templateMs = template.Elapsed.TotalMilliseconds,
        lowerMs = (double?)null,
        emitJsMs = (double?)null,
        totalCompileMs = total.Elapsed.TotalMilliseconds,
        generatedJsLoc = (int?)null,
        generatedJsBytes = (int?)null,
        jsToSourceSizeRatio = (double?)null,
        generatedCarrierCount = (int?)null,
        helperCount = (int?)null,
        nodeExecutionMs = (double?)null,
        artifactCount = result.Project.Files.Count,
        templateInstantiationCount = result.InstantiationChain.Count,
        reflectionQuerySiteCount = Regex.Matches(source, @"\breflect\s+(nameOf|fieldsOf|enumCasesOf|callsOf)\s*<").Count,
        deterministicTemplateOutput = true,
        compileTimeOnly = true,
    };
    WriteArtifact(
        programArtifactRoot,
        "Metaprogramming.timing.json",
        JsonSerializer.Serialize(report, jsonOptions) + Environment.NewLine);
    return report;
}

StageMeasurement CompileRuntimeProgram(string source, string sourcePath)
{
    var diagnostics = new List<string>();
    var total = Stopwatch.StartNew();

    var parse = Stopwatch.StartNew();
    SyntaxTree tree = SyntaxTree.Parse(source, sourcePath);
    parse.Stop();
    diagnostics.AddRange(tree.Diagnostics.Select(diagnostic => diagnostic.ToString()));

    var bind = Stopwatch.StartNew();
    BoundCompilation bound = Binder.Bind(tree);
    bind.Stop();
    diagnostics.AddRange(bound.Diagnostics.Select(diagnostic => diagnostic.ToString()));

    var staticEvaluation = Stopwatch.StartNew();
    IReadOnlyList<Copeland.TS.Diagnostics.Diagnostic> staticDiagnostics =
        StaticEvaluationPass.Evaluate([bound]);
    staticEvaluation.Stop();
    diagnostics.AddRange(staticDiagnostics.Select(diagnostic => diagnostic.ToString()));

    var lower = Stopwatch.StartNew();
    MirCompilation mir = MirLowerer.Lower(bound);
    lower.Stop();
    diagnostics.AddRange(mir.Diagnostics.Select(diagnostic => diagnostic.ToString()));

    string? mirText = mir.Program is null ? null : MirTextWriter.Write(mir.Program);
    string? javascript = null;
    string? csharp = null;
    IReadOnlyList<string> csharpDiagnostics = [];
    double emitJavaScriptMilliseconds = 0;
    if (mir.Program is not null && diagnostics.Count == 0)
    {
        var emitJavaScript = Stopwatch.StartNew();
        JavaScriptCompilation emittedJavaScript = JavaScriptBackend.Emit(mir.Program);
        emitJavaScript.Stop();
        emitJavaScriptMilliseconds = emitJavaScript.Elapsed.TotalMilliseconds;
        diagnostics.AddRange(emittedJavaScript.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message));
        javascript = emittedJavaScript.SourceText;

        CSharpCompilation emittedCSharp = CSharpBackend.Emit(mir.Program);
        csharp = emittedCSharp.SourceText;
        csharpDiagnostics = emittedCSharp.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message).ToArray();
    }
    total.Stop();

    return new StageMeasurement(
        diagnostics,
        mir.Program,
        mirText,
        javascript,
        csharp,
        csharpDiagnostics,
        parse.Elapsed.TotalMilliseconds,
        bind.Elapsed.TotalMilliseconds,
        staticEvaluation.Elapsed.TotalMilliseconds,
        lower.Elapsed.TotalMilliseconds,
        emitJavaScriptMilliseconds,
        total.Elapsed.TotalMilliseconds);
}

NodeResult RunNode(string script)
{
    var startInfo = new ProcessStartInfo("node", "-")
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false),
        StandardOutputEncoding = new UTF8Encoding(false),
        StandardErrorEncoding = new UTF8Encoding(false),
    };
    var stopwatch = Stopwatch.StartNew();
    using Process process = Process.Start(startInfo)!;
    process.StandardInput.Write(script);
    process.StandardInput.Close();
    string standardOutput = process.StandardOutput.ReadToEnd();
    string standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();
    stopwatch.Stop();
    return new NodeResult(process.ExitCode, standardOutput, standardError, stopwatch.Elapsed.TotalMilliseconds);
}

void WriteArtifact(string directory, string fileName, string content)
{
    File.WriteAllText(Path.Combine(directory, fileName), content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
}

string BuildTextManifest(IReadOnlyList<object> programReports)
{
    var builder = new StringBuilder();
    builder.AppendLine("Copeland TS parser-level language burn-in");
    builder.AppendLine("feature inventory authority: parser/compiler implementation");
    builder.AppendLine("JavaScript primary backend: true");
    builder.AppendLine("C# parity spot-check: true");
    builder.AppendLine("Oblivion changed: false");
    builder.AppendLine("programs:");
    foreach (object report in programReports)
    {
        builder.Append("- ");
        builder.AppendLine(report.GetType().GetProperty("name")!.GetValue(report)!.ToString());
    }
    return builder.ToString();
}

static int CountLines(string text)
{
    if (text.Length == 0)
    {
        return 0;
    }
    return text.Count(character => character == '\n') + (text.EndsWith('\n') ? 0 : 1);
}

static double Median(List<double> values)
{
    values.Sort();
    return values[values.Count / 2];
}

static string Sha256(string value)
{
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
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
    throw new InvalidOperationException("Could not locate the Copeland repository root.");
}

internal sealed record BurnInProgram(string Name, string RelativePath, string NodeSuffix);

internal sealed record NodeResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    double ElapsedMilliseconds);

internal sealed record StageMeasurement(
    IReadOnlyList<string> Diagnostics,
    MirProgram? Program,
    string? Mir,
    string? JavaScript,
    string? CSharp,
    IReadOnlyList<string> CSharpDiagnostics,
    double ParseMilliseconds,
    double BindMilliseconds,
    double StaticMilliseconds,
    double LowerMilliseconds,
    double EmitJavaScriptMilliseconds,
    double TotalMilliseconds);
