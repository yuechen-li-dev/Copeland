from pathlib import Path
import re,json,hashlib
p=Path('tests/Copeland/Copeland.Cli.Tests/CliIntegrationTests.cs')
s=p.read_text(); chunk=s[s.index('public async Task Inferred_generic_cli_emission'):]; source=re.search('temp.WriteFile\("main.ts", """\n(.*?)\n            """\)',chunk,re.S).group(1)
source='\n'.join(line[12:] for line in source.splitlines())+'\n'
Path('.tmp/m26-cli-pin.ts').write_text(source)
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text();old='Report("COPE-REC-0011", $"Cannot assign to immutable record field \'{recordType.Name}.{field.Name}\'.", member.NameToken);'
new='string replacement = $"{AuthoredText(member.Target)} with {{ {field.Name}: replacementValue }}";\n                        ReportRepair("COPE-REC-0011", $"Cannot assign to immutable record field \'{recordType.Name}.{field.Name}\'. Use {replacement} to construct an updated value.", member.NameToken, replacement);'
assert old in s;s=s.replace(old,new)
s=s.replace('Build a fixed MutableArray<T>(length), assign elements, then freeze(); growable native lists are not yet supported.','Build a fixed MutableArray<T>(length), assign elements, then freeze(). For CLR-only growth use using System.Collections.Generic; new List<T>(); Add(value); ToArray(). Native growable lists are not yet supported.')
p.write_text(s, newline='\n')
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');cases=json.loads(p.read_text());cases.append(dict(Id='record-compound-repair',Category='records',Input='record Counter { value: int; } function run(): int { let counter: Counter = { value: 1 }; counter.value += 2; return counter.value; }',Outcome='repair',PrimaryCode='COPE-REC-0011',Anchor='value',AnchorOccurrence=3,Repair='counter with { value: replacementValue }'));p.write_text(json.dumps(cases,indent=2)+'\n')
