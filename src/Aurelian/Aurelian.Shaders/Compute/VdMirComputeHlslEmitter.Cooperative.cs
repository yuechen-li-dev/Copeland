using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Compute;

public static partial class VdMirComputeHlslEmitter
{
    private static void EmitCooperativeMatMul(StringBuilder builder, VdMirStatement statement, int indentation)
    {
        string prefix = new(' ', indentation * 4);
        var args = statement.Expression!.Operands!.Select(EmitExpression).ToArray();
        if (args.Length == 5 && statement.Expression.Operands![0].Type == "workgroup:u32:512")
        {
            bool transpose = statement.Expression.Operands[3].Value == "true";
            builder.AppendLine($"{prefix}if ({args[4]} == 32u)");
            EmitPackedSharedProduct(builder, args, transpose, 32);
            builder.AppendLine(prefix + "else");
            EmitPackedSharedProduct(builder, args, transpose, 64);
            return;
        }
        if (args.Length == 4)
        {
            EmitPackedSharedProduct(builder, args, statement.Expression.Operands![3].Value == "true");
            return;
        }
        if (args.Length == 9)
        {
            builder.AppendLine($"{prefix}if ({args[8]} == 1u)");
            EmitPackedProjection(builder, args, 1);
            builder.AppendLine(prefix + "else");
            EmitPackedProjection(builder, args, 2);
            return;
        }
        if (args.Length == 5)
        {
            EmitCooperativeTile(builder, prefix, args);
            return;
        }
        string a = args[0], b = args[1], output = args[2], group = args[3], lane = args[4];
        string rows = args[5], columns = args[6], inner = args[7];
        builder.AppendLine(prefix + "{");
        builder.AppendLine($"{prefix}    uint __coop_row = {group}.x * 16u;");
        builder.AppendLine($"{prefix}    uint __coop_column = {group}.y * 16u;");
        builder.AppendLine($"{prefix}    __sdslv_cooperative::MatrixC __coop_acc;");
        builder.AppendLine($"{prefix}    __coop_acc.value = __sdslv_cooperative::Splat<__sdslv_cooperative::MatrixCType>(0.0f);");
        builder.AppendLine($"{prefix}    [loop] for (uint __coop_k = 0u; __coop_k < {inner}; __coop_k += 16u)");
        builder.AppendLine(prefix + "    {");
        builder.AppendLine($"{prefix}        [unroll] for (uint __coop_e = {lane}; __coop_e < 256u; __coop_e += 32u)");
        builder.AppendLine(prefix + "        {");
        builder.AppendLine($"{prefix}            uint __coop_r = __coop_e / 16u;");
        builder.AppendLine($"{prefix}            uint __coop_c = __coop_e % 16u;");
        builder.AppendLine($"{prefix}            uint __coop_ai = (__coop_row + __coop_r) * {inner} + __coop_k + __coop_c;");
        builder.AppendLine($"{prefix}            uint __coop_bi = (__coop_k + __coop_r) * {columns} + __coop_column + __coop_c;");
        builder.AppendLine($"{prefix}            uint __coop_ap = {a}[__coop_ai / 2u];");
        builder.AppendLine($"{prefix}            uint __coop_bp = {b}[__coop_bi / 2u];");
        builder.AppendLine($"{prefix}            __sdslv_cooperative_tile_a[__coop_e] = (float16_t)f16tof32((__coop_ai % 2u) == 0u ? (__coop_ap & 65535u) : (__coop_ap >> 16u));");
        builder.AppendLine($"{prefix}            __sdslv_cooperative_tile_b[__coop_e] = (float16_t)f16tof32((__coop_bi % 2u) == 0u ? (__coop_bp & 65535u) : (__coop_bp >> 16u));");
        builder.AppendLine(prefix + "        }");
        builder.AppendLine(prefix + "        GroupMemoryBarrierWithGroupSync();");
        builder.AppendLine($"{prefix}        __sdslv_cooperative::MatrixA __coop_a;");
        builder.AppendLine($"{prefix}        __coop_a.value = __sdslv_cooperative::Load<__sdslv_cooperative::MatrixAType>(__sdslv_cooperative::GetGroupSharedAddress(__sdslv_cooperative_tile_a[0]), __sdslv_cooperative::RowMajor, 16u, 48, __sdslv_cooperative::ScopeWorkgroup);");
        builder.AppendLine($"{prefix}        __sdslv_cooperative::MatrixB __coop_b;");
        builder.AppendLine($"{prefix}        __coop_b.value = __sdslv_cooperative::Load<__sdslv_cooperative::MatrixBType>(__sdslv_cooperative::GetGroupSharedAddress(__sdslv_cooperative_tile_b[0]), __sdslv_cooperative::RowMajor, 16u, 48, __sdslv_cooperative::ScopeWorkgroup);");
        builder.AppendLine($"{prefix}        __coop_acc.value = __sdslv_cooperative::MultiplyAdd<__sdslv_cooperative::MatrixCType>(__coop_a.value, __coop_b.value, __coop_acc.value, 0);");
        builder.AppendLine(prefix + "        GroupMemoryBarrierWithGroupSync();");
        builder.AppendLine(prefix + "    }");
        builder.AppendLine($"{prefix}    __sdslv_cooperative::Store({output}[__coop_row * {columns} + __coop_column], __coop_acc.value, __sdslv_cooperative::RowMajor, {columns}, 0);");
        builder.AppendLine(prefix + "}");
    }

