using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Language.VdMir.Emission.Hlsl;

/// <summary>Emit certified inline aggregates after their value dependencies.</summary>
internal static class VdMirAggregateHlslEmitter
{
    public static void Emit(StringBuilder builder, IReadOnlyList<VdMirEnum>? enums, IReadOnlyList<VdMirValueType>? values)
    {
        var enumerations = (enums ?? []).ToDictionary(value => value.Name, StringComparer.Ordinal);
        var records = (values ?? []).ToDictionary(value => value.Name, StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in enumerations.Keys.Concat(records.Keys)) Visit(name);

        void Visit(string name)
        {
            if (completed.Contains(name)) return;
            if (!enumerations.ContainsKey(name) && !records.ContainsKey(name)) return;
            if (!active.Add(name)) throw new InvalidOperationException("Uncertified recursive inline aggregate: " + name);
            if (enumerations.TryGetValue(name, out VdMirEnum? enumeration))
            {
                foreach (VdMirEnumField field in enumeration.CarrierFields) Visit(field.Type);
                VdMirEnumHlslEmitter.Emit(builder, [enumeration]);
            }
            else
            {
                VdMirValueType value = records[name];
                foreach (VdMirValueField field in value.Fields) Visit(field.Type);
                VdMirValueHlslEmitter.Emit(builder, [value]);
            }
            active.Remove(name);
            completed.Add(name);
        }
    }
}
