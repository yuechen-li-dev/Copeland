using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Machina.Fonts.Generation;

namespace Machina.Fonts.Artifacts.DistanceField;

public static class DistanceFieldPageArtifactWriter
{
    public const string Header = "machina-font-atlas-dfpage";
    public const string DataMarker = "---DATA---";
    private const string NewLine = "\n";

    public static DistanceFieldPageArtifact Write(
        string path,
        string distanceField,
        GeneratedFieldAtlasPage page,
        bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(distanceField);
        ArgumentNullException.ThrowIfNull(page);

        byte[] bytes = BuildBytes(distanceField, page);
        FileMode mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        using (FileStream stream = new(path, mode, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
        }

        return new DistanceFieldPageArtifact(
            path,
            ComputeSha256(bytes),
            bytes.Length,
            page.Index,
            page.Width,
            page.Height,
            page.ChannelCount,
            distanceField,
            page.Entries.Select(static entry => entry.Key.Codepoint).OrderBy(static codepoint => codepoint).ToArray());
    }

    public static byte[] BuildBytes(string distanceField, GeneratedFieldAtlasPage page)
    {
        string header = BuildHeader(distanceField, page);
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        byte[] dataBytes = new byte[checked(page.Data.Length * sizeof(float))];
        for (int i = 0; i < page.Data.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                dataBytes.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(page.Data[i]));
        }

        byte[] bytes = new byte[checked(headerBytes.Length + dataBytes.Length)];
        Buffer.BlockCopy(headerBytes, 0, bytes, 0, headerBytes.Length);
        Buffer.BlockCopy(dataBytes, 0, bytes, headerBytes.Length, dataBytes.Length);
        return bytes;
    }

    public static string BuildHeader(string distanceField, GeneratedFieldAtlasPage page)
    {
        StringBuilder builder = new();
        builder.Append(Header).Append(NewLine);
        builder.Append("format=1").Append(NewLine);
        builder.Append("kind=").Append(distanceField).Append(NewLine);
        builder.Append("page=").Append(page.Index.ToString(CultureInfo.InvariantCulture)).Append(NewLine);
        builder.Append("width=").Append(page.Width.ToString(CultureInfo.InvariantCulture)).Append(NewLine);
        builder.Append("height=").Append(page.Height.ToString(CultureInfo.InvariantCulture)).Append(NewLine);
        builder.Append("channels=").Append(page.ChannelCount.ToString(CultureInfo.InvariantCulture)).Append(NewLine);
        builder.Append("data=float32-le").Append(NewLine);
        builder.Append("glyphs=").Append(string.Join(",", page.Entries
            .OrderBy(static entry => entry.Key.Codepoint)
            .ThenBy(static entry => entry.Key.EmSize)
            .ThenBy(static entry => entry.Key.Weight)
            .ThenBy(static entry => entry.Key.Slant)
            .Select(static entry => $"U+{entry.Key.Codepoint:X4}"))).Append(NewLine);
        builder.Append(DataMarker).Append(NewLine);
        return builder.ToString();
    }

    public static string ComputeFileSha256(string path)
    {
        return ComputeSha256(File.ReadAllBytes(path));
    }

    public static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
