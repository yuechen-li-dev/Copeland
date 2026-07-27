namespace Copeland.TS.Syntax;

public abstract record SyntaxNode
{
    public abstract SyntaxKind Kind { get; }

    public abstract IEnumerable<object> GetChildren();
}

public abstract record MemberSyntax : SyntaxNode;

public abstract record StatementSyntax : SyntaxNode;

public abstract record ExpressionSyntax : SyntaxNode;
public abstract record TypeSyntax : SyntaxNode;

public sealed record TemplateExpressionSyntax(SyntaxToken TemplateToken, IReadOnlyList<TemplatePartSyntax> Parts) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TemplateExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return TemplateToken;
        foreach (TemplatePartSyntax part in Parts)
        {
            yield return part;
        }
    }
}

public abstract record TemplatePartSyntax : SyntaxNode;

public sealed record TemplateTextPartSyntax(string Text) : TemplatePartSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TemplateTextPart;
    public override IEnumerable<object> GetChildren() => [];
}

public sealed record TemplateInterpolationPartSyntax(ExpressionSyntax Expression) : TemplatePartSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TemplateInterpolationPart;
    public override IEnumerable<object> GetChildren() => [Expression];
}

public sealed record CompilationUnitSyntax(IReadOnlyList<MemberSyntax> Members, SyntaxToken EndOfFileToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.CompilationUnit;

    public override IEnumerable<object> GetChildren()
    {
        foreach (var member in Members)
        {
            yield return member;
        }

        yield return EndOfFileToken;
    }
}

public sealed record GlobalStatementMemberSyntax(StatementSyntax Statement) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.GlobalStatementMember;

    public override IEnumerable<object> GetChildren()
    {
        yield return Statement;
    }
}

public sealed record PredefinedTypeSyntax(SyntaxToken Keyword) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.PredefinedType;

    public override IEnumerable<object> GetChildren()
    {
        yield return Keyword;
    }
}

public sealed record IdentifierTypeSyntax(SyntaxToken Identifier) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.IdentifierType;

    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
    }
}

public sealed record ArrayTypeSyntax(TypeSyntax ElementType, SyntaxToken OpenBracketToken, SyntaxToken CloseBracketToken) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ArrayType;

    public override IEnumerable<object> GetChildren()
    {
        yield return ElementType;
        yield return OpenBracketToken;
        yield return CloseBracketToken;
    }
}

/// <summary>
/// Preserves a TypeScript import for profile-owned validation. The base language
/// intentionally gives imports no executable meaning.
/// </summary>
public sealed record ImportDeclarationSyntax(IReadOnlyList<SyntaxToken> Tokens) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ImportDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        foreach (SyntaxToken token in Tokens)
        {
            yield return token;
        }
    }
}

/// <summary>
/// A module-level CLR namespace or named-type import. This is intentionally a
/// different syntax node from TypeScript's resource <c>using</c> declaration.
/// </summary>
public sealed record ClrUsingDirectiveSyntax(
    SyntaxToken UsingKeyword,
    IReadOnlyList<SyntaxToken> NameParts,
    IReadOnlyList<SyntaxToken> DotTokens,
    SyntaxToken SemicolonToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ClrUsingDirective;

    public string QualifiedName => string.Join('.', NameParts.Select(part => part.Text));

    public override IEnumerable<object> GetChildren()
    {
        yield return UsingKeyword;
        for (int index = 0; index < NameParts.Count; index++)
        {
            yield return NameParts[index];
            if (index < DotTokens.Count)
            {
                yield return DotTokens[index];
            }
        }

        yield return SemicolonToken;
    }
}

/// <summary>
/// Preserves an export-default wrapper for a profile-owned document root.
/// </summary>
public sealed record ExportDefaultDeclarationSyntax(
    SyntaxToken ExportToken,
    SyntaxToken DefaultToken,
    ExpressionSyntax Expression,
    SyntaxToken? SemicolonToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ExportDefaultDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        yield return ExportToken;
        yield return DefaultToken;
        yield return Expression;
        if (SemicolonToken is not null)
        {
            yield return SemicolonToken;
        }
    }
}

public sealed record AsyncTypeSyntax(
    SyntaxToken AsyncKeyword,
    SyntaxToken LessToken,
    TypeSyntax EventualType,
    SyntaxToken GreaterToken) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.AsyncType;

    public override IEnumerable<object> GetChildren()
    {
        yield return AsyncKeyword;
        yield return LessToken;
        yield return EventualType;
        yield return GreaterToken;
    }
}

public sealed record IterableTypeSyntax(
    SyntaxToken IterableIdentifier,
    SyntaxToken LessToken,
    TypeSyntax ElementType,
    SyntaxToken GreaterToken) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.IterableType;

    public override IEnumerable<object> GetChildren()
    {
        yield return IterableIdentifier;
        yield return LessToken;
        yield return ElementType;
        yield return GreaterToken;
    }
}

public sealed record TypeAliasDeclarationSyntax(
    SyntaxToken TypeKeyword,
    SyntaxToken Identifier,
    IReadOnlyList<SyntaxToken> TypeParameterTokens,
    SyntaxToken EqualsToken,
    TypeSyntax TargetType,
    IReadOnlyList<SyntaxToken> UnsupportedTokens,
    SyntaxToken SemicolonToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TypeAliasDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        yield return TypeKeyword;
        yield return Identifier;
        foreach (var token in TypeParameterTokens)
        {
            yield return token;
        }

        yield return EqualsToken;
        yield return TargetType;
        foreach (var token in UnsupportedTokens)
        {
            yield return token;
        }

        yield return SemicolonToken;
    }
}

