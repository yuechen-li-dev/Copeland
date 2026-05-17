namespace Copeland.Script.Syntax;

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
    SyntaxToken OpenParenToken,
    IReadOnlyList<ParameterSyntax> Parameters,
    IReadOnlyList<SyntaxToken> CommaTokens,
    SyntaxToken CloseParenToken,
    SyntaxToken? ReturnTypeColonToken,
    TypeSyntax? ReturnType,
    SyntaxToken? ErrorTypeBangToken,
    TypeSyntax? ErrorType,
    BlockStatementSyntax Body) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.FunctionDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        yield return FunctionKeyword;
        yield return Identifier;
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
        if (ErrorTypeBangToken is not null)
        {
            yield return ErrorTypeBangToken;
        }
        if (ErrorType is not null)
        {
            yield return ErrorType;
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

public sealed record MissingExpressionSyntax(SyntaxToken Token) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.MissingExpression;

    public override IEnumerable<object> GetChildren()
    {
        yield return Token;
    }
}
