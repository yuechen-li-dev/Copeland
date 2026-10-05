from pathlib import Path
import shutil
root=Path('samples/copeland-ts/typescript-instinct-m26-benchmark');(root/'Copeland').mkdir(parents=True,exist_ok=True);(root/'js').mkdir(exist_ok=True)
for p in Path('.tmp/m26-benchmark/cb/Copeland').glob('*.ts'):shutil.copyfile(p,root/'Copeland'/p.name)
shutil.copyfile('.tmp/m26-benchmark/cb/js/twin.mjs',root/'js/twin.mjs')
(root/'CopeBench.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>CopeBench</RootNamespace>
    <ServerGarbageCollection>false</ServerGarbageCollection>
    <TieredPGO>true</TieredPGO>
    <CopelandTaskAssembly>$(MSBuildThisFileDirectory)../../../src/Copeland/Copeland.TS.MSBuild/bin/Release/net10.0/Copeland.TS.MSBuild.dll</CopelandTaskAssembly>
  </PropertyGroup>
  <ItemGroup>
    <CopelandCompile Include="Copeland/**/*.ts" />
    <ProjectReference Include="../../../src/Copeland/Copeland.TS.Backend.CSharp/Copeland.TS.Backend.CSharp.csproj" />
  </ItemGroup>
  <Import Project="../../../src/Copeland/Copeland.TS.MSBuild/build/Copeland.TS.Sdk.targets" />
</Project>
''')
s=Path('.tmp/m26-benchmark/cb/Program.cs').read_text();s=s.replace('using System.Diagnostics;','using System.Diagnostics;\nusing System.Text.Json;')
start=s.index('    long best =');end=s.index('\n}\n',start)
s=s[:start]+'''    var trials = new List<object>();
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
    Console.WriteLine(JsonSerializer.Serialize(new { name, quick, trials, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 }));'''+s[end:]
(root/'Program.cs').write_text(s)
# Native-string matched twin added separately, original source preserved above.
(root/'Copeland/NativeStrings.ts').write_text('''export function nativeStringsBench(rounds: int, count: int): int {
    let total: int = 0;
    for (let round: int = 0; round < rounds; round++) {
        const parts: MutableArray<string> = MutableArray<string>(count, "");
        for (let i: int = 0; i < count; i++) {
            parts[i] = `item${i}`;
        }
        const joined: string = String.Join(parts.freeze(), ",");
        const back: string[] = String.Split(joined, ",");
        let hash: int = 0;
        for (let i: int = 0; i < joined.length; i++) {
            hash = hash * 31 + String.CodeAt(joined, i);
        }
        total += back.length + hash % 256 + String.IndexOf(joined, "item99999");
    }
    return total;
}
''')
p=root/'Program.cs';s=p.read_text();s+='\nRun("native-strings", () => M.nativeStringsBench(quick ? 1 : 20, quick ? 1000 : 100_000).ToString());\n';p.write_text(s)
# Need export through entry graph.
p=root/'Copeland/Bench.ts';s=p.read_text();s+='\nimport { nativeStringsBench as nativeStrings } from "./NativeStrings";\nexport function nativeStringsBench(rounds: int, count: int): int { return nativeStrings(rounds, count); }\n';p.write_text(s)
