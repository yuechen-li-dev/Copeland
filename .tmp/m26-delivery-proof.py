from pathlib import Path
import hashlib,json
root=Path.cwd();a=root/'artifacts/copeland-typescript-instinct-m26'
(a/'cleanup.json').write_text(json.dumps(dict(status='blocked-by-automatic-approval-review',action='Delete only M26-created repository-local scratch files and directories',reason='blocked by policy',retainedUnder='.tmp',userFilesRemoved=False),indent=2)+'\n',encoding='utf-8',newline='\n')
p=a/'compiler-payloads.json';j=json.loads(p.read_text(encoding='utf-8'));j['executedPayloadSha256']={}
for name in ['samples/copeland-ts/typescript-instinct-m26-benchmark/bin/Release/net10.0/CopeBench.dll','tools/copeland-typescript-instinct-m26/bin/Release/net10.0/InstinctProof.dll','src/Copeland/Copeland.TS.Backend.JavaScript/bin/Release/net10.0/Copeland.TS.Backend.JavaScript.dll']:
 j['executedPayloadSha256'][name]=hashlib.sha256((root/name).read_bytes()).hexdigest().upper()
p.write_text(json.dumps(j,indent=2)+'\n',encoding='utf-8',newline='\n')
