using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Copeland.Profile;

/// <summary>A named canonical contour layer in explicit SVG paint order.</summary>
public sealed record ProfileSvgLayer(string Name, VectorShape Shape, ProfileStyle? Style = null);

/// <summary>Static paint data, independent of geometric identity. No CSS evaluation.</summary>
public sealed record ProfileStyle(string Fill)
{
    public static ProfileStyle Default { get; } = new("black");

    public bool IsValid => Fill is "black" or "white"
        || (Fill is { Length: 7 } && Fill[0] == '#' && Fill.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") == false);
}

/// <summary>
/// Static inspection export. Layers may overlap; this is paint composition,
/// not a Boolean union and not a combined shape suitable for MSDF compilation.
/// </summary>
public static class ProfileSvgExporter
{
    public static string ExportComposition(ProfileComposition composition, double padding = 12)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(composition.Layers);
        if (!double.IsFinite(padding) || padding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(padding));
        }

        var layerNames = new HashSet<string>(StringComparer.Ordinal);
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProfileLayer layer in composition.Layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            ArgumentNullException.ThrowIfNull(layer.Items);
            if (string.IsNullOrWhiteSpace(layer.Id.Name) || !layerNames.Add(layer.Id.Name))
            {
                throw new ArgumentException("Composition layer identities must be nonempty and unique.", nameof(composition));
            }
            foreach (ResolvedProfilePaintItem item in layer.Items)
            {
                ArgumentNullException.ThrowIfNull(item);
                ArgumentNullException.ThrowIfNull(item.Shape);
                ArgumentNullException.ThrowIfNull(item.Style);
                if (string.IsNullOrWhiteSpace(item.Id) || !profileIds.Add(item.Id))
                {
                    throw new ArgumentException("Composed Profile identities must be nonempty and unique.", nameof(composition));
                }
                if (!item.Style.IsValid)
                {
                    throw new ArgumentException("Profile fill must be black, white, or a six-digit hexadecimal color.", nameof(composition));
                }
            }
        }

        ResolvedProfilePaintItem[] items = composition.Layers
            .SelectMany(layer => layer.Items)
            .ToArray();
        if (items.Length == 0)
        {
            throw new ArgumentException("At least one resolved Profile is required.", nameof(composition));
        }

        double left = items.Min(item => item.Shape.Bounds.MinX) - padding;
        double top = -items.Max(item => item.Shape.Bounds.MaxY) - padding;
        double width = items.Max(item => item.Shape.Bounds.MaxX) - left + padding;
        double height = -items.Min(item => item.Shape.Bounds.MinY) - top + padding;
        XNamespace svgNamespace = "http://www.w3.org/2000/svg";
        var root = new XElement(svgNamespace + "svg",
            new XAttribute("viewBox", string.Create(CultureInfo.InvariantCulture, $"{left:R} {top:R} {width:R} {height:R}")));
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProfileLayer layer in composition.Layers)
        {
            string groupId = UniqueSvgId(layer.Id.Name, "layer", usedIds);
            var group = new XElement(svgNamespace + "g",
                new XAttribute("id", groupId),
                new XAttribute("data-profile-layer", layer.Id.Name));
            foreach (ResolvedProfilePaintItem item in layer.Items)
            {
                string pathId = UniqueSvgId(groupId + "--" + item.Id, "profile", usedIds);
                group.Add(new XElement(svgNamespace + "path",
                    new XAttribute("id", pathId),
                    new XAttribute("data-profile-id", item.Id),
                    new XAttribute("fill", item.Style.Fill),
                    new XAttribute("fill-rule", "nonzero"),
                    new XAttribute("d", ProfileGeometry.ToSvgPath(item.Shape))));
            }
            root.Add(group);
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    public static string ExportLayers(IReadOnlyList<ProfileSvgLayer> layers, double padding = 12)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new ArgumentException("At least one canonical Profile layer is required.", nameof(layers));
        }
        if (!double.IsFinite(padding) || padding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(padding));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProfileSvgLayer layer in layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            ArgumentNullException.ThrowIfNull(layer.Shape);
            if (layer.Style is { IsValid: false })
            {
                throw new ArgumentException("Profile fill must be black, white, or a six-digit hexadecimal color.", nameof(layers));
            }
            if (string.IsNullOrWhiteSpace(layer.Name) || !names.Add(layer.Name))
            {
                throw new ArgumentException("Layer names must be nonempty and unique.", nameof(layers));
            }
        }

        double left = layers.Min(layer => layer.Shape.Bounds.MinX) - padding;
        double top = -layers.Max(layer => layer.Shape.Bounds.MaxY) - padding;
        double width = layers.Max(layer => layer.Shape.Bounds.MaxX) - left + padding;
        double height = -layers.Min(layer => layer.Shape.Bounds.MinY) - top + padding;
        XNamespace svgNamespace = "http://www.w3.org/2000/svg";
        var root = new XElement(svgNamespace + "svg",
            new XAttribute("viewBox", string.Create(CultureInfo.InvariantCulture, $"{left:R} {top:R} {width:R} {height:R}")));
        foreach (ProfileSvgLayer layer in layers)
        {
            root.Add(new XElement(svgNamespace + "path",
                new XAttribute("id", layer.Name),
                new XAttribute("fill", (layer.Style ?? ProfileStyle.Default).Fill),
                new XAttribute("fill-rule", "nonzero"),
                new XAttribute("d", ProfileGeometry.ToSvgPath(layer.Shape))));
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static string UniqueSvgId(string name, string fallbackPrefix, HashSet<string> usedIds)
    {
        var builder = new StringBuilder(name.Length);
        bool separatorPending = false;
        foreach (char character in name)
        {
            if (character is >= 'A' and <= 'Z')
            {
                if (builder.Length > 0 && !separatorPending)
                {
                    separatorPending = true;
                }
                AppendSeparator(builder, ref separatorPending);
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                AppendSeparator(builder, ref separatorPending);
                builder.Append(character);
            }
            else
            {
                separatorPending = builder.Length > 0;
            }
        }

        string candidate = builder.ToString().Trim('-');
        if (candidate.Length == 0 || char.IsDigit(candidate[0]))
        {
            candidate = fallbackPrefix + "-" + ShortHash(name);
        }
        if (!usedIds.Add(candidate))
        {
            candidate = candidate + "-" + ShortHash(name);
            int suffix = 2;
            while (!usedIds.Add(candidate))
            {
                candidate = candidate + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }
        }
        return candidate;
    }

    private static void AppendSeparator(StringBuilder builder, ref bool separatorPending)
    {
        if (separatorPending && builder.Length > 0 && builder[^1] != '-')
        {
            builder.Append('-');
        }
        separatorPending = false;
    }

    private static string ShortHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }
}
