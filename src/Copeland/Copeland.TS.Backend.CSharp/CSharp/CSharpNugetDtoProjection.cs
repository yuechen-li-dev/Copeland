using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

public static partial class CSharpBackend
{
    private static readonly AsyncLocal<NugetDtoProjection?> CurrentNugetProjection = new();

    private sealed class NugetDtoProjection
    {
        private readonly Dictionary<MirRecordTypeId, MirRecordDefinition> records;
        private readonly IReadOnlyDictionary<string, MirEnum> enums;

        public NugetDtoProjection(IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records,
            IReadOnlyDictionary<string, MirEnum> enums, IReadOnlySet<MirRecordTypeId>? ids)
        {
            this.enums = enums;
            this.records = ids is null || ids.Count == 0
                ? []
                : ids.ToDictionary(id => id, id => records[id]);
        }

        public IEnumerable<MirRecordTypeId> RecordIds => records.Keys;

        public IEnumerable<string> OptionNames()
        {
            var options = new HashSet<string>(StringComparer.Ordinal);
            foreach (MirRecordFieldDefinition field in records.Values.SelectMany(record => record.Fields))
            {
                MirType type = field.Type;
                while (true)
                {
                    if (type is MirArrayType array)
                    {
                        type = array.ElementType;
                    }
                    else if (TryOption(type, out MirType? payload))
                    {
                        options.Add(type.Identifier);
                        type = payload!;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return options.OrderBy(name => name, StringComparer.Ordinal);
        }

        public string Invoke(NugetWorkerBinding binding, MirRecordDefinition request, MirType result)
        {
            if (binding.RequestRecordName is string expected && request.Name != expected)
            {
                throw new InvalidDataException($"NuGet operation '{binding.Operation}' uses the generated '{expected}' request. Call its generated binding.");
            }
            var arguments = new List<string>();
            for (int index = 0; index < request.Fields.Count; index++)
            {
                MirRecordFieldDefinition field = request.Fields[index];
                string value = "request." + RecordFieldName(field.Id);
                string argument = binding.ProjectDto ? ToClr(field.Type, value) : value;
                if (binding.ObjectParameters.Contains(index))
                {
                    string nativeType = TryOption(field.Type, out _) ? "object?" : "object";
                    argument = $"({nativeType})({argument})";
                }
                arguments.Add(argument);
            }
            string target = $"global::{binding.Type}.{binding.Method}";
            if (binding.TypeArguments.Count > 0)
            {
                target += "<" + string.Join(", ", binding.TypeArguments.Select(DtoTypeName)) + ">";
            }
            string call = $"{target}({string.Join(", ", arguments)})";
            return binding.ProjectDto ? FromClr(result, call) : call;
        }

        public void Emit(CSharpTextWriter writer)
        {
            if (records.Count == 0)
            {
                return;
            }
            writer.WriteLine("internal static class CopeNugetProjection");
            writer.WriteLine("{");
            writer.Indent();
            foreach (MirRecordDefinition record in records.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                EmitRecordMirror(writer, record);
                EmitRecordConversions(writer, record);
            }
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
        }

        private void EmitRecordMirror(CSharpTextWriter writer, MirRecordDefinition record)
        {
            writer.WriteLine("[global::System.Runtime.Serialization.DataContract]");
            writer.WriteLine($"internal sealed class {DtoName(record.Name)}");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"public {DtoName(record.Name)}() {{ }}");
            foreach (MirRecordFieldDefinition field in record.Fields)
            {
                string required = TryOption(field.Type, out _) ? "false" : "true";
                writer.WriteLine($"[global::System.Runtime.Serialization.DataMember(Name = {CSharpLiteralWriter.Write(field.Name)}, IsRequired = {required})]");
                writer.WriteLine($"public {ClrType(field.Type)} @{field.Name} {{ get; set; }} = default!;");
            }
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
        }

        private void EmitRecordConversions(CSharpTextWriter writer, MirRecordDefinition record)
        {
            string copeland = RecordTypeName(record.Id);
            string name = CSharpNameMangler.Mangle(record.Name);
            writer.WriteLine($"internal static {DtoName(record.Name)} To_{name}({copeland} value)");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"return new {DtoName(record.Name)}");
            writer.WriteLine("{");
            writer.Indent();
            foreach (MirRecordFieldDefinition field in record.Fields)
            {
                writer.WriteLine($"@{field.Name} = {ToClr(field.Type, "value." + RecordFieldName(field.Id))},");
            }
            writer.Unindent();
            writer.WriteLine("};");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
            writer.WriteLine($"internal static {copeland} From_{name}({DtoName(record.Name)}? value)");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"if (value is null) throw new global::System.IO.InvalidDataException({CSharpLiteralWriter.Write($"Required NuGet DTO '{record.Name}' is null.")});");
            var values = record.Fields.Select(field => FromClr(field.Type, "value.@" + field.Name));
            writer.WriteLine($"return new {copeland}({string.Join(", ", values)});");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
        }

