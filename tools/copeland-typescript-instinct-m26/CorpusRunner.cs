using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation;

namespace Copeland.TS.InstinctCorpus;

public sealed record InstinctCase(
    string Id,
    string Category,
    string Input,
    string Outcome,
    string? PrimaryCode = null,
    string? Anchor = null,
    string? Repair = null,
    string? ExpectedJson = null,
    bool ClrOnly = false,
    int AnchorOccurrence = 1,
    string? ExpectedFailure = null);

public sealed record InstinctResult(
    string Id,
    string Outcome,
    object[] Diagnostics,
    string? CSharpResult = null,
    string? JavaScriptResult = null);

public static class CorpusRunner
{
    public static InstinctCase[] ReadCases(string path)
    {
        return JsonSerializer.Deserialize<InstinctCase[]>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public static async Task<InstinctResult> Verify(InstinctCase fixture)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(fixture.Input,
            new CopelandCompilationOptions { SourcePath = fixture.Id + ".ts" });
        if (fixture.Outcome != "compile")
        {
            Require(compilation.Diagnostics.Count == 1,
                fixture.Id + ": expected one primary diagnostic: " + string.Join("\n", compilation.Diagnostics));
            var diagnostic = compilation.Diagnostics[0];
            Require(diagnostic.Id == fixture.PrimaryCode, fixture.Id + ": unexpected code " + diagnostic);
            int anchorPosition = -1;
            for (int occurrence = 0; occurrence < fixture.AnchorOccurrence; occurrence++)
            {
                anchorPosition = fixture.Input.IndexOf(fixture.Anchor!, anchorPosition + 1, StringComparison.Ordinal);
            }
            Require(diagnostic.Position == anchorPosition,
                fixture.Id + ": diagnostic does not point at authored construct: " + diagnostic);
            Require(diagnostic.Length == fixture.Anchor!.Length, fixture.Id + ": unexpected source length: " + diagnostic);
            Require(diagnostic.Message.Contains(fixture.Repair!, StringComparison.Ordinal), fixture.Id + ": missing repair: " + diagnostic);
            Require(diagnostic.SourcePath == fixture.Id + ".ts", fixture.Id + ": missing authored source path");
            return new InstinctResult(fixture.Id, fixture.Outcome, compilation.Diagnostics.Cast<object>().ToArray());
        }
        Require(compilation.Success, fixture.Id + ": " + string.Join("\n", compilation.Diagnostics));
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Require(emitted.Diagnostics.Count == 0, fixture.Id + ": " + string.Join("\n", emitted.Diagnostics));
        Type module = CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        string csharp;
        try
        {
            object? result = module.GetMethod("run")!.Invoke(null, null);
            Require(fixture.ExpectedFailure is null, fixture.Id + ": expected a runtime failure.");
            csharp = JsonSerializer.Serialize(result);
            Require(csharp == fixture.ExpectedJson, fixture.Id + ": C# result " + csharp + ", expected " + fixture.ExpectedJson);
        }
        catch (TargetInvocationException exception) when (fixture.ExpectedFailure is not null)
        {
            csharp = exception.GetBaseException().Message;
            Require(csharp.Contains(fixture.ExpectedFailure, StringComparison.Ordinal), fixture.Id + ": wrong C# failure: " + csharp);
        }
        string? javascript = null;
        if (!fixture.ClrOnly)
        {
            foreach (var profile in new[] { JavaScriptEmissionProfile.Production, JavaScriptEmissionProfile.Symbolic })
            {
                var js = JavaScriptBackend.Emit(compilation.MirCompilation.Program!, new JavaScriptEmissionOptions { Profile = profile });
                Require(js.Diagnostics.Count == 0, fixture.Id + ": " + string.Join("\n", js.Diagnostics));
                string invocation = fixture.ExpectedFailure is null
                    ? "\nconsole.log(JSON.stringify(run()));\n"
                    : "\ntry { run(); throw new Error('Expected a runtime failure'); } catch (error) { console.log(error.message); }\n";
                javascript = (await RunNode(js.SourceText! + invocation)).Trim();
                if (fixture.ExpectedFailure is null)
                {
                    Require(javascript == fixture.ExpectedJson, fixture.Id + ": " + profile + " result " + javascript + ", expected " + fixture.ExpectedJson);
                }
                else
                {
                    Require(javascript.Contains(fixture.ExpectedFailure, StringComparison.Ordinal), fixture.Id + ": wrong JS failure: " + javascript);
                }
            }
        }
        return new InstinctResult(fixture.Id, "compile", [], csharp, javascript);
    }

    public static Assembly CompileCSharp(string source, bool checkedArithmetic = true)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = RoslynCompilation.Create("InstinctProof_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: checkedArithmetic));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Require(result.Success, "Generated C# failed: " + string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    public static async Task<string> RunNode(string source)
    {
        string path = Path.Combine(Path.GetTempPath(), "copeland-instinct-" + Guid.NewGuid().ToString("N") + ".mjs");
        await File.WriteAllTextAsync(path, source);
        try
        {
            var start = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(path);
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Node instinct proof exceeded 30 seconds.");
            }
            Require(process.ExitCode == 0, await error);
            return await output;
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
