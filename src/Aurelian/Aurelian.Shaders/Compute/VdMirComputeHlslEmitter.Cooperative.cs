using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Compute;

public static partial class VdMirComputeHlslEmitter
{
    private static void EmitCooperativeMatMul(StringBuilder builder, VdMirStatement statement, int indentation)
    {
        string prefix = new(' ', indentation * 4);
        var args = statement.Expression!.Operands!.Select(EmitExpression).ToArray();
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
}
