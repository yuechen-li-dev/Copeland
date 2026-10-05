from pathlib import Path
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:70]);p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
k='src/Copeland/Copeland.TS/Syntax/SyntaxKind.cs'; f='src/Copeland/Copeland.TS/Syntax/SyntaxFacts.cs';p='src/Copeland/Copeland.TS/Syntax/Parser.cs';l='src/Copeland/Copeland.TS/Syntax/Lexer.cs';b='src/Copeland/Copeland.TS/Semantics/Binder.cs';n='src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs'
new_tokens={'PlusPlusToken':'++','MinusMinusToken':'--','PlusEqualsToken':'+=','MinusEqualsToken':'-=','StarEqualsToken':'*=','SlashEqualsToken':'/=','PercentEqualsToken':'%=','CaretToken':'^','ShiftLeftToken':'<<','ShiftRightToken':'>>'}
edit(k,'    PlusToken,',''.join('    '+x+',\n' for x in new_tokens)+'    PlusToken,')
edit(f,'            SyntaxKind.PlusToken => "+",',''.join('            SyntaxKind.'+x+' => "'+v+'",\n' for x,v in new_tokens.items())+'            SyntaxKind.PlusToken => "+",')
for token,character,compound in [('PlusToken','+','PlusEqualsToken'),('MinusToken','-','MinusEqualsToken'),('StarToken','*','StarEqualsToken'),('SlashToken','/','SlashEqualsToken'),('PercentToken','%','PercentEqualsToken')]:
 old="                case '"+character+"':\n"
 extra="                    if (Peek(1) == '=') return DoubleCharToken(SyntaxKind."+compound+");\n"
 if character in '+-': extra+="                    if (Peek(1) == '"+character+"') return DoubleCharToken(SyntaxKind."+('PlusPlusToken' if character=='+' else 'MinusMinusToken')+");\n"
 edit(l,old,old+extra)
edit(l,"                case '&':","                case '^':\n                    return SingleCharToken(SyntaxKind.CaretToken);\n                case '&':")
start=Path(f).read_text().index('    public static int GetBinaryOperatorPrecedence')
s=Path(f).read_text();end=s.index('    public static string? GetText',start)
s=s[:start]+'''    public static int GetBinaryOperatorPrecedence(SyntaxKind kind)
        => kind switch
        {
            SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken => 12,
            SyntaxKind.PlusToken or SyntaxKind.MinusToken => 11,
            SyntaxKind.ShiftLeftToken or SyntaxKind.ShiftRightToken => 10,
            SyntaxKind.LessToken or SyntaxKind.LessOrEqualsToken or SyntaxKind.GreaterToken or SyntaxKind.GreaterOrEqualsToken => 9,
            SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken => 8,
            SyntaxKind.AmpersandToken => 7,
            SyntaxKind.CaretToken => 6,
            SyntaxKind.PipeToken => 5,
            SyntaxKind.AmpersandAmpersandToken => 4,
            SyntaxKind.PipePipeToken => 3,
            SyntaxKind.PipeGreaterToken => 1,
            _ => 0,
        };

'''+s[end:]
# Unary must bind tighter than the new arithmetic precedence.
s=s.replace('=> 8,','=> 13,',1) if 'GetUnaryOperatorPrecedence' in s else s
Path(f).write_text(s,encoding='utf-8',newline='\n')
edit(n,'''public sealed record AssignmentExpressionSyntax(ExpressionSyntax Left, SyntaxToken EqualsToken, ExpressionSyntax Right) : ExpressionSyntax
{''','''public sealed record AssignmentExpressionSyntax(ExpressionSyntax Left, SyntaxToken EqualsToken, ExpressionSyntax Right) : ExpressionSyntax
{
    public bool ReturnsPreviousValue { get; init; }
''')
edit(p,'        if (Current.Kind == SyntaxKind.EqualsToken)\n        {\n            var equalsToken = Match(SyntaxKind.EqualsToken);','''        if (Current.Kind is SyntaxKind.EqualsToken or SyntaxKind.PlusEqualsToken or SyntaxKind.MinusEqualsToken
            or SyntaxKind.StarEqualsToken or SyntaxKind.SlashEqualsToken or SyntaxKind.PercentEqualsToken)
        {
            var equalsToken = NextToken();''')
