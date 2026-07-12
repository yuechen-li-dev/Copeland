namespace Aurelian.Shaders.Language.Ast;

public readonly record struct SdslvSpan(int Start, int End, int Line, int Column)
{
    public static SdslvSpan Unknown { get; } = new(0, 0, 0, 0);
}
