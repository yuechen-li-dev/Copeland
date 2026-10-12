namespace Copeland.TS.Syntax;

/// <summary>Named eager renderable values, independent of layout geometry.</summary>
public sealed record ContributionStreamDeclarationSyntax(
    SyntaxToken StreamKeyword,
    SyntaxToken Identifier,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<LayoutBindingEntrySyntax> Entries,
    SyntaxToken CloseBraceToken) : MemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ContributionStreamDeclaration;

    public override IEnumerable<object> GetChildren()
    {
        yield return StreamKeyword;
        yield return Identifier;
        yield return OpenBraceToken;
        foreach (LayoutBindingEntrySyntax entry in Entries) yield return entry;
        yield return CloseBraceToken;
    }
}

public sealed record LocalContributionStreamStatementSyntax(
    ContributionStreamDeclarationSyntax Declaration) : StatementSyntax
{
    public override SyntaxKind Kind => SyntaxKind.LocalContributionStreamStatement;
    public override IEnumerable<object> GetChildren() => [Declaration];
}

/// <summary>A class-owned declaration has no implicit receiver or instance storage.</summary>
public sealed record ClassContributionStreamSyntax(
    SyntaxToken? VisibilityKeyword,
    IReadOnlyList<SyntaxToken> Modifiers,
    ContributionStreamDeclarationSyntax Declaration) : ClassMemberSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ClassContributionStream;
    public override SyntaxToken NameToken => Declaration.Identifier;

    public override IEnumerable<object> GetChildren()
    {
        if (VisibilityKeyword is not null) yield return VisibilityKeyword;
        foreach (SyntaxToken modifier in Modifiers) yield return modifier;
        yield return Declaration;
    }
}

public sealed record ContributionBindingExpressionSyntax(
    SyntaxToken BindKeyword,
    SyntaxToken LayoutIdentifier,
    SyntaxToken UsingKeyword,
    ExpressionSyntax Contributions) : ExpressionSyntax
{
    public override SyntaxKind Kind => SyntaxKind.ContributionBindingExpression;
    public override IEnumerable<object> GetChildren()
        => [BindKeyword, LayoutIdentifier, UsingKeyword, Contributions];
}