/// <summary>
/// A compilation-unit nominal union declaration. Pipe spelling is deliberately
/// not admitted into general TypeSyntax.
/// </summary>
public sealed record NominalUnionDeclarationSyntax(
    SyntaxToken TypeKeyword,
    SyntaxToken Identifier,
    SyntaxToken EqualsToken,
    SyntaxToken? LeadingPipeToken,
    IReadOnlyList<SyntaxToken> Alternatives,
    IReadOnlyList<SyntaxToken> PipeTokens,
    SyntaxToken SemicolonToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.NominalUnionDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        yield return TypeKeyword;
        yield return Identifier;
        yield return EqualsToken;
        if (LeadingPipeToken is not null)
        {
            yield return LeadingPipeToken;
        }

        for (var index = 0; index < Alternatives.Count; index++)
        {
            yield return Alternatives[index];
            if (index < PipeTokens.Count)
            {
                yield return PipeTokens[index];
            }
        }

        yield return SemicolonToken;
    }
}

public sealed record InterfaceFieldSyntax(
    SyntaxToken Identifier,
    SyntaxToken ColonToken,
    TypeSyntax Type,
    IReadOnlyList<SyntaxToken> UnsupportedTokens,
    SyntaxToken SemicolonToken,
    bool HasExplicitType,
    bool HasTerminator) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.InterfaceField;
    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        yield return ColonToken;
        yield return Type;
        foreach (var token in UnsupportedTokens) yield return token;
        yield return SemicolonToken;
    }
}

public sealed record InterfaceDeclarationSyntax(
    SyntaxToken InterfaceKeyword,
    SyntaxToken Identifier,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<InterfaceFieldSyntax> Fields,
    SyntaxToken CloseBraceToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.InterfaceDeclaration;
    public override IEnumerable<object> GetChildren()
    {
        yield return InterfaceKeyword;
        yield return Identifier;
        yield return OpenBraceToken;
        foreach (var field in Fields) yield return field;
        yield return CloseBraceToken;
    }
}

public sealed record TypeParameterSyntax(
    SyntaxToken Identifier,
    SyntaxToken? ExtendsKeyword,
    IReadOnlyList<SyntaxToken> RequirementNames,
    IReadOnlyList<SyntaxToken> AmpersandTokens) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.TypeParameter;
    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        if (ExtendsKeyword is not null) yield return ExtendsKeyword;
        for (var i = 0; i < RequirementNames.Count; i++)
        {
            if (i > 0) yield return AmpersandTokens[i - 1];
            yield return RequirementNames[i];
        }
    }
}

public sealed record ParenthesizedTypeSyntax(SyntaxToken OpenParenToken, TypeSyntax Type, SyntaxToken CloseParenToken) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ParenthesizedType;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenParenToken;
        yield return Type;
        yield return CloseParenToken;
    }
}

public sealed record ResultTypeSyntax(TypeSyntax SuccessType, SyntaxToken BangToken, TypeSyntax ErrorType) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ResultType;

    public override IEnumerable<object> GetChildren()
    {
        yield return SuccessType;
        yield return BangToken;
        yield return ErrorType;
    }
}

public sealed record CallableTypeParameterSyntax(SyntaxToken Identifier, SyntaxToken ColonToken, TypeSyntax Type) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.Parameter;

    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        yield return ColonToken;
        yield return Type;
    }
}

public sealed record CallableTypeSyntax(
    SyntaxToken OpenParenToken,
    IReadOnlyList<CallableTypeParameterSyntax> Parameters,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseParenToken,
    SyntaxToken ArrowToken,
    TypeSyntax ReturnType) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.CallableType;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenParenToken;
        foreach (var parameter in Parameters) yield return parameter;
        foreach (var comma in CommaTokens) yield return comma;
        yield return CloseParenToken;
        yield return ArrowToken;
        yield return ReturnType;
    }
}

public sealed record QualifiedRowTypeSyntax(SyntaxToken TableIdentifier, SyntaxToken DotToken, SyntaxToken RowIdentifier) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.QualifiedRowType;
    public override IEnumerable<object> GetChildren() { yield return TableIdentifier; yield return DotToken; yield return RowIdentifier; }
}

public sealed record ColumnTypeSyntax(SyntaxToken ColumnKeyword, TypeSyntax ElementType) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ColumnType;
    public override IEnumerable<object> GetChildren() { yield return ColumnKeyword; yield return ElementType; }
}

public sealed record ParameterSyntax(SyntaxToken Identifier, SyntaxToken? ColonToken, TypeSyntax? Type) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.Parameter;

    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        if (ColonToken is not null)
        {
            yield return ColonToken;
        }

        if (Type is not null)
        {
            yield return Type;
        }
    }
}

public sealed record FunctionDeclarationSyntax(
    SyntaxToken? RemoteKeyword,
    SyntaxToken? AsyncKeyword,
    SyntaxToken FunctionKeyword,
    SyntaxToken? GeneratorStarToken,
    SyntaxToken Identifier,
    SyntaxToken? LessToken,
    IReadOnlyList<TypeParameterSyntax> TypeParameters,
    IReadOnlyList<SyntaxToken> TypeParameterCommas,
    SyntaxToken? GreaterToken,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ParameterSyntax> Parameters,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseParenToken,
    SyntaxToken? ReturnTypeColonToken,
    TypeSyntax? ReturnType,
    BlockStatementSyntax Body) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.FunctionDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        if (RemoteKeyword is not null) yield return RemoteKeyword;
        if (AsyncKeyword is not null) yield return AsyncKeyword;
        yield return FunctionKeyword;
        if (GeneratorStarToken is not null) yield return GeneratorStarToken;
        yield return Identifier;
        if (LessToken is not null) yield return LessToken;
        for (var i = 0; i < TypeParameters.Count; i++)
        {
            if (i > 0) yield return TypeParameterCommas[i - 1];
            yield return TypeParameters[i];
        }
        if (GreaterToken is not null) yield return GreaterToken;
        yield return OpenParenToken;

        for (var i = 0; i < Parameters.Count; i++)
        {
            if (i > 0)
            {
                yield return CommaTokens[i - 1];
            }

            yield return Parameters[i];
        }

        yield return CloseParenToken;
        if (ReturnTypeColonToken is not null)
        {
            yield return ReturnTypeColonToken;
        }

        if (ReturnType is not null)
        {
            yield return ReturnType;
        }
        yield return Body;
    }
}

