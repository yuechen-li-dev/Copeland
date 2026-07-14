using Copeland.TS.Diagnostics;

namespace Copeland.TS.Syntax;

public sealed class Parser
{
    private readonly SyntaxToken[] _tokens;
    private readonly DiagnosticBag _diagnostics = new();
    private int _position;

    public Parser(string text)
    {
        var lexer = new Lexer(text);
        var tokens = new List<SyntaxToken>();

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind != SyntaxKind.BadToken)
            {
                tokens.Add(token);
            }

            if (token.Kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }

        _tokens = [.. tokens];

        foreach (var diagnostic in lexer.Diagnostics)
        {
            _diagnostics.Report(diagnostic.Id, diagnostic.Message, diagnostic.Position, diagnostic.Length);
        }
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.Diagnostics;

    public CompilationUnitSyntax ParseCompilationUnit()
    {
        var members = new List<MemberSyntax>();

        while (Current.Kind != SyntaxKind.EndOfFileToken)
        {
            var startToken = Current;
            var member = ParseMember();
            members.Add(member);

            if (Current == startToken)
            {
                ReportUnexpectedToken(Current);
                NextToken();
            }
        }

        var endOfFileToken = Match(SyntaxKind.EndOfFileToken);
        return new CompilationUnitSyntax(members, endOfFileToken);
    }

    private MemberSyntax ParseMember()
    {
        if (Current.Kind == SyntaxKind.ConstKeyword && Peek(1).Kind == SyntaxKind.RecordKeyword)
        {
            var constKeyword = Match(SyntaxKind.ConstKeyword);
            return ParseRecordDeclaration(constKeyword);
        }
        if (Current.Kind == SyntaxKind.FunctionKeyword)
        {
            return ParseFunctionDeclaration();
        }
        if (Current.Kind == SyntaxKind.EnumKeyword)
        {
            return ParseEnumDeclaration();
        }
        if (Current.Kind == SyntaxKind.RecordKeyword)
        {
            return ParseRecordDeclaration(null);
        }

        return new GlobalStatementMemberSyntax(ParseStatement());
    }

