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

    public GpuEnums(GpuModuleGraph modules, Func<string, TypeSyntax, string> bindType,
        Action<string, string, VdMirSourceSpan> error)
    {
        this.modules = modules;
        this.error = error;
        foreach ((GpuSourceFile source, SyntaxTree tree) in modules.Sources)
        {
            foreach (EnumDeclarationSyntax syntax in tree.Root.Members.OfType<EnumDeclarationSyntax>())
            {
                string name = modules.Declare(source.Path, syntax.Identifier.Text);
                var cases = new List<VdMirEnumCase>();
                var fields = new List<VdMirEnumField> { new("tag", "u32", Span(source.Path, syntax.Identifier)) };
                var names = new HashSet<string>(StringComparer.Ordinal);
                if (syntax.Cases.Count is < 1 or > 64)
                {
                    error("COPE-GPU-ENUM-0001", "GPU enums require 1 to 64 finite cases.", Span(source.Path, syntax.Identifier));
                }
                foreach (EnumCaseSyntax variant in syntax.Cases)
                {
                    if (!names.Add(variant.Identifier.Text))
                    {
                        error("COPE-GPU-ENUM-0001", "Duplicate enum case: " + variant.Identifier.Text, Span(source.Path, variant.Identifier));
                    }
                    var payload = new List<VdMirEnumField>();
                    var payloadNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (EnumPayloadFieldSyntax field in variant.PayloadFields)
                    {
                        string type = bindType(source.Path, field.Type);
                        if (type is not ("f32" or "u32" or "bool" or "float2" or "float3" or "float4"))
                        {
                            error("COPE-GPU-ENUM-0002", "Local GPU payloads require scalar or floating vector values; resources and nested sums are deferred.", Span(source.Path, field.Identifier));
                        }
                        if (!payloadNames.Add(field.Identifier.Text))
                        {
                            error("COPE-GPU-ENUM-0001", "Duplicate enum payload field: " + field.Identifier.Text, Span(source.Path, field.Identifier));
                        }
                        var item = new VdMirEnumField(field.Identifier.Text, type, Span(source.Path, field.Identifier));
                        payload.Add(item);
                        fields.Add(item with { Name = CarrierField((uint)cases.Count, item.Name) });
                    }
                    cases.Add(new(variant.Identifier.Text, (uint)cases.Count, "VtsPayloadType_" + name + "_" + cases.Count, payload, Span(source.Path, variant.Identifier)));
                }
                enums.TryAdd(name, new(name, cases, fields, Span(source.Path, syntax.Identifier)));
            }
        }
    }

    public IReadOnlyList<VdMirEnum> Definitions => enums.Values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
    public bool Contains(string name) => enums.ContainsKey(name);
    public VdMirEnum? Find(string name) => enums.GetValueOrDefault(name);
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
            || !enums.TryGetValue(modules.Resolve(path, qualifier.IdentifierToken.Text), out VdMirEnum? enumeration))
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
                    values.Add(candidate == variant ? new("name", field.Type, source, parameters[index].Name) : Zero(field.Type, source));
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

    private static VdMirExpression Zero(string type, VdMirSourceSpan source)
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
        return new("call", type, source, type, Enumerable.Range(0, count).Select(_ => new VdMirExpression("literal", "f32", source, "0.0")).ToArray());
    }

    private static VdMirSourceSpan Span(string path, SyntaxToken token) => new(path, token.Position, token.Text.Length);
}