    private static void EmitPackedSharedProduct(StringBuilder builder, string[] args, bool transposeB, int keyTile = 16)
    {
        string left = args[0], right = args[1], output = args[2];
        int innerTiles = transposeB ? 4 : keyTile / 16;
        int columnTiles = transposeB ? keyTile / 16 : 4;
        string layout = transposeB ? "ColumnMajor" : "RowMajor";
        string rightOffset = transposeB ? "col * 16 * 32 + k * 8" : "k * 16 * 32 + col * 8";
        // Strides of packed operands are measured in u32 words, two halves per word.
        builder.AppendLine($$"""
            {
                [unroll] for (uint col = 0; col < {{columnTiles}}; ++col) {
                    __sdslv_cooperative::MatrixC acc;
                    acc.value = __sdslv_cooperative::Splat<__sdslv_cooperative::MatrixCType>(0.0f);
                    [unroll] for (uint k = 0; k < {{innerTiles}}; ++k) {
                        __sdslv_cooperative::MatrixA a;
                        __sdslv_cooperative::MatrixB b;
                        a.value = __sdslv_cooperative::Load<__sdslv_cooperative::MatrixAType>(__sdslv_cooperative::GetGroupSharedAddress({{left}}[k * 8]), __sdslv_cooperative::RowMajor, {{innerTiles * 8}}, 48, __sdslv_cooperative::ScopeWorkgroup);
                        b.value = __sdslv_cooperative::Load<__sdslv_cooperative::MatrixBType>(__sdslv_cooperative::GetGroupSharedAddress({{right}}[{{rightOffset}}]), __sdslv_cooperative::{{layout}}, 32, 48, __sdslv_cooperative::ScopeWorkgroup);
                        acc.value = __sdslv_cooperative::MultiplyAdd<__sdslv_cooperative::MatrixCType>(a.value, b.value, acc.value, 0);
                    }
                    __sdslv_cooperative::Store({{output}}[col * 16], acc.value, __sdslv_cooperative::RowMajor, {{columnTiles * 16}}, 0);
                }
            }
            """);
    }

