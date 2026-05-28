namespace Machina.Standard.Text;

public sealed record MachinaTextDocument
{
    public MachinaTextDocument(IReadOnlyList<MachinaTextBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (blocks.Any(block => block is null))
        {
            throw new ArgumentException("Document blocks cannot contain null items.", nameof(blocks));
        }

        Blocks = blocks.ToArray();
    }

    public IReadOnlyList<MachinaTextBlock> Blocks { get; }
}
