using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private VdMirStatement BindCooperativeMatMul(string path, CallExpressionSyntax call, Dictionary<string, ValueBinding> scope)
        {
            var arguments = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            bool valid = arguments.Length == 8;
            if (valid)
            {
                valid = arguments[0].Type == "storage-buffer<u32>" && arguments[1].Type == "storage-buffer<u32>"
                    && arguments[2].Type == "storage-buffer<f32>" && arguments.Skip(5).All(argument => argument.Type == "u32")
                    && ResourceArgument(call.Arguments[0], scope)?.Access == VdMirResourceAccess.Readonly
                    && ResourceArgument(call.Arguments[1], scope)?.Access == VdMirResourceAccess.Readonly
                    && ResourceArgument(call.Arguments[2], scope)?.Access == VdMirResourceAccess.Readwrite
                    && arguments[3].Kind == "name" && _builtinSemantics.GetValueOrDefault(arguments[3].Value!) == "group_id"
                    && arguments[4].Kind == "name" && _builtinSemantics.GetValueOrDefault(arguments[4].Value!) == "group_index";
            }
            if (!valid)
            {
                AddDiagnostic("COPE-GPU-COOPERATIVE-0001", "SDSL-V4400", "cooperative-matrix",
                    "CooperativeMatMul requires readonly packed-u32 A/B, readwrite f32 C, actual groupId/groupIndex builtins, and uniform u32 M/N/K. Host admission must prove 16-aligned dimensions and the F16/F32 subgroup tuple.", Span(path, call));
            }
            return new("cooperative-matmul", Span(path, call), Expression: new("command", "void", Span(path, call), Operands: arguments));
        }

        private void ValidateCooperativeUniformity(VdMirStatement command, IEnumerable<VdMirStatement> preceding)
        {
            var locals = preceding.Where(statement => statement.Kind == "local" && !statement.Mutable)
                .GroupBy(statement => statement.Name!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Expression!, StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            bool Uniform(VdMirExpression expression)
            {
                if (expression.Kind == "literal")
                {
                    return true;
                }
                if (expression.Kind == "name" && locals.TryGetValue(expression.Value!, out var initializer))
                {
                    if (!active.Add(expression.Value!))
                    {
                        return false;
                    }
                    bool uniform = Uniform(initializer);
                    active.Remove(expression.Value!);
                    return uniform;
                }
                if (expression.Kind is "storage-load" or "index")
                {
                    var buffer = expression.Operands![0];
                    return buffer.Kind == "name" && _resources.Any(resource => resource.Name == buffer.Value
                        && resource.Access == VdMirResourceAccess.Readonly) && Uniform(expression.Operands[1]);
                }
                return expression.Kind is "field" or "binary" or "unary"
                    && (expression.Operands ?? []).All(Uniform);
            }
            if (!(command.Expression!.Operands ?? []).Skip(5).All(Uniform))
            {
                AddDiagnostic("COPE-GPU-COOPERATIVE-0002", "SDSL-V4401", "cooperative-matrix",
                    "M/N/K must come from immutable workgroup-uniform literals or readonly parameters; invocation-dependent dimensions cannot synchronize safely.", command.Source);
            }
        }
    }
}
