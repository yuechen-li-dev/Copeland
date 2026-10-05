from pathlib import Path
import json
j=json.loads(Path('artifacts/copeland-typescript-instinct-m26/dogfood-replay.json').read_text());cases=[]
wrap={'csv':('function run(): int[] { return parseCsvIntegers("1, -2, 3"); }','[1,-2,3]',True),'counter-map':('function run(): Dictionary<string, int> { return countValues(["a", "b", "a"]); }','{"a":2,"b":1}',True),'increment-loop':('function run(): int { return sumRange(10); }','45',False),'batch-guard':('function run(): int[] { return transformBatch([-2, 0, 3]); }','[2,0,6]',False),'growing-array':('function run(): int[] { return collectEvenValues([-2, -1, 0, 1, 2]); }','[-2,0,2]',True),'integer-hash':('function run(): int { return hashIntegers([1, 2, 3]); }','1456420779',False)}
for r in j['results']:
 w,v,clr=wrap[r['id']];cases.append(dict(Id='dogfood-'+r['id'],Category='dogfood execution',Input=r['finalSource']+'\n'+w,Outcome='compile',ExpectedJson=v,ClrOnly=clr))
p=Path('artifacts/copeland-typescript-instinct-m26/dogfood-execution-inputs.json');p.write_text(json.dumps(cases,indent=2)+'\n')
