namespace Aurelian.Shaders.Language.VdMir;

public enum VdMirScalarKind
{
    Void,
    Bool,
    Int,
    UInt,
    Float,
}

public abstract record VdMirType
{
    public abstract string DisplayName { get; }
}

public sealed record VdMirVoidType : VdMirType
{
    public override string DisplayName => "void";
}

public sealed record VdMirScalarType(VdMirScalarKind Kind) : VdMirType
{
    public override string DisplayName => Kind switch
    {
        VdMirScalarKind.Bool => "bool",
        VdMirScalarKind.Int => "int",
        VdMirScalarKind.UInt => "uint",
        VdMirScalarKind.Float => "float",
        _ => "void",
    };
}

public sealed record VdMirVectorType(VdMirScalarKind ElementKind, int Length) : VdMirType
{
    public override string DisplayName => ElementKind switch
    {
        VdMirScalarKind.Float => $"float{Length}",
        VdMirScalarKind.Int => $"int{Length}",
        VdMirScalarKind.UInt => $"uint{Length}",
        _ => $"{ElementKind.ToString().ToLowerInvariant()}{Length}",
    };
}

public sealed record VdMirMatrixType(VdMirScalarKind ElementKind, int Rows, int Columns) : VdMirType
{
    public override string DisplayName => ElementKind switch
    {
        VdMirScalarKind.Float => $"float{Rows}x{Columns}",
        _ => $"{ElementKind.ToString().ToLowerInvariant()}{Rows}x{Columns}",
    };
}

public sealed record VdMirStructType(string Name) : VdMirType
{
    public override string DisplayName => Name;
}
