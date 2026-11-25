namespace Copeland.TS.Syntax;

public sealed record SyntaxToken
{
    private ReadOnlyMemory<char> _sourceText;
    private string? _text;

    public SyntaxToken(SyntaxKind Kind, int Position, string Text, object? Value)
    {
        this.Kind = Kind;
        this.Position = Position;
        _text = Text;
        this.Value = Value;
    }

    internal SyntaxToken(SyntaxKind kind, int position, ReadOnlyMemory<char> sourceText, object? value)
    {
        Kind = kind;
        Position = position;
        _sourceText = sourceText;
        Value = value;
    }

    public SyntaxKind Kind { get; init; }
    public int Position { get; init; }
    public object? Value { get; init; }

    public string Text
    {
        get
        {
            if (_text is { } text)
            {
                return text;
            }

            string materialized = _sourceText.ToString();
            return Interlocked.CompareExchange(ref _text, materialized, null) ?? materialized;
        }
        init
        {
            _text = value;
            _sourceText = default;
        }
    }

    internal ReadOnlySpan<char> TextSpan => _text is { } text ? text.AsSpan() : _sourceText.Span;

    internal int TextLength => _text?.Length ?? _sourceText.Length;

    internal bool TextEquals(ReadOnlySpan<char> text) => TextSpan.SequenceEqual(text);

    public void Deconstruct(out SyntaxKind Kind, out int Position, out string Text, out object? Value)
        => (Kind, Position, Text, Value) = (this.Kind, this.Position, this.Text, this.Value);

    public bool Equals(SyntaxToken? other)
        => ReferenceEquals(this, other)
            || other is not null
            && Kind == other.Kind
            && Position == other.Position
            && TextEquals(other.TextSpan)
            && EqualityComparer<object?>.Default.Equals(Value, other.Value);

    public override int GetHashCode()
        => HashCode.Combine(Kind, Position, string.GetHashCode(TextSpan, StringComparison.Ordinal), Value);
}
