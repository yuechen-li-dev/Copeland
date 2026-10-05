using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Backend.CSharp;

if (args[0] == "diagnose")
{
    var compilation = CopelandCompiler.CompileToMir(File.ReadAllText(args[1]),
        new CopelandCompilationOptions { SourcePath = args[1] });
    Console.WriteLine(JsonSerializer.Serialize(compilation.Diagnostics));
    return;
}
if (args[0] == "emit")
{
    var compilation = CopelandCompiler.CompileToMir(File.ReadAllText(args[1]));
    if (!compilation.Success) throw new Exception(string.Join("\n", compilation.Diagnostics));
    var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
    if (emitted.Diagnostics.Count > 0) throw new Exception(string.Join("\n", emitted.Diagnostics));
    File.WriteAllText(args[2], emitted.SourceText);
    return;
}
var fixtures = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(args[1]))!;
var results = fixtures.Select(fixture =>
{
    string id = fixture.GetProperty("Id").GetString()!;
    var compilation = CopelandCompiler.CompileToMir(fixture.GetProperty("Input").GetString()!,
        new CopelandCompilationOptions { SourcePath = id + ".ts" });
    return new { Id = id, compilation.Success, compilation.Diagnostics };
});
File.WriteAllText(args[2], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
