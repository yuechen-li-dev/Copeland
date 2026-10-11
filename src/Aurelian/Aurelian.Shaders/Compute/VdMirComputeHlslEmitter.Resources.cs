using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Compute;

public static partial class VdMirComputeHlslEmitter
{
    private static IReadOnlyList<VdMirFunction> ResourceVariants(VdMirComputeModule module)
    {
        var functions = module.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        var variants = new Dictionary<string, Dictionary<string, VdMirFunction>>(StringComparer.Ordinal);
        var pending = new Queue<VdMirFunction>();
        Add(functions[module.EntryPoint!.Name], new Dictionary<int, VdMirResourceAccess>());
        while (pending.TryDequeue(out var function))
        {
            var access = module.Resources.ToDictionary(resource => resource.Name, resource => resource.Access, StringComparer.Ordinal);
            foreach (var parameter in function.Parameters.Where(parameter => parameter.Access is not null))
            {
                access[parameter.Name] = parameter.Access!.Value;
            }
            VisitStatements(function.Statements);

            void VisitStatements(IEnumerable<VdMirStatement> statements)
            {
                foreach (var statement in statements)
                {
                    if (statement.Expression is not null)
                    {
                        VisitExpression(statement.Expression);
                    }
                    VisitStatements(statement.Body ?? []);
                    VisitStatements(statement.ElseBody ?? []);
                }
            }

            void VisitExpression(VdMirExpression expression)
            {
                foreach (var operand in expression.Operands ?? [])
                {
                    VisitExpression(operand);
                }
                if (expression.Kind != "call" || !functions.TryGetValue(expression.Value!, out var target))
                {
                    return;
                }
                var resources = new Dictionary<int, VdMirResourceAccess>();
                for (int index = 0; index < target.Parameters.Count; index++)
                {
                    if (target.Parameters[index].Access is null)
                    {
                        continue;
                    }
                    var argument = expression.Operands![index];
                    if (argument.Kind != "name" || !access.TryGetValue(argument.Value!, out var argumentAccess))
                    {
                        throw new InvalidOperationException("Certified resource arguments must retain named access authority.");
                    }
                    resources.Add(index, argumentAccess);
                }
                Add(target, resources);
            }
        }

        // Keep canonical declaration order. HLSL overloads differ only in the
        // physical RO/RW buffer type required by each reachable call context.
        // The source binder has already proved the helper's declared permissions.
        return module.Functions.SelectMany(function => variants.TryGetValue(function.Name, out var choices)
            ? choices.Values
            : (IEnumerable<VdMirFunction>)[function]).ToArray();

        void Add(VdMirFunction function, IReadOnlyDictionary<int, VdMirResourceAccess> resources)
        {
            VdMirParameter[] parameters = function.Parameters.Select((parameter, index) => resources.TryGetValue(index, out var access)
                ? parameter with { Access = access }
                : parameter).ToArray();
            string signature = string.Join(",", parameters.Select(parameter => parameter.Access?.ToString() ?? "value"));
            if (!variants.TryGetValue(function.Name, out var choices))
            {
                choices = new(StringComparer.Ordinal);
                variants.Add(function.Name, choices);
            }
            var variant = function with { Parameters = parameters };
            if (choices.TryAdd(signature, variant))
            {
                pending.Enqueue(variant);
            }
        }
    }
}
