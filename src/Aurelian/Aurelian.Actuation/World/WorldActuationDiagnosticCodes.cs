namespace Aurelian.Actuation.World;

public static class WorldActuationDiagnosticCodes
{
    public const string UnitAlreadyExists = "AAW1001";
    public const string UnitNotFound = "AAW1002";
    public const string ParentNotFound = "AAW1003";
    public const string ChildNotFound = "AAW1004";
    public const string ChildAlreadyAttached = "AAW1005";
    public const string ChildNotAttached = "AAW1006";
    public const string CannotDestroyRoot = "AAW1007";
    public const string InvalidMutationWouldBreakWorld = "AAW1008";
    public const string DuplicateChildSlot = "AAW1009";
    public const string CannotDestroyUnitWithChildren = "AAW1010";
    public const string InvalidUnitName = "AAW1011";
    public const string InvalidTransform = "AAW1012";
    public const string UnitNameNotSet = "AAW1013";
    public const string UnitTransformNotSet = "AAW1014";
    public const string InvalidMeshRef = "AAW1015";
    public const string InvalidMaterialRef = "AAW1016";
    public const string RenderableMissing = "AAW1017";
}
