using Machina.Core.Lowering;
using Machina.Core.Nodes;

namespace Machina.Pipeline;

/// <summary>
/// Reuses preparation while the authored declaration, viewport and measurement service agree.
/// Callers rebuild their pure declaration; no parallel change key or hash is required.
/// This is whole-view invalidation, not a subtree diff or reconciliation engine.
/// </summary>
public sealed class MachinaPresentationCache
{
    private readonly MachinaPresentationPipeline pipeline = new();
    private UiDeclarationSnapshot? previous;
    private MachinaPreparedPresentation? prepared;
    private UiLoweringOptions? previousOptions;
    private int previousWidth;
    private int previousHeight;

    public int Preparations { get; private set; }

    public MachinaPreparedPresentation Prepare(UiNode declaration, int width, int height, UiLoweringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        UiDeclarationSnapshot snapshot = UiDeclarationSnapshot.Capture(declaration);
        if (prepared is not null && width == previousWidth && height == previousHeight
            && Equals(options, previousOptions) && snapshot.SameAs(previous))
        {
            return prepared;
        }

        // Publish the cache entry only after preparation succeeds. Invalid declarations
        // must not poison the last successfully prepared presentation.
        MachinaPreparedPresentation next = pipeline.Prepare(declaration, width, height, options);
        previous = snapshot;
        prepared = next;
        previousOptions = options;
        previousWidth = width;
        previousHeight = height;
        Preparations++;
        return next;
    }

    public void Invalidate()
    {
        previous = null;
        prepared = null;
    }
}
