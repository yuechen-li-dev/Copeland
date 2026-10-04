namespace TinyFarm.Core;

/// <summary>Small development fixture. It is not added to the normal campaign.</summary>
public static class TinyFarmAgentExamples
{
    public static TinyFarmAuthoredWorld Create()
    {
        TinyFarmDefinitions definitions = TinyFarmSliceContent.Load();
        var neighbour = TinyFarmAgentTemplate.Character("garden-neighbour") with
        {
            Money = 5,
            Level = 2,
            Conditions = ["well-rested"],
            Items = [new TinyFarmAgentItemSeed("hoe", "Ivy's hoe", 4, EquipmentSlot.Tool, Equip: true)],
            Appearance = new TinyFarmAgentAppearance(TinyFarmAgentSprite.Gardener, ScalePercent: 110)
        };
        var cache = TinyFarmAgentTemplate.Object("supply-cache") with
        {
            Products = [new TinyFarmAgentProductSeed(TinyFarmIds.TurnipSeed, 3)]
        };
        TinyFarmAgentSpawn[] spawns =
        [
            neighbour.Spawn("ivy", "Ivy", TinyFarmSceneIds.Farm, new GridPosition(8, 7)),
            cache.Spawn("garden-cache", "Garden supply cache", TinyFarmSceneIds.Farm, new GridPosition(7, 7))
        ];
        return TinyFarmAgentAuthoring.Build(TinyFarmSliceContent.Start(definitions), definitions, spawns);
    }
}
