using System.Globalization;
using Copeland.TS.Gpu.VdMir;

namespace Copeland.TS.Gpu.Wgsl;

/// <summary>Deterministic target legality/name/ABI preparation, not a second semantic binder.</summary>
internal sealed class WgslGraphicsPreparation
{
    private readonly VdMirGraphicsModule _module;
    private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resources = new(StringComparer.Ordinal);
    private readonly List<WgslStructure> _structures = [];
    private int _localId;

    public WgslGraphicsPreparation(VdMirGraphicsModule module)
    {
        _module = module;
        int typeId = 0;
        foreach (var stream in module.Streams.Where(stream => stream.Role != VdMirStreamRole.Resource))
        {
            _types.Add(stream.Name, $"vd_s{typeId++}");
        }
        foreach (var material in module.Materials)
        {
            _types.Add(material.Name, $"vd_s{typeId++}");
        }
        foreach (var enumeration in module.Enums ?? [])
        {
            _types.Add(enumeration.Name, $"vd_s{typeId++}");
            foreach (var variant in enumeration.Cases.Where(item => item.Payload.Count > 0))
            {
                _types.Add(variant.PayloadType, $"vd_s{typeId++}");
            }
        }
        for (int index = 0; index < module.Functions.Count; index++)
        {
            _functions.Add(module.Functions[index].Name, $"vd_f{index}");
        }
        foreach (var resource in module.GraphicsProgram!.Resources)
        {
            _resources.Add(resource.Name, $"vd_r{resource.Order}");
        }
    }

    public WgslPreparedModule Prepare()
    {
        foreach (var enumeration in _module.Enums ?? [])
        {
            foreach (var variant in enumeration.Cases.Where(item => item.Payload.Count > 0))
            {
                _structures.Add(new WgslStructure(_types[variant.PayloadType], variant.Payload
                    .Select((field, index) => new WgslField(MemberName(index), Type(field.Type, field.Source))).ToArray()));
            }
            _structures.Add(new WgslStructure(_types[enumeration.Name], enumeration.CarrierFields
                .Select((field, index) => new WgslField(MemberName(index), Type(field.Type, field.Source))).ToArray()));
        }
        foreach (var entry in _module.EntryPoints)
        {
            ValidateStage(entry.Name, entry.Stage, new HashSet<string>(StringComparer.Ordinal));
        }
        foreach (var stream in _module.Streams.Where(stream => stream.Role != VdMirStreamRole.Resource))
        {
            _structures.Add(new WgslStructure(_types[stream.Name], stream.Members.Select(member =>
                new WgslField(MemberName(member.Order), Type(member.PhysicalType ?? member.Type, member.Source))).ToArray()));
        }
        foreach (var material in _module.Materials)
        {
            var fields = new List<WgslField>();
            var ordered = material.Fields.OrderBy(field => field.Order).ToArray();
            for (int index = 0; index < ordered.Length; index++)
            {
                var field = ordered[index];
                if (field.Type == "bool")
                {
                    Fail("LAYOUT", "Boolean uniform fields require an explicit host encoding; not admitted by this backend.", field.Source);
                }
                int end = index + 1 < ordered.Length ? ordered[index + 1].Offset : material.Size;
                int size = end - field.Offset;
                if (size < field.Size || field.Offset % field.Alignment != 0)
                {
                    Fail("LAYOUT", "Material layout cannot be represented without changing the canonical byte offsets.", field.Source);
                }
                fields.Add(new WgslField(MemberName(field.Order), Type(field.PhysicalType, field.Source),
                    Alignment: field.Alignment, Size: size));
            }
            _structures.Add(new WgslStructure(_types[material.Name], fields));
        }

        var resources = _module.GraphicsProgram!.Resources.Select(resource => new WgslResource(
            _resources[resource.Name], ResourceType(resource), resource.Set, resource.Binding,
            resource.Kind == VdMirGraphicsResourceKind.Material)).ToArray();
        if (resources.Select(resource => (resource.Group, resource.Binding)).Distinct().Count() != resources.Length)
        {
            Fail("BINDING", "Resource group/binding pairs must be unique.", _module.GraphicsProgram.Resources[0].Source);
        }
        var functions = _module.Functions.Select(function => new WgslFunction(PrepareFunction(function))).ToList();
        foreach (var entry in _module.EntryPoints.OrderBy(entry => entry.Stage))
        {
            functions.Add(PrepareEntry(entry));
        }
        return new WgslPreparedModule(_structures, resources, functions, new Dictionary<string, string>(_functions));
    }

