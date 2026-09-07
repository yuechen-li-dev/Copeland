using Aurelian.Machina;
using Machina.Pipeline;
using Machina.Presentation;

namespace Aurelian.StrategyDemo;

/// <summary>Application-owned farming facts used only by the presentation extension proof.</summary>
public sealed record FarmingHudFacts(
    int Seeds,
    int Water,
    int Harvested,
    int PlantedBeds,
    int Day);

public sealed record FreshPresentationProofResult(
    string Consumer,
    int ResolvedNodeCount,
    int PanelCount,
    int TextCount,
    int ChangedPanelColors,
    int ChangedTextColors,
    bool IdenticalNodeIdsAndRectangles,
    bool IdenticalTextAndOperationOrder,
    string ScopeLimit);

/// <summary>
/// A fresh extension of the existing HUD snapshot/style seam. This owns no game state,
/// layout implementation, renderer implementation, or input behavior.
/// </summary>
public static class FreshPresentationProof
{
    public static StrategyHudStyle Style()
    {
        return new StrategyHudStyle(
            Panel: 0xE9DFC6F5,
            Border: 0x657846FF,
            Text: 0x293E2CFF,
            Muted: 0x546648FF,
            Accent: 0x94612CFF);
    }

    public static StrategyHudSnapshot FarmingSnapshot(FarmingHudFacts? facts = null)
    {
        FarmingHudFacts farm = facts ?? new FarmingHudFacts(18, 12, 6, 3, 4);
        return new StrategyHudSnapshot(
            Title: "FERN & FURROW",
            Subtitle: "A SMALL FARM BENEATH THE PINES",
            Resources:
            [
                new("SEEDS", farm.Seeds.ToString()),
                new("WATER", farm.Water.ToString()),
                new("HARVEST", farm.Harvested.ToString()),
                new("SPRING", $"DAY {farm.Day:00}")
            ],
            ObjectiveTitle: "The first kitchen garden",
            Objectives:
            [
                $"Plant beds   {farm.PlantedBeds} / 4",
                $"Bring in harvest   {farm.Harvested} / 10",
                "Water the young seedlings"
            ],
            SelectionTitle: "Garden bed",
            SelectionDetail: "Carrots / ready to tend",
            Actions:
            [
                new("P", "Plant", "1 seed", farm.Seeds > 0),
                new("W", "Water", "1 water", farm.Water > 0),
                new("H", "Harvest", "Ripe crops", true),
                new("R", "Rest", "End the day", true)
            ],
            Notice: "Morning light reaches the garden. Choose a bed to tend.",
            Copy: new("SPRING / KITCHEN GARDEN", "TEND THE GARDEN", "FARM OVERVIEW", "Select a garden bed / Plant / Water / Harvest / Rest"));
    }

    public static FreshPresentationProofResult Run()
    {
        StrategyHudSnapshot snapshot = FarmingSnapshot();
        var pipeline = new MachinaPresentationPipeline();
        MachinaPreparedPresentation original = pipeline.Prepare(
            StrategyHudProfile.Build(snapshot), StrategyHudProfile.Width, StrategyHudProfile.Height);
        MachinaPreparedPresentation restyled = pipeline.Prepare(
            StrategyHudProfile.Build(snapshot, Style()), StrategyHudProfile.Width, StrategyHudProfile.Height);

        Require(original.Resolved.Nodes.Count == restyled.Resolved.Nodes.Count, "Resolved node count changed.");
        foreach (var pair in original.Resolved.Nodes)
        {
            Require(restyled.Resolved.Nodes.TryGetValue(pair.Key, out var node), $"Missing node: {pair.Key}.");
            Require(pair.Value.Rect == node!.Rect, $"Resolved rectangle changed: {pair.Key}.");
        }

        Require(original.PresentationFrame.Operations.Count == restyled.PresentationFrame.Operations.Count,
            "Presentation operation count changed.");
        int panelCount = 0;
        int textCount = 0;
        int changedPanels = 0;
        int changedText = 0;
        for (int index = 0; index < original.PresentationFrame.Operations.Count; index++)
        {
            MachinaPresentationOperation before = original.PresentationFrame.Operations[index];
            MachinaPresentationOperation after = restyled.PresentationFrame.Operations[index];
            switch (before, after)
            {
                case (FillRectangleOperation first, FillRectangleOperation second):
                    Require(first.SourceId == second.SourceId && first.Rect == second.Rect,
                        $"Panel identity or rectangle changed at operation {index}.");
                    panelCount++;
                    if (first.Color != second.Color)
                    {
                        changedPanels++;
                    }
                    break;
                case (PositionedTextOperation first, PositionedTextOperation second):
                    Require(first.SourceId == second.SourceId && first.Rect == second.Rect && first.Text == second.Text,
                        $"Text content, identity or rectangle changed at operation {index}.");
                    textCount++;
                    if (first.Color != second.Color)
                    {
                        changedText++;
                    }
                    break;
                default:
                    Require(before == after, $"Unexpected operation change at index {index}.");
                    break;
            }
        }

        Require(changedPanels > 0 && changedText > 0, "The restyle did not change panel and text paint.");
        Require(restyled.PresentationFrame.Operations.OfType<PositionedTextOperation>()
            .Any(operation => operation.SourceId == "title" && operation.Text == snapshot.Title),
            "The farming title did not reach presentation operations.");
        return new FreshPresentationProofResult(
            "Farming facts projected through StrategyHudSnapshot",
            original.Resolved.Nodes.Count,
            panelCount,
            textCount,
            changedPanels,
            changedText,
            IdenticalNodeIdsAndRectangles: true,
            IdenticalTextAndOperationOrder: true,
            ScopeLimit: "Presentation fixture only; farming facts, captions, controls and style are supplied by the application. No farming simulation is implemented.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
