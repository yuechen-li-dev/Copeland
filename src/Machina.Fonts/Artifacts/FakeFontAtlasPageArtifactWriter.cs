using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Machina.Fonts.Artifacts;

public static class FakeFontAtlasPageArtifactWriter
{
    public const string Header = "machina-font-atlas-fake-page";

    public static FakeFontAtlasPageArtifact Write(string path, string atlasName, FontAtlasPage page, IEnumerable<GlyphAtlasEntry> glyphs, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(atlasName);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(glyphs);

        string content = BuildContent(atlasName, page, glyphs);
        FileMode mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        using (FileStream stream = new(path, mode, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
        }

        return new FakeFontAtlasPageArtifact(path, ComputeSha256(bytes), bytes.Length);
    }

    public static string BuildContent(string atlasName, FontAtlasPage page, IEnumerable<GlyphAtlasEntry> glyphs)
    {
        StringBuilder builder = new();
        builder.AppendLine(Header);
        builder.AppendLine("format=1");
        builder.Append("atlas=").AppendLine(atlasName);
        builder.Append("page=").AppendLine(page.Index.ToString(CultureInfo.InvariantCulture));
        builder.Append("width=").AppendLine(page.Width.ToString(CultureInfo.InvariantCulture));
        builder.Append("height=").AppendLine(page.Height.ToString(CultureInfo.InvariantCulture));
        builder.Append("glyphs=").AppendLine(string.Join(",", glyphs.OrderBy(glyph => glyph.Key.Codepoint).ThenBy(glyph => glyph.Key.EmSize).ThenBy(glyph => glyph.Key.Weight).ThenBy(glyph => glyph.Key.Slant).Select(glyph => FormatCodepoint(glyph.Key.Codepoint))));
        return builder.ToString();
    }

    public static string ComputeFileSha256(string path)
    {
        return ComputeSha256(File.ReadAllBytes(path));
    }

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatCodepoint(int codepoint)
    {
        return "U+" + codepoint.ToString("X4", CultureInfo.InvariantCulture);
    }
}

public sealed record FakeFontAtlasPageArtifact(string Path, string ContentHash, int ByteCount);
