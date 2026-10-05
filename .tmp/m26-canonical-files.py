from pathlib import Path
import subprocess
names=subprocess.check_output(['git','diff','--name-only'],text=True).splitlines()
for name in names:
 p=Path(name)
 if p.is_file():
  raw=p.read_bytes()
  if b'\r\n' in raw:p.write_bytes(raw.replace(b'\r\n',b'\n'))
p=Path('src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs');s=p.read_text(encoding='utf-8');p.write_text('\n'.join(line.rstrip() for line in s.split('\n')),encoding='utf-8',newline='\n')