    private static void EmitPackedProjection(StringBuilder builder, string[] args, int tiles)
    {
        string left = args[0], right = args[1], output = args[2], group = args[3];
        string columns = args[6], inner = args[7];
        builder.AppendLine($$"""
            {
                uint row = {{group}}.x * {{16 * tiles}};
                uint col = {{group}}.y * {{16 * tiles}};
                __sdslv_cooperative::MatrixC sums[{{tiles * tiles}}];
                [unroll] for (uint i = 0; i < {{tiles * tiles}}; ++i) {
                    sums[i].value = __sdslv_cooperative::Splat<__sdslv_cooperative::MatrixCType>(0.0f);
                }
                [loop] for (uint k = 0; k < {{inner}}; k += 16) {
                    __sdslv_cooperative::MatrixA a[{{tiles}}];
                    __sdslv_cooperative::MatrixB b[{{tiles}}];
                    [unroll] for (uint i = 0; i < {{tiles}}; ++i) {
                        uint ai = ((row + i * 16) * {{inner}} + k) / 2;
                        uint bi = (k * {{columns}} + col + i * 16) / 2;
                        a[i].value = __sdslv_cooperative::LoadPacked<__sdslv_cooperative::MatrixAType>({{left}}[ai], __sdslv_cooperative::RowMajor, {{inner}} / 2);
                        b[i].value = __sdslv_cooperative::LoadPacked<__sdslv_cooperative::MatrixBType>({{right}}[bi], __sdslv_cooperative::RowMajor, {{columns}} / 2);
                    }
                    [unroll] for (uint r = 0; r < {{tiles}}; ++r) {
                        [unroll] for (uint c = 0; c < {{tiles}}; ++c) {
                            uint i = r * {{tiles}} + c;
                            sums[i].value = __sdslv_cooperative::MultiplyAdd<__sdslv_cooperative::MatrixCType>(a[r].value, b[c].value, sums[i].value, 0);
                        }
                    }
                }
                [unroll] for (uint r = 0; r < {{tiles}}; ++r) {
                    [unroll] for (uint c = 0; c < {{tiles}}; ++c) {
                        uint start = (row + r * 16) * {{columns}} + col + c * 16;
                        __sdslv_cooperative::Store({{output}}[start], sums[r * {{tiles}} + c].value, __sdslv_cooperative::RowMajor, {{columns}}, 0);
                    }
                }
            }
            """);
    }

    private static void EmitCooperativeTile(StringBuilder builder, string prefix, string[] args)
    {
        string source = args[0], weights = args[1], destination = args[2], lane = args[3], offset = args[4];
        builder.AppendLine(prefix + "{");
        builder.AppendLine(prefix + "    GroupMemoryBarrierWithGroupSync();");
        builder.AppendLine($"{prefix}    [unroll] for (uint element = {lane}; element < 256u; element += 32u)");
        builder.AppendLine(prefix + "    {");
        builder.AppendLine($"{prefix}        uint pair = {weights}[{offset} + element / 2u];");
        builder.AppendLine($"{prefix}        __sdslv_cooperative_tile_a[element] = (float16_t){source}[element];");
        builder.AppendLine(prefix + "        __sdslv_cooperative_tile_b[element] = (float16_t)f16tof32((element % 2u == 0u) ? (pair & 65535u) : (pair >> 16u));");
        builder.AppendLine(prefix + "    }");
        builder.AppendLine(prefix + "    GroupMemoryBarrierWithGroupSync();");
        builder.AppendLine(prefix + "    __sdslv_cooperative::MatrixA a;");
        builder.AppendLine(prefix + "    __sdslv_cooperative::MatrixB b;");
        builder.AppendLine(prefix + "    __sdslv_cooperative::MatrixC c;");
        builder.AppendLine(prefix + "    a.value = __sdslv_cooperative::Load<__sdslv_cooperative::MatrixAType>(__sdslv_cooperative::GetGroupSharedAddress(__sdslv_cooperative_tile_a[0]), __sdslv_cooperative::RowMajor, 16u, 48, __sdslv_cooperative::ScopeWorkgroup);");
        builder.AppendLine(prefix + "    b.value = __sdslv_cooperative::Load<__sdslv_cooperative::MatrixBType>(__sdslv_cooperative::GetGroupSharedAddress(__sdslv_cooperative_tile_b[0]), __sdslv_cooperative::RowMajor, 16u, 48, __sdslv_cooperative::ScopeWorkgroup);");
        builder.AppendLine(prefix + "    c.value = __sdslv_cooperative::MultiplyAdd<__sdslv_cooperative::MatrixCType>(a.value, b.value, __sdslv_cooperative::Splat<__sdslv_cooperative::MatrixCType>(0.0f), 0);");
        builder.AppendLine($"{prefix}    __sdslv_cooperative::Store({destination}[0], c.value, __sdslv_cooperative::RowMajor, 16u, 0);");
        builder.AppendLine(prefix + "    GroupMemoryBarrierWithGroupSync();");
        builder.AppendLine(prefix + "}");
    }
}
