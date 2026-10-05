import json
from pathlib import Path
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');j=json.loads(p.read_text())
probes={name:Path('.tmp/m26-probe-'+name+'.ts').read_text() for name in ['malformed-generic','nested-template','batch-loop-repair','bitwise-float','clr-constructor-call']}
for name,category,code,anchor,repair,occurrence in [
 ('malformed-generic','generics','COPE-CLR-GENERIC-0002','List','Use int, float, string, boolean, or an imported CLR type.',1),
 ('nested-template','diagnostics','COPE-BIND-0001','Missing','Undefined name',1),
 ('batch-loop-repair','batch','COPE-BATCH-0010','{','return if (condition) { value } else { otherValue }',2),
 ('bitwise-float','numeric','COPE-TYPE-0007','&','use an explicit Int conversion policy',1)]:
 j.append(dict(Id=name,Category=category,Input=probes[name],Outcome='repair',PrimaryCode=code,Anchor=anchor,Repair=repair,AnchorOccurrence=occurrence))
j.append(dict(Id='clr-constructor-call',Category='interop',Input=probes['clr-constructor-call'],Outcome='compile',ExpectedJson='0',ClrOnly=True))
p.write_text(json.dumps(j,indent=2)+'\n')
