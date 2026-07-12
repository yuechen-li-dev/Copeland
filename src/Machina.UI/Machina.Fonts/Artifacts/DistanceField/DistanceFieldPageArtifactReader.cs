using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Machina.Fonts.Artifacts.DistanceField;

public static class DistanceFieldPageArtifactReader
{
    private const string NewLine = "\n";

    public static bool TryRead(
        string path,
        out DistanceFieldPageArtifactDocument? document,
        out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] bytes = File.ReadAllBytes(path);
        return TryRead(bytes, path, out document, out error);
    }

    public static bool TryRead(
        byte[] bytes,
        string path,
        out DistanceFieldPageArtifactDocument? document,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        document = null;
        error = null;

        byte[] markerBytes = Encoding.UTF8.GetBytes(DistanceFieldPageArtifactWriter.DataMarker + NewLine);
        int markerIndex = bytes.AsSpan().IndexOf(markerBytes);
        if (markerIndex < 0)
        {
            error = "DF page artifact is missing the data marker.";
            return false;
        }

        int headerLength = markerIndex + markerBytes.Length;
        string headerText = Encoding.UTF8.GetString(bytes, 0, headerLength);
        string[] lines = headerText.Split(NewLine, StringSplitOptions.None);
        if (lines.Length < 2 || lines[0] != DistanceFieldPageArtifactWriter.Header)
        {
            error = "DF page artifact header is invalid.";
            return false;
        }

        Dictionary<string, string> fields = [];
        foreach (string rawLine in lines.Skip(1))
        {
            if (string.IsNullOrEmpty(rawLine))
            {
                continue;
            }

            if (rawLine == DistanceFieldPageArtifactWriter.DataMarker)
            {
                break;
            }

            int separatorIndex = rawLine.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                error = "DF page artifact field is invalid.";
                return false;
            }

            fields[rawLine[..separatorIndex]] = rawLine[(separatorIndex + 1)..];
        }

        if (!fields.TryGetValue("format", out string? formatText) || formatText != "1")
        {
            error = "DF page artifact format is invalid.";
            return false;
        }

        if (!fields.TryGetValue("kind", out string? kind) || string.IsNullOrWhiteSpace(kind))
        {
            error = "DF page artifact kind is missing.";
            return false;
        }

        if (!TryParseInt(fields, "page", out int pageIndex)
            || !TryParseInt(fields, "width", out int width)
            || !TryParseInt(fields, "height", out int height)
            || !TryParseInt(fields, "channels", out int channelCount))
        {
            error = "DF page artifact numeric metadata is invalid.";
            return false;
        }

        if (!fields.TryGetValue("data", out string? encoding) || encoding != "float32-le")
        {
            error = "DF page artifact data encoding is invalid.";
            return false;
        }

        if (width <= 0 || height <= 0 || channelCount <= 0)
        {
            error = "DF page artifact dimensions and channels must be positive.";
            return false;
        }

        int expectedDataBytes = checked(width * height * channelCount * sizeof(float));
        int actualDataBytes = bytes.Length - headerLength;
        if (actualDataBytes != expectedDataBytes)
        {
            error = $"DF page artifact data length must be {expectedDataBytes} bytes.";
            return false;
        }

        float[] data = new float[checked(width * height * channelCount)];
        ReadOnlySpan<byte> dataSpan = bytes.AsSpan(headerLength);
        for (int i = 0; i < data.Length; i++)
        {
            int raw = BinaryPrimitives.ReadInt32LittleEndian(dataSpan.Slice(i * sizeof(float), sizeof(float)));
            data[i] = BitConverter.Int32BitsToSingle(raw);
        }

        int[] glyphCodepoints = fields.TryGetValue("glyphs", out string? glyphsText) && !string.IsNullOrWhiteSpace(glyphsText)
            ? glyphsText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseCodepoint)
                .OrderBy(static value => value)
                .ToArray()
            : Array.Empty<int>();

        document = new DistanceFieldPageArtifactDocument(
            path,
            kind,
            pageIndex,
            width,
            height,
            channelCount,
            glyphCodepoints,
            data);
        return true;
    }

    private static bool TryParseInt(Dictionary<string, string> fields, string key, out int value)
    {
        value = 0;
        return fields.TryGetValue(key, out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int ParseCodepoint(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("U+", StringComparison.Ordinal))
        {
            throw new FormatException("Glyph codepoint entry must start with U+.");
        }

        return int.Parse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}

public sealed record DistanceFieldPageArtifactDocument(
    string Path,
    string DistanceField,
    int PageIndex,
    int Width,
    int Height,
    int ChannelCount,
    IReadOnlyList<int> GlyphCodepoints,
    float[] Data);
