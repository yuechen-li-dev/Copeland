namespace Aurelian.World.Units;

public static class WorldResolutionDiagnosticCodes
{
    public const string RootUnitMissing = "AW1001";
    public const string ChildUnitMissing = "AW1002";
    public const string DuplicateImmediateChild = "AW1003";
    public const string DuplicateChildSlot = "AW1004";
    public const string CompositionCycle = "AW1005";
}
