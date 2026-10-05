from pathlib import Path
import hashlib,re,json,subprocess
p=Path('tests/Copeland/Copeland.Cli.Tests/CliIntegrationTests.cs');s=p.read_text();s=s.replace('Length: 626, Hash: "D0C536F3951C1A1955F985FECF5DA2098D0DB4FC8760234CB53D94A34F79EB93"','Length: 602, Hash: "7D8233C236DF3B5F38DD1F5793346B51D5EE7A33D58E0054393D8E0408C6C595"');p.write_text(s,newline='\n')
base=json.loads(Path('artifacts/copeland-typescript-instinct-m26/dogfood-baseline-replay.json').read_text())
wrap={'increment-loop':('function run(): int { return sumRange(10); }','45'),'batch-guard':('function run(): int[] { return transformBatch([-2, 0, 3]); }','[2,0,6]'),'integer-hash':('function run(): int { return hashIntegers([1, 2, 3]); }','1456420779')}
for case in base['Cases']:
 if case['Id'] not in wrap: continue
 code,value=wrap[case['Id']]; path=Path('.tmp/m26-baseline-'+case['Id']+'.ts');path.write_text(case['FinalSource']+'\n'+code)
 result=subprocess.run(['dotnet','.tmp/m26-baseline/tools/InstinctBaseline/bin/Debug/net10.0/InstinctBaseline.dll','emit',str(path),str(path.with_suffix('.g.cs'))],capture_output=True,text=True)
 print(case['Id'],result.returncode,result.stdout,result.stderr)
