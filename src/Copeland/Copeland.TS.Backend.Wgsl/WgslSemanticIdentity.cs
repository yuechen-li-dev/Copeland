using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Copeland.TS.Gpu.Wgsl;

/// <summary>Hashes canonical semantic MIR, excluding source paths/spans and emitted formatting.</summary>
internal static class WgslSemanticIdentity
{
    private static readonly VdMirSourceSpan Empty = new("", 0, 0);

    public static string Hash(VdMirGraphicsModule module)
    {
        var canonical = module with
        {
            ValueTypes = module.ValueTypes?.Select(value => value with
            {
                Source = Empty,
                Fields = value.Fields.Select(field => field with { Source = Empty }).ToArray(),
            }).ToArray(),
            SourceFiles = [],
            Enums = module.Enums?.Select(enumeration => enumeration with
            {
                Source = Empty,
                CarrierFields = enumeration.CarrierFields.Select(field => field with { Source = Empty }).ToArray(),
                Cases = enumeration.Cases.Select(variant => variant with
                {
                    Source = Empty,
                    Payload = variant.Payload.Select(field => field with { Source = Empty }).ToArray(),
                }).ToArray(),
            }).ToArray(),
            SemanticSpaces = module.SemanticSpaces.Select(space => space with { Source = Empty }).ToArray(),
            Streams = module.Streams.Select(stream => stream with
            {
                Source = Empty,
                Members = stream.Members.Select(Member).ToArray(),
            }).ToArray(),
            Materials = module.Materials.Select(Material).ToArray(),
            Functions = module.Functions.Select(function => function with
            {
                Source = Empty,
                Parameters = function.Parameters.Select(parameter => parameter with { Source = Empty }).ToArray(),
                Statements = function.Statements.Select(Statement).ToArray(),
            }).ToArray(),
            EntryPoints = module.EntryPoints.Select(entry => entry with { Source = Empty }).ToArray(),
            GraphicsProgram = module.GraphicsProgram! with
            {
                VertexInputs = module.GraphicsProgram.VertexInputs.Select(Member).ToArray(),
                PixelTargets = module.GraphicsProgram.PixelTargets.Select(Member).ToArray(),
                Resources = module.GraphicsProgram.Resources.Select(resource => resource with { Source = Empty, BindingSource = Empty }).ToArray(),
                Material = module.GraphicsProgram.Material is null ? null : Material(module.GraphicsProgram.Material),
            },
        };
        string canonicalJson = VdMirJson.Serialize(canonical).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WgslGraphicsBackend.CompatibilityVersion + "\n" + canonicalJson)))
            .ToLowerInvariant();
    }

    private static VdMirStreamMember Member(VdMirStreamMember member) => member with { Source = Empty, MetadataSource = null };
    private static VdMirMaterial Material(VdMirMaterial material) => material with
    {
        Source = Empty, BindingSource = Empty,
        Fields = material.Fields.Select(field => field with { Source = Empty }).ToArray(),
    };
    private static VdMirExpression Expression(VdMirExpression expression) => expression with
    {
        Source = Empty,
        Value = expression.Kind == "literal"
            ? WgslGraphicsPreparation.Literal(expression.Value!, expression.Type, expression.Source)
            : expression.Value,
        Operands = expression.Operands?.Select(Expression).ToArray(),
    };
    private static VdMirStatement Statement(VdMirStatement statement) => statement with
    {
        Source = Empty,
        Expression = statement.Expression is null ? null : Expression(statement.Expression),
        Body = statement.Body?.Select(Statement).ToArray(),
        ElseBody = statement.ElseBody?.Select(Statement).ToArray(),
        Initializer = statement.Initializer is null ? null : Statement(statement.Initializer),
        Increment = statement.Increment is null ? null : Statement(statement.Increment),
    };
}
