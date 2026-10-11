using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>Typed local payload sums shared by graphics and compute binding.</summary>
internal sealed class GpuEnums
{
    private readonly GpuModuleGraph modules;
    private readonly Action<string, string, VdMirSourceSpan> error;
    private readonly Dictionary<string, VdMirEnum> enums = new(StringComparer.Ordinal);
    private readonly HashSet<string> constructors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Path, EnumDeclarationSyntax Syntax)> declarations = new(StringComparer.Ordinal);
    private readonly HashSet<string> active = new(StringComparer.Ordinal);
    private readonly Func<string, TypeSyntax, string> bindType;
    private readonly GpuValues values;

    public GpuEnums(GpuModuleGraph modules, Func<string, TypeSyntax, string> bindType,
        Action<string, string, VdMirSourceSpan> error, GpuValues values)
    {
        this.modules = modules;
        this.error = error;
        this.bindType = bindType;
        this.values = values;
        foreach ((GpuSourceFile source, SyntaxTree tree) in modules.Sources)
        {
            foreach (EnumDeclarationSyntax syntax in tree.Root.Members.OfType<EnumDeclarationSyntax>())
            {
                declarations.TryAdd(modules.Declare(source.Path, syntax.Identifier.Text), (source.Path, syntax));
            }
        }
    }

    public void BindDefinitions()
    {
        foreach (string name in declarations.Keys) Find(name);
    }

    private VdMirEnum? BindDefinition(string name)
    {
        if (!declarations.TryGetValue(name, out var declaration)) return null;
        if (active.Count >= 64 || !active.Add(name))
        {
            error("COPE-GPU-ENUM-0002", "Recursive payload sums cannot have finite inline storage; use finite value records and explicit state transitions.", Span(declaration.Path, declaration.Syntax.Identifier));
            return null;
        }
        try
        {
            string path = declaration.Path;
            EnumDeclarationSyntax syntax = declaration.Syntax;
            var cases = new List<VdMirEnumCase>();
            var fields = new List<VdMirEnumField> { new("tag", "u32", Span(path, syntax.Identifier)) };
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (syntax.Cases.Count is < 1 or > 64)
            {
                error("COPE-GPU-ENUM-0001", "GPU enums require 1 to 64 finite cases.", Span(path, syntax.Identifier));
            }
            foreach (EnumCaseSyntax variant in syntax.Cases)
            {
                if (!names.Add(variant.Identifier.Text))
                {
                    error("COPE-GPU-ENUM-0001", "Duplicate enum case: " + variant.Identifier.Text, Span(path, variant.Identifier));
                }
                var payload = new List<VdMirEnumField>();
                var payloadNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (EnumPayloadFieldSyntax field in variant.PayloadFields)
                {
                    string type = bindType(path, field.Type);
                    if (values.Layout(type).Size <= 0)
                    {
                        error("COPE-GPU-ENUM-0002", "GPU payloads require certified finite value storage; resources and recursive aggregates are excluded.", Span(path, field.Identifier));
                    }
                    if (!payloadNames.Add(field.Identifier.Text))
                    {
                        error("COPE-GPU-ENUM-0001", "Duplicate enum payload field: " + field.Identifier.Text, Span(path, field.Identifier));
                    }
                    var item = new VdMirEnumField(field.Identifier.Text, type, Span(path, field.Identifier));
                    payload.Add(item);
                    fields.Add(item with { Name = CarrierField((uint)cases.Count, item.Name) });
                }
                string payloadName = "VtsPayloadType_" + name + "_" + cases.Count;
                cases.Add(new(variant.Identifier.Text, (uint)cases.Count, payloadName, payload, Span(path, variant.Identifier))
                {
                    PayloadLayout = payload.Count == 0 ? null : StorageLayout(payloadName, payload, Span(path, variant.Identifier)),
                });
            }
            var result = new VdMirEnum(name, cases, fields, Span(path, syntax.Identifier))
            {
                CarrierLayout = StorageLayout(name, fields, Span(path, syntax.Identifier)),
            };
            enums.Add(name, result);
            return result;
        }
        finally
        {
            active.Remove(name);
        }
    }

    private VdMirValueType StorageLayout(string name, IReadOnlyList<VdMirEnumField> sourceFields, VdMirSourceSpan source)
    {
        int offset = 0;
        var fields = new List<VdMirValueField>();
        if (sourceFields.Count > 256)
        {
            error("COPE-GPU-ENUM-0002", "Payload sum carriers admit at most 256 inline fields.", source);
        }
        foreach (VdMirEnumField field in sourceFields)
        {
            (int size, int alignment) = values.Layout(field.Type);
            if (size <= 0 || alignment <= 0) continue;
            offset = (offset + alignment - 1) / alignment * alignment;
            if (size <= 16 && offset / 16 != (offset + size - 1) / 16)
            {
                offset = (offset + 15) / 16 * 16;
            }
            if (offset > 65536 - size)
            {
                error("COPE-GPU-ENUM-0002", "Payload sums exceed the 64 KiB inline storage budget.", field.Source);
                continue;
            }
            fields.Add(new(field.Name, field.Type, offset, size, alignment, field.Source));
            offset += size;
        }
        return new(name, "record", null, [], fields, (offset + 15) / 16 * 16, 16, "row-major", source);
    }

    public IReadOnlyList<VdMirEnum> Definitions => enums.Values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();

    public IEnumerable<VdMirValueType> StaticLayouts => Definitions.SelectMany(enumeration =>
        enumeration.Cases.Where(variant => variant.PayloadLayout is not null)
            .Select(variant => variant.PayloadLayout!)
            .Prepend(enumeration.CarrierLayout!));
    public bool Contains(string name) => declarations.ContainsKey(name);
    public VdMirEnum? Find(string name) => enums.GetValueOrDefault(name) ?? BindDefinition(name);
    public (int Size, int Alignment)? Layout(string name)
    {
        VdMirEnum? enumeration = Find(name);
        if (enumeration is null) return null;
        return (enumeration.CarrierLayout!.Size, enumeration.CarrierLayout.Alignment);
    }
    public string? MemberType(string type, string member) => enums.Values.SelectMany(item => item.Cases)
        .FirstOrDefault(item => item.PayloadType == type)?.Payload.FirstOrDefault(field => field.Name == member)?.Type;
    public static string CarrierField(uint tag, string field) => "p" + tag + "_" + field;

    public VdMirExpression Extract(VdMirEnum enumeration, VdMirEnumCase variant, VdMirExpression subject, Action<VdMirFunction> addFunction)
    {
        string accessor = "VtsPayload_" + enumeration.Name + "_" + variant.Tag;
        if (constructors.Add(accessor))
        {
            VdMirSourceSpan source = variant.Source;
            var parameter = new VdMirParameter("subject", enumeration.Name, null, source);
            var value = new VdMirExpression("name", enumeration.Name, source, "subject");
            var result = new VdMirExpression("object", variant.PayloadType, source, variant.PayloadType,
                variant.Payload.Select(field => new VdMirExpression("field", field.Type, source, CarrierField(variant.Tag, field.Name), [value])).ToArray(),
                variant.Payload.Select(field => field.Name).ToArray());
            addFunction(new(accessor, [parameter], variant.PayloadType, [new("return", source, Expression: result)], source));
        }
        return new("call", variant.PayloadType, subject.Source, accessor, [subject]);
    }

    public VdMirExpression? Construct(string path, MemberAccessExpressionSyntax member,
        IReadOnlyList<VdMirExpression> arguments, Action<VdMirFunction> addFunction)
    {
        if (member.Target is not NameExpressionSyntax qualifier
            || Find(modules.Resolve(path, qualifier.IdentifierToken.Text)) is not { } enumeration)
        {
            return null;
        }
        VdMirSourceSpan source = Span(path, member.NameToken);
        VdMirEnumCase? variant = enumeration.Cases.FirstOrDefault(item => item.Name == member.NameToken.Text);
        if (variant is null)
        {
            error("COPE-GPU-ENUM-0003", "Unknown enum case: " + member.NameToken.Text, source);
            return new("error", "error", source);
        }
        if (arguments.Count != variant.Payload.Count || arguments.Where((argument, index) => index < variant.Payload.Count && argument.Type != variant.Payload[index].Type).Any())
        {
            error("COPE-GPU-ENUM-0004", "Enum construction must initialize every payload with its exact declared type.", source);
            return new("error", "error", source);
        }
        string constructor = "VtsMake_" + enumeration.Name + "_" + variant.Tag;
        if (constructors.Add(constructor))
        {
            var parameters = variant.Payload.Select((field, index) => new VdMirParameter("value" + index, field.Type, null, source)).ToArray();
            var values = new List<VdMirExpression> { new("literal", "u32", source, variant.Tag.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
            foreach (VdMirEnumCase candidate in enumeration.Cases)
            {
                for (int index = 0; index < candidate.Payload.Count; index++)
                {
                    VdMirEnumField field = candidate.Payload[index];
                    values.Add(candidate == variant ? new("name", field.Type, source, parameters[index].Name) : Zero(field.Type, source, addFunction));
                }
            }
            var result = new VdMirExpression("object", enumeration.Name, source, enumeration.Name, values, enumeration.CarrierFields.Select(field => field.Name).ToArray());
            addFunction(new(constructor, parameters, enumeration.Name, [new("return", source, Expression: result)], source));
        }
        return new("call", enumeration.Name, source, constructor, arguments);
    }

    public VdMirEnumCase? MatchCase(string path, MatchPatternSyntax pattern, VdMirEnum enumeration)
    {
        if (pattern.EnumQualifier is null || modules.Resolve(path, pattern.EnumQualifier.Text) != enumeration.Name)
        {
            error("COPE-GPU-MATCH-0001", "GPU match arms require SubjectEnum.Case qualification using the subject's enum type.", Span(path, pattern.CaseIdentifier));
            return null;
        }
        VdMirEnumCase? variant = enumeration.Cases.FirstOrDefault(item => item.Name == pattern.CaseIdentifier.Text);
        if (variant is null)
        {
            error("COPE-GPU-MATCH-0001", "Unknown match case: " + pattern.CaseIdentifier.Text, Span(path, pattern.CaseIdentifier));
        }
        return variant;
    }

    private VdMirExpression Zero(string type, VdMirSourceSpan source, Action<VdMirFunction> addFunction)
    {
        if (type is "f32" or "u32" or "bool")
        {
            string value = type switch
            {
                "f32" => "0.0",
                "bool" => "false",
                _ => "0",
            };
            return new("literal", type, source, value);
        }
        int count = type switch
        {
            "float2" => 2,
            "float3" => 3,
            "float4" => 4,
            _ => 0,
        };
        if (count == 0)
        {
            string helper = "VtsZero_" + type;
            if (constructors.Add(helper))
            {
                VdMirValueType? layout = values.Find(type) ?? Find(type)?.CarrierLayout;
                if (layout is null) return new("error", "error", source);
                var result = new VdMirExpression("object", type, source, type,
                    layout.Fields.Select(field => Zero(field.Type, source, addFunction)).ToArray(),
                    layout.Fields.Select(field => field.Name).ToArray());
                addFunction(new(helper, [], type, [new("return", source, Expression: result)], source));
            }
            return new("call", type, source, helper, []);
        }
        return new("call", type, source, type, Enumerable.Range(0, count).Select(_ => new VdMirExpression("literal", "f32", source, "0.0")).ToArray());
    }

    private static VdMirSourceSpan Span(string path, SyntaxToken token) => new(path, token.Position, token.Text.Length);
}
