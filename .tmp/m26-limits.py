from pathlib import Path
p=Path('src/Copeland/Copeland.TS/Syntax/Lexer.cs');s=p.read_text();s=s.replace('''        object? value = !parsed
            ? null''','''        object? value = !parsed
            ? numericText == "2147483648" ? new MinimumIntMagnitudeTokenValue() : null''',1).replace('''        if (!parsed)
        {
            _diagnostics.Report("COPE-LEX-0004", "Invalid number literal.", start, text.Length);''','''        if (!parsed && value is null)
        {
            _diagnostics.Report("COPE-LEX-0004", "Invalid number literal.", start, text.Length);''',1)
s=s.replace('''        object? value = !parsed
            ? numericText == "2147483648" ? new MinimumIntMagnitudeTokenValue() : null
            : hasDecimalPoint
                ? (object)floatValue
                : intValue;''','''        object? value = null;
        if (parsed)
        {
            value = hasDecimalPoint ? (object)floatValue : intValue;
        }
        else if (numericText == "2147483648")
        {
            // Only unary minus may consume this magnitude as the minimum int.
            value = new MinimumIntMagnitudeTokenValue();
        }''');s+='\n/// <summary>The magnitude of int.MinValue is legal only directly after unary minus.</summary>\npublic sealed record MinimumIntMagnitudeTokenValue;\n';p.write_text(s)
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text();needle='''            var op = u.OperatorToken.Kind; var operand = BindExpression(u.Operand);''';s=s.replace(needle,'''            if (u.OperatorToken.Kind == SyntaxKind.MinusToken
                && u.Operand is LiteralExpressionSyntax { LiteralToken.Value: MinimumIntMagnitudeTokenValue })
            {
                return new BoundLiteralExpression(int.MinValue, PrimitiveTypeSymbol.Int);
            }
'''+needle,1)
# String conversion suppresses only dependent error input.
start=s.index('Report("COPE-NUM-0004",');pos=s.rfind('        private BoundExpression',0,start);body=s.index('        {',pos)+len('        {');s=s[:body]+'\n            if (operand.Type == PrimitiveTypeSymbol.Error) return new BoundErrorExpression();'+s[body:];p.write_text(s)
import json
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');c=json.loads(p.read_text())
for f in c:
 if f['Id']=='array-filled-negative-trap':f['Input']='function negative(): int { return -1; } function run(): int[] { return MutableArray<int>(negative(), 7).freeze(); }'
c.append(dict(Id='decimal-int-minimum',Category='numeric literals',Input='function run(): int { return -2147483648 / -1; }',Outcome='compile',ExpectedJson='-2147483648'))
p.write_text(json.dumps(c,indent=2)+'\n')