        private string ToClr(MirType type, string value, int depth = 0)
        {
            if (TryOption(type, out MirType? payload))
            {
                string item = "option" + depth;
                string some = CSharpNameMangler.Mangle(type.Identifier) + ".Some";
                return $"({value} switch {{ {some} {item} => ({ClrType(type)}){ToClr(payload!, item + ".value", depth + 1)}, _ => null }})";
            }
            if (type is MirArrayType array)
            {
                string item = "item" + depth;
                return $"global::System.Array.ConvertAll({value}, {item} => {ToClr(array.ElementType, item, depth + 1)})";
            }
            if (type is MirRecordType record)
            {
                return $"CopeNugetProjection.To_{CSharpNameMangler.Mangle(records[record.RecordTypeId].Name)}({value})";
            }
            return value;
        }

        private string FromClr(MirType type, string value, int depth = 0)
        {
            if (TryOption(type, out MirType? payload))
            {
                string item = "present" + depth;
                string option = CSharpNameMangler.Mangle(type.Identifier);
                string some = FromClr(payload!, item, depth + 1);
                return $"({value} switch {{ {{ }} {item} => ({option})new {option}.Some({some}), _ => {option}.None.__Singleton }})";
            }
            if (type is MirArrayType array)
            {
                string item = "item" + depth;
                return $"global::System.Array.ConvertAll({Required(value, "array")}, {item} => {FromClr(array.ElementType, item, depth + 1)})";
            }
            if (type is MirRecordType record)
            {
                return $"CopeNugetProjection.From_{CSharpNameMangler.Mangle(records[record.RecordTypeId].Name)}({value})";
            }
            return type.Identifier == "string" ? Required(value, "string") : value;
        }

        private string ClrType(MirType type)
        {
            if (TryOption(type, out MirType? payload))
            {
                return ClrType(payload!) + "?";
            }
            return type switch
            {
                MirArrayType array => ClrType(array.ElementType) + "[]",
                MirRecordType record => DtoTypeName(records[record.RecordTypeId].Name),
                _ => MapType(type),
            };
        }

        private bool TryOption(MirType type, out MirType? payload)
        {
            if (enums.TryGetValue(type.Identifier, out MirEnum? definition) && definition.IsOption)
            {
                payload = definition.Cases.Single(item => item.Name == "Some").PayloadFields[0].Type;
                return true;
            }
            payload = null;
            return false;
        }

        private static string Required(string value, string type)
            => $"({value} ?? throw new global::System.IO.InvalidDataException(\"Required NuGet DTO {type} is null.\"))";

        private static string DtoName(string name) => "Dto_" + CSharpNameMangler.Mangle(name);

        private static string DtoTypeName(string name) => "CopeNugetProjection." + DtoName(name);
    }
}
