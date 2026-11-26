using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private VdMirExpression BindUtility(string path, WhenUtilityExpressionSyntax syntax,
            Dictionary<string, ValueBinding> scope, string? expected)
        {
            string helperName;
            do { helperName = "VtsUtility" + _utilitySequence++; }
            while (_functions.ContainsKey(helperName) || _completedFunctions.Contains(helperName)
                || _resources.Any(resource => resource.Name == helperName)
                || _workgroupArrays.Any(array => array.Name == helperName));
            var source = Span(path, syntax);
            return GpuUtilityChoice.Bind(syntax, helperName, expected, node => Span(path, node),
                (expression, type) => BindExpression(path, expression, scope, type), scope.ContainsKey,
                Capture,
                (target, value) => new("assign", source,
                    Expression: new("assignment", target.Type, source, Operands: [target, value])),
                AddEnumFunction,
                span => AddDiagnostic("COPE-GPU-UTILITY-0001", "SDSL-V4200", "utility",
                    "Utility requires 1 to 16 finite source cases and an else value.", span),
                (expression, type, actual) => TypeMismatch(path, expression, type, actual));

            IReadOnlyList<VdMirParameter> Capture(IReadOnlySet<string> used)
            {
                var parameters = new List<VdMirParameter>();
                foreach (var (name, binding) in scope)
                {
                    if (!used.Contains(name) || IsWorkgroupArray(binding.Type)) { continue; }
                    if (IsSampledResource(binding.Type) || binding.Type == "acceleration_structure")
                    {
                        // Entry descriptors already have global declarations in the
                        // native shader. The helper uses that same bound resource.
                        if (_resources.Any(resource => resource.Name == name && ReferenceEquals(resource, binding.Resource)))
                        {
                            continue;
                        }
                        AddDiagnostic("COPE-GPU-UTILITY-0002", "SDSL-V4200", "utility",
                            "Read a value from the resource before using it in a local utility choice.", source);
                        continue;
                    }
                    parameters.Add(new(name, binding.Type, null, source) { Access = binding.Resource?.Access });
                }
                return parameters;
            }
        }
    }
}
