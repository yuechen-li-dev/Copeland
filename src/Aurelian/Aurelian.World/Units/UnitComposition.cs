namespace Aurelian.World.Units;

/// <summary>
/// Declares the immediate children of a unit only. Transitive composition is computed by the resolver.
/// </summary>
public sealed record UnitComposition(
    IReadOnlyList<UnitChild> Children)
{
    public static UnitComposition Empty { get; } = new(Array.Empty<UnitChild>());
}