public sealed record EnumDeclarationSyntax(
    SyntaxToken EnumKeyword,
    SyntaxToken Identifier,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<EnumCaseSyntax> Cases,
    SyntaxToken CloseBraceToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.EnumDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        yield return EnumKeyword;
        yield return Identifier;
        yield return OpenBraceToken;
        foreach (var @case in Cases)
        {
            yield return @case;
        }

        yield return CloseBraceToken;
    }
}

public sealed record EnumCaseSyntax(
    SyntaxToken Identifier,
    SyntaxToken? OpenParenToken,
    IReadOnlyList<EnumPayloadFieldSyntax> PayloadFields,
    SyntaxToken? CloseParenToken,
    SyntaxToken? CommaToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.EnumCase;

    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        if (OpenParenToken is not null)
        {
            yield return OpenParenToken;
        }

        foreach (var field in PayloadFields)
        {
            yield return field;
        }

        if (CloseParenToken is not null)
        {
            yield return CloseParenToken;
        }

        if (CommaToken is not null)
        {
            yield return CommaToken;
        }
    }
}

public sealed record EnumPayloadFieldSyntax(
    SyntaxToken Identifier,
    SyntaxToken ColonToken,
    TypeSyntax Type,
    SyntaxToken? CommaToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.EnumPayloadField;

    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        yield return ColonToken;
        yield return Type;
        if (CommaToken is not null)
        {
            yield return CommaToken;
        }
    }
}

public sealed record BlockStatementSyntax(
    SyntaxToken OpenBraceToken,
    IReadOnlyList<StatementSyntax> Statements,
    SyntaxToken CloseBraceToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.BlockStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenBraceToken;
        foreach (var statement in Statements)
        {
            yield return statement;
        }

        yield return CloseBraceToken;
    }
}

public sealed record VariableDeclarationStatementSyntax(
    SyntaxToken Keyword,
    SyntaxToken Identifier,
    SyntaxToken? TypeColonToken,
    TypeSyntax? Type,
    SyntaxToken EqualsToken,
    ExpressionSyntax Initializer,
    SyntaxToken SemicolonToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.VariableDeclarationStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return Keyword;
        yield return Identifier;
        if (TypeColonToken is not null)
        {
            yield return TypeColonToken;
        }

        if (Type is not null)
        {
            yield return Type;
        }

        yield return EqualsToken;
        yield return Initializer;
        yield return SemicolonToken;
    }
}

public sealed record ResourceUsingDeclarationStatementSyntax(
    SyntaxToken? AwaitKeyword,
    SyntaxToken UsingKeyword,
    SyntaxToken Identifier,
    SyntaxToken EqualsToken,
    ExpressionSyntax Initializer,
    SyntaxToken SemicolonToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ResourceUsingDeclarationStatement;

    public override IEnumerable<object> GetChildren()
    {
        if (AwaitKeyword is not null)
        {
            yield return AwaitKeyword;
        }

        yield return UsingKeyword;
        yield return Identifier;
        yield return EqualsToken;
        yield return Initializer;
        yield return SemicolonToken;
    }
}

/// <summary>Module-level, compiler-owned event automaton declaration.</summary>
public sealed record FlowDeclarationSyntax(
    SyntaxToken FlowKeyword,
    SyntaxToken Identifier,
    SyntaxToken? ResultArrowToken,
    TypeSyntax? ResultType,
    SyntaxToken OpenBraceToken,
    FlowBoardSyntax? Board,
    IReadOnlyList<FlowEventSyntax> Events,
    IReadOnlyList<FlowStateSyntax> States,
    SyntaxToken CloseBraceToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.FlowDeclaration;
    public override IEnumerable<object> GetChildren()
    {
        yield return FlowKeyword; yield return Identifier;
        if (ResultArrowToken is not null) yield return ResultArrowToken;
        if (ResultType is not null) yield return ResultType;
        yield return OpenBraceToken;
        if (Board is not null) yield return Board;
        foreach (var @event in Events) yield return @event;
        foreach (var state in States) yield return state;
        yield return CloseBraceToken;
    }
}

public sealed record FlowBoardSyntax(SyntaxToken BoardKeyword, SyntaxToken OpenBraceToken, IReadOnlyList<FlowBoardFieldSyntax> Fields, SyntaxToken CloseBraceToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.FlowBoard;
    public override IEnumerable<object> GetChildren()
    {
        yield return BoardKeyword; yield return OpenBraceToken;
        foreach (var field in Fields) yield return field;
        yield return CloseBraceToken;
    }
}

public sealed record FlowBoardFieldSyntax(SyntaxToken Identifier, SyntaxToken ColonToken, TypeSyntax Type, SyntaxToken? EqualsToken, ExpressionSyntax? Initializer, SyntaxToken SemicolonToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.FlowBoardField;
    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier; yield return ColonToken; yield return Type;
        if (EqualsToken is not null) yield return EqualsToken;
        if (Initializer is not null) yield return Initializer;
        yield return SemicolonToken;
    }
}

public sealed record FlowEventSyntax(SyntaxToken EventKeyword, SyntaxToken Identifier, SyntaxToken OpenParenToken, IReadOnlyList<ParameterSyntax> Parameters, IReadOnlyList<SyntaxToken> CommaTokens, SyntaxToken CloseParenToken, SyntaxToken SemicolonToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.FlowEvent;
    public override IEnumerable<object> GetChildren()
    {
        yield return EventKeyword; yield return Identifier; yield return OpenParenToken;
        foreach (var parameter in Parameters) yield return parameter;
        foreach (var comma in CommaTokens) yield return comma;
        yield return CloseParenToken; yield return SemicolonToken;
    }
}