    private FunctionDeclarationSyntax ParseFunctionDeclaration()
    {
        var functionKeyword = Match(SyntaxKind.FunctionKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var openParenToken = Match(SyntaxKind.OpenParenToken);

        var parameters = new List<ParameterSyntax>();
        var commas = new List<SyntaxToken>();
        while (Current.Kind != SyntaxKind.CloseParenToken && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            var parameterIdentifier = Match(SyntaxKind.IdentifierToken);
            SyntaxToken? parameterColon = null;
            TypeSyntax? parameterType = null;
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                parameterColon = Match(SyntaxKind.ColonToken);
                parameterType = ParseTypeSyntax();
            }

            parameters.Add(new ParameterSyntax(parameterIdentifier, parameterColon, parameterType));

            if (Current.Kind != SyntaxKind.CommaToken)
            {
                break;
            }

            commas.Add(Match(SyntaxKind.CommaToken));
        }

        var closeParenToken = Match(SyntaxKind.CloseParenToken);
        SyntaxToken? returnTypeColonToken = null;
        TypeSyntax? returnType = null;
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            returnTypeColonToken = Match(SyntaxKind.ColonToken);
            returnType = ParseTypeSyntax();
        }
        var body = ParseBlockStatement();
        return new FunctionDeclarationSyntax(functionKeyword, identifier, openParenToken, parameters, commas, closeParenToken, returnTypeColonToken, returnType, body);
    }

    private EnumDeclarationSyntax ParseEnumDeclaration()
    {
        var enumKeyword = Match(SyntaxKind.EnumKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var openBraceToken = Match(SyntaxKind.OpenBraceToken);
        var cases = new List<EnumCaseSyntax>();

        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            var startToken = Current;
            cases.Add(ParseEnumCase());
            if (Current == startToken)
            {
                ReportUnexpectedToken(Current);
                NextToken();
            }
        }

        var closeBraceToken = Match(SyntaxKind.CloseBraceToken);
        return new EnumDeclarationSyntax(enumKeyword, identifier, openBraceToken, cases, closeBraceToken);
    }

    private EnumCaseSyntax ParseEnumCase()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? openParenToken = null;
        var payloadFields = new List<EnumPayloadFieldSyntax>();
        SyntaxToken? closeParenToken = null;

        if (Current.Kind == SyntaxKind.OpenParenToken)
        {
            openParenToken = Match(SyntaxKind.OpenParenToken);
            while (Current.Kind is not SyntaxKind.CloseParenToken and not SyntaxKind.EndOfFileToken)
            {
                var startToken = Current;
                payloadFields.Add(ParseEnumPayloadField());
                if (Current == startToken)
                {
                    ReportUnexpectedToken(Current);
                    NextToken();
                }

                if (Current.Kind == SyntaxKind.CloseParenToken)
                {
                    break;
                }
            }

            closeParenToken = Match(SyntaxKind.CloseParenToken);
        }

        SyntaxToken? commaToken = null;
        if (Current.Kind == SyntaxKind.CommaToken)
        {
            commaToken = Match(SyntaxKind.CommaToken);
        }

        return new EnumCaseSyntax(identifier, openParenToken, payloadFields, closeParenToken, commaToken);
    }

    private EnumPayloadFieldSyntax ParseEnumPayloadField()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        var colonToken = Match(SyntaxKind.ColonToken);
        var type = ParseTypeSyntax();
        SyntaxToken? commaToken = null;
        if (Current.Kind == SyntaxKind.CommaToken)
        {
            commaToken = Match(SyntaxKind.CommaToken);
        }

        return new EnumPayloadFieldSyntax(identifier, colonToken, type, commaToken);
    }

    private StatementSyntax ParseStatement()
        => Current.Kind switch
        {
            SyntaxKind.OpenBraceToken => ParseBlockStatement(),
            SyntaxKind.ConstKeyword or SyntaxKind.LetKeyword or SyntaxKind.VarKeyword => ParseVariableDeclarationStatement(requireSemicolon: true),
            SyntaxKind.IfKeyword => ParseIfStatement(),
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.ForKeyword => ParseForStatement(),
            SyntaxKind.ReturnKeyword => ParseReturnStatement(),
            SyntaxKind.RecordKeyword => new NestedRecordDeclarationStatementSyntax(ParseRecordDeclaration(null)),
            _ => ParseExpressionStatementOrRecovery(),
        };

    private StatementSyntax ParseExpressionStatementOrRecovery()
    {
        if (Current.Kind is SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken)
        {
            _diagnostics.Report("COPE-PARSE-0003", "Expected statement.", Current.Position, 0);
            return new ExpressionStatementSyntax(new MissingExpressionSyntax(Match(SyntaxKind.IdentifierToken)), MissingToken(SyntaxKind.SemicolonToken, Current.Position));
        }

        return ParseExpressionStatement();
    }

    private BlockStatementSyntax ParseBlockStatement()
    {
        var openBraceToken = Match(SyntaxKind.OpenBraceToken);
        var statements = new List<StatementSyntax>();

        while (Current.Kind != SyntaxKind.CloseBraceToken && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            var startToken = Current;
            statements.Add(ParseStatement());

            if (Current == startToken)
            {
                ReportUnexpectedToken(Current);
                NextToken();
            }
        }

        var closeBraceToken = Match(SyntaxKind.CloseBraceToken);
        return new BlockStatementSyntax(openBraceToken, statements, closeBraceToken);
    }

    private VariableDeclarationStatementSyntax ParseVariableDeclarationStatement(bool requireSemicolon)
    {
        var keyword = Current.Kind switch
        {
            SyntaxKind.ConstKeyword => Match(SyntaxKind.ConstKeyword),
            SyntaxKind.LetKeyword => Match(SyntaxKind.LetKeyword),
            SyntaxKind.VarKeyword => Match(SyntaxKind.VarKeyword),
            _ => throw new InvalidOperationException("Expected a variable declaration keyword."),
        };
        var identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? typeColonToken = null;
        TypeSyntax? type = null;
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            typeColonToken = Match(SyntaxKind.ColonToken);
            type = ParseTypeSyntax();
        }

        var equalsToken = Match(SyntaxKind.EqualsToken);
        var initializer = ParseExpression();
        var semicolonToken = requireSemicolon ? Match(SyntaxKind.SemicolonToken) : MissingToken(SyntaxKind.SemicolonToken, Current.Position);
        return new VariableDeclarationStatementSyntax(keyword, identifier, typeColonToken, type, equalsToken, initializer, semicolonToken);
    }

    private TypeSyntax ParseTypeSyntax()
    {
        var type = ParsePostfixTypeSyntax();
        if (Current.Kind != SyntaxKind.BangToken)
        {
            return type;
        }

        var bangToken = Match(SyntaxKind.BangToken);
        var errorType = ParseTypeSyntax();
        return new ResultTypeSyntax(type, bangToken, errorType);
    }

    private RecordDeclarationSyntax ParseRecordDeclaration(SyntaxToken? constKeyword)
    {
        var recordKeyword = Match(SyntaxKind.RecordKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var openBraceToken = Match(SyntaxKind.OpenBraceToken);
        var fields = new List<RecordFieldSyntax>();

        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            var fieldIdentifier = Match(SyntaxKind.IdentifierToken);
            var hasColon = Current.Kind == SyntaxKind.ColonToken;
            var colonToken = hasColon
                ? NextToken()
                : MissingToken(SyntaxKind.ColonToken, Current.Position);
            TypeSyntax type;
            var hasExplicitType = hasColon && Current.Kind != SyntaxKind.SemicolonToken;
            if (!hasExplicitType)
            {
                type = new IdentifierTypeSyntax(MissingToken(SyntaxKind.IdentifierToken, Current.Position));
            }
            else
            {
                type = ParseTypeSyntax();
            }

            var unsupportedTokens = new List<SyntaxToken>();
            while (Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
            {
                unsupportedTokens.Add(NextToken());
            }
            var hasTerminator = Current.Kind == SyntaxKind.SemicolonToken;
            var semicolonToken = hasTerminator
                ? NextToken()
                : MissingToken(SyntaxKind.SemicolonToken, Current.Position);
            fields.Add(new RecordFieldSyntax(fieldIdentifier, colonToken, type, unsupportedTokens, semicolonToken, hasExplicitType, hasTerminator));
        }

        var closeBraceToken = Match(SyntaxKind.CloseBraceToken);
        return new RecordDeclarationSyntax(constKeyword, recordKeyword, identifier, openBraceToken, fields, closeBraceToken);
    }

    private TypeSyntax ParsePostfixTypeSyntax()
    {
        TypeSyntax type = Current.Kind switch
        {
            SyntaxKind.NumberKeyword or SyntaxKind.StringKeyword or SyntaxKind.BooleanKeyword or SyntaxKind.VoidKeyword or SyntaxKind.NullKeyword
                => new PredefinedTypeSyntax(NextToken()),
            SyntaxKind.IdentifierToken
                => new IdentifierTypeSyntax(NextToken()),
            SyntaxKind.OpenParenToken
                => ParseParenthesizedTypeSyntax(),
            _ => ParseMissingTypeSyntax(),
        };

        while (Current.Kind == SyntaxKind.OpenBracketToken)
        {
            var openBracketToken = Match(SyntaxKind.OpenBracketToken);
            SyntaxToken closeBracketToken;
            if (Current.Kind == SyntaxKind.CloseBracketToken)
            {
                closeBracketToken = Match(SyntaxKind.CloseBracketToken);
            }
            else
            {
                _diagnostics.Report("COPE-PARSE-0008", "Expected ']' in array type.", Current.Position, 0);
                closeBracketToken = MissingToken(SyntaxKind.CloseBracketToken, Current.Position);
            }

            type = new ArrayTypeSyntax(type, openBracketToken, closeBracketToken);
        }

        return type;
    }

    private ParenthesizedTypeSyntax ParseParenthesizedTypeSyntax()
    {
        var openParenToken = Match(SyntaxKind.OpenParenToken);
        var type = ParseTypeSyntax();
        var closeParenToken = Match(SyntaxKind.CloseParenToken);
        return new ParenthesizedTypeSyntax(openParenToken, type, closeParenToken);
    }

    private TypeSyntax ParseMissingTypeSyntax()
    {
        _diagnostics.Report("COPE-PARSE-0006", "Expected type.", Current.Position, 0);
        return new IdentifierTypeSyntax(MissingToken(SyntaxKind.IdentifierToken, Current.Position));
    }

    private IfStatementSyntax ParseIfStatement()
    {
        var ifKeyword = Match(SyntaxKind.IfKeyword);
        var openParenToken = Match(SyntaxKind.OpenParenToken);
        var condition = ParseExpression();
        var closeParenToken = Match(SyntaxKind.CloseParenToken);
        var thenStatement = ParseStatement();

        SyntaxToken? elseKeyword = null;
        StatementSyntax? elseStatement = null;
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = Match(SyntaxKind.ElseKeyword);
            elseStatement = ParseStatement();
        }

        return new IfStatementSyntax(ifKeyword, openParenToken, condition, closeParenToken, thenStatement, elseKeyword, elseStatement);
    }

    private WhileStatementSyntax ParseWhileStatement()
    {
        var whileKeyword = Match(SyntaxKind.WhileKeyword);
        var openParenToken = Match(SyntaxKind.OpenParenToken);
        var condition = ParseExpression();
        var closeParenToken = Match(SyntaxKind.CloseParenToken);
        var body = ParseStatement();
        return new WhileStatementSyntax(whileKeyword, openParenToken, condition, closeParenToken, body);
    }

    private ForStatementSyntax ParseForStatement()
    {
        var forKeyword = Match(SyntaxKind.ForKeyword);
        var openParenToken = Match(SyntaxKind.OpenParenToken);

        SyntaxNode? initializer = null;
        if (Current.Kind != SyntaxKind.SemicolonToken)
        {
            if (Current.Kind is SyntaxKind.ConstKeyword or SyntaxKind.LetKeyword or SyntaxKind.VarKeyword)
            {
                initializer = ParseVariableDeclarationStatement(requireSemicolon: false);
            }
            else
            {
                initializer = ParseExpression();
            }
        }

        var firstSemicolonToken = Match(SyntaxKind.SemicolonToken);

        ExpressionSyntax? condition = null;
        if (Current.Kind != SyntaxKind.SemicolonToken)
        {
            condition = ParseExpression();
        }

        var secondSemicolonToken = Match(SyntaxKind.SemicolonToken);

        ExpressionSyntax? increment = null;
        if (Current.Kind != SyntaxKind.CloseParenToken)
        {
            increment = ParseExpression();
        }

        var closeParenToken = Match(SyntaxKind.CloseParenToken);
        var body = ParseStatement();

        return new ForStatementSyntax(forKeyword, openParenToken, initializer, firstSemicolonToken, condition, secondSemicolonToken, increment, closeParenToken, body);
    }

    private ReturnStatementSyntax ParseReturnStatement()
    {
        var returnKeyword = Match(SyntaxKind.ReturnKeyword);

        ExpressionSyntax? expression = null;
        if (Current.Kind != SyntaxKind.SemicolonToken)
        {
            expression = ParseExpression();
        }

        var semicolonToken = Match(SyntaxKind.SemicolonToken);
        return new ReturnStatementSyntax(returnKeyword, expression, semicolonToken);
    }

    private ExpressionStatementSyntax ParseExpressionStatement()
    {
        var expression = ParseExpression();
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new ExpressionStatementSyntax(expression, semicolon);
    }

    private ExpressionSyntax ParseExpression()
        => ParseAssignmentExpression();

    private ExpressionSyntax ParseAssignmentExpression()
    {
        var left = ParseBinaryExpression();

        if (Current.Kind == SyntaxKind.EqualsToken)
        {
            var equalsToken = Match(SyntaxKind.EqualsToken);
            if (left is not NameExpressionSyntax and not MemberAccessExpressionSyntax)
            {
                _diagnostics.Report("COPE-PARSE-0005", "Invalid assignment target.", left is MissingExpressionSyntax ? equalsToken.Position : equalsToken.Position - 1, 1);
            }

            var right = ParseAssignmentExpression();
            return new AssignmentExpressionSyntax(left, equalsToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseBinaryExpression(int parentPrecedence = 0)
    {
        ExpressionSyntax left;

        var unaryPrecedence = SyntaxFacts.GetUnaryOperatorPrecedence(Current.Kind);
        if (unaryPrecedence != 0 && unaryPrecedence >= parentPrecedence)
        {
            var operatorToken = NextToken();
            var operand = ParseBinaryExpression(unaryPrecedence);
            left = new UnaryExpressionSyntax(operatorToken, operand);
        }
        else
        {
            left = ParsePostfixExpression();
        }

        while (true)
        {
            var precedence = SyntaxFacts.GetBinaryOperatorPrecedence(Current.Kind);
            if (precedence == 0 || precedence <= parentPrecedence)
            {
                break;
            }

            var operatorToken = NextToken();
            var right = ParseBinaryExpression(precedence);
            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParsePostfixExpression()
    {
        var expression = ParsePrimaryExpression();

        while (true)
        {
            if (Current.Kind == SyntaxKind.OpenParenToken)
            {
                expression = ParseCallExpression(expression);
                continue;
            }

            if (Current.Kind == SyntaxKind.DotToken)
            {
                var dot = Match(SyntaxKind.DotToken);
                var name = Match(SyntaxKind.IdentifierToken);
                expression = new MemberAccessExpressionSyntax(expression, dot, name);
                continue;
            }
            if (Current.Kind == SyntaxKind.QuestionToken)
            {
                var questionToken = Match(SyntaxKind.QuestionToken);
                if (Current.Kind == SyntaxKind.DotToken)
                {
                    _diagnostics.Report("COPE-PROFILE-0008", "Optional chaining is not supported. Use explicit fallible APIs or enum/option modeling.", questionToken.Position, 2);
                    var dot = Match(SyntaxKind.DotToken);
                    var _ = Match(SyntaxKind.IdentifierToken);
                    expression = new MissingExpressionSyntax(dot);
                    continue;
                }

                if (Current.Kind != SyntaxKind.SemicolonToken && Current.Kind != SyntaxKind.CloseParenToken && Current.Kind != SyntaxKind.CommaToken && Current.Kind != SyntaxKind.CloseBraceToken && Current.Kind != SyntaxKind.BangToken)
                {
                    _diagnostics.Report("COPE-PROFILE-0007", "The ternary operator is not supported. Use if/else expressions.", questionToken.Position, 1);
                }

                expression = new PropagateExpressionSyntax(expression, questionToken);
                continue;
            }

            if (Current.Kind == SyntaxKind.BangToken)
            {
                expression = new UnwrapExpressionSyntax(expression, Match(SyntaxKind.BangToken));
                continue;
            }

            if (Current.Kind == SyntaxKind.WithKeyword)
            {
                var withKeyword = Match(SyntaxKind.WithKeyword);
                var replacements = ParseObjectLiteralExpression();
                expression = new WithExpressionSyntax(expression, withKeyword, replacements);
                continue;
            }

            break;
        }

        return expression;
    }

    private CallExpressionSyntax ParseCallExpression(ExpressionSyntax target)
    {
        var openParenToken = Match(SyntaxKind.OpenParenToken);
        var arguments = new List<ExpressionSyntax>();
        var commas = new List<SyntaxToken>();

        while (Current.Kind != SyntaxKind.CloseParenToken && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            arguments.Add(ParseExpression());
            if (Current.Kind != SyntaxKind.CommaToken)
            {
                break;
            }

            commas.Add(Match(SyntaxKind.CommaToken));
        }

        var closeParenToken = Match(SyntaxKind.CloseParenToken);
        return new CallExpressionSyntax(target, openParenToken, arguments, commas, closeParenToken);
    }

    private ExpressionSyntax ParsePrimaryExpression()
        => Current.Kind switch
        {
            SyntaxKind.OpenParenToken => ParseParenthesizedExpression(),
            SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword or SyntaxKind.NumberToken or SyntaxKind.StringToken => new LiteralExpressionSyntax(NextToken()),
            SyntaxKind.IdentifierToken => new NameExpressionSyntax(NextToken()),
            SyntaxKind.OpenBracketToken => ParseArrayLiteralExpression(),
            SyntaxKind.OpenBraceToken => ParseObjectLiteralExpression(),
            SyntaxKind.MatchKeyword => ParseMatchExpression(),
            SyntaxKind.IfKeyword => ParseIfExpression(),
            SyntaxKind.TryKeyword => ParseTryExceptExpression(),
            _ => ParseMissingExpression(),
        };

    private TryExceptExpressionSyntax ParseTryExceptExpression()
    {
        var tryKeyword = Match(SyntaxKind.TryKeyword);
        var protectedBlock = ParseTryValueBlock();
        var exceptKeyword = MatchTryToken(SyntaxKind.ExceptKeyword, "Expected 'except' after the protected try value block.");
        var openParen = MatchTryToken(SyntaxKind.OpenParenToken, "Expected '(' after 'except'.");
        var binding = MatchTryToken(SyntaxKind.IdentifierToken, "Expected exactly one handler binding name in 'except (...)'.");
        var closeParen = MatchTryToken(SyntaxKind.CloseParenToken, "Expected ')' after the handler binding name.");
        var handlerBlock = ParseTryValueBlock();
        return new TryExceptExpressionSyntax(tryKeyword, protectedBlock, exceptKeyword, openParen, binding, closeParen, handlerBlock);
    }

    private TryValueBlockSyntax ParseTryValueBlock()
    {
        var openBrace = MatchTryToken(SyntaxKind.OpenBraceToken, "Expected '{' to begin a try value block.");
        var prefixStatements = new List<StatementSyntax>();

        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            if (Current.Kind is SyntaxKind.ReturnKeyword
                or SyntaxKind.IfKeyword
                or SyntaxKind.WhileKeyword
                or SyntaxKind.ForKeyword
                or SyntaxKind.BreakKeyword
                or SyntaxKind.ContinueKeyword
                or SyntaxKind.OpenBraceToken)
            {
                _diagnostics.Report("COPE-TRY-0005", "Try value blocks support only declarations, expression statements, and one final expression.", Current.Position, Math.Max(1, Current.Text.Length));
                prefixStatements.Add(ParseStatement());
                continue;
            }

            if (Current.Kind is SyntaxKind.ConstKeyword or SyntaxKind.LetKeyword)
            {
                prefixStatements.Add(ParseVariableDeclarationStatement(requireSemicolon: true));
                continue;
            }

            var expression = ParseExpression();
            if (Current.Kind == SyntaxKind.SemicolonToken)
            {
                prefixStatements.Add(new ExpressionStatementSyntax(expression, Match(SyntaxKind.SemicolonToken)));
                continue;
            }

            var closeBrace = MatchTryToken(SyntaxKind.CloseBraceToken, "Expected '}' after the final try value expression.");
            return new TryValueBlockSyntax(openBrace, prefixStatements, expression, closeBrace);
        }

        _diagnostics.Report("COPE-TRY-0001", "Try value blocks require one final expression without a semicolon.", openBrace.Position, Math.Max(1, openBrace.Text.Length));
        var missing = new MissingExpressionSyntax(openBrace);
        var close = MatchTryToken(SyntaxKind.CloseBraceToken, "Expected '}' to close a try value block.");
        return new TryValueBlockSyntax(openBrace, prefixStatements, missing, close);
    }

    private SyntaxToken MatchTryToken(SyntaxKind kind, string message)
    {
        if (Current.Kind == kind)
        {
            return NextToken();
        }

        _diagnostics.Report("COPE-TRY-0001", message, Current.Position, Math.Max(1, Current.Text.Length));
        return MissingToken(kind, Current.Position);
    }


    private IfExpressionSyntax ParseIfExpression()
    {
        var ifKeyword = Match(SyntaxKind.IfKeyword);
        var condition = ParseExpression();
        var thenOpen = Match(SyntaxKind.OpenBraceToken);
        var thenExpression = ParseExpression();
        var thenClose = Match(SyntaxKind.CloseBraceToken);
        var elseKeyword = Match(SyntaxKind.ElseKeyword);
        var elseOpen = Match(SyntaxKind.OpenBraceToken);
        var elseExpression = ParseExpression();
        var elseClose = Match(SyntaxKind.CloseBraceToken);
        return new IfExpressionSyntax(ifKeyword, condition, thenOpen, thenExpression, thenClose, elseKeyword, elseOpen, elseExpression, elseClose);
    }

    private ParenthesizedExpressionSyntax ParseParenthesizedExpression()
    {
        var openParen = Match(SyntaxKind.OpenParenToken);
        var expression = ParseExpression();
        var closeParen = Match(SyntaxKind.CloseParenToken);
        return new ParenthesizedExpressionSyntax(openParen, expression, closeParen);
    }

    private ArrayLiteralExpressionSyntax ParseArrayLiteralExpression()
    {
        var openBracket = Match(SyntaxKind.OpenBracketToken);
        var elements = new List<ExpressionSyntax>();
        var commas = new List<SyntaxToken>();

        while (Current.Kind != SyntaxKind.CloseBracketToken && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            elements.Add(ParseExpression());
            if (Current.Kind != SyntaxKind.CommaToken)
            {
                break;
            }

            commas.Add(Match(SyntaxKind.CommaToken));
        }

        var closeBracket = Match(SyntaxKind.CloseBracketToken);
        return new ArrayLiteralExpressionSyntax(openBracket, elements, commas, closeBracket);
    }

    private ObjectLiteralExpressionSyntax ParseObjectLiteralExpression()
    {
        var openBrace = Match(SyntaxKind.OpenBraceToken);
        var properties = new List<ObjectPropertySyntax>();
        var commas = new List<SyntaxToken>();

        while (Current.Kind != SyntaxKind.CloseBraceToken && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            var name = Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.StringToken
                ? NextToken()
                : Match(SyntaxKind.IdentifierToken);
            var colon = Match(SyntaxKind.ColonToken);
            var value = ParseExpression();
            properties.Add(new ObjectPropertySyntax(name, colon, value));

            if (Current.Kind != SyntaxKind.CommaToken)
            {
                break;
            }

            commas.Add(Match(SyntaxKind.CommaToken));
        }

        var closeBrace = Match(SyntaxKind.CloseBraceToken);
        return new ObjectLiteralExpressionSyntax(openBrace, properties, commas, closeBrace);
    }

    private MatchExpressionSyntax ParseMatchExpression()
    {
        var matchKeyword = Match(SyntaxKind.MatchKeyword);
        var scrutinee = ParseExpression();
        var openBraceToken = Match(SyntaxKind.OpenBraceToken);
        var arms = new List<MatchArmSyntax>();

        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            var startToken = Current;
            arms.Add(ParseMatchArm());
            if (Current == startToken)
            {
                ReportUnexpectedToken(Current);
                NextToken();
            }
        }

        var closeBraceToken = Match(SyntaxKind.CloseBraceToken);
        return new MatchExpressionSyntax(matchKeyword, scrutinee, openBraceToken, arms, closeBraceToken);
    }

    private MatchArmSyntax ParseMatchArm()
    {
        var pattern = ParseMatchPattern();
        var arrow = Match(SyntaxKind.ArrowToken);
        var expression = ParseExpression();
        SyntaxToken? comma = null;
        if (Current.Kind == SyntaxKind.CommaToken)
        {
            comma = Match(SyntaxKind.CommaToken);
        }

        return new MatchArmSyntax(pattern, arrow, expression, comma);
    }

    private MatchPatternSyntax ParseMatchPattern()
    {
        var caseIdentifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? openParen = null;
        var payloadIdentifiers = new List<SyntaxToken>();
        var commas = new List<SyntaxToken>();
        SyntaxToken? closeParen = null;

        if (Current.Kind == SyntaxKind.OpenParenToken)
        {
            openParen = Match(SyntaxKind.OpenParenToken);
            while (Current.Kind is not SyntaxKind.CloseParenToken and not SyntaxKind.EndOfFileToken)
            {
                payloadIdentifiers.Add(Match(SyntaxKind.IdentifierToken));
                if (Current.Kind != SyntaxKind.CommaToken)
                {
                    break;
                }

                commas.Add(Match(SyntaxKind.CommaToken));
            }
            closeParen = Match(SyntaxKind.CloseParenToken);
        }

        return new MatchPatternSyntax(caseIdentifier, openParen, payloadIdentifiers, commas, closeParen);
    }

    private MissingExpressionSyntax ParseMissingExpression()
    {
        _diagnostics.Report("COPE-PARSE-0002", "Expected expression.", Current.Position, 0);
        return new MissingExpressionSyntax(MissingToken(SyntaxKind.IdentifierToken, Current.Position));
    }

    private SyntaxToken Match(SyntaxKind kind)
    {
        if (Current.Kind == kind)
        {
            return NextToken();
        }

        _diagnostics.Report("COPE-PARSE-0004", $"Expected token '{SyntaxFacts.GetText(kind) ?? kind.ToString()}'.", Current.Position, 0);
        return MissingToken(kind, Current.Position);
    }

    private void ReportUnexpectedToken(SyntaxToken token)
    {
        _diagnostics.Report("COPE-PARSE-0001", $"Unexpected token '{token.Kind}'.", token.Position, Math.Max(1, token.Text.Length));
    }

    private SyntaxToken MissingToken(SyntaxKind kind, int position)
        => new(kind, position, SyntaxFacts.GetText(kind) ?? string.Empty, null);

    private SyntaxToken Current => Peek(0);

    private SyntaxToken Peek(int offset)
    {
        var index = _position + offset;
        if (index >= _tokens.Length)
        {
            return _tokens[^1];
        }

        return _tokens[index];
    }

    private SyntaxToken NextToken()
    {
        var current = Current;
        _position++;
        return current;
    }
}
