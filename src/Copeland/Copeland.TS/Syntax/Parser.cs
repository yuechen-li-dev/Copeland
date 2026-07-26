using Copeland.TS.Diagnostics;

namespace Copeland.TS.Syntax;

public sealed class Parser
{
    private readonly string _text;
    private readonly SyntaxToken[] _tokens;
    private readonly IReadOnlyList<Diagnostic> _lexerDiagnostics;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly List<(int Start, int End)> _tsXmlTextRanges = [];
    private readonly List<(int Start, int End)> _csharpBlockRanges = [];
    private readonly bool _allowsTsXml;
    private readonly bool _allowsImports;
    private int _position;

    public Parser(string text, bool allowsTsXml = false, bool allowsImports = false)
    {
        _text = text;
        _allowsTsXml = allowsTsXml;
        _allowsImports = allowsImports;
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

        for (var index = 0; index + 1 < _tokens.Length; index++)
        {
            if (!IsWord(_tokens[index], "csharp") || _tokens[index + 1].Kind != SyntaxKind.OpenBraceToken)
            {
                continue;
            }

            if (CSharpBlockScanner.TryFindClosingBrace(text, _tokens[index + 1].Position, out int closePosition))
            {
                _csharpBlockRanges.Add((_tokens[index + 1].Position, closePosition));
            }
        }

        _lexerDiagnostics = lexer.Diagnostics.ToArray();
    }

    public IReadOnlyList<Diagnostic> Diagnostics
        => _lexerDiagnostics
            .Where(diagnostic => !IsTsXmlTextDiagnostic(diagnostic) && !IsCSharpBlockDiagnostic(diagnostic))
            .Concat(_diagnostics.Diagnostics)
            .ToArray();

    private bool IsCSharpBlockDiagnostic(Diagnostic diagnostic)
        => _csharpBlockRanges.Any(range => diagnostic.Position > range.Start && diagnostic.Position < range.End);

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
        if (IsWord(Current, "using") && IsClrUsingDirectiveAhead())
        {
            return ParseClrUsingDirective();
        }

        if ((_allowsTsXml || _allowsImports) && Current.Kind == SyntaxKind.IdentifierToken && Current.Text == "import")
        {
            return ParseImportDeclaration();
        }

        if (_allowsTsXml
            && Current.Kind == SyntaxKind.IdentifierToken
            && Current.Text == "export"
            && Peek(1).Kind == SyntaxKind.IdentifierToken
            && Peek(1).Text == "default")
        {
            return ParseExportDefaultDeclaration();
        }

        if (Current.Kind == SyntaxKind.IdentifierToken
            && Current.Text == "export"
            && (Peek(1).Kind == SyntaxKind.FunctionKeyword
                || Peek(1).Kind == SyntaxKind.AsyncKeyword && Peek(2).Kind == SyntaxKind.FunctionKeyword))
        {
            _ = NextToken();
            return ParseFunctionDeclaration();
        }

        // Module exports do not change the shape of a declaration. The project
        // compiler records export ownership while it builds the module graph;
        // the ordinary parser can then bind the declaration itself.
        if (Current.Kind == SyntaxKind.IdentifierToken
            && Current.Text == "export"
            && IsExportableDeclarationAhead())
        {
            _ = NextToken();
        }

        if (IsWord(Current, "flow"))
        {
            return ParseFlowDeclaration();
        }

        if (Current.Kind == SyntaxKind.IdentifierToken
            && Current.Text == "type")
        {
            return ParseTypeDeclaration();
        }

        if (Current.Kind == SyntaxKind.IdentifierToken
            && Current.Text == "interface")
        {
            return ParseInterfaceDeclaration();
        }

        if (Current.Kind == SyntaxKind.ConstKeyword && Peek(1).Kind == SyntaxKind.RecordKeyword)
        {
            var constKeyword = Match(SyntaxKind.ConstKeyword);
            return ParseRecordDeclaration(constKeyword);
        }
        if ((Current.Kind == SyntaxKind.AsyncKeyword && Peek(1).Kind == SyntaxKind.FunctionKeyword)
            || Current.Kind == SyntaxKind.FunctionKeyword)
        {
            return ParseFunctionDeclaration();
        }
        if (Current.Kind == SyntaxKind.EnumKeyword)
        {
            return ParseEnumDeclaration();
        }
        if (Current.Kind == SyntaxKind.RecordKeyword)
        {
            if (Peek(1).Kind == SyntaxKind.TableKeyword)
            {
                return ParseTableDeclaration();
            }
            return ParseRecordDeclaration(null);
        }
        if (IsClassWord(Current, "class"))
        {
            return ParseClassDeclaration();
        }

