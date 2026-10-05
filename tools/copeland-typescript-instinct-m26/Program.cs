using System.Text.Json;
using System.Security.Cryptography;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using Copeland.TS.Tson;

if (args.Length == 4 && args[0] == "execute-emitted")
{
    var assembly = CorpusRunner.CompileCSharp(File.ReadAllText(args[1]), checkedArithmetic: false);
    var module = assembly.GetType("Copeland.Generated.CopelandModule", true)!;
    string actual = JsonSerializer.Serialize(module.GetMethod("run")!.Invoke(null, null));
    CorpusRunner.Require(actual == args[2], "Baseline execution returned " + actual + ", expected " + args[2]);
    await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(new { result = actual, expected = args[2], passed = true }));
    return;
}
if (args.Length == 3 && args[0] == "emit-csharp-assets")
{
    string sourcePath = Path.GetFullPath(args[1]);
    var compilation = CopelandCompiler.CompileToMir(File.ReadAllText(sourcePath),
        new CopelandCompilationOptions { SourcePath = sourcePath, ProjectRoot = Path.GetDirectoryName(sourcePath), AssetSource = FileAssetSource.Instance });
    CorpusRunner.Require(compilation.Success, string.Join("\n", compilation.Diagnostics));
    var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
    CorpusRunner.Require(emitted.Diagnostics.Count == 0, string.Join("\n", emitted.Diagnostics));
    await File.WriteAllTextAsync(args[2], emitted.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal));
    return;
}
if (args.Length == 3 && args[0] == "emit-csharp")
{
    var mir = MirLowerer.Lower(SyntaxTree.Parse(File.ReadAllText(args[1])));
    CorpusRunner.Require(mir.Program is not null && mir.Diagnostics.Count == 0, string.Join("\n", mir.Diagnostics));
    var emitted = CSharpBackend.Emit(mir.Program!);
    CorpusRunner.Require(emitted.Diagnostics.Count == 0, string.Join("\n", emitted.Diagnostics));
    await File.WriteAllTextAsync(args[2], emitted.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal));
    return;
}

if (args.Length >= 2 && args[0] == "runtime-proof")
{
    await RuntimeProof.Write(args[1], args.Length > 2 ? args[2] : null);
    return;
}

if (args.Length == 3 && args[0] == "snapshot")
{
    var snapshots = CorpusRunner.ReadCases(args[1]).Select(fixture =>
    {
        var compilation = CopelandCompiler.CompileToMir(fixture.Input,
            new CopelandCompilationOptions { SourcePath = fixture.Id + ".ts" });
        return new { fixture.Id, compilation.Success, compilation.Diagnostics };
    });
    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(snapshots,
        new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Length == 2 && args[0] == "mir-hash")
{
    var compilation = CopelandCompiler.CompileToMir(File.ReadAllText(args[1]));
    CorpusRunner.Require(compilation.Success, string.Join("\n", compilation.Diagnostics));
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(compilation.MirText!);
    Console.WriteLine(JsonSerializer.Serialize(new { length = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }));
    return;
}

if (args.Length == 2 && args[0] == "diagnose")
{
    var compilation = CopelandCompiler.CompileToMir(File.ReadAllText(args[1]),
        new CopelandCompilationOptions { SourcePath = args[1] });
    Console.WriteLine(JsonSerializer.Serialize(compilation.Diagnostics));
    return;
}

string corpusPath = args.Length > 0 ? args[0] : "tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json";
var results = new List<InstinctResult>();
var failures = new List<string>();
foreach (var fixture in CorpusRunner.ReadCases(corpusPath))
{
    try
    {
        results.Add(await CorpusRunner.Verify(fixture));
        Console.WriteLine("PASS " + fixture.Id);
    }
    catch (Exception exception)
    {
        failures.Add(fixture.Id + ": " + exception);
        Console.Error.WriteLine(failures[^1]);
    }
}

if (args.Length > 1)
{
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new { results, failures },
        new JsonSerializerOptions { WriteIndented = true }));
}
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

internal sealed class FileAssetSource : ICopelandAssetSource
{
    public static FileAssetSource Instance { get; } = new();

    public bool TryRead(string absolutePath, out string? text)
    {
        if (File.Exists(absolutePath))
        {
            text = File.ReadAllText(absolutePath);
            return true;
        }
        text = null;
        return false;
    }
}
