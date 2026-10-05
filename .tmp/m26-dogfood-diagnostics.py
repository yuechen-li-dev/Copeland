from pathlib import Path
p=Path('src/Copeland/Copeland.TS/Syntax/Lexer.cs');s=p.read_text();pos=s.index('        var start = _position;',s.index('private SyntaxToken LexNumber'))+len('        var start = _position;');s=s[:pos]+'''
        if (Current == '0' && Peek(1) is 'x' or 'X')
        {
            _position += 2;
            int digitsStart = _position;
            while (char.IsAsciiLetterOrDigit(Current)) _position++;
            string radixText = _text[start.._position];
            if (uint.TryParse(_text[digitsStart.._position], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint bits))
            {
                return new SyntaxToken(SyntaxKind.NumberToken, start, radixText, unchecked((int)bits));
            }
            _diagnostics.Report("COPE-LEX-0004", "Hexadecimal int literals must contain one to eight hex digits (a signed 32-bit bit pattern).", start, radixText.Length);
            return new SyntaxToken(SyntaxKind.NumberToken, start, radixText, null);
        }
'''+s[pos:];p.write_text(s)
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text();s=s.replace('private readonly Scope _global = new(null);','private readonly Scope _global = new(null);\n        private bool _missingNativeMapReported;')
s=s.replace('bool inferArrowLocal = v.Type is null && _arrowBodyDepth > 0;','bool inferArrowLocal = v.Type is null && _arrowBodyDepth > 0;\n            bool inferCoalesce = v.Type is null && v.Initializer is CoalesceExpressionSyntax;')
s=s.replace('bool inferInitializer = inferCallableReference || inferArrowLocal || inferNumericLiteral || inferRecordValue;','bool inferInitializer = inferCallableReference || inferArrowLocal || inferNumericLiteral || inferRecordValue || inferCoalesce;')
needle='''        private void ReportMissingNativeMap(SyntaxToken anchor)
        {''';s=s.replace(needle,needle+'\n            if (_missingNativeMapReported) return;\n            _missingNativeMapReported = true;')
needle='''            if (TryBindNativeStringCall(c, out BoundExpression? nativeString))''';pos=s.index(needle);s=s[:pos]+'''            if (c.Target is MemberAccessExpressionSyntax { Target: NameExpressionSyntax { IdentifierToken.Text: "Math" }, NameToken.Text: "imul" } multiply
                && !_scope.TryLookup("Math", out _))
            {
                string replacement = c.Arguments.Count == 2
                    ? $"({AuthoredText(c.Arguments[0])} * {AuthoredText(c.Arguments[1])})"
                    : "left * right";
                ReportRepair("COPE-NUM-0006", $"Copeland int multiplication already wraps at 32 bits. Use {replacement}.", multiply.NameToken, replacement);
                return new BoundErrorExpression();
            }

'''+s[pos:];p.write_text(s)
import json
p=Path('tests/Copeland/Copeland.TS.Tests/TypeScriptInstinctCorpus/instinct-corpus.json');c=json.loads(p.read_text());c.append(dict(Id='hex-int-bit-pattern',Category='numeric literals',Input='function run(): int { return 0xffffffff; }',Outcome='compile',ExpectedJson='-1'));c.append(dict(Id='imul-repair',Category='numeric',Input='function run(): int { return Math.imul(7, 9); }',Outcome='repair',PrimaryCode='COPE-NUM-0006',Anchor='imul',Repair='(7 * 9)'));p.write_text(json.dumps(c,indent=2)+'\n')
