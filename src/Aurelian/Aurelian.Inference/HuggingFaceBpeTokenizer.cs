using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Aurelian.Inference;

/// <summary>The explicit Gemma tokenizer.json BPE/space-marker/byte-fallback profile.
/// Other normalizer/pretokenizer graphs are rejected rather than approximated.</summary>
public sealed class HuggingFaceBpeTokenizer
{
    private readonly Dictionary<string, int> vocabulary;
    private readonly Dictionary<(string Left, string Right), int> ranks = [];
    private readonly Dictionary<int, string> pieces;
    private readonly HashSet<int> specialIds = [];
    private readonly string[] specialTokens;

    public HuggingFaceBpeTokenizer(string path)
    {
        if (new FileInfo(path).Length > 64L * 1024 * 1024)
        {
            throw new InvalidDataException("AUR-TOKENIZER-001: Tokenizer exceeds its 64 MiB profile budget.");
        }
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var model = root.GetProperty("model");
        var normalizer = root.GetProperty("normalizer");
        var splitter = root.GetProperty("pre_tokenizer");
        if (model.GetProperty("type").GetString() != "BPE" || !model.GetProperty("byte_fallback").GetBoolean()
            || model.GetProperty("ignore_merges").GetBoolean() || model.GetProperty("dropout").ValueKind != JsonValueKind.Null
            || model.GetProperty("continuing_subword_prefix").ValueKind != JsonValueKind.Null
            || model.GetProperty("end_of_word_suffix").ValueKind != JsonValueKind.Null
            || normalizer.GetProperty("type").GetString() != "Replace"
            || normalizer.GetProperty("pattern").GetProperty("String").GetString() != " "
            || normalizer.GetProperty("content").GetString() != "▁"
            || splitter.GetProperty("type").GetString() != "Split"
            || splitter.GetProperty("pattern").GetProperty("String").GetString() != " "
            || splitter.GetProperty("behavior").GetString() != "MergedWithPrevious"
            || splitter.GetProperty("invert").GetBoolean())
        {
            throw new InvalidDataException("AUR-TOKENIZER-001: Unsupported BPE preprocessing profile.");
        }
        var decoder = root.GetProperty("decoder");
        var decoderSteps = decoder.GetProperty("decoders");
        var processor = root.GetProperty("post_processor");
        if (decoder.GetProperty("type").GetString() != "Sequence" || decoderSteps.GetArrayLength() != 3
            || decoderSteps[0].GetProperty("type").GetString() != "Replace"
            || decoderSteps[0].GetProperty("pattern").GetProperty("String").GetString() != "▁"
            || decoderSteps[0].GetProperty("content").GetString() != " "
            || decoderSteps[1].GetProperty("type").GetString() != "ByteFallback"
            || decoderSteps[2].GetProperty("type").GetString() != "Fuse"
            || processor.GetProperty("type").GetString() != "TemplateProcessing"
            || processor.GetProperty("single").GetArrayLength() != 1
            || processor.GetProperty("single")[0].GetProperty("Sequence").GetProperty("id").GetString() != "A"
            || processor.GetProperty("single")[0].GetProperty("Sequence").GetProperty("type_id").GetInt32() != 0
            || processor.GetProperty("special_tokens").EnumerateObject().Any()
            || root.GetProperty("truncation").ValueKind != JsonValueKind.Null
            || root.GetProperty("padding").ValueKind != JsonValueKind.Null)
        {
            throw new InvalidDataException("AUR-TOKENIZER-001: Unsupported decoder/postprocessing profile.");
        }
        vocabulary = model.GetProperty("vocab").EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.GetInt32(), StringComparer.Ordinal);
        if (vocabulary.Count is < 256 or > 1048576 || vocabulary.Values.Any(id => id is < 0 or >= 1048576)
            || vocabulary.Values.Distinct().Count() != vocabulary.Count)
        {
            throw new InvalidDataException("AUR-TOKENIZER-001: Invalid vocabulary IDs.");
        }
        pieces = vocabulary.ToDictionary(item => item.Value, item => item.Key);
        int rank = 0;
        foreach (var merge in model.GetProperty("merges").EnumerateArray())
        {
            string left = merge[0].GetString()!;
            string right = merge[1].GetString()!;
            if (!vocabulary.ContainsKey(left + right) || !ranks.TryAdd((left, right), rank++))
            {
                throw new InvalidDataException("AUR-TOKENIZER-001: Invalid/duplicate merge.");
            }
        }
        var specials = new List<string>();
        foreach (var token in root.GetProperty("added_tokens").EnumerateArray())
        {
            if (!token.GetProperty("special").GetBoolean() || token.GetProperty("normalized").GetBoolean()
                || token.GetProperty("single_word").GetBoolean() || token.GetProperty("lstrip").GetBoolean()
                || token.GetProperty("rstrip").GetBoolean())
            {
                throw new InvalidDataException("AUR-TOKENIZER-001: Unsupported added-token behavior.");
            }
            string content = token.GetProperty("content").GetString()!;
            int id = token.GetProperty("id").GetInt32();
            if (string.IsNullOrEmpty(content) || content.Length > 256 || !vocabulary.TryGetValue(content, out int existing) || existing != id)
            {
                throw new InvalidDataException("AUR-TOKENIZER-001: Added token differs from vocabulary.");
            }
            specialIds.Add(id);
            specials.Add(content);
        }
        specialTokens = specials.OrderByDescending(item => item.Length).ToArray();
        for (int value = 0; value < 256; value++)
        {
            if (!vocabulary.ContainsKey($"<0x{value:X2}>"))
            {
                throw new InvalidDataException("AUR-TOKENIZER-001: Incomplete byte fallback.");
            }
        }
    }

    public int TokenId(string piece) => vocabulary[piece];

    public int[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 65536) { throw new ArgumentOutOfRangeException(nameof(text)); }
        var result = new List<int>();
        int first = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '<') { continue; }
            string? special = specialTokens.FirstOrDefault(token => text.AsSpan(index).StartsWith(token, StringComparison.Ordinal));
            if (special is null) { continue; }
            AppendText(text[first..index], result);
            result.Add(vocabulary[special]);
            index += special.Length - 1;
            first = index + 1;
        }
        AppendText(text[first..], result);
        return result.ToArray();
    }

    private void AppendText(string text, List<int> result)
    {
        var symbols = new List<string>();
        foreach (var rune in text.Replace(' ', '▁').EnumerateRunes())
        {
            string piece = rune.ToString();
            if (vocabulary.ContainsKey(piece)) { symbols.Add(piece); }
            else
            {
                foreach (byte value in Encoding.UTF8.GetBytes(piece)) { symbols.Add($"<0x{value:X2}>"); }
            }
        }
        // Stable rank ordering and leftmost tie-breaking match the reference BPE.
        while (symbols.Count > 1)
        {
            int bestIndex = -1;
            int bestRank = int.MaxValue;
            for (int index = 0; index + 1 < symbols.Count; index++)
            {
                if (ranks.TryGetValue((symbols[index], symbols[index + 1]), out int rank) && rank < bestRank)
                {
                    bestIndex = index;
                    bestRank = rank;
                }
            }
            if (bestIndex < 0) { break; }
            symbols[bestIndex] += symbols[bestIndex + 1];
            symbols.RemoveAt(bestIndex + 1);
        }
        foreach (string piece in symbols) { result.Add(vocabulary[piece]); }
    }

    public string Decode(IEnumerable<int> tokens, bool skipSpecial = true)
    {
        using var bytes = new MemoryStream();
        foreach (int id in tokens)
        {
            if (skipSpecial && specialIds.Contains(id)) { continue; }
            string piece = pieces[id];
            if (piece.Length == 6 && piece.StartsWith("<0x", StringComparison.Ordinal) && piece.EndsWith('>'))
            {
                bytes.WriteByte(byte.Parse(piece.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            else
            {
                bytes.Write(Encoding.UTF8.GetBytes(piece.Replace('▁', ' ')));
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
