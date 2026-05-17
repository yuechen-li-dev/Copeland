namespace Copeland.Script.Semantics;

public abstract class TypeSymbol
{
    public abstract string Name { get; }

    public sealed override string ToString() => Name;
}

public sealed class PrimitiveTypeSymbol : TypeSymbol
{
    private PrimitiveTypeSymbol(string name) => Name = name;

    public static readonly PrimitiveTypeSymbol Number = new("number");
    public static readonly PrimitiveTypeSymbol String = new("string");
    public static readonly PrimitiveTypeSymbol Boolean = new("boolean");
    public static readonly PrimitiveTypeSymbol Void = new("void");
    public static readonly PrimitiveTypeSymbol Null = new("null");
    public static readonly PrimitiveTypeSymbol Error = new("error");

    public override string Name { get; }
}

public sealed class ArrayTypeSymbol(TypeSymbol elementType) : TypeSymbol
{
    public TypeSymbol ElementType { get; } = elementType;

    public override string Name => $"{ElementType.Name}[]";
}
