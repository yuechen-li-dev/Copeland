from pathlib import Path
import subprocess,json
sources={
'malformed-generic':'using System.Collections.Generic; function run(): int { const values = new List<int[]>(); return 0; }',
'nested-template':'function run(): string { return `outer=${`inner=${Missing}`}`; }',
'batch-loop-repair':'function run(): int[] { return batch [1] as value { for (let index: int = 0; index < 2; index++) { } return value; }; }',
'bitwise-float':'function run(): int { return 1.5 & 1; }',
'clr-constructor-call':'using System.Collections.Generic; function run(): int { const values = List<int>(); return values.Count; }'
}
for name,source in sources.items():
 path=Path('.tmp/m26-probe-'+name+'.ts');path.write_text(source)
 result=subprocess.run(['dotnet','tools/copeland-typescript-instinct-m26/bin/Release/net10.0/InstinctProof.dll','diagnose',str(path)],capture_output=True,text=True)
 print(name,result.stdout,result.stderr)