public sealed record FlowStateSyntax(SyntaxToken StateKeyword, SyntaxToken Identifier, SyntaxToken? InitialKeyword, SyntaxToken OpenBraceToken, IReadOnlyList<FlowTransitionSyntax> Transitions, FlowTerminalSyntax? Terminal, SyntaxToken CloseBraceToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.FlowState;
    public override IEnumerable<object> GetChildren()
    {
        yield return StateKeyword; yield return Identifier;
        if (InitialKeyword is not null) yield return InitialKeyword;
        yield return OpenBraceToken;
        foreach (var transition in Transitions) yield return transition;
        if (Terminal is not null) yield return Terminal;
        yield return CloseBraceToken;
    }
}

public sealed record FlowTransitionSyntax(SyntaxToken OnKeyword, SyntaxToken EventIdentifier, SyntaxToken OpenParenToken, IReadOnlyList<SyntaxToken> Bindings, IReadOnlyList<SyntaxToken> CommaTokens, SyntaxToken CloseParenToken, SyntaxToken? WhenKeyword, ExpressionSyntax? Guard, SyntaxToken ArrowToken, SyntaxToken TargetIdentifier, BlockStatementSyntax? Body, SyntaxToken SemicolonToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.FlowTransition;
    public override IEnumerable<object> GetChildren()
    {
        yield return OnKeyword; yield return EventIdentifier; yield return OpenParenToken;
        foreach (var binding in Bindings) yield return binding;
        foreach (var comma in CommaTokens) yield return comma;
        yield return CloseParenToken;
        if (WhenKeyword is not null) yield return WhenKeyword;
        if (Guard is not null) yield return Guard;
        yield return ArrowToken; yield return TargetIdentifier;
        if (Body is not null) yield return Body;
        yield return SemicolonToken;
    }
}

public sealed record FlowTerminalSyntax(SyntaxToken Keyword, ExpressionSyntax? Expression, SyntaxToken SemicolonToken) : SyntaxNode
{
    public override SyntaxKind Kind => Keyword.Text == "finish" ? SyntaxKind.FlowFinish : SyntaxKind.FlowFail;
    public override IEnumerable<object> GetChildren() { yield return Keyword; if (Expression is not null) yield return Expression; yield return SemicolonToken; }
}

public sealed record CSharpBlockStatementSyntax(
    SyntaxToken CSharpKeyword,
    SyntaxToken OpenBraceToken,
    string BodyText,
    int BodyPosition,
    SyntaxToken CloseBraceToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.CSharpBlockStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return CSharpKeyword;
        yield return OpenBraceToken;
        yield return CloseBraceToken;
    }
}

public sealed record ExpressionStatementSyntax(
    ExpressionSyntax Expression,
    SyntaxToken SemicolonToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ExpressionStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return Expression;
        yield return SemicolonToken;
    }
}

public sealed record IfStatementSyntax(
    SyntaxToken IfKeyword,
    SyntaxToken OpenParenToken,
    ExpressionSyntax Condition,
    SyntaxToken CloseParenToken,
    StatementSyntax ThenStatement,
    SyntaxToken? ElseKeyword,
    StatementSyntax? ElseStatement) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.IfStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return IfKeyword;
        yield return OpenParenToken;
        yield return Condition;
        yield return CloseParenToken;
        yield return ThenStatement;

        if (ElseKeyword is not null)
        {
            yield return ElseKeyword;
        }

        if (ElseStatement is not null)
        {
            yield return ElseStatement;
        }
    }
}

public sealed record WhileStatementSyntax(
    SyntaxToken WhileKeyword,
    SyntaxToken OpenParenToken,
    ExpressionSyntax Condition,
    SyntaxToken CloseParenToken,
    StatementSyntax Body) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.WhileStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return WhileKeyword;
        yield return OpenParenToken;
        yield return Condition;
        yield return CloseParenToken;
        yield return Body;
    }
}

public sealed record ForStatementSyntax(
    SyntaxToken ForKeyword,
    SyntaxToken OpenParenToken,
    SyntaxNode? Initializer,
    SyntaxToken FirstSemicolonToken,
    ExpressionSyntax? Condition,
    SyntaxToken SecondSemicolonToken,
    ExpressionSyntax? Increment,
    SyntaxToken CloseParenToken,
    StatementSyntax Body) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ForStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return ForKeyword;
        yield return OpenParenToken;

        if (Initializer is not null)
        {
            yield return Initializer;
        }

        yield return FirstSemicolonToken;

        if (Condition is not null)
        {
            yield return Condition;
        }

        yield return SecondSemicolonToken;

        if (Increment is not null)
        {
            yield return Increment;
        }

        yield return CloseParenToken;
        yield return Body;
    }
}

public sealed record ForOfStatementSyntax(
    SyntaxToken ForKeyword,
    SyntaxToken OpenParenToken,
    SyntaxToken DeclarationKeyword,
    SyntaxToken Identifier,
    SyntaxToken OfKeyword,
    ExpressionSyntax Iterable,
    SyntaxToken CloseParenToken,
    StatementSyntax Body) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ForOfStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return ForKeyword;
        yield return OpenParenToken;
        yield return DeclarationKeyword;
        yield return Identifier;
        yield return OfKeyword;
        yield return Iterable;
        yield return CloseParenToken;
        yield return Body;
    }
}

public sealed record ReturnStatementSyntax(
    SyntaxToken ReturnKeyword,
    ExpressionSyntax? Expression,
    SyntaxToken SemicolonToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ReturnStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return ReturnKeyword;
        if (Expression is not null)
        {
            yield return Expression;
        }

        yield return SemicolonToken;
    }
}

public sealed record YieldStatementSyntax(
    SyntaxToken YieldKeyword,
    SyntaxToken? ReturnKeyword,
    SyntaxToken? StarToken,
    SyntaxToken? BreakKeyword,
    ExpressionSyntax? Expression,
    SyntaxToken SemicolonToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.YieldStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return YieldKeyword;
        if (ReturnKeyword is not null) yield return ReturnKeyword;
        if (StarToken is not null) yield return StarToken;
        if (BreakKeyword is not null) yield return BreakKeyword;
        if (Expression is not null) yield return Expression;
        yield return SemicolonToken;
    }
}

