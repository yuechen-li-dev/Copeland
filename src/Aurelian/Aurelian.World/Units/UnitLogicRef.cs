namespace Aurelian.World.Units;

public readonly record struct UnitLogicRef(string Value)
{
    public override string ToString() => Value;
}