    private void ValidateStage(string name, VdMirGraphicsStage stage, HashSet<string> visited)
    {
        if (!visited.Add(name))
        {
            return;
        }
        var function = _module.Functions.Single(function => function.Name == name);
        foreach (var statement in Descendants(function.Statements))
        {
            if (statement.Kind == "discard" && stage != VdMirGraphicsStage.Pixel)
            {
                Fail("STAGE", "Discard is only legal in a fragment entry's reachable functions.", statement.Source);
            }
            if (statement.Expression is null)
            {
                continue;
            }
            foreach (var expression in Descendants(statement.Expression))
            {
                if (expression.Kind == "intrinsic" && expression.Value is "Sample2D" or "Fwidth" && stage != VdMirGraphicsStage.Pixel)
                {
                    Fail("STAGE", $"'{expression.Value}' requires fragment derivatives; vertex sampling needs explicit LOD semantics not present in this MIR.", expression.Source);
                }
                if (expression.Kind == "call" && _functions.ContainsKey(expression.Value!))
                {
                    ValidateStage(expression.Value!, stage, visited);
                }
            }
        }
    }

    private static IEnumerable<VdMirStatement> Descendants(IReadOnlyList<VdMirStatement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            foreach (var child in Descendants(statement.Body ?? []))
            {
                yield return child;
            }
            foreach (var child in Descendants(statement.ElseBody ?? []))
            {
                yield return child;
            }
            if (statement.Initializer is not null)
            {
                yield return statement.Initializer;
            }
            if (statement.Increment is not null)
            {
                yield return statement.Increment;
            }
        }
    }

    private static IEnumerable<VdMirExpression> Descendants(VdMirExpression expression)
    {
        yield return expression;
        foreach (var operand in expression.Operands ?? [])
        {
            foreach (var nested in Descendants(operand))
            {
                yield return nested;
            }
        }
    }

    private string ResourceType(VdMirGraphicsResource resource)
    {
        return resource.Kind switch
        {
            VdMirGraphicsResourceKind.Texture2D when resource.ElementType == "float4" => "texture_2d<f32>",
            VdMirGraphicsResourceKind.Sampler => "sampler",
            VdMirGraphicsResourceKind.Material => Type(resource.Type, resource.Source),
            _ => throw Error("RESOURCE", $"Resource '{resource.Kind}' has no admitted WGSL representation.", resource.Source),
        };
    }

    private VdMirFunction PrepareFunction(VdMirFunction function)
    {
        var scope = new Dictionary<string, string>(StringComparer.Ordinal);
        var parameters = new List<VdMirParameter>();
        foreach (var parameter in function.Parameters.Where(parameter => !IsResourceStream(parameter.Type)))
        {
            string name = LocalName();
            scope.Add(parameter.Name, name);
            parameters.Add(parameter with { Name = name, Type = Type(parameter.Type, parameter.Source) });
        }
        return function with
        {
            Name = _functions[function.Name], Parameters = parameters,
            ReturnType = Type(function.ReturnType, function.Source),
            Statements = Statements(function.Statements, scope),
        };
    }

    private WgslFunction PrepareEntry(VdMirGraphicsEntryPoint entry)
    {
        var original = _module.Functions.Single(function => function.Name == entry.Name);
        var parameters = new List<VdMirParameter>();
        var arguments = new List<VdMirExpression>();
        foreach (var parameter in original.Parameters.Where(parameter => !IsResourceStream(parameter.Type)))
        {
            var stream = _module.Streams.Single(stream => stream.Name == parameter.Type);
            string ioType = $"vd_io{_structures.Count}";
            _structures.Add(new WgslStructure(ioType, InterfaceFields(stream, entry.Stage, output: false)));
            string name = LocalName();
            parameters.Add(parameter with { Name = name, Type = ioType });
            var root = new VdMirExpression("name", ioType, parameter.Source, name);
            arguments.Add(new VdMirExpression("call", _types[stream.Name], parameter.Source, _types[stream.Name],
                stream.Members.Select(member => new VdMirExpression("field", Type(member.Type, member.Source),
                    member.Source, MemberName(member.Order), [root])).ToArray()));
        }
        var output = _module.Streams.Single(stream => stream.Name == entry.OutputStream);
        string outputType = $"vd_io{_structures.Count}";
        _structures.Add(new WgslStructure(outputType, InterfaceFields(output, entry.Stage, output: true)));
        string resultName = LocalName();
        var result = new VdMirExpression("name", _types[output.Name], entry.Source, resultName);
        var body = new VdMirStatement[]
        {
            new("local", entry.Source, resultName, _types[output.Name], Expression:
                new VdMirExpression("call", _types[output.Name], entry.Source, _functions[entry.Name], arguments)),
            new("return", entry.Source, Expression: new VdMirExpression("call", outputType, entry.Source, outputType,
                output.Members.Select(member => new VdMirExpression("field", Type(member.Type, member.Source),
                    member.Source, MemberName(member.Order), [result])).ToArray())),
        };
        string entryName = entry.Stage == VdMirGraphicsStage.Vertex ? "vd_vertex" : "vd_fragment";
        return new WgslFunction(new VdMirFunction(entryName, parameters, outputType, body, entry.Source), entry.Stage);
    }

    private IReadOnlyList<WgslField> InterfaceFields(VdMirStream stream, VdMirGraphicsStage stage, bool output)
    {
        var fields = new List<WgslField>();
        foreach (var member in stream.Members)
        {
            string? builtin = member.Builtin switch
            {
                null => null,
                "position" => "position",
                "vertex_id" => "vertex_index",
                "instance_id" => "instance_index",
                "front_face" => "front_facing",
                "frag_depth" => "frag_depth",
                _ => throw Error("BUILTIN", $"Unknown builtin '{member.Builtin}'.", member.Source),
            };
            string? interpolation = null;
            if (builtin is null && ((stage == VdMirGraphicsStage.Vertex && output) || (stage == VdMirGraphicsStage.Pixel && !output)))
            {
                interpolation = member.Interpolation switch
                {
                    "flat" => "flat",
                    "noperspective" => "linear",
                    "linear" => "perspective",
                    _ => throw Error("INTERFACE", $"Unknown interpolation '{member.Interpolation}'.", member.Source),
                };
                if (member.Type is "u32" or "i32" && interpolation != "flat")
                {
                    Fail("INTERFACE", "Integer varyings require explicit flat interpolation.", member.Source);
                }
            }
            fields.Add(new WgslField(MemberName(member.Order), Type(member.PhysicalType ?? member.Type, member.Source),
                member.Target ?? member.Location, builtin, interpolation));
        }
        return fields;
    }

    private IReadOnlyList<VdMirStatement> Statements(IReadOnlyList<VdMirStatement> statements, Dictionary<string, string> scope)
    {
        var output = new List<VdMirStatement>();
        foreach (var statement in statements)
        {
            switch (statement.Kind)
            {
                case "local":
                    var initializer = Expression(statement.Expression!, scope);
                    string name = LocalName();
                    scope[statement.Name!] = name;
                    output.Add(statement with { Name = name, Type = Type(statement.Type!, statement.Source), Expression = initializer });
                    break;
                case "assign":
                    output.Add(statement with { Name = scope[statement.Name!], Expression = Expression(statement.Expression!, scope) });
                    break;
                case "for":
                    if (statement.Initializer is not { Kind: "local", Mutable: true } || statement.Increment is not { Kind: "assign" })
                    {
                        Fail("LOOP", "A normalized loop requires an explicit mutable initializer and assignment increment.", statement.Source);
                    }
                    var loopScope = new Dictionary<string, string>(scope, StringComparer.Ordinal);
                    var loopInitializer = Statements([statement.Initializer!], loopScope).Single();
                    output.Add(statement with
                    {
                        Initializer = loopInitializer, Expression = Expression(statement.Expression!, loopScope),
                        Increment = Statements([statement.Increment!], loopScope).Single(),
                        Body = Statements(statement.Body ?? [], new Dictionary<string, string>(loopScope, StringComparer.Ordinal)),
                    });
                    break;
                case "if":
                case "block":
                    output.Add(statement with
                    {
                        Expression = statement.Expression is null ? null : Expression(statement.Expression, scope),
                        Body = Statements(statement.Body ?? [], new Dictionary<string, string>(scope, StringComparer.Ordinal)),
                        ElseBody = Statements(statement.ElseBody ?? [], new Dictionary<string, string>(scope, StringComparer.Ordinal)),
                    });
                    break;
                case "return":
                    output.Add(statement with { Expression = Expression(statement.Expression!, scope) });
                    break;
                case "discard":
                case "break":
                    output.Add(statement);
                    break;
                default:
                    Fail("STATEMENT", $"Unsupported VD-MIR statement '{statement.Kind}'.", statement.Source);
                    break;
            }
        }
        return output;
    }

    private VdMirExpression Expression(VdMirExpression expression, Dictionary<string, string> scope)
    {
        string type = Type(expression.Type, expression.Source);
        if (expression.Kind == "field" && IsResourceStream(expression.Operands![0].Type))
        {
            var resource = _module.GraphicsProgram!.Resources.Single(candidate => candidate.Name == expression.Value);
            return new VdMirExpression("name", ResourceType(resource), expression.Source, _resources[resource.Name]);
        }
        var operands = expression.Operands?.Where(operand => !IsResourceStream(operand.Type))
            .Select(operand => Expression(operand, scope)).ToArray();
        string? value = expression.Value;
        string kind = expression.Kind;
        switch (kind)
        {
            case "name":
                value = scope[value!];
                break;
            case "literal":
                value = Literal(value!, type, expression.Source);
                break;
            case "field":
                string ownerType = expression.Operands![0].Type;
                var stream = _module.Streams.FirstOrDefault(stream => stream.Name == ownerType);
                var material = _module.Materials.FirstOrDefault(material => material.Name == ownerType);
                if (stream is not null)
                {
                    value = MemberName(stream.Members.Single(member => member.Name == value).Order);
                }
                else if (material is not null)
                {
                    value = MemberName(material.Fields.Single(field => field.Name == value).Order);
                }
                else
                {
                    IReadOnlyList<VdMirEnumField>? fields = _module.Enums?.FirstOrDefault(item => item.Name == ownerType)?.CarrierFields;
                    fields ??= _module.Enums?.SelectMany(item => item.Cases).FirstOrDefault(item => item.PayloadType == ownerType)?.Payload;
                    if (fields is not null)
                    {
                        int index = fields.ToList().FindIndex(field => field.Name == value);
                        value = MemberName(index);
                    }
                }
                break;
            case "object":
                kind = "call";
                value = type;
                break;
            case "call":
                value = value is "float2" or "float3" or "float4" ? Type(value, expression.Source) : _functions[value!];
                break;
            case "intrinsic":
                kind = "call";
                value = value switch
                {
                    "ConvertU32ToF32" => "f32",
                    "Sample2D" => "textureSample",
                    "Fwidth" => "fwidth",
                    "Min" or "Max" or "Clamp" or "Abs" or "Sqrt" or "Floor" => value.ToLowerInvariant(),
                    _ => throw Error("INTRINSIC", $"Unsupported intrinsic '{value}'.", expression.Source),
                };
                break;
            case "binary":
            case "unary":
                break;
            default:
                Fail("EXPRESSION", $"Unsupported expression '{kind}'.", expression.Source);
                break;
        }
        return expression with { Kind = kind, Type = type, Value = value, Operands = operands, MemberNames = null };
    }

    private string Type(string type, VdMirSourceSpan source)
    {
        if (_types.TryGetValue(type, out string? mapped))
        {
            return mapped;
        }
        var physical = _module.Streams.SelectMany(stream => stream.Members)
            .FirstOrDefault(member => member.Type == type && member.PhysicalType is not null && member.PhysicalType != type);
        if (physical is not null)
        {
            return Type(physical.PhysicalType!, source);
        }
        return type switch
        {
            "f32" or "u32" or "bool" => type,
            "float2" => "vec2<f32>",
            "float3" => "vec3<f32>",
            "float4" => "vec4<f32>",
            "Texture2D<float4>" => "texture_2d<f32>",
            "Sampler" => "sampler",
            _ => throw Error("TYPE", $"Type '{type}' is outside the current graphics WGSL subset (matrices/arrays/i32 are not frontend-admitted).", source),
        };
    }

    internal static string Literal(string value, string type, VdMirSourceSpan source)
    {
        if (type == "bool" && value is "true" or "false")
        {
            return value;
        }
        if (type == "u32" && uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture) + "u";
        }
        if (type == "f32" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number))
        {
            return number.ToString("R", CultureInfo.InvariantCulture) + "f";
        }
        throw Error("LITERAL", $"Literal '{value}' is not representable as {type}.", source);
    }

    private bool IsResourceStream(string type) => _module.Streams.Any(stream => stream.Name == type && stream.Role == VdMirStreamRole.Resource);
    private string LocalName() => $"vd_l{_localId++}";
    private static string MemberName(int order) => $"m{order}";
    private static WgslPreparationException Error(string category, string message, VdMirSourceSpan source)
        => new($"COPE-WGSL-{category}-0001", message, source);
    private static void Fail(string category, string message, VdMirSourceSpan source) => throw Error(category, message, source);
}
