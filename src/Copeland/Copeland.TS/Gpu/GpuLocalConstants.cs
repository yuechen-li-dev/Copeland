using Copeland.TS.Gpu.VdMir;

namespace Copeland.TS.Gpu;

/// <summary>Compile-time values follow the same lexical scopes as shader locals.
/// Evaluation belongs to GpuStaticEvaluation; this table only preserves bindings.</summary>
internal sealed class GpuLocalConstants
{
    private readonly Dictionary<object, Dictionary<string, VdMirExpression>> scopes = new(ReferenceEqualityComparer.Instance);

    public VdMirExpression? Find(object scope, string name)
    {
        if (scopes.TryGetValue(scope, out var values) && values.TryGetValue(name, out var value))
        {
            return value;
        }
        return null;
    }

    public void Declare(object scope, string name, VdMirExpression? value)
    {
        if (value is null)
        {
            if (scopes.TryGetValue(scope, out var existing))
            {
                existing.Remove(name);
            }
            return;
        }
        if (!scopes.TryGetValue(scope, out var values))
        {
            values = new(StringComparer.Ordinal);
            scopes.Add(scope, values);
        }
        values[name] = value;
    }

    public void Inherit(object source, object destination)
    {
        if (scopes.TryGetValue(source, out var values))
        {
            scopes[destination] = new(values, StringComparer.Ordinal);
        }
    }
}
