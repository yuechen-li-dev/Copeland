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
            // Two closed, row-major 16-row products. A word holds two binary16 values.
            // QK reads B transposed. PV writes four adjacent output fragments.
            if (arguments.Length == 4)
            {
                bool transposed = arguments[3] is { Kind: "literal", Value: "true" };
                bool rowMajor = arguments[3] is { Kind: "literal", Value: "false" };
                bool correctShape = (transposed && arguments[0].Type == "workgroup:u32:512"
                    && arguments[2].Type == "workgroup:f32:256")
                    || (rowMajor && arguments[0].Type == "workgroup:u32:128"
                    && arguments[2].Type == "workgroup:f32:1024");
                if (!correctShape || arguments[1].Type != "workgroup:u32:512"
                    || arguments.Take(3).Any(argument => argument.Kind != "name"))
                {
                    AddDiagnostic("COPE-GPU-COOPERATIVE-0001", "SDSL-V4400", "cooperative-matrix",
                        "Packed shared tiles require 16x64 times 64x16 transposed, or 16x16 times 16x64, a matching FP32 output tile and a literal transpose flag.", Span(path, call));
                }
                return new("cooperative-matmul", Span(path, call), Expression: new("command", "void", Span(path, call), Operands: arguments));
            }
            if (arguments.Length == 5 && arguments[0].Type == "workgroup:u32:512")
            {
                bool wide = arguments[1].Type == "workgroup:u32:2048"
                    && arguments[2].Type == "workgroup:f32:1024"
                    && arguments.Take(3).All(argument => argument.Kind == "name")
                    && arguments[3] is { Kind: "literal", Type: "bool" } && arguments[4].Type == "u32"
                    && (arguments[4].Kind != "literal" || arguments[4].Value is "32" or "64");
                if (!wide)
                {
                    AddDiagnostic("COPE-GPU-COOPERATIVE-0001", "SDSL-V4400", "cooperative-matrix",
                        "Wide packed products need 512/2048 shared u32 words, 1024 shared f32 outputs, a literal transpose flag and a uniform key tile width (32 or 64).", Span(path, call));
                }
                return new("cooperative-matmul", Span(path, call), Expression: new("command", "void", Span(path, call), Operands: arguments));
            }
            if (arguments.Length == 5)
            {
                bool tile = arguments[0].Type == "workgroup:f32:256" && arguments[0].Kind == "name"
                    && arguments[1].Type == "storage-buffer<u32>"
                    && ResourceArgument(call.Arguments[1], scope)?.Access == VdMirResourceAccess.Readonly
                    && arguments[2].Type == "workgroup:f32:256" && arguments[2].Kind == "name"
                    && arguments[3].Kind == "name" && _builtinSemantics.GetValueOrDefault(arguments[3].Value!) == "group_index"
                    && arguments[4].Type == "u32";
                if (!tile)
                {
                    AddDiagnostic("COPE-GPU-COOPERATIVE-0001", "SDSL-V4400", "cooperative-matrix",
                        "The tile overload takes two WorkgroupArray<f32, 256> tiles, readonly packed-u32 weights between them, the groupIndex builtin and a uniform weight offset in words.", Span(path, call));
                }
                return new("cooperative-matmul", Span(path, call), Expression: new("command", "void", Span(path, call), Operands: arguments));
            }
            bool valid = arguments.Length is 8 or 9;
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
                    "CooperativeMatMul requires readonly packed-u32 A/B, readwrite f32 C, actual groupId/groupIndex builtins, and uniform u32 M/N/K. An optional tile count of one or two selects direct packed loads. Host admission must prove complete tiles and the F16/F32 subgroup tuple.", Span(path, call));
            }
            return new("cooperative-matmul", Span(path, call), Expression: new("command", "void", Span(path, call), Operands: arguments));
        }

        private void ValidateCooperativeUniformity(VdMirStatement command, IEnumerable<VdMirStatement> preceding)
        {
            var arguments = command.Expression!.Operands ?? [];
            if (arguments.Count == 4)
            {
                return;
            }
            if (!arguments.Skip(arguments.Count == 5 ? 4 : 5).All(expression => IsWorkgroupUniform(expression, preceding)))
            {
                AddDiagnostic("COPE-GPU-COOPERATIVE-0002", "SDSL-V4401", "cooperative-matrix",
                    "The cooperative dimensions, tile count or weight offset must be workgroup-uniform literals or readonly parameters.", command.Source);
            }
        }

        private bool IsWorkgroupUniform(VdMirExpression value, IEnumerable<VdMirStatement> preceding)
        {
            // A spelling is insufficient authority when distinct lexical bindings
            // share it. Reject that ambiguity instead of selecting an initializer.
            var locals = preceding.Where(statement => statement.Kind == "local")
                .GroupBy(statement => statement.Name!, StringComparer.Ordinal)
                .Where(group => group.Count() == 1 && !group.First().Mutable)
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
                return (expression.Kind is "field" or "binary" or "unary" || expression is { Kind: "call", Value: "U32" })
                    && (expression.Operands ?? []).All(Uniform);
            }
            return Uniform(value);
        }
    }
}
