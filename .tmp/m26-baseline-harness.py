from pathlib import Path
import json
initial=[('csv', 'function parseCsvIntegers(text: string): int[] { return text.split(",").map(part => parseInt(part.trim(), 10)); }'),('counter-map','function countValues(values: string[]): Map<string, int> { const counts = new Map<string, int>(); for (const value of values) { const previous = counts.get(value) ?? 0; counts.set(value, previous + 1); } return counts; }'),('increment-loop','function sumRange(limit: int): int { let total: int = 0; for (let index: int = 0; index < limit; index++) { total += index; } return total; }'),('batch-guard','function transformBatch(values: int[]): int[] { return batch values as item { if (item < 0) { return -item; } return item * 2; }; }'),('growing-array','function collectEvenValues(values: int[]): int[] { const result: int[] = []; for (const value of values) { if (value % 2 === 0) { result.push(value); } } return result; }'),('integer-hash','function hashIntegers(values: int[]): int { let hash: int = 0x811c9dc5 | 0; for (const value of values) { hash = Math.imul(hash ^ value, 0x01000193) | 0; } return hash; }')]
# Preserve fresh-context authored inputs before any compiler feedback.
corpus=[dict(Id=id,Category='fresh-context dogfood',Input=source,Outcome='snapshot') for id,source in initial]
p=Path('artifacts/copeland-typescript-instinct-m26/llm-dogfood-inputs.json');p.write_text(json.dumps(corpus,indent=2)+'\n')
h=Path('.tmp/m26-baseline/tools/InstinctBaseline');h.mkdir(parents=True,exist_ok=True)
(h/'InstinctBaseline.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Copeland/Copeland.TS/Copeland.TS.csproj" />
    <ProjectReference Include="../../src/Copeland/Copeland.TS.Backend.CSharp/Copeland.TS.Backend.CSharp.csproj" />
  </ItemGroup>
</Project>''')
(h/'Program.cs').write_text('''using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Backend.CSharp;

if (args[0] == "emit")
{
    var compilation = CopelandCompiler.CompileToMir(File.ReadAllText(args[1]));
    if (!compilation.Success) throw new Exception(string.Join("\\n", compilation.Diagnostics));
    var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
    if (emitted.Diagnostics.Count > 0) throw new Exception(string.Join("\\n", emitted.Diagnostics));
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
''')
