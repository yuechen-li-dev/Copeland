namespace Machina.Presentation;

/// <summary>
/// Applies presentation-only text realization choices without rebuilding Machina topology or layout.
/// </summary>
public static class MachinaTextPresentationFrame
{
    public static MachinaPresentationFrame Apply(
        MachinaPresentationFrame frame,
        IReadOnlyDictionary<string, MachinaTextPresentationPrimitive> primitives)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(primitives);

        MachinaPresentationOperation[] operations = frame.Operations.ToArray();
        var applied = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < operations.Length; index++)
        {
            if (operations[index] is not PositionedTextOperation text ||
                !primitives.TryGetValue(text.SourceId, out MachinaTextPresentationPrimitive? primitive))
            {
                continue;
            }

            operations[index] = new PositionedTextOperation(
                text.SourceId,
                text.Rect,
                text.Text,
                text.Style,
                text.Color,
                primitive);
            applied.Add(text.SourceId);
        }

        string[] missing = primitives.Keys
            .Where(key => !applied.Contains(key))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Text presentation primitives target missing operations: {string.Join(", ", missing)}.");
        }

        return new MachinaPresentationFrame(frame.Viewport, operations);
    }
}