        return new GlobalStatementMemberSyntax(ParseStatement());
    }

    private bool IsExportableDeclarationAhead()
    {
        SyntaxToken next = Peek(1);
        return next.Kind is SyntaxKind.EnumKeyword or SyntaxKind.RecordKeyword
            || IsWord(next, "type")
            || IsWord(next, "interface")
            || IsWord(next, "flow")
            || IsClassWord(next, "class")
            || (next.Kind == SyntaxKind.ConstKeyword && Peek(2).Kind == SyntaxKind.RecordKeyword);
    }

    private FlowDeclarationSyntax ParseFlowDeclaration()
    {
        SyntaxToken flowKeyword = NextToken();
        SyntaxToken identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? resultArrow = Current.Kind == SyntaxKind.ArrowToken ? NextToken() : null;
        TypeSyntax? resultType = resultArrow is null ? null : ParseTypeSyntax();
        SyntaxToken openBrace = Match(SyntaxKind.OpenBraceToken);
        FlowBoardSyntax? board = null;
        var events = new List<FlowEventSyntax>();
        var states = new List<FlowStateSyntax>();
        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            SyntaxToken start = Current;
            if (IsWord(Current, "board"))
            {
                if (board is not null) _diagnostics.Report("COPE-FLOW-0001", "A flow may declare only one board.", Current.Position, Current.Text.Length);
                board = ParseFlowBoard();
            }
            else if (IsWord(Current, "event")) events.Add(ParseFlowEvent());
            else if (IsWord(Current, "state")) states.Add(ParseFlowState());
            else { ReportUnexpectedToken(Current); NextToken(); }
            if (Current == start) NextToken();
        }
        return new FlowDeclarationSyntax(flowKeyword, identifier, resultArrow, resultType, openBrace, board, events, states, Match(SyntaxKind.CloseBraceToken));
    }

    private FlowBoardSyntax ParseFlowBoard()
    {
        SyntaxToken boardKeyword = NextToken();
        SyntaxToken openBrace = Match(SyntaxKind.OpenBraceToken);
        var fields = new List<FlowBoardFieldSyntax>();
        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            SyntaxToken identifier = Match(SyntaxKind.IdentifierToken);
            SyntaxToken colon = Match(SyntaxKind.ColonToken);
            TypeSyntax type = ParseTypeSyntax();
            SyntaxToken? equals = Current.Kind == SyntaxKind.EqualsToken ? NextToken() : null;
            ExpressionSyntax? initializer = equals is null ? null : ParseExpression();
            SyntaxToken semicolon = Match(SyntaxKind.SemicolonToken);
            fields.Add(new FlowBoardFieldSyntax(identifier, colon, type, equals, initializer, semicolon));
        }
        return new FlowBoardSyntax(boardKeyword, openBrace, fields, Match(SyntaxKind.CloseBraceToken));
    }

    private FlowEventSyntax ParseFlowEvent()
    {
        SyntaxToken eventKeyword = NextToken();
        SyntaxToken identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken openParen = Match(SyntaxKind.OpenParenToken);
        var parameters = new List<ParameterSyntax>();
        var commas = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.CloseParenToken and not SyntaxKind.EndOfFileToken)
        {
            SyntaxToken name = Match(SyntaxKind.IdentifierToken);
            SyntaxToken? colon = Current.Kind == SyntaxKind.ColonToken ? NextToken() : null;
            TypeSyntax? type = colon is null ? null : ParseTypeSyntax();
            parameters.Add(new ParameterSyntax(name, colon, type));
            if (Current.Kind != SyntaxKind.CommaToken) break;
            commas.Add(NextToken());
        }
        return new FlowEventSyntax(eventKeyword, identifier, openParen, parameters, commas, Match(SyntaxKind.CloseParenToken), Match(SyntaxKind.SemicolonToken));
    }

    private FlowStateSyntax ParseFlowState()
    {
        SyntaxToken stateKeyword = NextToken();
        SyntaxToken identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? initial = IsWord(Current, "initial") ? NextToken() : null;
        SyntaxToken openBrace = Match(SyntaxKind.OpenBraceToken);
        var transitions = new List<FlowTransitionSyntax>();
        FlowTerminalSyntax? terminal = null;
        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            if (IsWord(Current, "on")) transitions.Add(ParseFlowTransition());
            else if (IsWord(Current, "finish") || IsWord(Current, "fail"))
            {
                SyntaxToken keyword = NextToken();
                ExpressionSyntax? expression = Current.Kind == SyntaxKind.SemicolonToken ? null : ParseExpression();
                terminal = new FlowTerminalSyntax(keyword, expression, Match(SyntaxKind.SemicolonToken));
            }
            else { ReportUnexpectedToken(Current); NextToken(); }
        }
        return new FlowStateSyntax(stateKeyword, identifier, initial, openBrace, transitions, terminal, Match(SyntaxKind.CloseBraceToken));
    }

    private FlowTransitionSyntax ParseFlowTransition()
    {
        SyntaxToken onKeyword = NextToken();
        SyntaxToken eventIdentifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken openParen = Match(SyntaxKind.OpenParenToken);
        var bindings = new List<SyntaxToken>();
        var commas = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.CloseParenToken and not SyntaxKind.EndOfFileToken)
        {
            bindings.Add(Match(SyntaxKind.IdentifierToken));
            if (Current.Kind != SyntaxKind.CommaToken) break;
            commas.Add(NextToken());
        }
        SyntaxToken closeParen = Match(SyntaxKind.CloseParenToken);
        SyntaxToken? when = IsWord(Current, "when") ? NextToken() : null;
        ExpressionSyntax? guard = when is null ? null : ParseExpression();
        SyntaxToken arrow = Match(SyntaxKind.ArrowToken);
        SyntaxToken target = Match(SyntaxKind.IdentifierToken);
        BlockStatementSyntax? body = Current.Kind == SyntaxKind.OpenBraceToken ? ParseBlockStatement() : null;
        return new FlowTransitionSyntax(onKeyword, eventIdentifier, openParen, bindings, commas, closeParen, when, guard, arrow, target, body, Match(SyntaxKind.SemicolonToken));
    }

    private ClrUsingDirectiveSyntax ParseClrUsingDirective()
    {
        SyntaxToken usingKeyword = NextToken();
        var nameParts = new List<SyntaxToken> { Match(SyntaxKind.IdentifierToken) };
        var dots = new List<SyntaxToken>();
        while (Current.Kind == SyntaxKind.DotToken)
        {
            dots.Add(NextToken());
            nameParts.Add(Match(SyntaxKind.IdentifierToken));
        }

        return new ClrUsingDirectiveSyntax(
            usingKeyword,
            nameParts,
            dots,
            Match(SyntaxKind.SemicolonToken));
    }

    private bool IsClrUsingDirectiveAhead()
    {
        if (!IsWord(Current, "using"))
        {
            return false;
        }

        int offset = 1;
        if (Peek(offset).Kind != SyntaxKind.IdentifierToken)
        {
            return false;
        }

        offset++;
        while (Peek(offset).Kind == SyntaxKind.DotToken)
        {
            if (Peek(offset + 1).Kind != SyntaxKind.IdentifierToken)
            {
                return false;
            }

            offset += 2;
        }

        return Peek(offset).Kind == SyntaxKind.SemicolonToken;
    }

    private ImportDeclarationSyntax ParseImportDeclaration()
    {
        var tokens = new List<SyntaxToken> { NextToken() };
        while (Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.EndOfFileToken)
        {
            tokens.Add(NextToken());
        }

        if (Current.Kind == SyntaxKind.SemicolonToken)
        {
            tokens.Add(NextToken());
        }

        return new ImportDeclarationSyntax(tokens);
    }

    private ExportDefaultDeclarationSyntax ParseExportDefaultDeclaration()
    {
        SyntaxToken exportToken = NextToken();
        SyntaxToken defaultToken = NextToken();
        ExpressionSyntax expression = ParseExpression();
        SyntaxToken? semicolonToken = Current.Kind == SyntaxKind.SemicolonToken ? NextToken() : null;
        return new ExportDefaultDeclarationSyntax(exportToken, defaultToken, expression, semicolonToken);
    }

    private MemberSyntax ParseTypeDeclaration()
    {
        var typeKeyword = NextToken();
        SyntaxToken identifier;
        if (Current.Kind == SyntaxKind.IdentifierToken)
        {
            identifier = NextToken();
        }
        else
        {
            ReportAliasSyntax("Expected an alias name after 'type'.", Current);
            identifier = MissingToken(SyntaxKind.IdentifierToken, Current.Position);
        }

        var typeParameterTokens = new List<SyntaxToken>();
        if (Current.Kind == SyntaxKind.LessToken)
        {
            ReportAliasSyntax("Generic type aliases are not supported.", Current, "COPE-ALIAS-0002");
            while (Current.Kind is not SyntaxKind.GreaterToken
                   and not SyntaxKind.EqualsToken
                   and not SyntaxKind.SemicolonToken
                   and not SyntaxKind.EndOfFileToken)
            {
                typeParameterTokens.Add(NextToken());
            }

            if (Current.Kind == SyntaxKind.GreaterToken)
            {
                typeParameterTokens.Add(NextToken());
            }
        }

        SyntaxToken equalsToken;
        if (Current.Kind == SyntaxKind.EqualsToken)
        {
            equalsToken = NextToken();
        }
        else
        {
            ReportAliasSyntax("Expected '=' in type alias declaration.", Current);
            equalsToken = MissingToken(SyntaxKind.EqualsToken, Current.Position);
        }

        if (HasPipeBeforeTypeDeclarationTerminator())
        {
            return ParseNominalUnionDeclaration(typeKeyword, identifier, equalsToken);
        }

        TypeSyntax targetType;
        if (Current.Kind is SyntaxKind.SemicolonToken or SyntaxKind.EndOfFileToken)
        {
            ReportAliasSyntax("Expected a type alias target.", Current);
            targetType = new IdentifierTypeSyntax(MissingToken(SyntaxKind.IdentifierToken, Current.Position));
        }
        else
        {
            targetType = ParseTypeSyntax();
        }

        var unsupportedTokens = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.EndOfFileToken)
        {
            unsupportedTokens.Add(NextToken());
        }

        if (unsupportedTokens.Count > 0)
        {
            ReportAliasSyntax("Unsupported type-level syntax in type alias declaration.", unsupportedTokens[0]);
        }

        SyntaxToken semicolonToken;
        if (Current.Kind == SyntaxKind.SemicolonToken)
        {
            semicolonToken = NextToken();
        }
        else
        {
            ReportAliasSyntax("Type alias declarations require a terminating semicolon.", Current);
            semicolonToken = MissingToken(SyntaxKind.SemicolonToken, Current.Position);
        }

        return new TypeAliasDeclarationSyntax(
            typeKeyword,
            identifier,
            typeParameterTokens,
            equalsToken,
            targetType,
            unsupportedTokens,
            semicolonToken);
    }

    private NominalUnionDeclarationSyntax ParseNominalUnionDeclaration(
        SyntaxToken typeKeyword,
        SyntaxToken identifier,
        SyntaxToken equalsToken)
    {
        SyntaxToken? leadingPipe = null;
        if (Current.Kind == SyntaxKind.PipeToken)
        {
            leadingPipe = NextToken();
        }

        var alternatives = new List<SyntaxToken>();
        var pipes = new List<SyntaxToken>();
        bool expectingAlternative = true;

        while (Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.EndOfFileToken)
        {
            if (expectingAlternative)
            {
                if (Current.Kind != SyntaxKind.IdentifierToken)
                {
                    ReportUnionSyntax("Expected a nominal record alternative after '|'.", Current);
                    NextToken();
                    continue;
                }

                alternatives.Add(NextToken());
                expectingAlternative = false;
                continue;
            }

            if (Current.Kind == SyntaxKind.PipeToken)
            {
                pipes.Add(NextToken());
                expectingAlternative = true;
                continue;
            }

            if (Current.Kind == SyntaxKind.PipePipeToken)
            {
                ReportUnionSyntax("'||' is a logical expression operator and cannot separate nominal union alternatives.", Current);
                NextToken();
                expectingAlternative = true;
                continue;
            }

            ReportUnionSyntax("Nominal union alternatives must be separated by '|'.", Current);
            NextToken();
        }

        if (expectingAlternative)
        {
            ReportUnionSyntax("Expected a nominal record alternative after '|'.", Current);
        }

        if (alternatives.Count < 2)
        {
            ReportUnionSyntax("A nominal union declaration requires at least two alternatives.", identifier);
        }

        SyntaxToken semicolon = Current.Kind == SyntaxKind.SemicolonToken
            ? NextToken()
            : MissingToken(SyntaxKind.SemicolonToken, Current.Position);
        if (semicolon.Text.Length == 0)
        {
            ReportUnionSyntax("Nominal union declarations require a terminating semicolon.", Current);
        }

        return new NominalUnionDeclarationSyntax(
            typeKeyword,
            identifier,
            equalsToken,
            leadingPipe,
            alternatives,
            pipes,
            semicolon);
    }

    private void ReportAliasSyntax(
        string message,
        SyntaxToken token,
        string diagnosticId = "COPE-ALIAS-0001")
    {
        _diagnostics.Report(
            diagnosticId,
            message,
            token.Position,
            Math.Max(1, token.Text.Length));
    }

    private void ReportUnionSyntax(string message, SyntaxToken token)
    {
        _diagnostics.Report(
            "COPE-UNION-0001",
            message,
            token.Position,
            Math.Max(1, token.Text.Length));
    }

    private void ReportIllegalPipeUsage(SyntaxToken token)
    {
        _diagnostics.Report(
            "COPE-UNION-0012",
            "'|' is permitted only between alternatives in a compilation-unit nominal union declaration.",
            token.Position,
            Math.Max(1, token.Text.Length));
    }

    private bool HasPipeBeforeTypeDeclarationTerminator()
    {
        for (int offset = 0; ; offset++)
        {
            SyntaxKind kind = Peek(offset).Kind;
            if (kind is SyntaxKind.PipeToken or SyntaxKind.PipePipeToken)
            {
                return true;
            }

            if (kind is SyntaxKind.SemicolonToken or SyntaxKind.EndOfFileToken)
            {
                return false;
            }
        }
    }

    private FunctionDeclarationSyntax ParseFunctionDeclaration()
    {
        SyntaxToken? asyncKeyword = Current.Kind == SyntaxKind.AsyncKeyword ? NextToken() : null;
        var functionKeyword = Match(SyntaxKind.FunctionKeyword);
        SyntaxToken? generatorStarToken = Current.Kind == SyntaxKind.StarToken ? NextToken() : null;
        var identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? lessToken = null;
        SyntaxToken? greaterToken = null;
        var typeParameters = new List<TypeParameterSyntax>();
        var typeParameterCommas = new List<SyntaxToken>();
        if (Current.Kind == SyntaxKind.LessToken)
        {
            lessToken = NextToken();
            while (Current.Kind is not SyntaxKind.GreaterToken and not SyntaxKind.EndOfFileToken)
            {
                var parameterName = Match(SyntaxKind.IdentifierToken);
                SyntaxToken? extendsKeyword = null;
                var requirementNames = new List<SyntaxToken>();
                var ampersands = new List<SyntaxToken>();
                if (Current.Kind == SyntaxKind.IdentifierToken && Current.Text == "extends")
                {
                    extendsKeyword = NextToken();
                    requirementNames.Add(Match(SyntaxKind.IdentifierToken));
                    while (Current.Kind == SyntaxKind.AmpersandToken)
                    {
                        ampersands.Add(NextToken());
                        requirementNames.Add(Match(SyntaxKind.IdentifierToken));
                    }
                }
                typeParameters.Add(new TypeParameterSyntax(parameterName, extendsKeyword, requirementNames, ampersands));
                if (Current.Kind != SyntaxKind.CommaToken) break;
                typeParameterCommas.Add(NextToken());
            }
            greaterToken = Match(SyntaxKind.GreaterToken);
        }
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
        return new FunctionDeclarationSyntax(asyncKeyword, functionKeyword, generatorStarToken, identifier, lessToken, typeParameters, typeParameterCommas, greaterToken, openParenToken, parameters, commas, closeParenToken, returnTypeColonToken, returnType, body);
    }

    private InterfaceDeclarationSyntax ParseInterfaceDeclaration()
    {
        var interfaceKeyword = NextToken();
        var identifier = Match(SyntaxKind.IdentifierToken);
        var openBrace = Match(SyntaxKind.OpenBraceToken);
        var fields = new List<InterfaceFieldSyntax>();
        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            var fieldIdentifier = Match(SyntaxKind.IdentifierToken);
            var hasColon = Current.Kind == SyntaxKind.ColonToken;
            var colon = hasColon ? NextToken() : MissingToken(SyntaxKind.ColonToken, Current.Position);
            var hasType = hasColon && Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.CloseBraceToken;
            var type = hasType
                ? ParseTypeSyntax()
                : new IdentifierTypeSyntax(MissingToken(SyntaxKind.IdentifierToken, Current.Position));
            var unsupported = new List<SyntaxToken>();
            while (Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
            {
                unsupported.Add(NextToken());
            }
            var hasTerminator = Current.Kind == SyntaxKind.SemicolonToken;
            var semicolon = hasTerminator ? NextToken() : MissingToken(SyntaxKind.SemicolonToken, Current.Position);
            fields.Add(new InterfaceFieldSyntax(fieldIdentifier, colon, type, unsupported, semicolon, hasType, hasTerminator));
        }
        return new InterfaceDeclarationSyntax(interfaceKeyword, identifier, openBrace, fields, Match(SyntaxKind.CloseBraceToken));
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
            SyntaxKind.YieldKeyword => ParseYieldStatement(),
            SyntaxKind.BreakKeyword => ParseBreakStatement(),
            SyntaxKind.ContinueKeyword => ParseContinueStatement(),
            SyntaxKind.RecordKeyword when Peek(1).Kind == SyntaxKind.TableKeyword => new NestedTableDeclarationStatementSyntax(ParseTableDeclaration()),
            SyntaxKind.RecordKeyword => new NestedRecordDeclarationStatementSyntax(ParseRecordDeclaration(null)),
            SyntaxKind.IdentifierToken when IsWord(Current, "using") && IsClrUsingDirectiveAhead() => ParseMisplacedClrUsingDirective(),
            SyntaxKind.IdentifierToken when IsWord(Current, "using") => ParseResourceUsingDeclaration(null),
            SyntaxKind.IdentifierToken when IsWord(Current, "csharp") && Peek(1).Kind == SyntaxKind.OpenBraceToken => ParseCSharpBlockStatement(),
            SyntaxKind.AwaitKeyword when IsWord(Peek(1), "using") => ParseResourceUsingDeclaration(NextToken()),
            _ => ParseExpressionStatementOrRecovery(),
        };

    private CSharpBlockStatementSyntax ParseCSharpBlockStatement()
    {
        SyntaxToken keyword = NextToken();
        SyntaxToken openBrace = Match(SyntaxKind.OpenBraceToken);
        if (!CSharpBlockScanner.TryFindClosingBrace(_text, openBrace.Position, out int closePosition))
        {
            _diagnostics.Report("COPE-CSHARP-0001", "Unterminated inline C# block.", openBrace.Position, 1);
            return new CSharpBlockStatementSyntax(
                keyword,
                openBrace,
                _text[(openBrace.Position + 1)..],
                openBrace.Position + 1,
                MissingToken(SyntaxKind.CloseBraceToken, _text.Length));
        }

        string body = _text.Substring(openBrace.Position + 1, closePosition - openBrace.Position - 1);
        while (Current.Kind != SyntaxKind.EndOfFileToken && Current.Position < closePosition) NextToken();
        SyntaxToken closeBrace = Match(SyntaxKind.CloseBraceToken);
        return new CSharpBlockStatementSyntax(keyword, openBrace, body, openBrace.Position + 1, closeBrace);
    }

    private StatementSyntax ParseMisplacedClrUsingDirective()
    {
        ClrUsingDirectiveSyntax directive = ParseClrUsingDirective();
        _diagnostics.Report(
            "COPE-CLR-0010",
            "CLR 'using Qualified.Name;' directives are allowed only at module scope.",
            directive.UsingKeyword.Position,
            Math.Max(1, directive.UsingKeyword.Text.Length));
        return new ExpressionStatementSyntax(new MissingExpressionSyntax(directive.UsingKeyword), directive.SemicolonToken);
    }

    private ResourceUsingDeclarationStatementSyntax ParseResourceUsingDeclaration(SyntaxToken? awaitKeyword)
    {
        SyntaxToken usingKeyword = NextToken();
        SyntaxToken identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken equalsToken = Match(SyntaxKind.EqualsToken);
        ExpressionSyntax initializer = ParseExpression();
        SyntaxToken semicolonToken = Match(SyntaxKind.SemicolonToken);
        return new ResourceUsingDeclarationStatementSyntax(
            awaitKeyword,
            usingKeyword,
            identifier,
            equalsToken,
            initializer,
            semicolonToken);
    }

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
            ConsumeIllegalInlinePipeTypeSyntax();
            return type;
        }

        var bangToken = Match(SyntaxKind.BangToken);
        var errorType = ParseTypeSyntax();
        ConsumeIllegalInlinePipeTypeSyntax();
        return new ResultTypeSyntax(type, bangToken, errorType);
    }

    private void ConsumeIllegalInlinePipeTypeSyntax()
    {
        while (Current.Kind == SyntaxKind.PipeToken)
        {
            ReportIllegalPipeUsage(Current);
            NextToken();
            _ = ParsePostfixTypeSyntax();
        }
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

    private ClassDeclarationSyntax ParseClassDeclaration()
    {
        var classKeyword = MatchClassWord("class");
        var identifier = Match(SyntaxKind.IdentifierToken);
        if (Current.Kind == SyntaxKind.LessToken)
        {
            _diagnostics.Report("COPE-CLASS-0001", "Generic classes are not supported; use a generic associated function instead.", Current.Position, Math.Max(1, Current.Text.Length));
            do
            {
                NextToken();
            }
            while (Current.Kind is not SyntaxKind.GreaterToken and not SyntaxKind.OpenBraceToken and not SyntaxKind.EndOfFileToken);
            if (Current.Kind == SyntaxKind.GreaterToken)
            {
                NextToken();
            }
        }
        SyntaxToken? extendsKeyword = null;
        SyntaxToken? baseTypeIdentifier = null;
        if (IsClassWord(Current, "extends"))
        {
            extendsKeyword = NextToken();
            baseTypeIdentifier = Match(SyntaxKind.IdentifierToken);
        }

        var openBrace = Match(SyntaxKind.OpenBraceToken);
        var members = new List<ClassMemberSyntax>();
        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            var start = Current;
            members.Add(ParseClassMember());
            if (Current == start)
            {
                ReportUnexpectedToken(Current);
                NextToken();
            }
        }

        return new ClassDeclarationSyntax(
            classKeyword,
            identifier,
            extendsKeyword,
            baseTypeIdentifier,
            openBrace,
            members,
            Match(SyntaxKind.CloseBraceToken));
    }

    private ClassMemberSyntax ParseClassMember()
    {
        SyntaxToken? visibility = null;
        if (IsClassWord(Current, "public") || IsClassWord(Current, "private") || IsClassWord(Current, "protected"))
        {
            visibility = NextToken();
        }

        var modifiers = new List<SyntaxToken>();
        while (IsClassWord(Current, "static") || IsClassWord(Current, "readonly") || IsClassWord(Current, "get") || IsClassWord(Current, "set"))
        {
            modifiers.Add(NextToken());
        }

        if (IsClassWord(Current, "constructor"))
        {
            return ParseClassConstructor(visibility, modifiers);
        }

        var identifier = Current.Kind == SyntaxKind.IdentifierToken
            ? NextToken()
            : Match(SyntaxKind.IdentifierToken);
        if (Current.Kind is SyntaxKind.OpenParenToken or SyntaxKind.LessToken)
        {
            return ParseClassAssociatedFunction(visibility, modifiers, identifier);
        }

        var hasColon = Current.Kind == SyntaxKind.ColonToken;
        SyntaxToken colon = hasColon ? NextToken() : MissingToken(SyntaxKind.ColonToken, Current.Position);
        bool hasType = hasColon && Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.EqualsToken and not SyntaxKind.CloseBraceToken;
        TypeSyntax type = hasType
            ? ParseTypeSyntax()
            : new IdentifierTypeSyntax(MissingToken(SyntaxKind.IdentifierToken, Current.Position));
        SyntaxToken? equals = null;
        ExpressionSyntax? initializer = null;
        if (Current.Kind == SyntaxKind.EqualsToken)
        {
            equals = NextToken();
            initializer = ParseExpression();
        }
        else
        {
            // Preserve unfamiliar member syntax so binding can issue a focused class diagnostic.
            while (Current.Kind is not SyntaxKind.SemicolonToken and not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
            {
                modifiers.Add(NextToken());
            }
        }
        bool hasTerminator = Current.Kind == SyntaxKind.SemicolonToken;
        SyntaxToken semicolon = hasTerminator ? NextToken() : MissingToken(SyntaxKind.SemicolonToken, Current.Position);
        return new ClassFieldSyntax(visibility, modifiers, identifier, colon, type, equals, initializer, semicolon, hasType, hasTerminator);
    }

    private ClassConstructorDeclarationSyntax ParseClassConstructor(SyntaxToken? visibility, IReadOnlyList<SyntaxToken> modifiers)
    {
        var constructor = MatchClassWord("constructor");
        var open = Match(SyntaxKind.OpenParenToken);
        ParseClassParameters(out var parameters, out var commas);
        var close = Match(SyntaxKind.CloseParenToken);
        SyntaxToken? returnColon = null;
        TypeSyntax? returnType = null;
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            returnColon = NextToken();
            returnType = ParseTypeSyntax();
        }
        var body = ParseBlockStatement();
        return new ClassConstructorDeclarationSyntax(visibility, modifiers, constructor, open, parameters, commas, close, returnColon, returnType, body);
    }

    private ClassAssociatedFunctionDeclarationSyntax ParseClassAssociatedFunction(
        SyntaxToken? visibility,
        IReadOnlyList<SyntaxToken> modifiers,
        SyntaxToken identifier)
    {
        SyntaxToken? less = null;
        SyntaxToken? greater = null;
        var typeParameters = new List<TypeParameterSyntax>();
        var typeCommas = new List<SyntaxToken>();
        if (Current.Kind == SyntaxKind.LessToken)
        {
            less = NextToken();
            while (Current.Kind is not SyntaxKind.GreaterToken and not SyntaxKind.EndOfFileToken)
            {
                var parameter = Match(SyntaxKind.IdentifierToken);
                SyntaxToken? extends = null;
                var requirements = new List<SyntaxToken>();
                var ampersands = new List<SyntaxToken>();
                if (IsClassWord(Current, "extends"))
                {
                    extends = NextToken();
                    requirements.Add(Match(SyntaxKind.IdentifierToken));
                    while (Current.Kind == SyntaxKind.AmpersandToken)
                    {
                        ampersands.Add(NextToken());
                        requirements.Add(Match(SyntaxKind.IdentifierToken));
                    }
                }
                typeParameters.Add(new TypeParameterSyntax(parameter, extends, requirements, ampersands));
                if (Current.Kind != SyntaxKind.CommaToken) break;
                typeCommas.Add(NextToken());
            }
            greater = Match(SyntaxKind.GreaterToken);
        }

        var open = Match(SyntaxKind.OpenParenToken);
        ParseClassParameters(out var parameters, out var commas);
        var close = Match(SyntaxKind.CloseParenToken);
        SyntaxToken? returnColon = null;
        TypeSyntax? returnType = null;
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            returnColon = NextToken();
            returnType = ParseTypeSyntax();
        }
        var body = ParseBlockStatement();
        return new ClassAssociatedFunctionDeclarationSyntax(
            visibility,
            modifiers,
            identifier,
            less,
            typeParameters,
            typeCommas,
            greater,
            open,
            parameters,
            commas,
            close,
            returnColon,
            returnType,
            body);
    }

    private void ParseClassParameters(out IReadOnlyList<ParameterSyntax> parameters, out IReadOnlyList<SyntaxToken> commas)
    {
        var parsedParameters = new List<ParameterSyntax>();
        var parsedCommas = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.CloseParenToken and not SyntaxKind.EndOfFileToken)
        {
            SyntaxToken identifier = Match(SyntaxKind.IdentifierToken);
            SyntaxToken? colon = null;
            TypeSyntax? type = null;
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                colon = NextToken();
                type = ParseTypeSyntax();
            }
            parsedParameters.Add(new ParameterSyntax(identifier, colon, type));
            if (Current.Kind != SyntaxKind.CommaToken) break;
            parsedCommas.Add(NextToken());
        }
        parameters = parsedParameters;
        commas = parsedCommas;
    }

    private static bool IsClassWord(SyntaxToken token, string text)
        => token.Kind == SyntaxKind.IdentifierToken && string.Equals(token.Text, text, StringComparison.Ordinal);

    private static bool IsWord(SyntaxToken token, string text)
        => token.Kind == SyntaxKind.IdentifierToken && string.Equals(token.Text, text, StringComparison.Ordinal);

    private SyntaxToken MatchClassWord(string text)
    {
        if (IsClassWord(Current, text))
        {
            return NextToken();
        }
        _diagnostics.Report("COPE-CLASS-0001", $"Expected '{text}' in class declaration.", Current.Position, Math.Max(1, Current.Text.Length));
        return MissingToken(SyntaxKind.IdentifierToken, Current.Position);
    }

    private TableDeclarationSyntax ParseTableDeclaration()
    {
        var recordKeyword = Match(SyntaxKind.RecordKeyword);
        var tableKeyword = Match(SyntaxKind.TableKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        TableAssetClauseSyntax? assetClause = null;
        if (Current.Kind == SyntaxKind.IdentifierToken
            && string.Equals(Current.Text, "from", StringComparison.Ordinal))
        {
            var fromToken = NextToken();
            var target = new NameExpressionSyntax(Match(SyntaxKind.IdentifierToken));
            assetClause = new TableAssetClauseSyntax(fromToken, ParseCallExpression(target));
        }
        var openBrace = Match(SyntaxKind.OpenBraceToken);
        var columns = new List<TableColumnSyntax>();
        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            var name = Match(SyntaxKind.IdentifierToken);
            var colon = Match(SyntaxKind.ColonToken);
            TypeSyntax? explicitType = null;
            SyntaxToken? equals = null;
            ArrayLiteralExpressionSyntax cells;
            bool hasInlineData;
            if (assetClause is not null)
            {
                if (Current.Kind is not SyntaxKind.SemicolonToken
                    and not SyntaxKind.EqualsToken
                    and not SyntaxKind.OpenBracketToken)
                {
                    explicitType = ParseTypeSyntax();
                }

                if (Current.Kind == SyntaxKind.EqualsToken)
                {
                    equals = Match(SyntaxKind.EqualsToken);
                }

                hasInlineData = Current.Kind == SyntaxKind.OpenBracketToken;
                cells = hasInlineData
                    ? ParseArrayLiteralExpression()
                    : new ArrayLiteralExpressionSyntax(
                        MissingToken(SyntaxKind.OpenBracketToken, Current.Position),
                        [],
                        [],
                        MissingToken(SyntaxKind.CloseBracketToken, Current.Position));
            }
            else
            {
                if (Current.Kind != SyntaxKind.OpenBracketToken)
                {
                    explicitType = ParseTypeSyntax();
                    equals = Match(SyntaxKind.EqualsToken);
                }
                cells = Current.Kind == SyntaxKind.OpenBracketToken
                    ? ParseArrayLiteralExpression()
                    : new ArrayLiteralExpressionSyntax(
                        MissingToken(SyntaxKind.OpenBracketToken, Current.Position),
                        [],
                        [],
                        MissingToken(SyntaxKind.CloseBracketToken, Current.Position));
                hasInlineData = true;
            }
            columns.Add(new TableColumnSyntax(
                name,
                colon,
                explicitType,
                equals,
                cells,
                Match(SyntaxKind.SemicolonToken),
                hasInlineData));
        }
        return new TableDeclarationSyntax(
            recordKeyword,
            tableKeyword,
            identifier,
            assetClause,
            openBrace,
            columns,
            Match(SyntaxKind.CloseBraceToken));
    }

    private TypeSyntax ParsePostfixTypeSyntax()
    {
        TypeSyntax type = Current.Kind switch
        {
            SyntaxKind.NumberKeyword or SyntaxKind.IntKeyword or SyntaxKind.FloatKeyword or SyntaxKind.StringKeyword or SyntaxKind.BooleanKeyword or SyntaxKind.VoidKeyword or SyntaxKind.NullKeyword
                => new PredefinedTypeSyntax(NextToken()),
            SyntaxKind.IdentifierToken when Current.Text == "Async" => ParseAsyncTypeSyntax(),
            SyntaxKind.IdentifierToken when Current.Text == "Iterable" => ParseIterableTypeSyntax(),
            SyntaxKind.IdentifierToken => ParseIdentifierOrQualifiedRowType(),
            SyntaxKind.ColumnKeyword => new ColumnTypeSyntax(NextToken(), ParsePostfixTypeSyntax()),
            SyntaxKind.OpenParenToken when IsCallableTypeAhead()
                => ParseCallableTypeSyntax(),
            SyntaxKind.OpenParenToken => ParseParenthesizedTypeSyntax(),
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

    private bool IsCallableTypeAhead()
    {
        if (Current.Kind != SyntaxKind.OpenParenToken) return false;
        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            SyntaxToken token = Peek(offset);
            if (token.Kind == SyntaxKind.EndOfFileToken) return false;
            if (token.Kind == SyntaxKind.OpenParenToken) depth++;
            else if (token.Kind == SyntaxKind.CloseParenToken && --depth == 0) return Peek(offset + 1).Kind == SyntaxKind.ArrowToken;
        }
    }

    private CallableTypeSyntax ParseCallableTypeSyntax()
    {
        var openParen = Match(SyntaxKind.OpenParenToken);
        var parameters = new List<CallableTypeParameterSyntax>();
        var commas = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.CloseParenToken and not SyntaxKind.EndOfFileToken)
        {
            var identifier = Match(SyntaxKind.IdentifierToken);
            var colon = Match(SyntaxKind.ColonToken);
            var type = ParseTypeSyntax();
            parameters.Add(new CallableTypeParameterSyntax(identifier, colon, type));
            if (Current.Kind != SyntaxKind.CommaToken) break;
            commas.Add(NextToken());
        }
        var closeParen = Match(SyntaxKind.CloseParenToken);
        var arrow = Match(SyntaxKind.ArrowToken);
        return new CallableTypeSyntax(openParen, parameters, commas, closeParen, arrow, ParseTypeSyntax());
    }

    private TypeSyntax ParseIdentifierOrQualifiedRowType()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        if (Current.Kind == SyntaxKind.DotToken && Peek(1).Kind == SyntaxKind.IdentifierToken)
        {
            var dot = Match(SyntaxKind.DotToken);
            return new QualifiedRowTypeSyntax(identifier, dot, Match(SyntaxKind.IdentifierToken));
        }
        return new IdentifierTypeSyntax(identifier);
    }

    private AsyncTypeSyntax ParseAsyncTypeSyntax()
    {
        var asyncKeyword = Match(SyntaxKind.IdentifierToken);
        var lessToken = Match(SyntaxKind.LessToken);
        var eventualType = ParseTypeSyntax();
        var greaterToken = Match(SyntaxKind.GreaterToken);
        return new AsyncTypeSyntax(asyncKeyword, lessToken, eventualType, greaterToken);
    }

    private IterableTypeSyntax ParseIterableTypeSyntax()
    {
        var iterableIdentifier = Match(SyntaxKind.IdentifierToken);
        var lessToken = Match(SyntaxKind.LessToken);
        var elementType = ParseTypeSyntax();
        var greaterToken = Match(SyntaxKind.GreaterToken);
        return new IterableTypeSyntax(iterableIdentifier, lessToken, elementType, greaterToken);
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

    private StatementSyntax ParseForStatement()
    {
        var forKeyword = Match(SyntaxKind.ForKeyword);
        var openParenToken = Match(SyntaxKind.OpenParenToken);

        if (Current.Kind is SyntaxKind.ConstKeyword or SyntaxKind.LetKeyword or SyntaxKind.VarKeyword
            && Peek(1).Kind == SyntaxKind.IdentifierToken
            && Peek(2).Kind == SyntaxKind.IdentifierToken
            && Peek(2).Text == "of")
        {
            SyntaxToken declarationKeyword = NextToken();
            SyntaxToken identifier = Match(SyntaxKind.IdentifierToken);
            SyntaxToken ofKeyword = Match(SyntaxKind.IdentifierToken);
            ExpressionSyntax iterable = ParseExpression();
            SyntaxToken forOfCloseParenToken = Match(SyntaxKind.CloseParenToken);
            StatementSyntax forOfBody = ParseStatement();
            return new ForOfStatementSyntax(forKeyword, openParenToken, declarationKeyword, identifier, ofKeyword, iterable, forOfCloseParenToken, forOfBody);
        }

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

    private YieldStatementSyntax ParseYieldStatement()
    {
        SyntaxToken yieldKeyword = Match(SyntaxKind.YieldKeyword);
        if (Current.Kind == SyntaxKind.BreakKeyword)
        {
            SyntaxToken breakKeyword = NextToken();
            return new YieldStatementSyntax(yieldKeyword, null, null, breakKeyword, null, Match(SyntaxKind.SemicolonToken));
        }

        SyntaxToken? returnKeyword = Current.Kind == SyntaxKind.ReturnKeyword ? NextToken() : null;
        SyntaxToken? starToken = Current.Kind == SyntaxKind.StarToken ? NextToken() : null;
        ExpressionSyntax? expression = Current.Kind == SyntaxKind.SemicolonToken ? null : ParseExpression();
        return new YieldStatementSyntax(yieldKeyword, returnKeyword, starToken, null, expression, Match(SyntaxKind.SemicolonToken));
    }

    private BreakStatementSyntax ParseBreakStatement()
    {
        var breakKeyword = Match(SyntaxKind.BreakKeyword);
        return new BreakStatementSyntax(breakKeyword, Match(SyntaxKind.SemicolonToken));
    }

    private ContinueStatementSyntax ParseContinueStatement()
    {
        var continueKeyword = Match(SyntaxKind.ContinueKeyword);
        return new ContinueStatementSyntax(continueKeyword, Match(SyntaxKind.SemicolonToken));
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

        while (Current.Kind == SyntaxKind.PipeToken)
        {
            ReportIllegalPipeUsage(Current);
            NextToken();
            _ = ParseBinaryExpression();
        }

        if (Current.Kind == SyntaxKind.EqualsToken)
        {
            var equalsToken = Match(SyntaxKind.EqualsToken);
            if (left is not NameExpressionSyntax and not MemberAccessExpressionSyntax and not IndexExpressionSyntax)
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

            if ((expression is NameExpressionSyntax or MemberAccessExpressionSyntax) && IsGenericFunctionSuffixAhead())
            {
                expression = ParseGenericFunctionExpression(expression);
                continue;
            }
            if (Current.Kind == SyntaxKind.OpenBracketToken)
            {
                var open = Match(SyntaxKind.OpenBracketToken);
                var index = ParseExpression();
                expression = new IndexExpressionSyntax(expression, open, index, Match(SyntaxKind.CloseBracketToken));
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
    {
        if (_allowsTsXml && Current.Kind == SyntaxKind.LessToken)
        {
            return ParseTsXmlExpression();
        }

        if (IsClassWord(Current, "new"))
        {
            return ParseNewExpression();
        }
        if (IsClassWord(Current, "this") || IsClassWord(Current, "super"))
        {
            return new UnsupportedExpressionSyntax(NextToken());
        }
        return Current.Kind switch
        {
            SyntaxKind.AwaitKeyword => new AwaitExpressionSyntax(NextToken(), ParseAwaitOperand()),
            SyntaxKind.OpenParenToken when IsArrowExpressionAhead() => ParseArrowExpression(),
            SyntaxKind.OpenParenToken => ParseParenthesizedExpression(),
            SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword or SyntaxKind.NumberToken or SyntaxKind.StringToken => new LiteralExpressionSyntax(NextToken()),
            SyntaxKind.TemplateToken => ParseTemplateExpression(),
            SyntaxKind.IdentifierToken when Current.Text == "capture" => ParseCaptureExpression(),
            SyntaxKind.IdentifierToken when Current.Text == "batch" && IsBatchExpressionAhead() => ParseBatchExpression(),
            SyntaxKind.IdentifierToken when Peek(1).Kind == SyntaxKind.ArrowToken => ParseSingleParameterArrowExpression(),
            SyntaxKind.IdentifierToken => new NameExpressionSyntax(NextToken()),
            SyntaxKind.OpenBracketToken => ParseArrayLiteralExpression(),
            SyntaxKind.OpenBraceToken => ParseObjectLiteralExpression(),
            SyntaxKind.MatchKeyword => ParseMatchExpression(),
            SyntaxKind.IfKeyword => ParseIfExpression(),
            SyntaxKind.TryKeyword => ParseTryExceptExpression(),
            _ => ParseMissingExpression(),
        };
    }

    private TemplateExpressionSyntax ParseTemplateExpression()
    {
        SyntaxToken token = NextToken();
        string text = (string?)token.Value ?? string.Empty;
        var parts = new List<TemplatePartSyntax>();
        var textBuilder = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                textBuilder.Append(text[index + 1]);
                index++;
                continue;
            }

            if (text[index] != '$' || index + 1 >= text.Length || text[index + 1] != '{')
            {
                textBuilder.Append(text[index]);
                continue;
            }

            if (textBuilder.Length > 0)
            {
                parts.Add(new TemplateTextPartSyntax(textBuilder.ToString()));
                textBuilder.Clear();
            }

            int expressionStart = index + 2;
            int depth = 1;
            index += 2;
            for (; index < text.Length && depth > 0; index++)
            {
                if (text[index] == '{') depth++;
                else if (text[index] == '}') depth--;
            }

            if (depth != 0)
            {
                _diagnostics.Report("COPE-PARSE-0024", "Unterminated template interpolation.", token.Position, Math.Max(1, token.Text.Length));
                break;
            }

            string expressionText = text.Substring(expressionStart, index - expressionStart - 1);
            var nested = new Parser(expressionText, _allowsTsXml, _allowsImports);
            ExpressionSyntax expression = nested.ParseExpression();
            foreach (Diagnostic diagnostic in nested.Diagnostics)
            {
                _diagnostics.Report(diagnostic.Id, diagnostic.Message, token.Position + expressionStart + diagnostic.Position + 1, diagnostic.Length);
            }
            parts.Add(new TemplateInterpolationPartSyntax(expression));
            index--;
        }

        if (textBuilder.Length > 0 || parts.Count == 0)
        {
            parts.Add(new TemplateTextPartSyntax(textBuilder.ToString()));
        }

        return new TemplateExpressionSyntax(token, parts);
    }

    private BatchExpressionSyntax ParseBatchExpression()
    {
        SyntaxToken batchKeyword = NextToken();
        ExpressionSyntax input = ParseExpression();
        SyntaxToken asKeyword;
        if (Current.Kind == SyntaxKind.IdentifierToken && Current.Text == "as")
        {
            asKeyword = NextToken();
        }
        else
        {
            _diagnostics.Report("COPE-BATCH-0001", "Expected 'as' after the batch input expression.", Current.Position, Math.Max(1, Current.Text.Length));
            asKeyword = MissingToken(SyntaxKind.IdentifierToken, Current.Position);
        }

        SyntaxToken itemIdentifier = Match(SyntaxKind.IdentifierToken);
        BlockStatementSyntax body = ParseBlockStatement();
        return new BatchExpressionSyntax(batchKeyword, input, asKeyword, itemIdentifier, body);
    }

    private bool IsBatchExpressionAhead()
    {
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        for (var offset = 1; ; offset++)
        {
            SyntaxToken token = Peek(offset);
            if (token.Kind is SyntaxKind.EndOfFileToken or SyntaxKind.SemicolonToken)
            {
                return false;
            }

            if (token.Kind == SyntaxKind.OpenParenToken)
            {
                parenthesisDepth++;
                continue;
            }

            if (token.Kind == SyntaxKind.CloseParenToken)
            {
                if (parenthesisDepth == 0)
                {
                    return false;
                }
                parenthesisDepth--;
                continue;
            }

            if (token.Kind == SyntaxKind.OpenBracketToken)
            {
                bracketDepth++;
                continue;
            }

            if (token.Kind == SyntaxKind.CloseBracketToken)
            {
                if (bracketDepth == 0)
                {
                    return false;
                }
                bracketDepth--;
                continue;
            }

            if (token.Kind == SyntaxKind.CloseBraceToken && parenthesisDepth == 0 && bracketDepth == 0)
            {
                return false;
            }

            if (parenthesisDepth == 0
                && bracketDepth == 0
                && IsWord(token, "as")
                && Peek(offset + 1).Kind == SyntaxKind.IdentifierToken
                && Peek(offset + 2).Kind == SyntaxKind.OpenBraceToken)
            {
                return true;
            }
        }
    }

    private ExpressionSyntax ParseAwaitOperand()
    {
        ExpressionSyntax expression = Current.Kind == SyntaxKind.AwaitKeyword
            ? new AwaitExpressionSyntax(NextToken(), ParseAwaitOperand())
            : ParsePrimaryExpression();

        while (Current.Kind is SyntaxKind.OpenParenToken or SyntaxKind.DotToken or SyntaxKind.OpenBracketToken)
        {
            if (Current.Kind == SyntaxKind.OpenParenToken)
            {
                expression = ParseCallExpression(expression);
            }
            else if (Current.Kind == SyntaxKind.DotToken)
            {
                var dot = Match(SyntaxKind.DotToken);
                expression = new MemberAccessExpressionSyntax(expression, dot, Match(SyntaxKind.IdentifierToken));
            }
            else
            {
                var open = Match(SyntaxKind.OpenBracketToken);
                expression = new IndexExpressionSyntax(expression, open, ParseExpression(), Match(SyntaxKind.CloseBracketToken));
            }
        }

        return expression;
    }

    private ExpressionSyntax ParseNewExpression()
    {
        var keyword = NextToken();
        ExpressionSyntax target = new NameExpressionSyntax(Match(SyntaxKind.IdentifierToken));
        while (Current.Kind == SyntaxKind.DotToken)
        {
            SyntaxToken dot = NextToken();
            target = new MemberAccessExpressionSyntax(target, dot, Match(SyntaxKind.IdentifierToken));
        }

        CallExpressionSyntax call = ParseCallExpression(target);
        return new NewExpressionSyntax(
            keyword,
            target,
            call.OpenParenToken,
            call.Arguments,
            call.CommaTokens,
            call.CloseParenToken);
    }

    private bool IsArrowExpressionAhead()
    {
        if (Current.Kind != SyntaxKind.OpenParenToken) return false;
        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            SyntaxToken token = Peek(offset);
            if (token.Kind == SyntaxKind.EndOfFileToken) return false;
            if (token.Kind == SyntaxKind.OpenParenToken) depth++;
            else if (token.Kind == SyntaxKind.CloseParenToken && --depth == 0)
            {
                var next = Peek(offset + 1);
                return next.Kind is SyntaxKind.ArrowToken or SyntaxKind.ColonToken;
            }
        }
    }

    private CaptureExpressionSyntax ParseCaptureExpression()
    {
        var capture = NextToken();
        var open = Match(SyntaxKind.OpenBraceToken);
        var identifiers = new List<SyntaxToken>();
        var commas = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.CloseBraceToken and not SyntaxKind.EndOfFileToken)
        {
            identifiers.Add(Match(SyntaxKind.IdentifierToken));
            if (Current.Kind != SyntaxKind.CommaToken) break;
            commas.Add(NextToken());
        }
        var close = Match(SyntaxKind.CloseBraceToken);
        if (!IsArrowExpressionAhead() && !(Current.Kind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.ArrowToken))
        {
            _diagnostics.Report("COPE-CALL-0010", "Expected an arrow expression after the capture list.", Current.Position, Math.Max(1, Current.Text.Length));
            return new CaptureExpressionSyntax(capture, open, identifiers, commas, close,
                new ArrowExpressionSyntax(null, [], [], null, null, null, MissingToken(SyntaxKind.ArrowToken, Current.Position), new MissingExpressionSyntax(Current), null));
        }
        var arrow = Current.Kind == SyntaxKind.IdentifierToken ? ParseSingleParameterArrowExpression() : ParseArrowExpression();
        return new CaptureExpressionSyntax(capture, open, identifiers, commas, close, arrow);
    }

    private ArrowExpressionSyntax ParseSingleParameterArrowExpression()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? colon = null;
        TypeSyntax? type = null;
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            colon = NextToken();
            type = ParseTypeSyntax();
        }
        return ParseArrowExpressionCore(null, [new ArrowParameterSyntax(identifier, colon, type)], [], null);
    }

    private ArrowExpressionSyntax ParseArrowExpression()
    {
        var open = Match(SyntaxKind.OpenParenToken);
        var parameters = new List<ArrowParameterSyntax>();
        var commas = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.CloseParenToken and not SyntaxKind.EndOfFileToken)
        {
            var identifier = Match(SyntaxKind.IdentifierToken);
            SyntaxToken? colon = null;
            TypeSyntax? type = null;
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                colon = NextToken();
                type = ParseTypeSyntax();
            }
            parameters.Add(new ArrowParameterSyntax(identifier, colon, type));
            if (Current.Kind != SyntaxKind.CommaToken) break;
            commas.Add(NextToken());
        }
        var close = Match(SyntaxKind.CloseParenToken);
        return ParseArrowExpressionCore(open, parameters, commas, close);
    }

    private ArrowExpressionSyntax ParseArrowExpressionCore(SyntaxToken? open, IReadOnlyList<ArrowParameterSyntax> parameters, IReadOnlyList<SyntaxToken> commas, SyntaxToken? close)
    {
        SyntaxToken? returnColon = null;
        TypeSyntax? returnType = null;
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            returnColon = NextToken();
            returnType = ParseTypeSyntax();
        }
        var arrow = Match(SyntaxKind.ArrowToken);
        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            return new ArrowExpressionSyntax(open, parameters, commas, close, returnColon, returnType, arrow, null, ParseBlockStatement());
        }
        return new ArrowExpressionSyntax(open, parameters, commas, close, returnColon, returnType, arrow, ParseExpression(), null);
    }

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

    private bool IsGenericFunctionSuffixAhead()
    {
        if (Current.Kind != SyntaxKind.LessToken) return false;
        var position = _position + 1;
        var depth = 0;
        while (position < _tokens.Length)
        {
            var token = _tokens[position];
            if (token.Kind is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken) depth++;
            else if (token.Kind is SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken) depth--;
            else if (token.Kind == SyntaxKind.GreaterToken && depth == 0)
            {
                SyntaxKind next = position + 1 < _tokens.Length ? _tokens[position + 1].Kind : SyntaxKind.EndOfFileToken;
                return next is SyntaxKind.OpenParenToken or SyntaxKind.SemicolonToken or SyntaxKind.CommaToken or SyntaxKind.CloseParenToken or SyntaxKind.CloseBraceToken or SyntaxKind.EqualsToken;
            }
            else if (token.Kind is SyntaxKind.SemicolonToken or SyntaxKind.EndOfFileToken) return false;
            position++;
        }
        return false;
    }

    private ExpressionSyntax ParseGenericFunctionExpression(ExpressionSyntax target)
    {
        var less = Match(SyntaxKind.LessToken);
        var typeArguments = new List<TypeSyntax>();
        var typeCommas = new List<SyntaxToken>();
        while (Current.Kind is not SyntaxKind.GreaterToken and not SyntaxKind.EndOfFileToken)
        {
            typeArguments.Add(ParseTypeSyntax());
            if (Current.Kind != SyntaxKind.CommaToken) break;
            typeCommas.Add(NextToken());
        }
        var greater = Match(SyntaxKind.GreaterToken);
        if (Current.Kind != SyntaxKind.OpenParenToken)
        {
            return new GenericFunctionReferenceExpressionSyntax(target, less, typeArguments, typeCommas, greater);
        }

        var call = ParseCallExpression(target);
        return new GenericCallExpressionSyntax(target, less, typeArguments, typeCommas, greater, call.OpenParenToken, call.Arguments, call.CommaTokens, call.CloseParenToken);
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
            var hasColon = Current.Kind == SyntaxKind.ColonToken;
            var colon = hasColon ? NextToken() : MissingToken(SyntaxKind.ColonToken, name.Position + name.Text.Length);
            var value = hasColon
                ? ParseExpression()
                : new NameExpressionSyntax(name);
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

    private TsXmlExpressionSyntax ParseTsXmlExpression()
    {
        SyntaxToken lessToken = MatchTsXml(SyntaxKind.LessToken, "Expected '<' to begin a TS-XML expression.");
        if (Current.Kind == SyntaxKind.GreaterToken)
        {
            return ParseTsXmlFragment(lessToken);
        }

        SyntaxToken nameToken = MatchTsXmlName("Expected an element name after '<'.");
        var attributes = new List<TsXmlAttributeSyntax>();
        while (IsTsXmlName(Current))
        {
            attributes.Add(ParseTsXmlAttribute());
        }

        SyntaxToken? slashToken = null;
        if (Current.Kind == SyntaxKind.SlashToken)
        {
            slashToken = NextToken();
        }

        SyntaxToken openCloseToken = MatchTsXml(SyntaxKind.GreaterToken, "Expected '>' to end the TS-XML opening tag.");
        if (slashToken is not null)
        {
            return new TsXmlElementExpressionSyntax(
                lessToken,
                nameToken,
                attributes,
                slashToken,
                openCloseToken,
                [],
                null,
                null,
                null,
                null);
        }

        var children = ParseTsXmlChildren(openCloseToken.Position + openCloseToken.Text.Length);
        SyntaxToken? closeLessToken = null;
        SyntaxToken? closeSlashToken = null;
        SyntaxToken? closeNameToken = null;
        SyntaxToken? closeGreaterToken = null;
        if (Current.Kind == SyntaxKind.LessToken)
        {
            closeLessToken = NextToken();
            closeSlashToken = MatchTsXml(SyntaxKind.SlashToken, "Expected '/' in the TS-XML closing tag.");
            closeNameToken = MatchTsXmlName("Expected an element name in the TS-XML closing tag.");
            closeGreaterToken = MatchTsXml(SyntaxKind.GreaterToken, "Expected '>' to end the TS-XML closing tag.");

            if (!string.Equals(nameToken.Text, closeNameToken.Text, StringComparison.Ordinal))
            {
                ReportTsXml(
                    "COPE-TSXML-0006",
                    $"TS-XML closing name '{closeNameToken.Text}' does not match opening name '{nameToken.Text}'.",
                    closeNameToken);
            }
        }
        else
        {
            ReportTsXml("COPE-TSXML-0005", $"Expected closing tag '</{nameToken.Text}>'.", Current);
        }

        return new TsXmlElementExpressionSyntax(
            lessToken,
            nameToken,
            attributes,
            null,
            openCloseToken,
            children,
            closeLessToken,
            closeSlashToken,
            closeNameToken,
            closeGreaterToken);
    }

    private TsXmlFragmentExpressionSyntax ParseTsXmlFragment(SyntaxToken lessToken)
    {
        SyntaxToken openCloseToken = MatchTsXml(SyntaxKind.GreaterToken, "Expected '>' to begin the TS-XML fragment.");
        IReadOnlyList<TsXmlChildSyntax> children = ParseTsXmlChildren(openCloseToken.Position + openCloseToken.Text.Length);
        SyntaxToken closeLessToken = MatchTsXml(SyntaxKind.LessToken, "Expected '</>' to close the TS-XML fragment.");
        SyntaxToken closeSlashToken = MatchTsXml(SyntaxKind.SlashToken, "Expected '</>' to close the TS-XML fragment.");
        SyntaxToken closeGreaterToken = MatchTsXml(SyntaxKind.GreaterToken, "Expected '</>' to close the TS-XML fragment.");
        return new TsXmlFragmentExpressionSyntax(
            lessToken,
            openCloseToken,
            children,
            closeLessToken,
            closeSlashToken,
            closeGreaterToken);
    }

    private IReadOnlyList<TsXmlChildSyntax> ParseTsXmlChildren(int textStart)
    {
        var children = new List<TsXmlChildSyntax>();

        while (textStart < _text.Length)
        {
            int specialPosition = FindTsXmlChildBoundary(textStart);
            if (specialPosition > textStart)
            {
                children.Add(CreateTsXmlText(textStart, specialPosition));
                AdvancePastSourcePosition(specialPosition);
                textStart = specialPosition;
            }

            if (textStart >= _text.Length)
            {
                break;
            }

            if (_text[textStart] == '<')
            {
                AdvancePastSourcePosition(textStart);
                if (Current.Kind == SyntaxKind.LessToken && Peek(1).Kind == SyntaxKind.SlashToken)
                {
                    break;
                }

                if (Current.Kind == SyntaxKind.LessToken)
                {
                    TsXmlExpressionSyntax nested = ParseTsXmlExpression();
                    children.Add(new TsXmlElementChildSyntax(nested));
                    textStart = GetTsXmlEndPosition(nested);
                    continue;
                }
            }

            if (_text[textStart] == '{')
            {
                AdvancePastSourcePosition(textStart);
                SyntaxToken openBrace = MatchTsXml(SyntaxKind.OpenBraceToken, "Expected '{' to begin a TS-XML expression child.");
                ExpressionSyntax expression = ParseExpression();
                SyntaxToken closeBrace = MatchTsXml(SyntaxKind.CloseBraceToken, "Expected '}' to close the TS-XML expression child.");
                children.Add(new TsXmlExpressionChildSyntax(openBrace, expression, closeBrace));
                textStart = closeBrace.Position + closeBrace.Text.Length;
                continue;
            }

            break;
        }

        return children;
    }

    private TsXmlAttributeSyntax ParseTsXmlAttribute()
    {
        SyntaxToken nameToken = MatchTsXmlName("Expected a TS-XML attribute name.");
        if (Current.Kind != SyntaxKind.EqualsToken)
        {
            return new TsXmlAttributeSyntax(nameToken, null, null, null, null, null);
        }

        SyntaxToken equalsToken = NextToken();
        if (Current.Kind == SyntaxKind.StringToken)
        {
            return new TsXmlAttributeSyntax(nameToken, equalsToken, NextToken(), null, null, null);
        }

        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            SyntaxToken openBrace = NextToken();
            if (Current.Kind == SyntaxKind.CloseBraceToken)
            {
                ReportTsXml(
                    "COPE-TSXML-0003",
                    "TS-XML attribute expressions cannot be empty.",
                    Current);
                return new TsXmlAttributeSyntax(nameToken, equalsToken, null, openBrace, null, NextToken());
            }

            if (Current.Kind is SyntaxKind.SlashToken or SyntaxKind.GreaterToken)
            {
                ReportTsXml(
                    "COPE-TSXML-0003",
                    "TS-XML attribute values must be a string literal or a braced TypeScript expression.",
                    Current);
                return new TsXmlAttributeSyntax(nameToken, equalsToken, null, openBrace, null, null);
            }

            ExpressionSyntax expression = ParseExpression();
            SyntaxToken closeBrace = MatchTsXml(SyntaxKind.CloseBraceToken, "Expected '}' to close the TS-XML attribute expression.");
            return new TsXmlAttributeSyntax(nameToken, equalsToken, null, openBrace, expression, closeBrace);
        }

        ReportTsXml("COPE-TSXML-0003", "TS-XML attribute values must be a string literal or a braced TypeScript expression.", Current);
        return new TsXmlAttributeSyntax(nameToken, equalsToken, null, null, null, null);
    }

    private TsXmlTextSyntax CreateTsXmlText(int start, int end)
    {
        _tsXmlTextRanges.Add((start, end));
        string text = _text.Substring(start, end - start);
        return new TsXmlTextSyntax(new SyntaxToken(SyntaxKind.TsXmlTextToken, start, text, text));
    }

    private int FindTsXmlChildBoundary(int start)
    {
        for (int position = start; position < _text.Length; position++)
        {
            if (_text[position] is '<' or '{')
            {
                return position;
            }
        }

        return _text.Length;
    }

    private static int GetTsXmlEndPosition(TsXmlExpressionSyntax expression)
        => expression switch
        {
            TsXmlElementExpressionSyntax element when element.CloseGreaterToken is not null
                => element.CloseGreaterToken.Position + element.CloseGreaterToken.Text.Length,
            TsXmlElementExpressionSyntax element
                => element.OpenCloseToken.Position + element.OpenCloseToken.Text.Length,
            TsXmlFragmentExpressionSyntax fragment
                => fragment.CloseGreaterToken.Position + fragment.CloseGreaterToken.Text.Length,
            _ => throw new InvalidOperationException("Expected a TS-XML syntax expression."),
        };

    private void AdvancePastSourcePosition(int position)
    {
        while (Current.Kind != SyntaxKind.EndOfFileToken && Current.Position < position)
        {
            NextToken();
        }
    }

    private SyntaxToken MatchTsXmlName(string message)
    {
        if (IsTsXmlName(Current))
        {
            return NextToken();
        }

        ReportTsXml("COPE-TSXML-0002", message, Current);
        return MissingToken(SyntaxKind.IdentifierToken, Current.Position);
    }

    private static bool IsTsXmlName(SyntaxToken token)
        => token.Kind == SyntaxKind.IdentifierToken;

    private SyntaxToken MatchTsXml(SyntaxKind kind, string message)
    {
        if (Current.Kind == kind)
        {
            return NextToken();
        }

        ReportTsXml("COPE-TSXML-0002", message, Current);
        return MissingToken(kind, Current.Position);
    }

    private void ReportTsXml(string diagnosticId, string message, SyntaxToken token)
    {
        _diagnostics.Report(diagnosticId, message, token.Position, Math.Max(1, token.Text.Length));
    }

    private bool IsTsXmlTextDiagnostic(Diagnostic diagnostic)
        => _tsXmlTextRanges.Any(range => diagnostic.Position >= range.Start && diagnostic.Position < range.End);

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
