namespace Machina.Standard.Text;

public abstract record MachinaTextBlock;

public sealed record ParagraphBlock : MachinaTextBlock
{
    public ParagraphBlock(IReadOnlyList<MachinaInline> inline)
    {
        Inline = ValidateInline(inline, nameof(inline));
    }

    public IReadOnlyList<MachinaInline> Inline { get; }

    private static IReadOnlyList<MachinaInline> ValidateInline(IReadOnlyList<MachinaInline> inline, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(inline, parameterName);

        if (inline.Any(run => run is null))
        {
            throw new ArgumentException("Paragraph inline runs cannot contain null items.", parameterName);
        }

        return inline.ToArray();
    }
}

public sealed record BulletListBlock : MachinaTextBlock
{
    public BulletListBlock(IReadOnlyList<MachinaBulletItem> items)
    {
        Items = ValidateItems(items, nameof(items));
    }

    public IReadOnlyList<MachinaBulletItem> Items { get; }

    private static IReadOnlyList<MachinaBulletItem> ValidateItems(IReadOnlyList<MachinaBulletItem> items, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);

        if (items.Any(item => item is null))
        {
            throw new ArgumentException("Bullet list items cannot contain null items.", parameterName);
        }

        return items.ToArray();
    }
}

public sealed record MachinaBulletItem
{
    public MachinaBulletItem(IReadOnlyList<MachinaInline> inline, IReadOnlyList<MachinaBulletItem>? children = null)
    {
        Inline = ValidateInline(inline, nameof(inline));
        Children = ValidateChildren(children, nameof(children));
    }

    public IReadOnlyList<MachinaInline> Inline { get; }

    public IReadOnlyList<MachinaBulletItem>? Children { get; }

    private static IReadOnlyList<MachinaInline> ValidateInline(IReadOnlyList<MachinaInline> inline, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(inline, parameterName);

        if (inline.Any(run => run is null))
        {
            throw new ArgumentException("Bullet item inline runs cannot contain null items.", parameterName);
        }

        return inline.ToArray();
    }

    private static IReadOnlyList<MachinaBulletItem>? ValidateChildren(IReadOnlyList<MachinaBulletItem>? children, string parameterName)
    {
        if (children is null)
        {
            return null;
        }

        if (children.Any(child => child is null))
        {
            throw new ArgumentException("Bullet item children cannot contain null items.", parameterName);
        }

        return children.ToArray();
    }
}
