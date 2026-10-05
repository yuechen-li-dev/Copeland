from pathlib import Path
p=Path('src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs');s=p.read_text(encoding='utf-8')
for condition,message in [
 ('!IsValidModuleClassName(options.ModuleClassName)','Invalid generated module class name.'),
 ("string.IsNullOrEmpty(options.Namespace) || !options.Namespace.Split('.').All(IsValidModuleClassName)",'Invalid generated namespace.'),
 ('options.RecordCarrierScope is not null && !IsValidModuleClassName(options.RecordCarrierScope)','Invalid record carrier scope.')]:
 old=f'        if ({condition}) throw new ArgumentException("{message}", nameof(options));'
 new=f'        if ({condition})\n        {{\n            throw new ArgumentException("{message}", nameof(options));\n        }}'
 assert old in s;s=s.replace(old,new)
p.write_text(s,encoding='utf-8',newline='\n')
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text(encoding='utf-8')
for condition in ['statements.Count == 0','statements[branchIndex] is not IfStatementSyntax branch','thenReturn?.Expression is null || elseReturn?.Expression is null']:
 s=s.replace(f'            if ({condition}) return batch;',f'            if ({condition})\n            {{\n                return batch;\n            }}')
s=s.replace('if (branch.ElseStatement is null) elseReturn = fallback;','if (branch.ElseStatement is null)\n            {\n                elseReturn = fallback;\n            }')
p.write_text(s,encoding='utf-8',newline='\n')
