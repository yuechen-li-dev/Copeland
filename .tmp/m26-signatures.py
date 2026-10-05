from pathlib import Path
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text();s=s.replace('            ResolveAliases();','            BindClrUsingDirectives(_tree.Root);\n            ResolveAliases();',1).replace('            PredeclareFunctions(_tree.Root);\n            BindClrUsingDirectives(_tree.Root);','            PredeclareFunctions(_tree.Root);',1)
s=s.replace('|| _global.TryLookup(name, out _);','|| _global.TryLookup(name, out _)\n                || _tree.Root.Members.OfType<FunctionDeclarationSyntax>().Any(function => function.Identifier.Text == name);',1)
p.write_text(s)
# Move superseded language-law specimens within the repo, preserving their source.
root=Path('tests/Copeland/Copeland.TS.Tests/Language');(root/'Valid/equality').mkdir(exist_ok=True)
for name in ['strict-equality','strict-inequality']:
 source=root/'Invalid/equality'/f'{name}.cl-invalid.ts';target=root/'Valid/equality'/f'{name}.cl-valid.ts';assert not target.exists();source.rename(target)
# Remove just obsolete copied output specimens, with each resolved target contained in test output.
for cfg in ['Debug','Release']:
 for name in ['strict-equality','strict-inequality']:
  target=Path('tests/Copeland/Copeland.TS.Tests/bin')/cfg/'net10.0/Language/Invalid/equality'/f'{name}.cl-invalid.ts'
  if target.exists():target.unlink()
