from pathlib import Path
import hashlib,json
p=Path('artifacts/copeland-typescript-instinct-m26/source-provenance.json');j=json.loads(p.read_text(encoding='utf-8'))
a=Path('.tmp/m26-benchmark/cb/js/twin.mjs').read_text(encoding='utf-8');b=Path('samples/copeland-ts/typescript-instinct-m26-benchmark/js/twin.mjs').read_text(encoding='utf-8')
prefix=a[:a.index('function run(')];assert prefix==b[:b.index('function run(')]
j['javaScriptOriginalAlgorithmPrefixByteIdentical']=True;j['javaScriptOriginalAlgorithmPrefixSha256']=hashlib.sha256(prefix.encode()).hexdigest().upper()
j['javaScriptChange']='Only the run telemetry host is replaced; native string and map twins are appended.'
p.write_text(json.dumps(j,indent=2)+'\n',encoding='utf-8',newline='\n')
