from pathlib import Path
p=Path('tests/Copeland/Copeland.TS.Tests/TableFeatureTests.cs');s=p.read_text().replace('62897D4142128179A9036545CBA4A0BDB4E3EB74ACF9D722E71E90A0EF93234F','CB293E99C3353216CE04AFD5703ADD331FA8A450AB4C5FDC8177DF9C97E4A144');p.write_text(s)
p=Path('src/Copeland/Copeland.TS/Syntax/SyntaxTree.cs');s=p.read_text().replace('SyntaxTree tree = Parse(text, fileKind);','SyntaxTree parsed = Parse(text, fileKind);\n        var locatedDiagnostics = parsed.Diagnostics.Select(diagnostic => diagnostic with { SourcePath = sourcePath }).ToArray();\n        SyntaxTree tree = new(text, parsed.Root, parsed.Tokens, locatedDiagnostics);');p.write_text(s)
p=Path('src/Copeland/Copeland.TS.Mir/MirValidator.cs');s=p.read_text();needle='''            case MirBinaryExpression binary:
                ValidateCallableExpression(binary.Left, functions, diagnostics);''';new='''            case MirBinaryExpression binary:
                if (binary.Operator is "&" or "|" or "^" or "<<" or ">>"
                    && (binary.Type.Identifier != "int" || binary.Left.Type.Identifier != "int" || binary.Right.Type.Identifier != "int"))
                {
                    diagnostics.Add(new MirValidationDiagnostic("Bitwise operations require int operands and result."));
                }
                ValidateCallableExpression(binary.Left, functions, diagnostics);''';assert needle in s;s=s.replace(needle,new,1);p.write_text(s)
# always restore scoped emission state even if an unsupported expression throws.
p=Path('src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs');s=p.read_text();start=s.index('        var enumNames',s.index('private static CSharpCompilation EmitCore')) if '        var enumNames' in s[s.index('private static CSharpCompilation EmitCore'):] else -1
# Put try immediately after state resets, preserving all body locals in scope.
needle='''        UsesIntegerRuntime.Value = false;''';pos=s.index(needle,s.index('private static CSharpCompilation EmitCore'))+len(needle);s=s[:pos]+'\n        try\n        {'+s[pos:]
start=s.index('        CurrentSourcePath.Value = previousSourcePath;',pos);end=s.index('\n    }\n',start)
old=s[start:end];new='''        return diagnostics.Count == 0
            ? new CSharpCompilation(writer.ToString(), diagnostics, sidecarContract)
            : new CSharpCompilation(string.Empty, diagnostics);
        }
        finally
        {
            CurrentSourcePath.Value = previousSourcePath;
            CurrentOptions.Value = previousOptions;
            UsesNativeStrings.Value = previousNativeStrings;
            UsesIntegerRuntime.Value = previousIntegerRuntime;
        }''';s=s[:start]+new+s[end:]
# Indent new try body using explicit boundaries.
start=s.index('        try\n        {',pos-5)+len('        try\n        {\n');end=s.index('\n        }\n        finally',start);s=s[:start]+''.join('    '+line+'\n' for line in s[start:end].splitlines()).rstrip('\n')+s[end:];p.write_text(s)