public sealed record NameExpressionSyntax(SyntaxToken IdentifierToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.NameExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return IdentifierToken;
    }
}

public sealed record LiteralExpressionSyntax(SyntaxToken LiteralToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.LiteralExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return LiteralToken;
    }
}

public sealed record ParenthesizedExpressionSyntax(SyntaxToken OpenParenToken, ExpressionSyntax Expression, SyntaxToken CloseParenToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ParenthesizedExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenParenToken;
        yield return Expression;
        yield return CloseParenToken;
    }
}

public sealed record UnaryExpressionSyntax(SyntaxToken OperatorToken, ExpressionSyntax Operand) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.UnaryExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return OperatorToken;
        yield return Operand;
    }
}

public sealed record BinaryExpressionSyntax(ExpressionSyntax Left, SyntaxToken OperatorToken, ExpressionSyntax Right) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.BinaryExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Left;
        yield return OperatorToken;
        yield return Right;
    }
}

public sealed record AssignmentExpressionSyntax(ExpressionSyntax Left, SyntaxToken EqualsToken, ExpressionSyntax Right) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.AssignmentExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Left;
        yield return EqualsToken;
        yield return Right;
    }
}

public sealed record CallExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ExpressionSyntax> Arguments,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseParenToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.CallExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Target;
        yield return OpenParenToken;

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i > 0)
            {
                yield return CommaTokens[i - 1];
            }

            yield return Arguments[i];
        }

        yield return CloseParenToken;
    }
}

public sealed record NewExpressionSyntax(
    SyntaxToken NewKeyword,
    ExpressionSyntax Target,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ExpressionSyntax> Arguments,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseParenToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.NewExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return NewKeyword;
        yield return Target;
        yield return OpenParenToken;
        for (int index = 0; index < Arguments.Count; index++)
        {
            if (index > 0)
            {
                yield return CommaTokens[index - 1];
            }

            yield return Arguments[index];
        }

        yield return CloseParenToken;
    }
}

public sealed record MemberAccessExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken DotToken,
    SyntaxToken NameToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.MemberAccessExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Target;
        yield return DotToken;
        yield return NameToken;
    }
}

public sealed record ArrayLiteralExpressionSyntax(
    SyntaxToken OpenBracketToken,
    IReadOnlyList<ExpressionSyntax> Elements,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseBracketToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ArrayLiteralExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenBracketToken;

        for (var i = 0; i < Elements.Count; i++)
        {
            if (i > 0)
            {
                yield return CommaTokens[i - 1];
            }

            yield return Elements[i];
        }

        yield return CloseBracketToken;
    }
}

public sealed record PropagateExpressionSyntax(ExpressionSyntax Operand, SyntaxToken QuestionToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.PropagateExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Operand;
        yield return QuestionToken;
    }
}

public sealed record BatchExpressionSyntax(
    SyntaxToken BatchKeyword,
    ExpressionSyntax Input,
    SyntaxToken AsKeyword,
    SyntaxToken ItemIdentifier,
    BlockStatementSyntax Body) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.BatchExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return BatchKeyword;
        yield return Input;
        yield return AsKeyword;
        yield return ItemIdentifier;
        yield return Body;
    }
}

public sealed record GenericCallExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken LessToken,
    IReadOnlyList<TypeSyntax> TypeArguments,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken GreaterToken,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ExpressionSyntax> Arguments,
    IReadOnlyList<SyntaxToken> ArgumentCommas,
    SyntaxToken CloseParenToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.GenericCallExpression;
    public override IEnumerable<object> GetChildren()
    {
        yield return Target;
        yield return LessToken;
        for (var i = 0; i < TypeArguments.Count; i++)
        {
            if (i > 0) yield return CommaTokens[i - 1];
            yield return TypeArguments[i];
        }
        yield return GreaterToken;
        yield return OpenParenToken;
        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i > 0) yield return ArgumentCommas[i - 1];
            yield return Arguments[i];
        }
        yield return CloseParenToken;
    }
}

public sealed record GenericFunctionReferenceExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken LessToken,
    IReadOnlyList<TypeSyntax> TypeArguments,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken GreaterToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.GenericFunctionReferenceExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Target;
        yield return LessToken;
        foreach (var argument in TypeArguments) yield return argument;
        foreach (var comma in CommaTokens) yield return comma;
        yield return GreaterToken;
    }
}

public sealed record BreakStatementSyntax(
    SyntaxToken BreakKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.BreakStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return BreakKeyword;
        yield return SemicolonToken;
    }
}

public sealed record ContinueStatementSyntax(
    SyntaxToken ContinueKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ContinueStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return ContinueKeyword;
        yield return SemicolonToken;
    }
}

public sealed record IndexExpressionSyntax(ExpressionSyntax Target, SyntaxToken OpenBracketToken, ExpressionSyntax Index, SyntaxToken CloseBracketToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.IndexExpression;
    public override IEnumerable<object> GetChildren() { yield return Target; yield return OpenBracketToken; yield return Index; yield return CloseBracketToken; }
}

public sealed record NestedRecordDeclarationStatementSyntax(RecordDeclarationSyntax Declaration) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.NestedRecordDeclarationStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return Declaration;
    }
}

public sealed record NestedTableDeclarationStatementSyntax(TableDeclarationSyntax Declaration) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.NestedTableDeclarationStatement;

    public override IEnumerable<object> GetChildren()
    {
        yield return Declaration;
    }
}

