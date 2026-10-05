from pathlib import Path
import json
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');c=json.loads(p.read_text())
c += [dict(Id='prefix-increment',Category='mutation',Input='function run(): int { let value: int = 3; const current: int = ++value; return current * 10 + value; }',Outcome='compile',ExpectedJson='44'),dict(Id='clr-dictionary-indexer',Category='interop',Input='using System.Collections.Generic; function run(): int { const values: Dictionary<string, int> = new Dictionary<string, int>(); values["a"] = 7; values["a"] = values["a"] + 2; return values["a"]; }',Outcome='compile',ExpectedJson='9',ClrOnly=True),dict(Id='Option-fallback',Category='Option',Input='function run(): int { const value: Option<int> = None; return value ?? 7; }',Outcome='compile',ExpectedJson='7'),dict(Id='enum-match',Category='enums',Input='enum Flag { On, Off } function run(): int { const value: Flag = Flag.On(); return match value { On => 1, Off => 0 }; }',Outcome='compile',ExpectedJson='1')]
p.write_text(json.dumps(c,indent=2)+'\n')
root=Path('samples/copeland-ts/typescript-instinct-m26-benchmark')
(root/'Copeland/Maps.ts').write_text('''using System.Collections.Generic;

export function runMaps(rounds: int, count: int): int {
    let checksum: int = 0;
    for (let round: int = 0; round < rounds; round++) {
        const values: Dictionary<string, int> = new Dictionary<string, int>();
        for (let i: int = 0; i < count; i++) {
            values[`key${i}`] = i;
        }
        for (let i: int = 0; i < count; i++) {
            const key: string = `key${i}`;
            values[key] = values[key] + 1;
            checksum += values[key];
        }
        for (let i: int = 0; i < count; i++) {
            if (i % 2 == 0) {
                values.Remove(`key${i}`);
            }
        }
        checksum += values.Count;
    }
    return checksum;
}
''')
p=root/'Copeland/Bench.ts';s=p.read_text();s+='\nimport { runMaps } from "./Maps";\nexport function mapsBench(rounds: int, count: int): int { return runMaps(rounds, count); }\n';p.write_text(s)
p=root/'Program.cs';s=p.read_text();s+='\nRun("maps", () => M.mapsBench(quick ? 1 : 10, quick ? 1000 : 100_000).ToString());\n';p.write_text(s)
p=root/'js/twin.mjs';s=p.read_text();start=s.index('function run(name, body) {');end=s.index("run('fib'",start)
s=s[:start]+'''function run(name, body) {
  if (only !== '' && only !== name) return;
  const trials = [];
  for (let attempt = 0; attempt < 3; attempt++) {
    const heapBefore = process.memoryUsage().heapUsed;
    const start = performance.now();
    const result = body();
    trials.push({
      result: String(result),
      milliseconds: performance.now() - start,
      heapUsedDeltaBytes: process.memoryUsage().heapUsed - heapBefore
    });
  }
  console.log(JSON.stringify({ name, quick, trials, peakWorkingSetBytes: process.resourceUsage().maxRSS * 1024 }));
}
'''+s[end:]
s+='''
function runNativeStrings(rounds, count) {
  let total = 0;
  for (let round = 0; round < rounds; round++) {
    const parts = new Array(count).fill('');
    for (let index = 0; index < count; index++) {
      parts[index] = `item${index}`;
    }
    const joined = parts.slice().join(',');
    const back = joined.split(',');
    let hash = 0;
    for (let index = 0; index < joined.length; index++) {
      hash = (Math.imul(hash, 31) + joined.charCodeAt(index)) | 0;
    }
    total = (total + back.length + hash % 256 + joined.indexOf('item99999')) | 0;
  }
  return total;
}

function runMaps(rounds, count) {
  let checksum = 0;
  for (let round = 0; round < rounds; round++) {
    const values = new Map();
    for (let index = 0; index < count; index++) {
      values.set(`key${index}`, index);
    }
    for (let index = 0; index < count; index++) {
      const key = `key${index}`;
      values.set(key, values.get(key) + 1);
      checksum = (checksum + values.get(key)) | 0;
    }
    for (let index = 0; index < count; index++) {
      if (index % 2 === 0) values.delete(`key${index}`);
    }
    checksum = (checksum + values.size) | 0;
  }
  return checksum;
}
run('native-strings', () => runNativeStrings(quick ? 1 : 20, quick ? 1000 : 100_000));
run('maps', () => runMaps(quick ? 1 : 10, quick ? 1000 : 100_000));
''';p.write_text(s)