# Shift tokens are synthesized only in expressions: nested generic >> remains two closers.
edit(p,'            var precedence = SyntaxFacts.GetBinaryOperatorPrecedence(Current.Kind);','''            SyntaxKind operatorKind = Current.Kind;
            bool isShift = Current.Kind == Peek(1).Kind && Current.Kind is SyntaxKind.LessToken or SyntaxKind.GreaterToken;
            if (isShift) operatorKind = Current.Kind == SyntaxKind.LessToken ? SyntaxKind.ShiftLeftToken : SyntaxKind.ShiftRightToken;
            var precedence = SyntaxFacts.GetBinaryOperatorPrecedence(operatorKind);''')
edit(p,'            var operatorToken = NextToken();\n            var right = ParseBinaryExpression(precedence);','''            var operatorToken = NextToken();
            if (isShift)
            {
                SyntaxToken second = NextToken();
                operatorToken = new SyntaxToken(operatorKind, operatorToken.Position, operatorToken.Text + second.Text, null);
            }
            var right = ParseBinaryExpression(precedence);''')
edit(p,'            if (Current.Kind == SyntaxKind.BangToken)\n            {\n                expression = new UnwrapExpressionSyntax','''            if (Current.Kind is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken)
            {
                SyntaxToken update = NextToken();
                SyntaxKind arithmetic = update.Kind == SyntaxKind.PlusPlusToken ? SyntaxKind.PlusToken : SyntaxKind.MinusToken;
                var one = new LiteralExpressionSyntax(new SyntaxToken(SyntaxKind.NumberToken, update.Position, "1", 1));
                var operation = new SyntaxToken(arithmetic, update.Position, arithmetic == SyntaxKind.PlusToken ? "+" : "-", null);
                expression = new AssignmentExpressionSyntax(expression, update, new BinaryExpressionSyntax(expression, operation, one))
                {
                    ReturnsPreviousValue = true,
                };
                continue;
            }
            if (Current.Kind == SyntaxKind.BangToken)
            {
                expression = new UnwrapExpressionSyntax''')
# Semantic operators are checked on closed int operands, with no host conversion.
edit(b,'            if (TypeFacts.IsNumeric(l.Type) && TypeFacts.IsNumeric(r.Type)\n                && op is SyntaxKind.PlusToken','''            if (op is SyntaxKind.AmpersandToken or SyntaxKind.PipeToken or SyntaxKind.CaretToken or SyntaxKind.ShiftLeftToken or SyntaxKind.ShiftRightToken)
            {
                if (TypeFacts.IsInt(l.Type) && TypeFacts.IsInt(r.Type)) return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Int);
                Report("COPE-TYPE-0007", "Bitwise operands must both have type int; use an explicit Int conversion policy.", b.OperatorToken);
                return new BoundErrorExpression();
            }
            if (TypeFacts.IsNumeric(l.Type) && TypeFacts.IsNumeric(r.Type)
                && op is SyntaxKind.PlusToken''')