public sealed record RecordDeclarationSyntax(
    SyntaxToken? ConstKeyword,
    SyntaxToken RecordKeyword,
    SyntaxToken Identifier,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<RecordFieldSyntax> Fields,
    SyntaxToken CloseBraceToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.RecordDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        if (ConstKeyword is not null)
        {
            yield return ConstKeyword;
        }
        yield return RecordKeyword;
        yield return Identifier;
        yield return OpenBraceToken;
        foreach (var field in Fields)
        {
            yield return field;
        }
        yield return CloseBraceToken;
    }
}

public sealed record RecordFieldSyntax(
    SyntaxToken Identifier,
    SyntaxToken ColonToken,
    TypeSyntax Type,
    IReadOnlyList<SyntaxToken> UnsupportedTokens,
    SyntaxToken SemicolonToken,
    bool HasExplicitType,
    bool HasTerminator) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.RecordField;

    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        yield return ColonToken;
        yield return Type;
        foreach (var token in UnsupportedTokens)
        {
            yield return token;
        }
        yield return SemicolonToken;
    }
}

/// <summary>
/// The intentionally small class grammar. Class members are not general TypeScript
/// declarations: fields are immutable product slots and member functions are
/// associated functions with no receiver.
/// </summary>
public sealed record ClassDeclarationSyntax(
    SyntaxToken ClassKeyword,
    SyntaxToken Identifier,
    SyntaxToken? ExtendsKeyword,
    SyntaxToken? BaseTypeIdentifier,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<ClassMemberSyntax> Members,
    SyntaxToken CloseBraceToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ClassDeclaration;
    public override IEnumerable<object> GetChildren()
    {
        yield return ClassKeyword;
        yield return Identifier;
        if (ExtendsKeyword is not null) yield return ExtendsKeyword;
        if (BaseTypeIdentifier is not null) yield return BaseTypeIdentifier;
        yield return OpenBraceToken;
        foreach (var member in Members) yield return member;
        yield return CloseBraceToken;
    }
}

public abstract record ClassMemberSyntax : SyntaxNode
{
    public abstract SyntaxToken NameToken { get; }
}

public sealed record ClassFieldSyntax(
    SyntaxToken? VisibilityKeyword,
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken Identifier,
    SyntaxToken ColonToken,
    TypeSyntax Type,
    SyntaxToken? EqualsToken,
    ExpressionSyntax? Initializer,
    SyntaxToken SemicolonToken,
    bool HasExplicitType,
    bool HasTerminator) : ClassMemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ClassField;
    public override SyntaxToken NameToken => Identifier;
    public override IEnumerable<object> GetChildren()
    {
        if (VisibilityKeyword is not null) yield return VisibilityKeyword;
        foreach (var modifier in Modifiers) yield return modifier;
        yield return Identifier;
        yield return ColonToken;
        yield return Type;
        if (EqualsToken is not null) yield return EqualsToken;
        if (Initializer is not null) yield return Initializer;
        yield return SemicolonToken;
    }
}

public sealed record ClassConstructorDeclarationSyntax(
    SyntaxToken? VisibilityKeyword,
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken ConstructorKeyword,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ParameterSyntax> Parameters,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseParenToken,
    SyntaxToken? ReturnTypeColonToken,
    TypeSyntax? ReturnType,
    BlockStatementSyntax Body) : ClassMemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ClassConstructor;
    public override SyntaxToken NameToken => ConstructorKeyword;
    public override IEnumerable<object> GetChildren()
    {
        if (VisibilityKeyword is not null) yield return VisibilityKeyword;
        foreach (var modifier in Modifiers) yield return modifier;
        yield return ConstructorKeyword;
        yield return OpenParenToken;
        foreach (var parameter in Parameters) yield return parameter;
        foreach (var comma in CommaTokens) yield return comma;
        yield return CloseParenToken;
        if (ReturnTypeColonToken is not null) yield return ReturnTypeColonToken;
        if (ReturnType is not null) yield return ReturnType;
        yield return Body;
    }
}

public sealed record ClassAssociatedFunctionDeclarationSyntax(
    SyntaxToken? VisibilityKeyword,
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken Identifier,
    SyntaxToken? LessToken,
    IReadOnlyList<TypeParameterSyntax> TypeParameters,
    IReadOnlyList<SyntaxToken> TypeParameterCommas,
    SyntaxToken? GreaterToken,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ParameterSyntax> Parameters,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseParenToken,
    SyntaxToken? ReturnTypeColonToken,
    TypeSyntax? ReturnType,
    BlockStatementSyntax Body) : ClassMemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ClassAssociatedFunction;
    public override SyntaxToken NameToken => Identifier;
    public override IEnumerable<object> GetChildren()
    {
        if (VisibilityKeyword is not null) yield return VisibilityKeyword;
        foreach (var modifier in Modifiers) yield return modifier;
        yield return Identifier;
        if (LessToken is not null) yield return LessToken;
        foreach (var parameter in TypeParameters) yield return parameter;
        foreach (var comma in TypeParameterCommas) yield return comma;
        if (GreaterToken is not null) yield return GreaterToken;
        yield return OpenParenToken;
        foreach (var parameter in Parameters) yield return parameter;
        foreach (var comma in CommaTokens) yield return comma;
        yield return CloseParenToken;
        if (ReturnTypeColonToken is not null) yield return ReturnTypeColonToken;
        if (ReturnType is not null) yield return ReturnType;
        yield return Body;
    }
}

public sealed record TableAssetClauseSyntax(
    SyntaxToken FromToken,
    CallExpressionSyntax AssetCall) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.TableAssetClause;

    public override IEnumerable<object> GetChildren()
    {
        yield return FromToken;
        yield return AssetCall;
    }
}

public sealed record TableColumnSyntax(
    SyntaxToken Identifier,
    SyntaxToken ColonToken,
    TypeSyntax? ExplicitType,
    SyntaxToken? EqualsToken,
    ArrayLiteralExpressionSyntax Cells,
    SyntaxToken SemicolonToken,
    bool HasInlineData = true) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.TableColumn;
    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier; yield return ColonToken;
        if (ExplicitType is not null) yield return ExplicitType;
        if (EqualsToken is not null) yield return EqualsToken;
        yield return Cells; yield return SemicolonToken;
    }
}

