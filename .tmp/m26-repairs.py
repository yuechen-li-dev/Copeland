from pathlib import Path
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text()
s=s.replace('or CallExpressionSyntax or ArrowExpressionSyntax or CaptureExpressionSyntax;','or CallExpressionSyntax or GenericCallExpressionSyntax or NewExpressionSyntax or ArrowExpressionSyntax or CaptureExpressionSyntax;',1)
s=s.replace('init = BindExpression(v.Initializer, inferInitializer ? null : type);','init = type == PrimitiveTypeSymbol.Error && !inferInitializer\n                    ? new BoundErrorExpression()\n                    : BindExpression(v.Initializer, inferInitializer ? null : type);',1)
# Explicitly report dynamic names as repairs only if no lexical declaration exists.
needle='''            if (!_scope.TryLookup(n.IdentifierToken.Text, out var symbol) || symbol is null)
            {''';pos=s.index(needle,s.index('private BoundExpression BindName('))+len(needle)
s=s[:pos]+'''
                if (n.IdentifierToken.Text == "undefined")
                {
                    ReportRepair("COPE-PROFILE-0011", "undefined has no Copeland runtime meaning. Use Option<T> with Some(value) or None.", n.IdentifierToken, "Option<T> with Some(value) or None");
                    return new BoundErrorExpression();
                }
'''+s[pos:]
s=s.replace('Report("COPE-PROFILE-0005", "Null is not supported in Browser TypeScript Profile v1. Use fallible functions or an explicit option type when available.", l.LiteralToken);','ReportRepair("COPE-PROFILE-0005", "Null is not supported in Browser TypeScript Profile v1. Use Option<T> with Some(value) or None.", l.LiteralToken, "Option<T> with Some(value) or None");')
pos=s.index('        {',s.index('private TypeSymbol ResolveIdentifierType('))+len('        {')
s=s[:pos]+'''
            if (i.Identifier.Text == "any")
            {
                ReportRepair("COPE-PROFILE-0012", "any erases the type needed for deterministic layout. Use a concrete value type, a nominal record, or a generic <T extends FieldRequirement>.", i.Identifier, "a concrete value type, a nominal record, or a generic <T extends FieldRequirement>");
                return PrimitiveTypeSymbol.Error;
            }
'''+s[pos:]
# Map is explicitly a missing native capability, not a missing CLR import.
pos=s.index('        {',s.index('private TypeSymbol BindGenericClrOrStructuralType('))+len('        {')
s=s[:pos]+'''
            if (syntax.Identifier.Text is "Map" or "MutableMap")
            {
                ReportMissingNativeMap(syntax.Identifier);
                return PrimitiveTypeSymbol.Error;
            }
'''+s[pos:]
pos=s.index('        {',s.index('private BoundExpression BindNew('))+len('        {')
s=s[:pos]+'''
            if (expression.Target is NameExpressionSyntax { IdentifierToken.Text: "Map" or "MutableMap" } map)
            {
                ReportMissingNativeMap(map.IdentifierToken);
                return new BoundErrorExpression();
            }
'''+s[pos:]
pos=s.index('        private BoundExpression BindNew(')
s=s[:pos]+'''        private void ReportMissingNativeMap(SyntaxToken anchor)
        {
            ReportRepair("COPE-COLLECTION-0001", "Native MutableMap<K, V> is not yet supported. For CLR-only storage use 'using System.Collections.Generic;' and 'new Dictionary<K, V>()'; use ContainsKey before indexed reads. Cross-backend Option-valued lookup and insertion-order iteration remain unsupported.", anchor,
                "using System.Collections.Generic; new Dictionary<K, V>()");
        }

'''+s[pos:]
s=s.replace('''            string repair = receiverType is ArrayTypeSymbol && method == "push"
                ?''','''            string repair = receiverType is ArrayTypeSymbol && method == "map"
                ? "Use batch values as item { return transform(item); } with a pure transform function and the receiver in place of values."
                : receiverType is ArrayTypeSymbol && method == "push"
                ?''')
p.write_text(s)
p=Path('src/Copeland/Copeland.TS/Semantics/StaticEvaluation.cs');s=p.read_text().replace('''                StaticValue value = EvaluateExpression(assignment.Expression, environment);
                environment.Set(assignment.Variable, value);
                return value;''','''                StaticValue? previous = assignment.ReturnsPreviousValue ? environment.Get(assignment.Variable) : null;
                StaticValue value = EvaluateExpression(assignment.Expression, environment);
                environment.Set(assignment.Variable, value);
                return previous ?? value;''');p.write_text(s)
p=Path('src/Copeland/Copeland.TS/Syntax/Parser.cs');s=p.read_text();start=s.index('        var unaryPrecedence',s.index('private ExpressionSyntax ParseBinaryExpression'));end=s.index('\n        }\n\n        while (true)',start);s=s[:start]+''.join('    '+line+'\n' for line in s[start:end].splitlines()).rstrip('\n')+s[end:];p.write_text(s)
import json
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');c=json.loads(p.read_text())
for f in c:
 if f['Id']=='enum-match':f['Input']=f['Input'].replace('Flag.On()','Flag.On')
def reject(id,cat,source,code,anchor,repair,occ=1,outcome='repair'):
 c.append(dict(Id=id,Category=cat,Input=source,Outcome=outcome,PrimaryCode=code,Anchor=anchor,Repair=repair,AnchorOccurrence=occ))
reject('native-map-gap','collections','function run(): int { const counts = new Map<string, int>(); return counts.size; }','COPE-COLLECTION-0001','Map','new Dictionary<K, V>()',outcome='unsupported capability')
reject('native-map-type-gap','collections','function run(): int { const counts: Map<string, int> = new Map<string, int>(); return counts.size; }','COPE-COLLECTION-0001','Map','new Dictionary<K, V>()',outcome='unsupported capability')
reject('any-repair','records','function run(): int { const value: any = 1; return value; }','COPE-PROFILE-0012','any','concrete value type')
reject('null-repair','Option','function run(): Option<int> { return null; }','COPE-PROFILE-0005','null','Option<T> with Some(value) or None')
reject('undefined-repair','Option','function run(): Option<int> { return undefined; }','COPE-PROFILE-0011','undefined','Option<T> with Some(value) or None')
reject('array-map','arrays','function run(): int[] { const values: int[] = [1, 2]; return values.map((item: int): int => item * 2); }','COPE-CALL-0021','map','batch values as item')
c.append(dict(Id='generic-array-inference',Category='arrays',Input='function run(): int[] { const a = MutableArray<int>(3, 7); a[1] = 9; return a.freeze(); }',Outcome='compile',ExpectedJson='[7,9,7]'))
c.append(dict(Id='exponent-literal',Category='numeric literals',Input='function run(): float { return 1.25e2; }',Outcome='compile',ExpectedJson='125'))
p.write_text(json.dumps(c,indent=2)+'\n')
