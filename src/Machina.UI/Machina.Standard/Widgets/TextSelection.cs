using System.Globalization;

namespace Machina.Standard.Widgets;

/// <summary>Small single-line selection editor. Indices are UTF-16, movement/deletion use grapheme boundaries.</summary>
internal sealed class TextSelection(string text)
{
    private readonly List<(string Text, int Caret, int Anchor)> undo = [];
    private readonly List<(string Text, int Caret, int Anchor)> redo = [];
    public string Text { get; private set; } = text;
    public string ModelText { get; set; } = text;
    public int Caret { get; private set; } = text.Length;
    public int Anchor { get; private set; } = text.Length;
    public int Start => Math.Min(Caret, Anchor);
    public int End => Math.Max(Caret, Anchor);
    public string Selected => Text[Start..End];

    public void Reset(string value)
    {
        undo.Clear();
        redo.Clear();
        Text = value;
        Caret = Math.Min(Caret, value.Length);
        Anchor = Math.Min(Anchor, value.Length);
        Move(Caret, false);
    }

    public void SelectAll() { Anchor = 0; Caret = Text.Length; }

    public void Move(int destination, bool extend)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length);
        Caret = boundaries.LastOrDefault(position => position <= Math.Clamp(destination, 0, Text.Length));
        if (!extend) Anchor = Caret;
    }

    public void Step(int direction, bool extend, bool word = false)
    {
        if (!extend && !word && Start != End) { Move(direction < 0 ? Start : End, false); return; }
        if (word) { Move(WordBoundary(Caret, direction), extend); return; }
        var boundaries = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).ToArray();
        int index = Array.IndexOf(boundaries, Caret);
        if (index < 0) index = Math.Max(0, Array.FindLastIndex(boundaries, b => b < Caret));
        int next = Math.Clamp(index + direction, 0, boundaries.Length - 1);
        Move(boundaries[next], extend);
    }

    /// <summary>
    /// Windows-style word stops. Backward: skip separators, then the word, landing on its start.
    /// Forward: skip the word, then separators, landing on the next word's start (or the end).
    /// </summary>
    private int WordBoundary(int position, int direction)
    {
        bool Separator(int i) => char.IsWhiteSpace(Text[i]) || char.IsPunctuation(Text[i]) && Text[i] is not '\'' and not '_';
        int i = position;
        if (direction < 0)
        {
            while (i > 0 && Separator(i - 1)) i--;
            while (i > 0 && !Separator(i - 1)) i--;
        }
        else
        {
            while (i < Text.Length && !Separator(i)) i++;
            while (i < Text.Length && Separator(i)) i++;
        }
        return i;
    }

    /// <summary>Selects the word (or run of separators) under the caret, as a double click does.</summary>
    public void SelectWord()
    {
        if (Text.Length == 0) return;
        int at = Math.Clamp(Caret, 0, Text.Length - 1);
        // A click on a word's right half lands the caret after it: select that word, not the following space.
        if (at > 0 && at == Caret && !char.IsLetterOrDigit(Text[at]) && char.IsLetterOrDigit(Text[at - 1])) at--;
        bool space = char.IsWhiteSpace(Text[at]);
        bool Same(int i) => char.IsWhiteSpace(Text[i]) == space && (space || char.IsLetterOrDigit(Text[i]) || Text[i] is '\'' or '_'
            || char.IsSurrogate(Text[i]) || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(Text[i]) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark);
        if (!Same(at)) { Anchor = at; Caret = Math.Min(Text.Length, at + 1); Move(Caret, true); return; }
        int start = at, end = at + 1;
        while (start > 0 && Same(start - 1)) start--;
        while (end < Text.Length && Same(end)) end++;
        Anchor = start; Caret = end;
    }

    public void Delete(int direction, bool word)
    {
        if (Start == End) Step(direction, true, word);
        if (Start != End) Replace("", int.MaxValue);
    }

    public void Replace(string value, int maximumLength)
    {
        int start = Start;
        int available = Math.Max(0, maximumLength - (Text.Length - (End - start)));
        int limit = Math.Min(value.Length, available);
        if (limit > 0 && limit < value.Length && char.IsHighSurrogate(value[limit - 1])
            && char.IsLowSurrogate(value[limit])) limit--;
        value = value[..limit];
        string candidate = Text[..start] + value + Text[End..];
        if (candidate != Text)
        {
            if (undo.Count == 64) undo.RemoveAt(0);
            undo.Add((Text, Caret, Anchor));
            redo.Clear();
        }
        Text = candidate;
        Move(Math.Min(start + value.Length, Text.Length), false);
    }

    public void Undo(bool forward)
    {
        var source = forward ? redo : undo;
        var destination = forward ? undo : redo;
        if (source.Count == 0) return;
        destination.Add((Text, Caret, Anchor));
        (Text, Caret, Anchor) = source[^1];
        source.RemoveAt(source.Count - 1);
    }
}
