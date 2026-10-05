from pathlib import Path
import json
p=Path('src/Copeland/Copeland.TS.Backend.CSharp/CSharp/NativeStringRuntime.cs');s=p.read_text().replace('if ((uint)index >= (uint)value.Length)','if (unchecked((uint)index) >= (uint)value.Length)');p.write_text(s)
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');c=json.loads(p.read_text())
def add(id,cat,source,result=None,failure=None,clr=False):
 c.append(dict(Id=id,Category=cat,Input=source,Outcome='compile',ExpectedJson=result,ExpectedFailure=failure,ClrOnly=clr))
add('codeat-negative-trap','strings','function run(): int { return String.CodeAt("a", -1); }',failure='String index is out of bounds.')
add('codeat-upper-trap','strings','function run(): int { return String.CodeAt("a", 1); }',failure='String index is out of bounds.')
add('divide-zero-trap','numeric','function run(): int { return 7 / 0; }',failure='Integer division by zero.')
add('remainder-zero-trap','numeric','function run(): int { return 7 % 0; }',failure='Integer division by zero.')
add('array-filled-negative-trap','arrays','function run(): int[] { return MutableArray<int>(-1, 7).freeze(); }',failure='Copeland mutable array length cannot be negative.')
add('string-empty-delimiter-surrogates','strings','function run(): int { const parts: string[] = String.Split("😀", ""); return String.CodeAt(parts[0], 0) + String.CodeAt(parts[1], 0); }','112189')
add('int-negative-shift','numeric','function run(): int { return -8 >> 1; }','-4')
add('record-initializer-immutable','arrays','record Pair { value: int; } function run(): int { const values: MutableArray<Pair> = MutableArray<Pair>(2, { value: 7 }); return values[0].value + values[1].value; }','14')
add('clr-generic-signature','interop','using System.Collections.Generic; function make(): Dictionary<string, int> { const values = new Dictionary<string, int>(); values["a"] = 7; return values; } function run(): int { return make()["a"]; }','7',clr=True)
c.append(dict(Id='template-hole-location',Category='diagnostics',Input='function run(): string { return `value=${Missing}`; }',Outcome='repair',PrimaryCode='COPE-BIND-0001',Anchor='Missing',Repair='Undefined name'))
p.write_text(json.dumps(c,indent=2)+'\n')
