from pathlib import Path
import hashlib
p=Path('tests/Copeland/Copeland.TS.Tests/InstinctArchitectureTests.cs');s=p.read_text().replace('[], [], [], 0, null,','[], [], [], 0, 2,');p.write_text(s)
p=Path('tests/Copeland/Copeland.TS.Backend.CSharp.Tests/CallableCorpusTests.cs');s=p.read_text();b=Path('tests/Copeland/Copeland.TS.Tests/TestData/Corpus/cts-call-m1/main.g.cs').read_bytes();s=s.replace('5062, "0C1FB55CFCC47E9E05BE677C53E38D9FA3C61AF3A32C3411E5C44E4C7326BA2A"',str(len(b))+', "'+hashlib.sha256(b).hexdigest().upper()+'"');p.write_text(s)
