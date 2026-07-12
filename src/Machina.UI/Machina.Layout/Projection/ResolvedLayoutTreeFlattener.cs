namespace Machina.Layout.Projection;

public static class ResolvedLayoutTreeFlattener
{
    public static IReadOnlyList<ResolvedLayoutTree> FlattenResolvedTree(ResolvedLayoutTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var flattened = new List<ResolvedLayoutTree>();
        FlattenNode(tree, flattened);
        return flattened;
    }

    private static void FlattenNode(ResolvedLayoutTree node, ICollection<ResolvedLayoutTree> flattened)
    {
        flattened.Add(node);

        foreach (var child in node.Children)
        {
            FlattenNode(child, flattened);
        }
    }
}
