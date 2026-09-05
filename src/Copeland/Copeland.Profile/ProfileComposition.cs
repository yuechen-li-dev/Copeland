using System.Text;

namespace Copeland.Profile;

/// <summary>A compiler-known semantic identity, independent of SVG naming.</summary>
public readonly record struct ProfileLayerId(string Name);

/// <summary>One independently resolved Profile carried by a paint layer.</summary>
public sealed record ResolvedProfilePaintItem(
    string Id,
    VectorShape Shape,
    ProfileStyle Style,
    string ProfileIrHash,
    string CanonicalContourHash);

/// <summary>A named group whose item order is paint order within the group.</summary>
public sealed record ProfileLayer(
    ProfileLayerId Id,
    IReadOnlyList<ResolvedProfilePaintItem> Items);

/// <summary>
/// Build-time painter composition. Layer source order is paint order. This is
/// not geometric union state and is not retained by a renderer.
/// </summary>
public sealed record ProfileComposition(IReadOnlyList<ProfileLayer> Layers)
{
    public string SemanticHash => ProfileHash.Utf8(WriteSemanticIdentity());

    public string CanonicalGeometryHash => ProfileHash.Utf8(WriteCanonicalGeometryIdentity());

    private string WriteSemanticIdentity()
    {
        var builder = new StringBuilder("profile-composition-v1\n");
        foreach (ProfileLayer layer in Layers)
        {
            Append(builder, layer.Id.Name);
            foreach (ResolvedProfilePaintItem item in layer.Items)
            {
                Append(builder, item.Id);
                Append(builder, item.ProfileIrHash);
                Append(builder, item.CanonicalContourHash);
                Append(builder, item.Style.Fill);
            }
            builder.Append("end-layer\n");
        }
        return builder.ToString();
    }

    private string WriteCanonicalGeometryIdentity()
    {
        var builder = new StringBuilder("profile-composition-geometry-v1\n");
        foreach (string contourHash in Layers
            .SelectMany(layer => layer.Items)
            .Select(item => item.CanonicalContourHash)
            .Order(StringComparer.Ordinal))
        {
            Append(builder, contourHash);
        }
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length).Append(':').Append(value).Append('\n');
    }
}

public sealed record ProfileCompositionCompilationResult(
    ProfileComposition? Composition,
    IReadOnlyList<ProfileDiagnostic> Diagnostics,
    string? CompositionHash,
    string? CanonicalGeometryHash,
    string? Svg)
{
    public bool Success => Composition is not null && Diagnostics.Count == 0;
}
