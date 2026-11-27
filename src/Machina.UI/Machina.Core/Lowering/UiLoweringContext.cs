using Machina.Core.Actions;
using Machina.Core.Measurement;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;

namespace Machina.Core.Lowering;

internal sealed class UiLoweringContext
{
    private int nextGeneratedId;
    private readonly Dictionary<NodeId, IdOrigin> seenIds = [];
    private readonly record struct IdOrigin(string Kind, NodeId? Parent)
    {
        public override string ToString() => Parent is { } parent ? $"{Kind} under '{parent}'" : $"{Kind} at the root";
    }

    public UiLoweringContext(ITextMeasurer textMeasurer)
    {
        TextMeasurer = textMeasurer;
    }

    public ITextMeasurer TextMeasurer { get; }

    public List<LayoutRow> Rows { get; } = [];

    public Dictionary<NodeId, UiStyle> Styles { get; } = [];

    public Dictionary<NodeId, TextStyle> TextStyles { get; } = [];

    public Dictionary<NodeId, UiSemantics> Semantics { get; } = [];

    public Dictionary<NodeId, UiAction> Actions { get; } = [];

    public Dictionary<NodeId, object> NodePayloads { get; } = [];

    public NodeId AllocateId(NodeId? explicitId, string kind, NodeId? parent)
    {
        var origin = new IdOrigin(kind, parent);
        if (explicitId is { } id)
        {
            ValidateExplicitId(id);
            if (!seenIds.TryAdd(id, origin))
            {
                throw new UiLoweringError("DuplicateUiNodeId",
                    $"Duplicate UI node id '{id}'. First used by {seenIds[id]}. Also used by {origin}. " +
                    "Components reserve their generated child IDs (for example, a button owns '.label' and '.label-region'). " +
                    "Give the authored node its own ID.");
            }
            return id;
        }

        while (true)
        {
            var generated = new NodeId($"ui_{nextGeneratedId}");
            nextGeneratedId++;

            if (seenIds.TryAdd(generated, origin))
            {
                return generated;
            }
        }
    }

    private static void ValidateExplicitId(NodeId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new UiLoweringError(
                "InvalidUiNodeId",
                "Explicit UI node ids must not be empty or whitespace.");
        }
    }
}
