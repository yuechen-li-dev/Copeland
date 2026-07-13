using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

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
        private readonly List<BoundEnumDeclaration> _enums = [];
        private readonly List<BoundStatement> _globals = [];
        private readonly Dictionary<string, EnumTypeSymbol> _enumTypes = new(StringComparer.Ordinal);

        public BoundCompilation Bind()
        {
            _scope = _global;
            PredeclareEnums(_tree.Root);
            PredeclareFunctions(_tree.Root);
            BindEnumBodies(_tree.Root);
            foreach (var m in _tree.Root.Members)
            {
                if (m is FunctionDeclarationSyntax f) _functions.Add(BindFunction(f));
                else if (m is EnumDeclarationSyntax e) _enums.Add(new BoundEnumDeclaration(_enumTypes[e.Identifier.Text]));
                else if (m is GlobalStatementMemberSyntax g) _globals.Add(BindStatement(g.Statement));
            }
            return new BoundCompilation(_tree, new BoundProgram(_functions, _enums, _globals), _tree.Diagnostics.Concat(_diagnostics.Diagnostics).ToArray());
        }

        private void PredeclareFunctions(CompilationUnitSyntax root)
        {
            foreach (var m in root.Members.OfType<FunctionDeclarationSyntax>())
            {
                var ps = new List<ParameterSymbol>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in m.Parameters)
                {
                    var pt = BindType(p.Type, p.Identifier, missingId: "COPE-TYPE-0002", missingPrefix: "parameter");
                    if (!seen.Add(p.Identifier.Text)) Report("COPE-BIND-0005", $"Duplicate parameter '{p.Identifier.Text}'.", p.Identifier);
                    ps.Add(new ParameterSymbol(p.Identifier.Text, pt));
                }
                var rt = BindType(m.ReturnType, m.Identifier, missingId: "COPE-TYPE-0002", missingPrefix: "function return");
                var et = BindErrorType(m.ErrorType);
                var fn = new FunctionSymbol(m.Identifier.Text, ps, rt, et);
                if (_enumTypes.ContainsKey(fn.Name))
                {
                    Report("COPE-ENUM-0009", $"Name '{fn.Name}' is already used by an enum.", m.Identifier);
                    continue;
                }
                if (!_global.TryDeclare(fn)) Report("COPE-BIND-0002", $"Duplicate declaration '{fn.Name}'.", m.Identifier);
            }
        }
        private void PredeclareEnums(CompilationUnitSyntax root)
        {
            foreach (var m in root.Members.OfType<EnumDeclarationSyntax>())
            {
                var enumType = new EnumTypeSymbol(m.Identifier.Text);
                if (!_global.TryDeclare(new VariableSymbol(m.Identifier.Text, enumType, true)) || _enumTypes.ContainsKey(m.Identifier.Text))
                {
                    Report("COPE-ENUM-0001", $"Duplicate enum declaration '{m.Identifier.Text}'.", m.Identifier);
                    continue;
                }
                _enumTypes[m.Identifier.Text] = enumType;
            }
        }

        private void BindEnumBodies(CompilationUnitSyntax root)
        {
            foreach (var decl in root.Members.OfType<EnumDeclarationSyntax>())
            {
                if (!_enumTypes.TryGetValue(decl.Identifier.Text, out var enumType))
                    continue;
                var seenCases = new HashSet<string>(StringComparer.Ordinal);
                foreach (var @case in decl.Cases)
                {
                    if (!seenCases.Add(@case.Identifier.Text))
                    {
                        Report("COPE-ENUM-0002", $"Duplicate enum case '{@case.Identifier.Text}' in enum '{enumType.Name}'.", @case.Identifier);
                        continue;
                    }
                    var seenPayload = new HashSet<string>(StringComparer.Ordinal);
                    var payloadFields = new List<EnumPayloadFieldSymbol>();
                    foreach (var field in @case.PayloadFields)
                    {
                        if (!seenPayload.Add(field.Identifier.Text))
                        {
                            Report("COPE-ENUM-0003", $"Duplicate payload field '{field.Identifier.Text}' in enum case '{@case.Identifier.Text}'.", field.Identifier);
                            continue;
                        }
                        payloadFields.Add(new EnumPayloadFieldSymbol(field.Identifier.Text, BindType(field.Type, field.Identifier, "COPE-TYPE-0002", "enum payload")));
                    }
                    enumType.AddCase(new EnumCaseSymbol(@case.Identifier.Text, enumType, payloadFields));
                }
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
                IfExpressionSyntax i => BindIfExpression(i),
                MatchExpressionSyntax m => BindMatch(m),
                _ => new BoundErrorExpression()
            };

            if (!allowUnhandledFallible && expression is BoundCallExpression call && call.IsFallible && s is not PropagateExpressionSyntax)
                Report("COPE-TYPE-0013", $"Fallible call to '{call.Function.Name}' must be handled or propagated with '?'.", AnchorToken(s));

            return expression;
        }


        private BoundExpression BindIfExpression(IfExpressionSyntax ifExpression)
        {
            var condition = BindExpression(ifExpression.Condition);
            if (condition.Type != PrimitiveTypeSymbol.Boolean)
                Report("COPE-TYPE-0017", $"If expression condition must be 'boolean', got '{condition.Type.Name}'.", ifExpression.IfKeyword);

            var thenExpression = BindExpression(ifExpression.ThenExpression, allowUnhandledFallible: true);
            var elseExpression = BindExpression(ifExpression.ElseExpression, allowUnhandledFallible: true);

            if (thenExpression.Type.Name != elseExpression.Type.Name)
            {
                Report("COPE-TYPE-0018", $"If expression branch type mismatch: expected '{thenExpression.Type.Name}', got '{elseExpression.Type.Name}'.", ifExpression.ElseKeyword);
                return new BoundErrorExpression();
            }

            return new BoundIfExpression(condition, thenExpression, elseExpression, thenExpression.Type);
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
            if (op is SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.BangEqualsEqualsToken)
            {
                Report("COPE-PROFILE-0009", $"Strict equality spelling '{b.OperatorToken.Text}' is reserved and not supported. Use typed '{(op == SyntaxKind.EqualsEqualsEqualsToken ? "==" : "!=")}' equality.", b.OperatorToken);
                return new BoundErrorExpression();
            }
            if (op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken)
            {
                if (l.Type == r.Type && IsPrimitiveEqualityType(l.Type))
                {
                    return new BoundBinaryExpression(l, op, r, PrimitiveTypeSymbol.Boolean);
                }
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

            if (c.Target is MemberAccessExpressionSyntax m && m.Target is NameExpressionSyntax enumName)
            {
                return BindEnumConstructorCall(c, m, enumName);
            }

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
        private BoundExpression BindMatch(MatchExpressionSyntax match)
        {
            var scrutinee = BindExpression(match.Expression);
            if (scrutinee.Type is not EnumTypeSymbol enumType)
            {
                Report("COPE-MATCH-0001", "Match expression requires an enum value.", match.MatchKeyword);
                return new BoundErrorExpression();
            }

            var boundArms = new List<BoundMatchArm>();
            var seenCases = new HashSet<string>(StringComparer.Ordinal);
            TypeSymbol? expectedArmType = null;

            foreach (var arm in match.Arms)
            {
                var caseName = arm.Pattern.CaseIdentifier.Text;
                var enumCase = enumType.Cases.FirstOrDefault(c => c.Name == caseName);
                if (enumCase is null)
                {
                    Report("COPE-MATCH-0002", $"Enum '{enumType.Name}' has no case '{caseName}'.", arm.Pattern.CaseIdentifier);
                    continue;
                }

                if (!seenCases.Add(caseName))
                {
                    Report("COPE-MATCH-0003", $"Duplicate match arm for case '{caseName}'.", arm.Pattern.CaseIdentifier);
                }

                var payloadCount = arm.Pattern.PayloadIdentifiers.Count;
                if (payloadCount != enumCase.PayloadFields.Count)
                {
                    Report("COPE-MATCH-0005", $"Match arm for case '{caseName}' expects {enumCase.PayloadFields.Count} payload values, got {payloadCount}.", arm.Pattern.CaseIdentifier);
                }

                var prevScope = _scope;
                _scope = new Scope(prevScope);
                var payloadVars = new List<VariableSymbol>();
                var seenPayload = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < Math.Min(payloadCount, enumCase.PayloadFields.Count); i++)
                {
                    var payloadIdentifier = arm.Pattern.PayloadIdentifiers[i];
                    if (!seenPayload.Add(payloadIdentifier.Text))
                    {
                        Report("COPE-MATCH-0006", $"Duplicate payload variable '{payloadIdentifier.Text}' in match arm for case '{caseName}'.", payloadIdentifier);
                        continue;
                    }

                    var symbol = new VariableSymbol(payloadIdentifier.Text, enumCase.PayloadFields[i].Type, true);
                    _scope.TryDeclare(symbol);
                    payloadVars.Add(symbol);
                }

                var armExpression = BindExpression(arm.Expression);
                _scope = prevScope;

                if (expectedArmType is null && armExpression.Type != PrimitiveTypeSymbol.Error)
                {
                    expectedArmType = armExpression.Type;
                }
                else if (expectedArmType is not null && armExpression.Type != PrimitiveTypeSymbol.Error && !IsAssignable(expectedArmType, armExpression.Type))
                {
                    Report("COPE-MATCH-0007", $"Match arm type mismatch: expected '{expectedArmType.Name}', got '{armExpression.Type.Name}'.", arm.ArrowToken);
                }

                boundArms.Add(new BoundMatchArm(enumCase, payloadVars, armExpression));
            }

            var missingCases = enumType.Cases.Where(c => !seenCases.Contains(c.Name)).Select(c => c.Name).ToArray();
            if (missingCases.Length > 0)
            {
                Report("COPE-MATCH-0004", $"Match expression for enum '{enumType.Name}' is missing cases: {string.Join(", ", missingCases)}.", match.MatchKeyword);
            }

            return new BoundMatchExpression(scrutinee, enumType, boundArms, expectedArmType ?? PrimitiveTypeSymbol.Error);
        }

        private BoundExpression BindMember(MemberAccessExpressionSyntax m)
        {
            if (m.Target is NameExpressionSyntax n && _enumTypes.TryGetValue(n.IdentifierToken.Text, out var enumType))
            {
                var @case = enumType.Cases.FirstOrDefault(c => c.Name == m.NameToken.Text);
                if (@case is null)
                {
                    Report("COPE-ENUM-0004", $"Enum '{enumType.Name}' has no case '{m.NameToken.Text}'.", m.NameToken);
                    return new BoundErrorExpression();
                }
                if (@case.HasPayload)
                {
                    Report("COPE-ENUM-0007", $"Enum case '{enumType.Name}.{@case.Name}' requires arguments.", m.NameToken);
                    return new BoundErrorExpression();
                }
                return new BoundEnumValueExpression(@case, []);
            }
            Report("COPE-TYPE-0012", "Member access is not supported in M0e.", m.DotToken); return new BoundErrorExpression();
        }

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
            if (_enumTypes.TryGetValue(i.Identifier.Text, out var enumType))
                return enumType;
            Report("COPE-BIND-0004", $"Unknown type '{i.Identifier.Text}'.", i.Identifier);
            return PrimitiveTypeSymbol.Error;
        }

        private BoundExpression BindEnumConstructorCall(CallExpressionSyntax call, MemberAccessExpressionSyntax member, NameExpressionSyntax enumName)
        {
            if (!_enumTypes.TryGetValue(enumName.IdentifierToken.Text, out var enumType))
            {
                Report("COPE-ENUM-0010", "Expected enum type name.", enumName.IdentifierToken);
                return new BoundErrorExpression();
            }
            var @case = enumType.Cases.FirstOrDefault(c => c.Name == member.NameToken.Text);
            if (@case is null)
            {
                Report("COPE-ENUM-0004", $"Enum '{enumType.Name}' has no case '{member.NameToken.Text}'.", member.NameToken);
                return new BoundErrorExpression();
            }
            if (!@case.HasPayload)
            {
                Report("COPE-ENUM-0008", $"Enum case '{enumType.Name}.{@case.Name}' does not take arguments.", call.OpenParenToken);
                return new BoundErrorExpression();
            }
            if (call.Arguments.Count != @case.PayloadFields.Count)
                Report("COPE-ENUM-0005", $"Enum case '{enumType.Name}.{@case.Name}' expects {@case.PayloadFields.Count} argument{(@case.PayloadFields.Count == 1 ? "" : "s")}, got {call.Arguments.Count}.", call.OpenParenToken);
            var args = call.Arguments.Select(a => BindExpression(a)).ToArray();
            for (var i = 0; i < Math.Min(args.Length, @case.PayloadFields.Count); i++)
            {
                if (!IsAssignable(@case.PayloadFields[i].Type, args[i].Type))
                    Report("COPE-ENUM-0006", $"Argument {i + 1} for enum case '{enumType.Name}.{@case.Name}' expected '{@case.PayloadFields[i].Type.Name}', got '{args[i].Type.Name}'.", call.OpenParenToken);
            }
            return new BoundEnumValueExpression(@case, args);
        }

        private static bool IsAssignable(TypeSymbol target, TypeSymbol actual)
            => target == PrimitiveTypeSymbol.Error || actual == PrimitiveTypeSymbol.Error || target.Name == actual.Name;

        private static bool IsPrimitiveEqualityType(TypeSymbol type)
            => type == PrimitiveTypeSymbol.Number
                || type == PrimitiveTypeSymbol.String
                || type == PrimitiveTypeSymbol.Boolean;

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
            MatchExpressionSyntax m => m.MatchKeyword,
            _ => throw new InvalidOperationException("No anchor token for expression kind.")
        };

        private void Report(string id, string msg, SyntaxToken at) => _diagnostics.Report(id, msg, at.Position, at.Text.Length);
    }
}
