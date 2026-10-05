from pathlib import Path
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text()
s=s.replace('if (receiverType == PrimitiveTypeSymbol.String && method is "Split" or "IndexOf" or "CodeAt" or "Slice" or "Join")','string? nativeMethod = method switch\n            {\n                "split" or "Split" => "Split",\n                "indexOf" or "IndexOf" => "IndexOf",\n                "charCodeAt" or "codeAt" or "CodeAt" => "CodeAt",\n                "slice" or "Slice" => "Slice",\n                _ => null,\n            };\n            if (receiverType == PrimitiveTypeSymbol.String && nativeMethod is not null)')
s=s.replace('string replacement = $"String.{method}(', 'string replacement = $"String.{nativeMethod}(')
s=s.replace('var op = u.OperatorToken.Kind; var operand = BindExpression(u.Operand);','var op = u.OperatorToken.Kind; var operand = BindExpression(u.Operand);\n            if (operand.Type == PrimitiveTypeSymbol.Error) return new BoundErrorExpression();')
s=s.replace('var l = BindExpression(b.Left); var r = BindExpression(b.Right); var op = b.OperatorToken.Kind;','var l = BindExpression(b.Left); var r = BindExpression(b.Right); var op = b.OperatorToken.Kind;\n            if (l.Type == PrimitiveTypeSymbol.Error || r.Type == PrimitiveTypeSymbol.Error) return new BoundErrorExpression();')
needle='Report("COPE-CALL-0017", $"Implicit lexical capture of'
pos=s.index(needle);end=s.index('\n',pos)
s=s[:end]+ '\n                    _scope.TryDeclare(new VariableSymbol(name.IdentifierToken.Text, PrimitiveTypeSymbol.Error, true));'+s[end:]
p.write_text(s)
import json
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');c=json.loads(p.read_text())
for f in c:
 if f['Id']=='array-filled': f['Input']=f['Input'].replace('const a =','const a: MutableArray<int> =')
 if f['Id']=='computed-compound': f['Input']=f['Input'].replace('const values =','const values: MutableArray<int> =')
 if f['Id']=='generic-clr-list':f['Input']=f['Input'].replace('const values =','const values: List<int> =')
 if f['Id']=='bitwise-hash':f['ExpectedJson']=str(((123^45)*16777619+2**31)%2**32-2**31)
 if f['Id']=='closure-capture':f['Input']=f['Input'].replace('(value: int): int =>','capture { bias } (value: int): int =>')
 if f['Id']=='record-value':f['Input']=f['Input'].replace('const p = Pair {','const p: Pair = {')
c.append(dict(Id='implicit-capture-repair',Category='closures',Input='function run(): int { const bias: int = 2; const add = (value: int): int => value + bias; return add(5); }',Outcome='repair',PrimaryCode='COPE-CALL-0017',Anchor='bias;',Repair='capture { bias }'))
# Anchor repeats in declaration: identify use via exact position option added below.
c[-1]['Anchor']='bias';c[-1]['AnchorOccurrence']=2
p.write_text(json.dumps(c,indent=2)+'\n')
