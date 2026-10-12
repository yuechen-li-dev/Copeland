namespace Aurelian.Inference.Gemma4;

public sealed record Gemma4ChatResponse(string Content, string Thinking, bool IncompleteThinking);

/// <summary>Single user turn, matching Google's canonical non-thinking prompt.
/// Tool calls, multi-turn replay and multimodal chat templates are outside this profile.</summary>
public static class Gemma4Chat
{
    public static string FormatUser(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return "<bos><|turn>user\n" + content.Trim() + "<turn|>\n<|turn>model\n";
    }

    public static Gemma4ChatResponse ParseResponse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        const string opening = "<|channel>thought\n";
        const string closing = "<channel|>";
        string thinking = "";
        string content = raw;
        if (raw.Length > 0 && opening.StartsWith(raw, StringComparison.Ordinal))
        {
            return new("", "", true);
        }
        if (raw.StartsWith(opening, StringComparison.Ordinal))
        {
            int end = raw.IndexOf(closing, opening.Length, StringComparison.Ordinal);
            if (end < 0) { return new("", raw[opening.Length..], true); }
            thinking = raw[opening.Length..end].Trim();
            content = raw[(end + closing.Length)..];
        }
        foreach (string ending in new[] { "<turn|>", "<eos>" })
        {
            int index = content.IndexOf(ending, StringComparison.Ordinal);
            if (index >= 0) { content = content[..index]; }
        }
        return new(content.Trim(), thinking, false);
    }
}
