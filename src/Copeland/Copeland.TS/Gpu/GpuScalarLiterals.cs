using System.Globalization;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

internal static class GpuScalarLiterals
{
    public static VdMirExpression? Bind(string path, LiteralExpressionSyntax syntax)
    {
        SyntaxToken token = syntax.LiteralToken;
        var source = new VdMirSourceSpan(path, token.Position, token.Text.Length);
        if (token.Kind is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword)
        {
            return new("literal", "bool", source, token.Kind == SyntaxKind.TrueKeyword ? "true" : "false");
        }
        if (token.Kind == SyntaxKind.NumberToken && token.Value is int integer)
        {
            return new("literal", "u32", source, unchecked((uint)integer).ToString(CultureInfo.InvariantCulture));
        }
        if (token.Kind == SyntaxKind.NumberToken && token.Value is double number && float.IsFinite((float)number))
        {
            string text = ((float)number).ToString("R", CultureInfo.InvariantCulture);
            if (!text.Contains('.') && !text.Contains('E'))
            {
                text += ".0";
            }
            return new("literal", "f32", source, text);
        }
        return null;
    }
}
