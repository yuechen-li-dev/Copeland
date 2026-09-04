namespace Copeland.Profile;

public static class ProfileFixtures
{
    private static readonly ProfileSourceSpan Source = ProfileSourceSpan.Generated("ProfileFixtures.profile.tsx");

    public static ProfileDefinition Gear(int teeth = 12, double holeRadius = 12)
    {
        return new ProfileDefinition(
            "Gear",
            "Base",
            new CircleProfileShape(32, 0, 0, Source),
            [
                new RepeatRadialProfileOperation("GearTeeth", "Base", "WithTeeth", teeth, 8, 0.52, 90, Source),
                new HoleProfileOperation("CenterHole", "WithTeeth", "Hollow", new CircleProfileShape(holeRadius, 0, 0, Source), Source),
            ],
            "Hollow",
            Source);
    }

    public static ProfileDefinition TabbedBadge()
    {
        return new ProfileDefinition(
            "TabbedBadge",
            "Base",
            new RoundedRectangleProfileShape(100, 56, 8, Source),
            [
                new TabProfileOperation("MountTab", "Base", "WithTab", ProfileEdge.Top, 22, 8, 0.5, Source),
                new NotchProfileOperation("CableNotch", "WithTab", "Notched", ProfileEdge.Right, 12, 7, 0.5, Source),
                new HoleProfileOperation("MountHole", "Notched", "Hollow", new CircleProfileShape(5, -30, 0, Source), Source),
            ],
            "Hollow",
            Source);
    }

    public static ProfileDefinition Shield()
    {
        return new ProfileDefinition(
            "Shield",
            "Base",
            new PolygonProfileShape([
                new VectorPoint(0, 36),
                new VectorPoint(30, 24),
                new VectorPoint(26, -8),
                new VectorPoint(0, -38),
                new VectorPoint(-26, -8),
                new VectorPoint(-30, 24),
            ], Source),
            [new HoleProfileOperation("Roundel", "Base", "Cutout", new CircleProfileShape(8, 0, 4, Source), Source)],
            "Cutout",
            Source);
    }

    public static ProfileDefinition MultiHole()
    {
        return new ProfileDefinition(
            "MultiHole",
            "Base",
            new RoundedRectangleProfileShape(96, 44, 6, Source),
            [
                new HoleProfileOperation("LeftHole", "Base", "LeftCut", new CircleProfileShape(5, -24, 0, Source), Source),
                new HoleProfileOperation("RightHole", "LeftCut", "BothCut", new CircleProfileShape(5, 24, 0, Source), Source),
            ],
            "BothCut",
            Source);
    }
}
