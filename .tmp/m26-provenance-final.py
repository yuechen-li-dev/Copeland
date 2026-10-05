from pathlib import Path
import hashlib,json
root=Path.cwd();a=root/'artifacts/copeland-typescript-instinct-m26'
original=root/'.tmp/m26-benchmark/cb/Copeland';copy=root/'samples/copeland-ts/typescript-instinct-m26-benchmark/Copeland'
source=[]
for p in sorted(original.glob('*.ts')):
 c=copy/p.name
 if p.name!='Bench.ts':assert p.read_bytes()==c.read_bytes(),p.name
 source.append(dict(name=p.name,originalSha256=hashlib.sha256(p.read_bytes()).hexdigest().upper(),currentSha256=hashlib.sha256(c.read_bytes()).hexdigest().upper(),unchanged=p.read_bytes()==c.read_bytes()))
(a/'source-provenance.json').write_text(json.dumps(dict(originalKernels=source,entryGraphChange='Bench.ts adds native strings and maps imports/exports; the original eight kernels are byte-identical to the archive.'),indent=2)+'\n',encoding='utf-8',newline='\n')
payloads={}
for stem in ['Copeland.TS','Copeland.TS.Mir','Copeland.TS.Backend.CSharp','Copeland.TS.MSBuild']:
 p=root/f'src/Copeland/{stem}/bin/Release/net10.0/{stem}.dll';payloads[str(p.relative_to(root)).replace('\\','/')]=hashlib.sha256(p.read_bytes()).hexdigest().upper()
(a/'compiler-payloads.json').write_text(json.dumps(dict(configuration='Release',benchmarkPayloadSha256=payloads),indent=2)+'\n',encoding='utf-8',newline='\n')
for p in a.rglob('*'):
 if p.is_file() and p.suffix in ['.json','.log','.txt','.cs']:
  data=p.read_bytes();p.write_bytes(data.replace(b'\r\n',b'\n'))
