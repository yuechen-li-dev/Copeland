using System.Collections.ObjectModel;

namespace Machina.Presentation;

/// <summary>
/// Immutable, backend-neutral presentation intent prepared by Machina UI.
/// </summary>
public sealed class MachinaPresentationFrame
{
    private readonly ReadOnlyCollection<MachinaPresentationOperation> _operations;

    public MachinaPresentationFrame(
        MachinaPresentationViewport viewport,
        IEnumerable<MachinaPresentationOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        Viewport = viewport;
        var copiedOperations = operations.ToArray();
        ValidateClipBalance(copiedOperations);
        _operations = Array.AsReadOnly(copiedOperations);
    }

    public MachinaPresentationViewport Viewport { get; }

    public IReadOnlyList<MachinaPresentationOperation> Operations => _operations;

    private static void ValidateClipBalance(IReadOnlyList<MachinaPresentationOperation> operations)
    {
        var clipDepth = 0;

        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            MachinaPresentationOperation? operation = operations[operationIndex];
            if (operation is null)
            {
                throw new ArgumentException("Presentation frame operations cannot contain null.", nameof(operations));
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
                        throw new InvalidOperationException("Presentation frame cannot pop an empty clip stack.");
                    }

                    break;
            }
        }

        if (clipDepth != 0)
        {
            throw new InvalidOperationException("Presentation frame clip operations must be balanced.");
        }
    }
}
