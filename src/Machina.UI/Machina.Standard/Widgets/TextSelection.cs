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
        if (!extend && Start != End) { Move(direction < 0 ? Start : End, false); return; }
        var boundaries = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).ToArray();
        int index = Array.IndexOf(boundaries, Caret);
        int next = Math.Clamp(index + direction, 0, boundaries.Length - 1);
        if (word)
        {
            while (next > 0 && next < boundaries.Length - 1
                && !char.IsWhiteSpace(Text[boundaries[next]])) next += direction;
        }
        Move(boundaries[next], extend);
    }

    public void Delete(int direction, bool word)
    {
        if (Start == End) Step(direction, true, word);
        Replace("", int.MaxValue);
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
