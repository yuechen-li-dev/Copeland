using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

namespace Hearth;

/// <summary>A fire in the scene: where it burns and how big it is. Effects and lights are derived from these.</summary>
public sealed record FireSite(string Id, Vector3 Base, float Scale, uint Seed)
{
    /// <summary>Embers and sparks spit from the fuel bed.</summary>
    public bool Embers { get; init; } = true;

    /// <summary>Where the fire's light sits relative to its base; wall torches push it out from the wall.</summary>
    public Vector3 LightOffset { get; init; } = new(0, 0.45f, 0);
}

/// <summary>A night campsite built from boxes: ground, stone ring and logs, a wall with two torches and a brazier.</summary>
public static class HearthScene
{
    public static readonly FireSite Campfire = new("campfire", new Vector3(0, 0.12f, 0), 1.0f, 17);
    public static readonly FireSite TorchLeft = new("torch-left", new Vector3(-3.2f, 2.05f, -3.55f), 0.42f, 41)
    {
        Embers = false,
        LightOffset = new(0, 0.3f, 0.9f),
    };
    public static readonly FireSite TorchRight = new("torch-right", new Vector3(3.2f, 2.05f, -3.55f), 0.42f, 43)
    {
        Embers = false,
        LightOffset = new(0, 0.3f, 0.9f),
    };
    public static readonly FireSite Brazier = new("brazier", new Vector3(4.6f, 1.12f, 1.6f), 0.55f, 77);

    public static IReadOnlyList<FireSite> Fires { get; } = [Campfire, TorchLeft, TorchRight, Brazier];

    private static readonly Vector4 Earth = Rgb(0x2E2620);
    private static readonly Vector4 Stone = Rgb(0x6F6A64);
    private static readonly Vector4 StoneDark = Rgb(0x4A4642);
    private static readonly Vector4 Bark = Rgb(0x3B2A1E);
    private static readonly Vector4 Iron = Rgb(0x2A2A2C);

    private static readonly ModelMaterial Embers = new("embers")
    {
        BaseColor = new(0.1f, 0.05f, 0.03f, 1),
        Roughness = 0.9f,
        Emissive = new(0.45f, 0.1f, 0.015f),
    };

    private static readonly ModelMaterial Charred = new("charred")
    {
        BaseColor = new(0.06f, 0.05f, 0.045f, 1),
        Roughness = 0.95f,
        Emissive = new(0.35f, 0.07f, 0.01f),
    };

    public static IReadOnlyList<PlacedSceneBox> Build()
    {
        var boxes = new List<PlacedSceneBox>
        {
            Box("ground", new(0, -0.1f, 0), new(14, 0.1f, 14), Earth),
            Box("wall", new(0, 1.8f, -4), new(7, 1.8f, 0.3f), StoneDark),
            Box("wall-cap", new(0, 3.65f, -4), new(7.2f, 0.08f, 0.38f), Stone),
        };

        // Stone ring around the campfire.
        for (int index = 0; index < 11; index++)
        {
            float angle = index * MathF.Tau / 11;
            var position = new Vector3(MathF.Cos(angle) * 0.95f, 0.1f, MathF.Sin(angle) * 0.95f);
            Matrix4x4 pose = Matrix4x4.CreateRotationY(-angle + 0.3f * (index % 3)) * Matrix4x4.CreateTranslation(position);
            boxes.Add(new PlacedSceneBox($"ring{index}", pose, new(0.16f, 0.11f + 0.02f * (index % 2), 0.12f),
                index % 2 == 0 ? Stone : StoneDark, SceneCollision.None));
        }

        // Logs leaning together over a bed of embers.
        boxes.Add(new PlacedSceneBox("coals", Matrix4x4.CreateTranslation(0, 0.04f, 0), new(0.42f, 0.04f, 0.42f), Embers.BaseColor,
            SceneCollision.None) { Material = Embers });
        for (int index = 0; index < 5; index++)
        {
            float angle = index * MathF.Tau / 5 + 0.4f;
            Matrix4x4 pose = Matrix4x4.CreateRotationZ(0.55f) * Matrix4x4.CreateTranslation(-0.32f, 0, 0)
                * Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(0, 0.26f, 0);
            boxes.Add(new PlacedSceneBox($"log{index}", pose, new(0.36f, 0.055f, 0.055f), Bark, SceneCollision.None)
            {
                Material = index % 2 == 0 ? Charred : null,
            });
        }

        // Wall torches: a bracket, a shaft and a cup.
        foreach (FireSite torch in new[] { TorchLeft, TorchRight })
        {
            Vector3 cup = torch.Base;
            boxes.Add(Box(torch.Id + ".plate", new(cup.X, cup.Y - 0.5f, -3.68f), new(0.12f, 0.2f, 0.02f), Iron));
            boxes.Add(new PlacedSceneBox(torch.Id + ".shaft",
                Matrix4x4.CreateRotationX(-0.35f) * Matrix4x4.CreateTranslation(cup.X, cup.Y - 0.38f, cup.Z - 0.04f),
                new(0.035f, 0.32f, 0.035f), Bark, SceneCollision.None));
            boxes.Add(Box(torch.Id + ".cup", new(cup.X, cup.Y - 0.06f, cup.Z), new(0.08f, 0.06f, 0.08f), Iron));
        }

        // Brazier: a stone pedestal and an iron bowl of coals.
        Vector3 bowl = Brazier.Base;
        boxes.Add(Box("brazier.pedestal", new(bowl.X, 0.45f, bowl.Z), new(0.22f, 0.45f, 0.22f), Stone));
        boxes.Add(Box("brazier.bowl", new(bowl.X, bowl.Y - 0.12f, bowl.Z), new(0.42f, 0.1f, 0.42f), Iron));
        boxes.Add(new PlacedSceneBox("brazier.coals", Matrix4x4.CreateTranslation(bowl.X, bowl.Y - 0.02f, bowl.Z),
            new(0.36f, 0.02f, 0.36f), Embers.BaseColor, SceneCollision.None) { Material = Embers });

        // Things to light: a bench and a crate near the fire.
        boxes.Add(new PlacedSceneBox("bench", Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(-2.2f, 0.25f, 1.1f),
            new(0.9f, 0.08f, 0.25f), Rgb(0x5A4030), SceneCollision.None));
        boxes.Add(new PlacedSceneBox("crate", Matrix4x4.CreateRotationY(-0.3f) * Matrix4x4.CreateTranslation(2.0f, 0.3f, -1.6f),
            new(0.3f, 0.3f, 0.3f), Rgb(0x6B4B2E), SceneCollision.None));
        return boxes;
    }

    private static PlacedSceneBox Box(string id, Vector3 centre, Vector3 half, Vector4 color) =>
        new(id, Matrix4x4.CreateTranslation(centre), half, color, SceneCollision.None);

    private static Vector4 Rgb(uint hex) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1);
}
