namespace Aurelian.World.Units;

public readonly record struct UnitKindId(string Value)
{
    public override string ToString() => Value;
}