public sealed record TableDeclarationSyntax(
    SyntaxToken RecordKeyword,
    SyntaxToken TableKeyword,
    SyntaxToken Identifier,
    TableAssetClauseSyntax? AssetClause,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<TableColumnSyntax> Columns,
    SyntaxToken CloseBraceToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TableDeclaration;
    public override IEnumerable<object> GetChildren()
    {
        yield return RecordKeyword;
        yield return TableKeyword;
        yield return Identifier;
        if (AssetClause is not null)
        {
            yield return AssetClause;
        }
        yield return OpenBraceToken;
        foreach (var column in Columns) yield return column;
        yield return CloseBraceToken;
    }
}

public sealed record UnwrapExpressionSyntax(ExpressionSyntax Operand, SyntaxToken BangToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.UnwrapExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Operand;
        yield return BangToken;
    }
}

public sealed record AwaitExpressionSyntax(SyntaxToken AwaitKeyword, ExpressionSyntax Operand) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.AwaitExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return AwaitKeyword;
        yield return Operand;
    }
}

public sealed record ObjectPropertySyntax(
    SyntaxToken NameToken,
    SyntaxToken ColonToken,
    ExpressionSyntax ValueExpression) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.ObjectProperty;

    public override IEnumerable<object> GetChildren()
    {
        yield return NameToken;
        yield return ColonToken;
        yield return ValueExpression;
    }
}

public sealed record ObjectLiteralExpressionSyntax(
    SyntaxToken OpenBraceToken,
    IReadOnlyList<ObjectPropertySyntax> Properties,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseBraceToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ObjectLiteralExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenBraceToken;

        for (var i = 0; i < Properties.Count; i++)
        {
            if (i > 0)
            {
                yield return CommaTokens[i - 1];
            }

            yield return Properties[i];
        }

        yield return CloseBraceToken;
    }
}


public sealed record IfExpressionSyntax(
    SyntaxToken IfKeyword,
    ExpressionSyntax Condition,
    SyntaxToken ThenOpenBraceToken,
    ExpressionSyntax ThenExpression,
    SyntaxToken ThenCloseBraceToken,
    SyntaxToken ElseKeyword,
    SyntaxToken ElseOpenBraceToken,
    ExpressionSyntax ElseExpression,
    SyntaxToken ElseCloseBraceToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.IfExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return IfKeyword;
        yield return Condition;
        yield return ThenOpenBraceToken;
        yield return ThenExpression;
        yield return ThenCloseBraceToken;
        yield return ElseKeyword;
        yield return ElseOpenBraceToken;
        yield return ElseExpression;
        yield return ElseCloseBraceToken;
    }
}

public sealed record WithExpressionSyntax(
    ExpressionSyntax Source,
    SyntaxToken WithKeyword,
    ObjectLiteralExpressionSyntax Replacements) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.WithExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Source;
        yield return WithKeyword;
        yield return Replacements;
    }
}

public sealed record TryValueBlockSyntax(
    SyntaxToken OpenBraceToken,
    IReadOnlyList<StatementSyntax> PrefixStatements,
    ExpressionSyntax ValueExpression,
    SyntaxToken CloseBraceToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.TryValueBlock;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenBraceToken;
        foreach (var statement in PrefixStatements)
        {
            yield return statement;
        }

        yield return ValueExpression;
        yield return CloseBraceToken;
    }
}

public sealed record TryExceptExpressionSyntax(
    SyntaxToken TryKeyword,
    TryValueBlockSyntax Protected,
    SyntaxToken ExceptKeyword,
    SyntaxToken OpenParenToken,
    SyntaxToken BindingIdentifier,
    SyntaxToken CloseParenToken,
    TryValueBlockSyntax Handler) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TryExceptExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return TryKeyword;
        yield return Protected;
        yield return ExceptKeyword;
        yield return OpenParenToken;
        yield return BindingIdentifier;
        yield return CloseParenToken;
        yield return Handler;
    }
}

public sealed record MatchPatternSyntax(
    SyntaxToken CaseIdentifier,
    SyntaxToken? OpenParenToken,
    IReadOnlyList<SyntaxToken> PayloadIdentifiers,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken? CloseParenToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.MatchPattern;

    public override IEnumerable<object> GetChildren()
    {
        yield return CaseIdentifier;
        if (OpenParenToken is not null)
        {
            yield return OpenParenToken;
        }

        for (var i = 0; i < PayloadIdentifiers.Count; i++)
        {
            if (i > 0)
            {
                yield return CommaTokens[i - 1];
            }

            yield return PayloadIdentifiers[i];
        }

        if (CloseParenToken is not null)
        {
            yield return CloseParenToken;
        }
    }
}

public sealed record MatchArmSyntax(
    MatchPatternSyntax Pattern,
    SyntaxToken ArrowToken,
    ExpressionSyntax Expression,
    SyntaxToken? CommaToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.MatchArm;

    public override IEnumerable<object> GetChildren()
    {
        yield return Pattern;
        yield return ArrowToken;
        yield return Expression;
        if (CommaToken is not null)
        {
            yield return CommaToken;
        }
    }
}

public sealed record MatchExpressionSyntax(
    SyntaxToken MatchKeyword,
    ExpressionSyntax Expression,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<MatchArmSyntax> Arms,
    SyntaxToken CloseBraceToken) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.MatchExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return MatchKeyword;
        yield return Expression;
        yield return OpenBraceToken;
        foreach (var arm in Arms)
        {
            yield return arm;
        }
        yield return CloseBraceToken;
    }
}

public sealed record MissingExpressionSyntax(SyntaxToken Token) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.MissingExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Token;
    }
}

public sealed record ArrowParameterSyntax(
    SyntaxToken Identifier,
    SyntaxToken? ColonToken,
    TypeSyntax? Type) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.ArrowParameter;
    public override IEnumerable<object> GetChildren()
    {
        yield return Identifier;
        if (ColonToken is not null) yield return ColonToken;
        if (Type is not null) yield return Type;
    }
}

