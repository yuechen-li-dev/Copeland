using Copeland.Script.Diagnostics;
using Copeland.Script.Semantics.Bound;
using Copeland.Script.Syntax;

namespace Copeland.Script.Semantics;

public static class Binder
{
    public static BoundCompilation Bind(SyntaxTree tree)
    {
        var impl = new BinderImpl(tree);
        return impl.Bind();
    }

    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);
        public Scope? Parent { get; } = parent;
        public bool TryDeclare(Symbol s) => _symbols.TryAdd(s.Name, s);
        public bool TryLookup(string n, out Symbol? symbol)
        {
            for (var c = this; c is not null; c = c.Parent)
                if (c._symbols.TryGetValue(n, out symbol)) return true;
            symbol = null; return false;
        }
    }

    private sealed class BinderImpl(SyntaxTree tree)
    {
        private readonly SyntaxTree _tree = tree;
        private readonly DiagnosticBag _diagnostics = new();
        private readonly Scope _global = new(null);
        private Scope _scope = null!;
        private FunctionSymbol? _currentFunction;
        private readonly List<BoundFunctionDeclaration> _functions = [];
        private readonly List<BoundStatement> _globals = [];

        public BoundCompilation Bind()
        {
            _scope = _global;
            PredeclareFunctions(_tree.Root);
            foreach (var m in _tree.Root.Members)
            {
                if (m is FunctionDeclarationSyntax f) _functions.Add(BindFunction(f));
                else if (m is GlobalStatementMemberSyntax g) _globals.Add(BindStatement(g.Statement));
            }
            return new BoundCompilation(_tree, new BoundProgram(_functions, _globals), _tree.Diagnostics.Concat(_diagnostics.Diagnostics).ToArray());
        }

        private void PredeclareFunctions(CompilationUnitSyntax root)
        {
            foreach (var m in root.Members.OfType<FunctionDeclarationSyntax>())
            {
                var ps = new List<ParameterSymbol>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in m.Parameters)
                {
                    var pt = BindType(p.Type, p.Identifier, missingId:"COPE-TYPE-0002", missingPrefix:"parameter");
                    if (!seen.Add(p.Identifier.Text)) Report("COPE-BIND-0005", $"Duplicate parameter '{p.Identifier.Text}'.", p.Identifier);
                    ps.Add(new ParameterSymbol(p.Identifier.Text, pt));
                }
                var rt = BindType(m.ReturnType, m.Identifier, missingId:"COPE-TYPE-0002", missingPrefix:"function return");
                var et = BindErrorType(m.ErrorType);
                var fn = new FunctionSymbol(m.Identifier.Text, ps, rt, et);
                if (!_global.TryDeclare(fn)) Report("COPE-BIND-0002", $"Duplicate declaration '{fn.Name}'.", m.Identifier);
            }
        }

        private BoundFunctionDeclaration BindFunction(FunctionDeclarationSyntax s)
        {
            _global.TryLookup(s.Identifier.Text, out var sym);
            var fn = (FunctionSymbol?)sym ?? new FunctionSymbol(s.Identifier.Text, [], PrimitiveTypeSymbol.Error, null);
            var prevFn = _currentFunction; _currentFunction = fn;
            var prev = _scope; _scope = new Scope(_global);
            foreach (var p in fn.Parameters)
            {
                if (!_scope.TryDeclare(p)) Report("COPE-BIND-0005", $"Duplicate parameter '{p.Name}'.", s.Identifier);
            }
            var body = (BoundBlockStatement)BindStatement(s.Body);
            _scope = prev; _currentFunction = prevFn;
            return new BoundFunctionDeclaration(fn, body);
        }

        private BoundStatement BindStatement(StatementSyntax s) => s switch
        {
            BlockStatementSyntax b => BindBlock(b),
            VariableDeclarationStatementSyntax v => BindVariable(v),
            ExpressionStatementSyntax e => new BoundExpressionStatement(BindExpression(e.Expression)),
            IfStatementSyntax i => BindIf(i),
            WhileStatementSyntax w => new BoundWhileStatement(EnsureBoolean(BindExpression(w.Condition), w.WhileKeyword), BindStatement(w.Body)),
            ForStatementSyntax f => BindFor(f),
            ReturnStatementSyntax r => BindReturn(r),
            _ => new BoundExpressionStatement(new BoundErrorExpression())
        };

        private BoundStatement BindBlock(BlockStatementSyntax b)
        {
            var prev = _scope; _scope = new Scope(prev);
            var list = b.Statements.Select(BindStatement).ToArray();
            _scope = prev;
            return new BoundBlockStatement(list);
        }

        private BoundStatement BindVariable(VariableDeclarationStatementSyntax v)
        {
            if (v.Keyword.Kind == SyntaxKind.VarKeyword) Report("COPE-PROFILE-0001", "'var' is not supported by Browser TypeScript Profile v1.", v.Keyword);
            var type = BindType(v.Type, v.Identifier, "COPE-TYPE-0002", "variable");
            var init = BindExpression(v.Initializer, type);
            if (!IsAssignable(type, init.Type)) Report("COPE-TYPE-0001", $"Type mismatch: expected '{type.Name}', got '{init.Type.Name}'.", v.Identifier);
            var varSym = new VariableSymbol(v.Identifier.Text, type, v.Keyword.Kind == SyntaxKind.ConstKeyword);
            if (!_scope.TryDeclare(varSym)) Report("COPE-BIND-0002", $"Duplicate declaration '{varSym.Name}'.", v.Identifier);
            return new BoundVariableDeclaration(varSym, init);
        }

        private BoundStatement BindIf(IfStatementSyntax i)
            => new BoundIfStatement(EnsureBoolean(BindExpression(i.Condition), i.IfKeyword), BindStatement(i.ThenStatement), i.ElseStatement is null ? null : BindStatement(i.ElseStatement));

        private BoundStatement BindFor(ForStatementSyntax f)
        {
            BoundStatement? init = f.Initializer switch
            {
                VariableDeclarationStatementSyntax v => BindVariable(v),
                ExpressionSyntax e => new BoundExpressionStatement(BindExpression(e)),
                _ => null
            };
            var c = f.Condition is null ? null : EnsureBoolean(BindExpression(f.Condition), f.ForKeyword);
            var inc = f.Increment is null ? null : BindExpression(f.Increment);
            return new BoundForStatement(init, c, inc, BindStatement(f.Body));
        }

        private BoundStatement BindReturn(ReturnStatementSyntax r)
        {
            var expected = _currentFunction?.ReturnType ?? PrimitiveTypeSymbol.Void;
            if (r.Expression is null)
            {
                if (expected != PrimitiveTypeSymbol.Void) Report("COPE-TYPE-0003", $"Type mismatch: expected '{expected.Name}', got 'void'.", r.ReturnKeyword);
                return new BoundReturnStatement(null);
            }
            var expr = BindExpression(r.Expression);
            if (expected == PrimitiveTypeSymbol.Void) Report("COPE-TYPE-0003", "Invalid return expression for void function.", r.ReturnKeyword);
            else if (!IsAssignable(expected, expr.Type)) Report("COPE-TYPE-0003", $"Type mismatch: expected '{expected.Name}', got '{expr.Type.Name}'.", r.ReturnKeyword);
            return new BoundReturnStatement(expr);
        }

        private BoundExpression BindExpression(ExpressionSyntax s, TypeSymbol? contextualType = null, bool allowUnhandledFallible = false)
        {
            var expression = s switch
        {
            LiteralExpressionSyntax l => BindLiteral(l),
            NameExpressionSyntax n => BindName(n),
            ParenthesizedExpressionSyntax p => BindExpression(p.Expression, contextualType),
            PropagateExpressionSyntax p => BindPropagate(p),
            UnaryExpressionSyntax u => BindUnary(u),
            BinaryExpressionSyntax b => BindBinary(b),
            AssignmentExpressionSyntax a => BindAssignment(a),
            CallExpressionSyntax c => BindCall(c),
            ArrayLiteralExpressionSyntax a => BindArray(a, contextualType),
            ObjectLiteralExpressionSyntax o => BindObject(o),
            MemberAccessExpressionSyntax m => BindMember(m),
            _ => new BoundErrorExpression()
            };

            if (!allowUnhandledFallible && expression is BoundCallExpression call && call.IsFallible && s is not PropagateExpressionSyntax)
                Report("COPE-TYPE-0013", $"Fallible call to '{call.Function.Name}' must be handled or propagated with '?'.", AnchorToken(s));

            return expression;
        }

        private BoundExpression BindName(NameExpressionSyntax n)
        {
            if (!_scope.TryLookup(n.IdentifierToken.Text, out var symbol) || symbol is null)
            {
                Report("COPE-BIND-0001", $"Undefined name '{n.IdentifierToken.Text}'.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            return symbol switch
            {
                VariableSymbol v => new BoundVariableExpression(v),
                ParameterSymbol p => new BoundVariableExpression(new VariableSymbol(p.Name, p.Type, true)),
                _ => new BoundErrorExpression()
            };
        }

        private BoundExpression BindLiteral(LiteralExpressionSyntax l)
        {
            var k = l.LiteralToken.Kind;
            return k switch
            {
                SyntaxKind.NumberToken => new BoundLiteralExpression(l.LiteralToken.Value, PrimitiveTypeSymbol.Number),
                SyntaxKind.StringToken => new BoundLiteralExpression(l.LiteralToken.Value, PrimitiveTypeSymbol.String),
                SyntaxKind.TrueKeyword => new BoundLiteralExpression(true, PrimitiveTypeSymbol.Boolean),
                SyntaxKind.FalseKeyword => new BoundLiteralExpression(false, PrimitiveTypeSymbol.Boolean),
                SyntaxKind.NullKeyword => BindNullLiteral(l),
                _ => new BoundErrorExpression()
            };
        }

        private BoundExpression BindUnary(UnaryExpressionSyntax u)
        {
            var op = u.OperatorToken.Kind; var operand = BindExpression(u.Operand);
            if (op == SyntaxKind.MinusToken && operand.Type == PrimitiveTypeSymbol.Number) return new BoundUnaryExpression(op, operand, PrimitiveTypeSymbol.Number);
            if (op == SyntaxKind.BangToken && operand.Type == PrimitiveTypeSymbol.Boolean) return new BoundUnaryExpression(op, operand, PrimitiveTypeSymbol.Boolean);
            Report("COPE-TYPE-0006", $"Invalid unary operand for '{u.OperatorToken.Text}'.", u.OperatorToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindBinary(BinaryExpressionSyntax b)
        {
            var l = BindExpression(b.Left); var r = BindExpression(b.Right); var op = b.OperatorToken.Kind;
            if (l.Type == PrimitiveTypeSymbol.Number && r.Type == PrimitiveTypeSymbol.Number && op is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken)
                return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Number);
            if (op == SyntaxKind.PlusToken && l.Type == PrimitiveTypeSymbol.String && r.Type == PrimitiveTypeSymbol.String)
                return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.String);
            if (op is SyntaxKind.LessToken or SyntaxKind.LessOrEqualsToken or SyntaxKind.GreaterToken or SyntaxKind.GreaterOrEqualsToken)
            {
                if (l.Type == PrimitiveTypeSymbol.Number && r.Type == PrimitiveTypeSymbol.Number) return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
            }
            if (op is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken)
            {
                if (l.Type == PrimitiveTypeSymbol.Boolean && r.Type == PrimitiveTypeSymbol.Boolean) return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
            }
            if (op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken)
            {
                if (l.Type.Name == r.Type.Name) return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
            }
            Report("COPE-TYPE-0007", $"Invalid binary operands for '{b.OperatorToken.Text}'.", b.OperatorToken);
            return new BoundErrorExpression();
        }

        private BoundExpression BindAssignment(AssignmentExpressionSyntax a)
        {
            if (a.Left is not NameExpressionSyntax n)
            {
                Report("COPE-BIND-0007", "Invalid assignment target.", a.EqualsToken);
                return new BoundErrorExpression();
            }
            if (!_scope.TryLookup(n.IdentifierToken.Text, out var symbol) || symbol is null)
            {
                Report("COPE-PROFILE-0004", $"Implicit global assignment is not supported: '{n.IdentifierToken.Text}'.", n.IdentifierToken);
                Report("COPE-BIND-0001", $"Undefined name '{n.IdentifierToken.Text}'.", n.IdentifierToken);
                return new BoundErrorExpression();
            }
            var variable = symbol as VariableSymbol ?? (symbol is ParameterSymbol p ? new VariableSymbol(p.Name, p.Type, true) : null);
            if (variable is null) { Report("COPE-BIND-0007", "Invalid assignment target.", n.IdentifierToken); return new BoundErrorExpression(); }
            if (variable.IsReadOnly) Report("COPE-BIND-0003", $"Cannot assign to const variable '{variable.Name}'.", n.IdentifierToken);
            var expr = BindExpression(a.Right, variable.Type);
            if (!IsAssignable(variable.Type, expr.Type)) Report("COPE-TYPE-0001", $"Type mismatch: expected '{variable.Type.Name}', got '{expr.Type.Name}'.", a.EqualsToken);
            return new BoundAssignmentExpression(variable, expr);
        }

        private BoundExpression BindCall(CallExpressionSyntax c)
        {
            if (c.Target is NameExpressionSyntax n && n.IdentifierToken.Text == "eval")
                Report("COPE-PROFILE-0003", "Dynamic evaluation is not supported by Browser TypeScript Profile v1.", n.IdentifierToken);

            if (c.Target is not NameExpressionSyntax name || !_scope.TryLookup(name.IdentifierToken.Text, out var s) || s is null)
            { Report("COPE-BIND-0001", "Undefined function name.", c.OpenParenToken); return new BoundErrorExpression(); }
            if (s is not FunctionSymbol fn) { Report("COPE-BIND-0006", $"Cannot call non-function '{s.Name}'.", c.OpenParenToken); return new BoundErrorExpression(); }
            if (c.Arguments.Count != fn.Parameters.Count) Report("COPE-TYPE-0004", $"Argument count mismatch: expected {fn.Parameters.Count}, got {c.Arguments.Count}.", c.OpenParenToken);
            var args = c.Arguments.Select(a => BindExpression(a)).ToArray();
            for (var i = 0; i < Math.Min(args.Length, fn.Parameters.Count); i++)
                if (!IsAssignable(fn.Parameters[i].Type, args[i].Type)) Report("COPE-TYPE-0005", $"Argument {i + 1} expected '{fn.Parameters[i].Type.Name}', got '{args[i].Type.Name}'.", c.Arguments[i] is LiteralExpressionSyntax le ? le.LiteralToken : c.OpenParenToken);
            return new BoundCallExpression(fn, args);
        }

        private BoundExpression BindArray(ArrayLiteralExpressionSyntax a, TypeSymbol? contextual)
        {
            var elems = a.Elements.Select(e => BindExpression(e)).ToArray();
            if (contextual is ArrayTypeSymbol ctx)
            {
                foreach (var e in elems) if (!IsAssignable(ctx.ElementType, e.Type)) Report("COPE-TYPE-0009", $"Type mismatch: expected '{ctx.ElementType.Name}', got '{e.Type.Name}'.", a.OpenBracketToken);
                return new BoundArrayExpression(elems, contextual);
            }
            if (elems.Length == 0) { Report("COPE-TYPE-0010", "Empty array requires contextual type.", a.OpenBracketToken); return new BoundErrorExpression(); }
            var first = elems[0].Type;
            if (elems.Any(x => x.Type.Name != first.Name)) { Report("COPE-TYPE-0009", "Array element type mismatch.", a.OpenBracketToken); return new BoundErrorExpression(); }
            return new BoundArrayExpression(elems, new ArrayTypeSymbol(first));
        }

        private BoundExpression BindObject(ObjectLiteralExpressionSyntax o) { Report("COPE-TYPE-0011", "Object literals are not supported in M0e.", o.OpenBraceToken); return new BoundErrorExpression(); }
        private BoundExpression BindMember(MemberAccessExpressionSyntax m) { Report("COPE-TYPE-0012", "Member access is not supported in M0e.", m.DotToken); return new BoundErrorExpression(); }

        private TypeSymbol BindType(TypeSyntax? type, SyntaxToken anchor, string missingId, string missingPrefix)
        {
            if (type is null) { Report(missingId, $"Missing type annotation for {missingPrefix} '{anchor.Text}'.", anchor); return PrimitiveTypeSymbol.Error; }
            return type switch
            {
                PredefinedTypeSyntax p => p.Keyword.Kind switch
                {
                    SyntaxKind.NumberKeyword => PrimitiveTypeSymbol.Number,
                    SyntaxKind.StringKeyword => PrimitiveTypeSymbol.String,
                    SyntaxKind.BooleanKeyword => PrimitiveTypeSymbol.Boolean,
                    SyntaxKind.VoidKeyword => PrimitiveTypeSymbol.Void,
                    SyntaxKind.NullKeyword => ReportedNullType(p.Keyword),
                    _ => PrimitiveTypeSymbol.Error
                },
                ArrayTypeSyntax a => new ArrayTypeSymbol(BindType(a.ElementType, anchor, missingId, missingPrefix)),
                IdentifierTypeSyntax i => ResolveIdentifierType(i),
                _ => PrimitiveTypeSymbol.Error
            };
        }


        private BoundExpression BindPropagate(PropagateExpressionSyntax p)
        {
            var operand = BindExpression(p.Operand, allowUnhandledFallible: true);
            if (!operand.IsFallible)
            {
                Report("COPE-TYPE-0016", "'?' can only be applied to a fallible expression.", p.QuestionToken);
                return new BoundErrorExpression();
            }

            if (_currentFunction is null || !_currentFunction.IsFallible || _currentFunction.ErrorType is null)
            {
                Report("COPE-TYPE-0014", "'?' can only be used inside a fallible function with a compatible error type.", p.QuestionToken);
                return new BoundErrorExpression();
            }

            if (_currentFunction.ErrorType.Name != operand.ErrorType!.Name)
            {
                Report("COPE-TYPE-0015", $"Cannot propagate error type '{operand.ErrorType.Name}' from function returning error type '{_currentFunction.ErrorType.Name}'.", p.QuestionToken);
                return new BoundErrorExpression();
            }

            return new BoundPropagateExpression(operand);
        }

        private BoundExpression BindNullLiteral(LiteralExpressionSyntax l)
        {
            Report("COPE-PROFILE-0005", "Null is not supported in Browser TypeScript Profile v1. Use fallible functions or an explicit option type when available.", l.LiteralToken);
            return new BoundErrorExpression();
        }

        private TypeSymbol? BindErrorType(TypeSyntax? type)
        {
            if (type is null)
            {
                return null;
            }

            return type switch
            {
                IdentifierTypeSyntax i => new ErrorNominalTypeSymbol(i.Identifier.Text),
                PredefinedTypeSyntax p => p.Keyword.Kind == SyntaxKind.NullKeyword ? ReportedNullType(p.Keyword) : new ErrorNominalTypeSymbol(p.Keyword.Text),
                ArrayTypeSyntax a => new ErrorNominalTypeSymbol(BindType(a, a.OpenBracketToken, "COPE-TYPE-0002", "error type").Name),
                _ => PrimitiveTypeSymbol.Error
            };
        }

        private TypeSymbol ReportedNullType(SyntaxToken token)
        {
            Report("COPE-PROFILE-0005", "Null is not supported in Browser TypeScript Profile v1. Use fallible functions or an explicit option type when available.", token);
            return PrimitiveTypeSymbol.Error;
        }

        private TypeSymbol ResolveIdentifierType(IdentifierTypeSyntax i)
        {
            Report("COPE-BIND-0004", $"Unknown type '{i.Identifier.Text}'.", i.Identifier);
            return PrimitiveTypeSymbol.Error;
        }

        private static bool IsAssignable(TypeSymbol target, TypeSymbol actual)
            => target == PrimitiveTypeSymbol.Error || actual == PrimitiveTypeSymbol.Error || target.Name == actual.Name;

        private BoundExpression EnsureBoolean(BoundExpression e, SyntaxToken at)
        {
            if (e.Type != PrimitiveTypeSymbol.Boolean && e.Type != PrimitiveTypeSymbol.Error)
                Report("COPE-TYPE-0001", $"Type mismatch: expected 'boolean', got '{e.Type.Name}'.", at);
            return e;
        }

        private static SyntaxToken AnchorToken(ExpressionSyntax s) => s switch
        {
            CallExpressionSyntax c => c.OpenParenToken,
            BinaryExpressionSyntax b => b.OperatorToken,
            UnaryExpressionSyntax u => u.OperatorToken,
            AssignmentExpressionSyntax a => a.EqualsToken,
            ParenthesizedExpressionSyntax p => p.OpenParenToken,
            PropagateExpressionSyntax p => p.QuestionToken,
            NameExpressionSyntax n => n.IdentifierToken,
            LiteralExpressionSyntax l => l.LiteralToken,
            ArrayLiteralExpressionSyntax a => a.OpenBracketToken,
            _ => throw new InvalidOperationException("No anchor token for expression kind.")
        };

        private void Report(string id, string msg, SyntaxToken at) => _diagnostics.Report(id, msg, at.Position, at.Text.Length);
    }
}
