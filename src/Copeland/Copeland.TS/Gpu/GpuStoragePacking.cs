using System.Buffers.Binary;
using System.Text;
using Copeland.TS.Gpu.VdMir;
using Microsoft.CodeAnalysis.CSharp;

namespace Copeland.TS.Gpu;

/// <summary>Host transport generated from certified shader storage, without CLR reflection.</summary>
public static class GpuStoragePacking
{
    public static void Write(Span<byte> bytes, VdMirStorageLayout layout, int index, string field, float value)
    {
        var leaf = Field(layout, field, "f32");
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(checked(index * layout.Stride + leaf.Offset), 4), value);
    }

    public static void Write(Span<byte> bytes, VdMirStorageLayout layout, int index, string field, uint value)
    {
        var leaf = Field(layout, field, "u32");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(checked(index * layout.Stride + leaf.Offset), 4), value);
    }

    public static string EmitCSharp(VdMirStorageLayout layout, string namespaceName, string className)
    {
        ValidateIdentifier(namespaceName, allowDots: true);
        ValidateIdentifier(className, allowDots: false);
        var builder = new StringBuilder();
        builder.AppendLine("// Generated from Visual TypeScript storage metadata.");
        builder.AppendLine($"namespace {namespaceName};");
        builder.AppendLine($"public static class {className}");
        builder.AppendLine("{");
        builder.AppendLine($"    public const int Stride = {layout.Stride};");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in layout.Fields)
        {
            string name = field.Path.Length == 0 ? "Value" : field.Path.Replace(".", "_", StringComparison.Ordinal);
            ValidateIdentifier(name, allowDots: false);
            if (!names.Add(name))
            {
                throw new ArgumentException("Storage field paths produce duplicate C# packing method names.");
            }
            builder.AppendLine($"    public static void Write_{name}(System.Span<byte> bytes, int index, {(field.Type == "f32" ? "float" : "uint")} value)");
            builder.AppendLine("    {");
            string method = field.Type == "f32" ? "WriteSingleLittleEndian" : "WriteUInt32LittleEndian";
            builder.AppendLine($"        System.Buffers.Binary.BinaryPrimitives.{method}(bytes.Slice(checked(index * Stride + {field.Offset}), 4), value);");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static VdMirStorageField Field(VdMirStorageLayout layout, string path, string type)
    {
        var field = layout.Fields.Single(field => field.Path == path);
        if (field.Type != type)
        {
            throw new ArgumentException("Host value does not match the shader storage field type.");
        }
        return field;
    }

    private static void ValidateIdentifier(string name, bool allowDots)
    {
        string[] parts = allowDots ? name.Split('.') : [name];
        if (parts.Any(part => !SyntaxFacts.IsValidIdentifier(part)
            || SyntaxFacts.GetKeywordKind(part) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None))
        {
            throw new ArgumentException("Generated C# identifiers must be ordinary identifiers.");
        }
    }
}
