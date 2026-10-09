namespace Copeland.TS.Syntax;

/// <summary>The common parameter surface for shorthand and explicit templates.</summary>
public sealed record GenericParameterListSyntax(
    SyntaxToken? TemplateKeyword,
    SyntaxToken LessToken,
    IReadOnlyList<TypeParameterSyntax> Types,
    IReadOnlyList<TemplateParameterSyntax> Values,
    IReadOnlyList<SyntaxToken> Commas,
    SyntaxToken GreaterToken
) : SyntaxNode
{
    public override SyntaxKind Kind => SyntaxKind.GenericParameterList;

    public override IEnumerable<object> GetChildren()
    {
        if (TemplateKeyword is not null)
        {
            yield return TemplateKeyword;
        }

        yield return LessToken;
        SyntaxNode[] parameters = Types.Cast<SyntaxNode>().Concat(Values).ToArray();
        for (int index = 0; index < parameters.Length; index++)
        {
            if (index > 0 && index <= Commas.Count)
            {
                yield return Commas[index - 1];
            }

            yield return parameters[index];
        }

        yield return GreaterToken;
    }
}

/// <summary>A typed static argument, optionally named. It is not a literal type.</summary>
public sealed record GenericValueArgumentTypeSyntax(SyntaxToken? NameToken, SyntaxToken? ColonToken, ExpressionSyntax Expression) : TypeSyntax
{
    public override SyntaxKind Kind => SyntaxKind.GenericValueArgument;

    public override IEnumerable<object> GetChildren()
    {
        if (NameToken is not null)
        {
            yield return NameToken;
        }

        if (ColonToken is not null)
        {
            yield return ColonToken;
        }

        yield return Expression;
    }
}
