using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private bool _usesProceduralIntersector;
        private bool _usesRayOcclusion;
        // This is a closed command operand, not a function value. Only ordinary
        // immutable ray data and one readonly buffer cross into the intersector.
        private VdMirExpression? BindRayIntersector(string path, CallExpressionSyntax command,
            Dictionary<string, ValueBinding> scope)
        {
            if (command.Arguments[6] is not NameExpressionSyntax name
                || !_functions.TryGetValue(_modules.Resolve(path, name.IdentifierToken.Text), out FunctionSource? function))
            {
                Invalid("The procedural intersector must name a source-defined function.");
                return null;
            }
            string[] types = ["storage-buffer<f32>", "u32", "float3", "float3", "f32"];
            using (var context = _values.Enter(GpuValues.Empty))
            {
                if (function.Syntax.Parameters.Count != types.Length
                    || function.Syntax.Parameters.Where((parameter, index) =>
                        BindType(function.Path, parameter.Type, parameter.Identifier) != types[index]
                        || parameter.AccessToken?.Text != (index == 0 ? "readonly" : null)).Any())
                {
                    Invalid("The procedural intersector requires (readonly StorageBuffer<f32>, u32 primitive, float3 origin, float3 direction, f32 minimum).");
                    return null;
                }
            }

            // Reuse normal helper binding, including recursion, resource authority,
            // reachable function checking and canonical module symbol resolution.
            var bindingScope = new Dictionary<string, ValueBinding>(scope, StringComparer.Ordinal);
            var inputs = new List<ExpressionSyntax> { command.Arguments[1] };
            int sequence = _matchSequence++;
            for (int index = 1; index < types.Length; index++)
            {
                string local = $"__ray_intersector_{sequence}_{index}";
                while (bindingScope.ContainsKey(local)) local += "_";
                bindingScope.Add(local, new(types[index], false, null, false));
                inputs.Add(new NameExpressionSyntax(name.IdentifierToken with { Text = local }));
            }
            var call = new CallExpressionSyntax(name, command.OpenParenToken, inputs,
                command.CommaTokens.Take(4).ToArray(), command.CloseParenToken);
            VdMirExpression bound = BindCall(path, call, bindingScope);
            var result = _values.Find(bound.Type);
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["normal"] = "float3", ["distance"] = "f32", ["layer"] = "u32", ["mask"] = "u32",
            };
            if (result is not { Kind: "record" } || result.Fields.Count != expected.Count
                || result.Fields.Any(field => !expected.TryGetValue(field.Name, out string? type) || field.Type != type))
            {
                Invalid("The procedural intersector must return a record containing normal: float3, distance: f32, layer: u32, mask: u32. Negative distance denotes a miss.");
                return null;
            }
            _usesProceduralIntersector = true;
            return new("ray-query-intersector", bound.Type, Span(path, name), bound.Value,
                Operands: [BindExpression(path, command.Arguments[1], scope)]);

            void Invalid(string message)
            {
                AddDiagnostic("COPE-GPU-RAYQUERY-0004", "SDSL-V4211", "ray-query", message, Span(path, command.Arguments[6]));
            }
        }
    }
}
