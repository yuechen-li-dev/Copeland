using Machina.Core.Actions;
using Machina.Core.Measurement;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;

namespace Machina.Core.Lowering;

internal sealed class UiLoweringContext
{
    private int nextGeneratedId;
    private readonly HashSet<NodeId> seenIds = [];

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

    public NodeId AllocateId(NodeId? explicitId)
    {
        if (explicitId is { } id)
        {
            ValidateExplicitId(id);
            RegisterId(id, "DuplicateUiNodeId");
            return id;
        }

        while (true)
        {
            var generated = new NodeId($"ui_{nextGeneratedId}");
            nextGeneratedId++;

            if (seenIds.Add(generated))
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

    private void RegisterId(NodeId id, string code)
    {
        if (!seenIds.Add(id))
        {
            throw new UiLoweringError(code, $"Duplicate UI node id '{id}'.");
        }
    }
}
