namespace Machina.Standard.Text;

public abstract record MachinaInline;

public sealed record TextRun : MachinaInline
{
    public TextRun(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}

public sealed record StrongRun : MachinaInline
{
    public StrongRun(IReadOnlyList<MachinaInline> children)
    {
        Children = ValidateChildren(children, nameof(children));
    }

    public IReadOnlyList<MachinaInline> Children { get; }

    private static IReadOnlyList<MachinaInline> ValidateChildren(IReadOnlyList<MachinaInline> children, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(children, parameterName);

        if (children.Any(child => child is null))
        {
            throw new ArgumentException("Inline children cannot contain null items.", parameterName);
        }

        return children.ToArray();
    }
}

public sealed record EmphasisRun : MachinaInline
{
    public EmphasisRun(IReadOnlyList<MachinaInline> children)
    {
        Children = ValidateChildren(children, nameof(children));
    }

    public IReadOnlyList<MachinaInline> Children { get; }

    private static IReadOnlyList<MachinaInline> ValidateChildren(IReadOnlyList<MachinaInline> children, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(children, parameterName);

        if (children.Any(child => child is null))
        {
            throw new ArgumentException("Inline children cannot contain null items.", parameterName);
        }

        return children.ToArray();
    }
}

public sealed record CodeRun : MachinaInline
{
    public CodeRun(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}

public sealed record LinkRun : MachinaInline
{
    public LinkRun(string href, IReadOnlyList<MachinaInline> children)
    {
        ArgumentNullException.ThrowIfNull(href);
        Href = href;
        Children = ValidateChildren(children, nameof(children));
    }

    public string Href { get; }

    public IReadOnlyList<MachinaInline> Children { get; }

    private static IReadOnlyList<MachinaInline> ValidateChildren(IReadOnlyList<MachinaInline> children, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(children, parameterName);

        if (children.Any(child => child is null))
        {
            throw new ArgumentException("Link children cannot contain null items.", parameterName);
        }

        return children.ToArray();
    }
}
