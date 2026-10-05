from pathlib import Path
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:60]);p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
b='src/Copeland/Copeland.TS/Semantics/Binder.cs'
edit(b,'        private BoundExpression BindBatch(BatchExpressionSyntax batch)\n        {','''        private BoundExpression BindBatch(BatchExpressionSyntax batch)
        {
            batch = NormalizeBatchReturns(batch);
''')
edit(b,'''            if (batch.Body.Statements.Take(batch.Body.Statements.Count - 1).Any(statement => statement is not VariableDeclarationStatementSyntax and not ExpressionStatementSyntax))
            {
                Report("COPE-BATCH-0004", "A CTS-BATCH-M1 body may contain item-local declarations and expressions before its final return.", batch.Body.OpenBraceToken);
            }''','''            foreach (StatementSyntax statement in batch.Body.Statements.Take(batch.Body.Statements.Count - 1))
            {
                if (!IsBatchPrefixStatement(statement))
                {
                    SyntaxToken anchor = statement is IfStatementSyntax conditional ? conditional.IfKeyword : batch.Body.OpenBraceToken;
                    ReportRepair("COPE-BATCH-0010", "This batch branch cannot return early or contain loops. Use return if (condition) { value } else { otherValue }, or call a pure helper function.", anchor,
                        "return if (condition) { value } else { otherValue };" );
                    return new BoundErrorExpression();
                }
            }''')
edit(b,'''                case BoundExpressionStatement expression:
                    ValidateBatchBodyEffects(expression.Expression, anchor);
                    break;
                default:
                    Report("COPE-BATCH-0010"''','''                case BoundExpressionStatement expression:
                    ValidateBatchBodyEffects(expression.Expression, anchor);
                    break;
                case BoundBlockStatement block:
                    foreach (BoundStatement child in block.Statements) ValidateBatchStatementEffects(child, anchor);
                    break;
                case BoundIfStatement conditional:
                    ValidateBatchBodyEffects(conditional.Condition, anchor);
                    ValidateBatchStatementEffects(conditional.ThenStatement, anchor);
                    if (conditional.ElseStatement is not null) ValidateBatchStatementEffects(conditional.ElseStatement, anchor);
                    break;
                default:
                    Report("COPE-BATCH-0010"''')
insert='''        private static bool IsBatchPrefixStatement(StatementSyntax statement)
            => statement switch
            {
                VariableDeclarationStatementSyntax or ExpressionStatementSyntax => true,
                BlockStatementSyntax block => block.Statements.All(IsBatchPrefixStatement),
                IfStatementSyntax conditional => IsBatchPrefixStatement(conditional.ThenStatement)
                    && (conditional.ElseStatement is null || IsBatchPrefixStatement(conditional.ElseStatement)),
                _ => false,
            };

        private static BatchExpressionSyntax NormalizeBatchReturns(BatchExpressionSyntax batch)
        {
            ReturnStatementSyntax? SingleReturn(StatementSyntax? statement)
                => statement switch
                {
                    ReturnStatementSyntax returned when returned.Expression is not null => returned,
                    BlockStatementSyntax { Statements.Count: 1 } block => SingleReturn(block.Statements[0]),
                    _ => null,
                };
            var statements = batch.Body.Statements.ToList();
            if (statements.Count == 0) return batch;
            int branchIndex = statements.Count - 1;
            ReturnStatementSyntax? fallback = null;
            if (statements[^1] is ReturnStatementSyntax returned && statements.Count > 1)
            {
                fallback = returned;
                branchIndex--;
            }
            if (statements[branchIndex] is not IfStatementSyntax branch) return batch;
            ReturnStatementSyntax? thenReturn = SingleReturn(branch.ThenStatement);
            ReturnStatementSyntax? elseReturn = SingleReturn(branch.ElseStatement);
            if (branch.ElseStatement is null) elseReturn = fallback;
            if (thenReturn?.Expression is null || elseReturn?.Expression is null) return batch;
            var conditional = new IfExpressionSyntax(branch.IfKeyword, branch.Condition,
                batch.Body.OpenBraceToken, thenReturn.Expression, batch.Body.CloseBraceToken,
                branch.ElseKeyword ?? branch.IfKeyword, batch.Body.OpenBraceToken, elseReturn.Expression, batch.Body.CloseBraceToken);
            statements.RemoveRange(branchIndex, statements.Count - branchIndex);
            statements.Add(thenReturn with { Expression = conditional });
            return batch with { Body = batch.Body with { Statements = statements } };
        }

'''
edit(b,'        private BoundExpression BindBatch(',insert+'        private BoundExpression BindBatch(')
# Reuse the ordinary statement emitter for bounded structured branch prefixes.
c='src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs'
s=Path(c).read_text();start=s.index('        int batchId = tempIndex++;',s.index('    private static string EmitBatchExpression('));end=s.index('    private static string EmitCallableConstruction(',start)
new='''        int batchId = tempIndex++;
        string input = "__cope_batch_input_" + batchId;
        string output = "__cope_batch_output_" + batchId;
        writer.WriteLine($"{MapType(batch.Input.Type)} {input} = {EmitExpression(writer, batch.Input, function, enumNames, ref tempIndex, diagnostics)};");
        writer.WriteLine($"{MapType(batch.ArrayType)} {output} = __cope_batch_map({input}, {CSharpNameMangler.Mangle(batch.Item.Name)} =>");
        writer.WriteLine("{");
        writer.Indent();
        foreach (MirStatement statement in batch.Body.PrefixStatements)
        {
            EmitStatement(writer, statement, function, enumNames, ref tempIndex, diagnostics);
        }
        writer.WriteLine($"return {EmitExpression(writer, batch.Body.ValueExpression, function, enumNames, ref tempIndex, diagnostics)};");
        writer.Unindent();
        writer.WriteLine("});");
        return output;
    }

'''
s=s[:start]+new+s[end:];Path(c).write_text(s,encoding='utf-8',newline='\n')
edit(c,'        writer.WriteLine("private static int __cope_batch_max_degree_for_testing = 0;");','        writer.WriteLine("private static int __cope_batch_max_degree_for_testing = 0;");\n        BatchRuntime.Emit(writer);')
j='src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs'
s=Path(j).read_text();start=s.index('        foreach (MirStatement statement in batch.Body.PrefixStatements)',s.index('    private static EmittedExpression EmitBatchExpression('));end=s.index('        EmittedExpression value =',start)
s=s[:start]+'''        var bodyWriter = new JavaScriptTextWriter(new JavaScriptEmissionDocument(), names.Profile);
        foreach (MirStatement statement in batch.Body.PrefixStatements)
        {
            EmitStatement(bodyWriter, statement, function, catalog, results, names, flowEnabled);
        }
        bodyLines.AddRange(bodyWriter.ToString().Split('\\n').Where(line => line.Length > 0));

'''+s[end:]
s=s.replace('bodyLines.AddRange(value.Prelude.Select(line => line.Text));','bodyLines.AddRange(value.Prelude.Select(line => new string(\' \', line.IndentOffset * 4) + line.Text));') if False else s
Path(j).write_text(s,encoding='utf-8',newline='\n')
