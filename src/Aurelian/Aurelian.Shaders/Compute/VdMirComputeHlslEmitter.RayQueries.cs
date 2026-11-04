using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Compute;

public static partial class VdMirComputeHlslEmitter
{
    private static void EmitQueryHelpers(StringBuilder builder, VdMirComputeModule module)
    {
        AddOverloads(RayQueryHlslEmitter.Helper);
        AddOverloads(RayQueryHlslEmitter.Occlusion());
        var intersectors = new Dictionary<string, VdMirExpression>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            Visit(function.Statements);
        }
        foreach (var intersector in intersectors.Values)
        {
            AddOverloads(RayQueryHlslEmitter.Procedural(MapType(intersector.Type, module), intersector.Value!));
            AddOverloads(RayQueryHlslEmitter.Occlusion(MapType(intersector.Type, module), intersector.Value!));
            AddOverloads(RayQueryHlslEmitter.Retained(MapType(intersector.Type, module), intersector.Value!));
        }

        void Visit(IEnumerable<VdMirStatement> statements)
        {
            foreach (var statement in statements)
            {
                if (statement.Kind == "ray-query" && statement.Expression?.Operands is { Count: 7 } operands)
                {
                    VdMirExpression intersector = operands[6];
                    intersectors.TryAdd(intersector.Value!, intersector);
                }
                Visit(statement.Body ?? []);
                Visit(statement.ElseBody ?? []);
            }
        }

        void AddOverloads(string helper)
        {
            builder.AppendLine(helper);
            builder.AppendLine(helper.Replace("StructuredBuffer<float> rays,",
                "RWStructuredBuffer<float> rays,", StringComparison.Ordinal));
        }
    }
}