public sealed record UnsupportedExpressionSyntax(SyntaxToken Token) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.UnsupportedExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Token;
    }
}

public sealed record ArrowExpressionSyntax(
    SyntaxToken? OpenParenToken,
    IReadOnlyList<ArrowParameterSyntax> Parameters,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken? CloseParenToken,
    SyntaxToken? ReturnColonToken,
    TypeSyntax? ReturnType,
    SyntaxToken ArrowToken,
    ExpressionSyntax? ExpressionBody,
    BlockStatementSyntax? BlockBody) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ArrowExpression;
    public override IEnumerable<object> GetChildren()
    {
        if (OpenParenToken is not null) yield return OpenParenToken;
        for (var index = 0; index < Parameters.Count; index++)
        {
            if (index > 0) yield return CommaTokens[index - 1];
            yield return Parameters[index];
        }
        if (CloseParenToken is not null) yield return CloseParenToken;
        if (ReturnColonToken is not null) yield return ReturnColonToken;
        if (ReturnType is not null) yield return ReturnType;
        yield return ArrowToken;
        if (ExpressionBody is not null) yield return ExpressionBody;
        if (BlockBody is not null) yield return BlockBody;
    }
}

public sealed record CaptureExpressionSyntax(
    SyntaxToken CaptureKeyword,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<SyntaxToken> Identifiers,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseBraceToken,
    ArrowExpressionSyntax Arrow) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.CaptureExpression;
    public override IEnumerable<object> GetChildren()
    {
        yield return CaptureKeyword;
        yield return OpenBraceToken;
        for (var index = 0; index < Identifiers.Count; index++)
        {
            if (index > 0) yield return CommaTokens[index - 1];
            yield return Identifiers[index];
        }
        yield return CloseBraceToken;
        yield return Arrow;
    }
}

/// <summary>
/// A backend-neutral TS-XML element. Semantic profiles decide what an element means;
/// this syntax node deliberately does not imply a UI, component, or runtime model.
/// </summary>
public abstract record TsXmlExpressionSyntax : ExpressionSyntax;

public sealed record TsXmlElementExpressionSyntax(
    SyntaxToken LessToken,
    SyntaxToken NameToken,
    IReadOnlyList<TsXmlAttributeSyntax> Attributes,
    SyntaxToken? SlashToken,
    SyntaxToken OpenCloseToken,
    IReadOnlyList<TsXmlChildSyntax> Children,
    SyntaxToken? CloseLessToken,
    SyntaxToken? CloseSlashToken,
    SyntaxToken? CloseNameToken,
    SyntaxToken? CloseGreaterToken) : TsXmlExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TsXmlElementExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return LessToken;
        yield return NameToken;
        foreach (TsXmlAttributeSyntax attribute in Attributes)
        {
            yield return attribute;
        }

        if (SlashToken is not null)
        {
            yield return SlashToken;
        }

        yield return OpenCloseToken;
        foreach (TsXmlChildSyntax child in Children)
        {
            yield return child;
        }

        if (CloseLessToken is not null)
        {
            yield return CloseLessToken;
        }

        if (CloseSlashToken is not null)
        {
            yield return CloseSlashToken;
        }

        if (CloseNameToken is not null)
        {
            yield return CloseNameToken;
        }

        if (CloseGreaterToken is not null)
        {
            yield return CloseGreaterToken;
        }
    }
}

public sealed record TsXmlFragmentExpressionSyntax(
    SyntaxToken LessToken,
    SyntaxToken OpenCloseToken,
    IReadOnlyList<TsXmlChildSyntax> Children,
    SyntaxToken CloseLessToken,
    SyntaxToken CloseSlashToken,
    SyntaxToken CloseGreaterToken) : TsXmlExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TsXmlFragmentExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return LessToken;
        yield return OpenCloseToken;
        foreach (TsXmlChildSyntax child in Children)
        {
            yield return child;
        }

        yield return CloseLessToken;
        yield return CloseSlashToken;
        yield return CloseGreaterToken;
    }
}

public sealed record TsXmlAttributeSyntax(
    SyntaxToken NameToken,
    SyntaxToken? EqualsToken,
    SyntaxToken? StringValueToken,
    SyntaxToken? OpenBraceToken,
    ExpressionSyntax? ExpressionValue,
    SyntaxToken? CloseBraceToken) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.TsXmlAttribute;

    public override IEnumerable<object> GetChildren()
    {
        yield return NameToken;
        if (EqualsToken is not null)
        {
            yield return EqualsToken;
        }

        if (StringValueToken is not null)
        {
            yield return StringValueToken;
        }

        if (OpenBraceToken is not null)
        {
            yield return OpenBraceToken;
        }

        if (ExpressionValue is not null)
        {
            yield return ExpressionValue;
        }

        if (CloseBraceToken is not null)
        {
            yield return CloseBraceToken;
        }
    }
}

public abstract record TsXmlChildSyntax : SyntaxNode;

public sealed record TsXmlTextSyntax(SyntaxToken TextToken) : TsXmlChildSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TsXmlText;

    public override IEnumerable<object> GetChildren()
    {
        yield return TextToken;
    }
}

public sealed record TsXmlExpressionChildSyntax(
    SyntaxToken OpenBraceToken,
    ExpressionSyntax Expression,
    SyntaxToken CloseBraceToken) : TsXmlChildSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TsXmlExpressionChild;

    public override IEnumerable<object> GetChildren()
    {
        yield return OpenBraceToken;
        yield return Expression;
        yield return CloseBraceToken;
    }
}

public sealed record TsXmlElementChildSyntax(TsXmlExpressionSyntax Element) : TsXmlChildSyntax
{
    public override SyntaxKind Kind => SyntaxKind.TsXmlElementChild;

    public override IEnumerable<object> GetChildren()
    {
        yield return Element;
    }
}
