from pathlib import Path
import hashlib,subprocess
files=['TsonAssets/Corpus/arrays','TsonEncoding/Corpus/record','TsonEncoding/Corpus/arrays','TsonEncoding/Corpus/tables-m2']
tests=[Path('tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/TsonAssetRuntimeTests.cs'),Path('tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/TsonEncodeRuntimeTests.cs')]
for rel in files:
 p=Path('tests/Copeland/Copeland.TS.Tests')/rel/'main.g.cs';old=subprocess.check_output(['git','show','HEAD:'+p.as_posix()]);new=p.read_bytes();h1=hashlib.sha256(old).hexdigest();h2=hashlib.sha256(new).hexdigest();print(rel,len(old),len(new),h2)
 for t in tests:
  s=t.read_text();s=s.replace(h1,h2).replace(h1.upper(),h2.upper());s=s.replace('('+str(len(old))+', "'+h2.upper()+'")','('+str(len(new))+', "'+h2.upper()+'")');t.write_text(s)
