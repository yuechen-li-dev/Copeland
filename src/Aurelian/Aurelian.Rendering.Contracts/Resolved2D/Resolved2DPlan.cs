using System.Collections.ObjectModel;

namespace Aurelian.Rendering.Contracts.Resolved2D;

/// <summary>
/// Immutable backend-neutral work accepted by a resolved 2D renderer.
/// </summary>
public sealed class Resolved2DPlan
{
    private readonly ReadOnlyCollection<Resolved2DOperation> operations;

    public Resolved2DPlan(Resolved2DViewport viewport, IEnumerable<Resolved2DOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        Viewport = viewport;
        Resolved2DOperation[] copiedOperations = operations.ToArray();
        ValidateClipBalance(copiedOperations);
        this.operations = Array.AsReadOnly(copiedOperations);
    }

    public Resolved2DViewport Viewport { get; }

    public IReadOnlyList<Resolved2DOperation> Operations => operations;

    private static void ValidateClipBalance(IReadOnlyList<Resolved2DOperation> operations)
    {
        var clipDepth = 0;

        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            Resolved2DOperation? operation = operations[operationIndex];
            if (operation is null)
            {
                throw new ArgumentException("Resolved 2D plan operations cannot contain null.", nameof(operations));
            }

            switch (operation)
            {
                case PushRectangularClipOperation:
                    clipDepth++;
                    break;
                case PopClipOperation:
                    clipDepth--;
                    if (clipDepth < 0)
                    {
                        throw new InvalidOperationException("Resolved 2D plan cannot pop an empty clip stack.");
                    }

                    break;
            }
        }

        if (clipDepth != 0)
        {
            throw new InvalidOperationException("Resolved 2D plan clip operations must be balanced.");
        }
    }
}