edit(b,'        private BoundExpression BindAssignment(AssignmentExpressionSyntax a)\n        {','''        private BoundExpression BindAssignment(AssignmentExpressionSyntax a)
        {
            if (a.EqualsToken.Kind != SyntaxKind.EqualsToken && a.Left is not NameExpressionSyntax)
            {
                // Ordinary assignment still owns immutable member and array diagnostics.
                if (a.Left is IndexExpressionSyntax index && BindExpression(index.Target).Type is MutableArrayTypeSymbol)
                {
                    ReportRepair("COPE-MUTATION-0001", "Compound updates of computed storage are not yet supported. Bind the receiver and index to const locals, then assign array[index] = array[index] + value.", a.EqualsToken,
                        "Bind receiver and index once; use explicit indexed assignment.");
                    return new BoundErrorExpression();
                }
            }
''')
edit(b,'            var expr = BindExpression(a.Right, variable.Type);','''            ExpressionSyntax valueSyntax = a.Right;
            if (a.EqualsToken.Kind is SyntaxKind.PlusEqualsToken or SyntaxKind.MinusEqualsToken or SyntaxKind.StarEqualsToken or SyntaxKind.SlashEqualsToken or SyntaxKind.PercentEqualsToken)
            {
                SyntaxKind operation = a.EqualsToken.Kind switch
                {
                    SyntaxKind.PlusEqualsToken => SyntaxKind.PlusToken,
                    SyntaxKind.MinusEqualsToken => SyntaxKind.MinusToken,
                    SyntaxKind.StarEqualsToken => SyntaxKind.StarToken,
                    SyntaxKind.SlashEqualsToken => SyntaxKind.SlashToken,
                    _ => SyntaxKind.PercentToken,
                };
                var token = new SyntaxToken(operation, a.EqualsToken.Position, SyntaxFacts.GetText(operation)!, null);
                valueSyntax = new BinaryExpressionSyntax(a.Left, token, a.Right);
            }
            var expr = BindExpression(valueSyntax, variable.Type);''')
edit(b,'            return new BoundAssignmentExpression(variable, expr);','            return new BoundAssignmentExpression(variable, expr) { ReturnsPreviousValue = a.ReturnsPreviousValue };')
bound='src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs'
edit(bound,'public sealed class BoundAssignmentExpression : BoundExpression { public BoundAssignmentExpression(VariableSymbol variable, BoundExpression expression) { Variable = variable; Expression = expression; } public VariableSymbol Variable { get; } public BoundExpression Expression { get; } public override TypeSymbol Type => Expression.Type; }','''public sealed class BoundAssignmentExpression(VariableSymbol variable, BoundExpression expression) : BoundExpression
{
    public VariableSymbol Variable { get; } = variable;
    public BoundExpression Expression { get; } = expression;
    public bool ReturnsPreviousValue { get; init; }
    public override TypeSymbol Type => Expression.Type;
}''')
edit(b,'new BoundAssignmentExpression(assignment.Variable, RewriteExpression(assignment.Expression))','new BoundAssignmentExpression(assignment.Variable, RewriteExpression(assignment.Expression)) { ReturnsPreviousValue = assignment.ReturnsPreviousValue }')
mir='src/Copeland/Copeland.TS.Mir/MirNodes.cs'
edit(mir,'public sealed record MirAssignmentExpression(string Name, MirExpression Expression, MirType Type) : MirExpression(Type);','''public sealed record MirAssignmentExpression(string Name, MirExpression Expression, MirType Type) : MirExpression(Type)
{
    public bool ReturnsPreviousValue { get; init; }
}''')
lower='src/Copeland/Copeland.TS/Lowering/MirLowerer.cs'
edit(lower,'new MirAssignmentExpression(assignment.Name, value, assignment.Type)','assignment with { Expression = value }')
edit(lower,'new MirAssignmentExpression(a.Variable.Name, LowerExpression(a.Expression), ToMirType(a.Type))','new MirAssignmentExpression(a.Variable.Name, LowerExpression(a.Expression), ToMirType(a.Type)) { ReturnsPreviousValue = a.ReturnsPreviousValue }')
edit(lower,'        SyntaxKind.PlusToken => "+",','''        SyntaxKind.AmpersandToken => "&",
        SyntaxKind.PipeToken => "|",
        SyntaxKind.CaretToken => "^",
        SyntaxKind.ShiftLeftToken => "<<",
        SyntaxKind.ShiftRightToken => ">>",
        SyntaxKind.PlusToken => "+",''')
