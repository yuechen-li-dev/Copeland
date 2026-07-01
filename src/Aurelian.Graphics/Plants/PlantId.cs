using System.Globalization;

namespace Aurelian.Graphics.Plants;

public readonly record struct PlantId(uint Value)
{
    public static PlantId Zero { get; } = new(0);

    public override string ToString()
        => Value.ToString(CultureInfo.InvariantCulture);
}
