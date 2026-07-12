using System.Globalization;

namespace Aurelian.World.Units;

public readonly record struct UnitId(ulong Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
