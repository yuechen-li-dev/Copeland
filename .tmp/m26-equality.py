from pathlib import Path
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text().replace('? $"({AuthoredText(c.Arguments[0])} * {AuthoredText(c.Arguments[1])})"','? $"(({AuthoredText(c.Arguments[0])}) * ({AuthoredText(c.Arguments[1])}))"')
start=s.index('            if (op is SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken)\n',s.index('private BoundExpression BindBinary'));end=s.index('            if (op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken)',start);s=s[:start]+s[end:]
# Normalize surface spellings to the same typed primitive equality node.
needle='var l = BindExpression(b.Left); var r = BindExpression(b.Right); var op = b.OperatorToken.Kind;';s=s.replace(needle,needle+'''\n            if (op == SyntaxKind.EqualsEqualsEqualsToken) op = SyntaxKind.EqualsEqualsToken;
            if (op == SyntaxKind.BangEqualsEqualsToken) op = SyntaxKind.BangEqualsToken;''',1);p.write_text(s)
p=Path('tests/Copeland/Copeland.TS.Tests/BinderTests.cs');s=p.read_text().replace('Profile_Rejects_Strict_Equality_Spellings','Strict_Equality_Spellings_use_the_same_typed_primitive_equality').replace('Assert.Contains(bound.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROFILE-0009");','Assert.Empty(bound.Diagnostics);');p.write_text(s)
p=Path('tests/Copeland/Copeland.TS.Tests/CompilerFacadeTests.cs');s=p.read_text();s=s.replace('    [InlineData("function equal(left: number, right: number): boolean { return left === right; }")]\n','').replace('    [InlineData("function different(left: number, right: number): boolean { return left !== right; }")]\n','');p.write_text(s)
import json
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');c=json.loads(p.read_text())
for f in c:
 if f['Id']=='imul-repair':f['Repair']='((7) * (9))'
c.append(dict(Id='typed-strict-equality',Category='conditionals',Input='function run(): boolean { return 7 === 7 && 7 !== 9; }',Outcome='compile',ExpectedJson='true'))
p.write_text(json.dumps(c,indent=2)+'\n')
