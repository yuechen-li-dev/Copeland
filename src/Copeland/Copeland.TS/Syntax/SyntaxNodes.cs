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
    SyntaxToken FunctionKeyword,
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
        yield return FunctionKeyword;
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
