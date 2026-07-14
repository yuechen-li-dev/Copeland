using System.Globalization;
using System.Runtime.CompilerServices;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// Diagnostic-profile event writer.  The current lowering still provides
/// trusted compiler-owned line templates, but formatting is recorded as
/// structural line/indent events and rendered only after the document is
/// balanced.  This keeps current bytes stable while token-family migration is
/// incremental rather than a second semantic backend.
/// </summary>
internal sealed class JavaScriptTextWriter
{
    private readonly List<LineEvent> events = [];
    private readonly JavaScriptEmissionDocument document;
    private JavaScriptScopeId currentScope;
    private int indent;
    private bool completed;

    public JavaScriptTextWriter(JavaScriptEmissionDocument document)
    {
        this.document = document;
        currentScope = document.ProgramScope;
    }

    public void EnterScope(JavaScriptScopeId scope)
    {
        document.ValidateScope(scope);
        currentScope = scope;
    }

    public void WriteLine(string text = "")
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(text);
        events.Add(new LineEvent([new TextPart(text)], indent, currentScope));
    }

    public void WriteLine(ref JavaScriptLineInterpolatedStringHandler line)
    {
        EnsureWritable();
        events.Add(new LineEvent(line.Parts, indent, currentScope));
    }

    public void Indent()
    {
        EnsureWritable();
        indent += 1;
    }

    public void Unindent()
    {
        EnsureWritable();
        if (indent == 0)
        {
            throw new InvalidOperationException("JavaScript writer indentation cannot become negative.");
        }

        indent -= 1;
    }

    public override string ToString()
    {
        EnsureWritable();
        if (indent != 0)
        {
            throw new InvalidOperationException("JavaScript writer has unfinished indentation.");
        }

        var builder = new System.Text.StringBuilder();
        foreach (LineEvent line in events)
        {
            bool hasContent = line.Parts.Any(part => part is BindingPart
                || part is TextPart { Value.Length: > 0 });
            if (hasContent)
            {
                builder.Append(' ', line.Indent * 4);
            }

            foreach (LinePart part in line.Parts)
            {
                switch (part)
                {
                    case TextPart text:
                        builder.Append(text.Value);
                        break;
                    case BindingPart binding:
                        document.Reference(line.Scope, binding.Reference.Id);
                        builder.Append(binding.Reference.Name);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown JavaScript line part '{part.GetType().Name}'.");
                }
            }

            builder.Append('\n');
        }

        document.Validate();
        completed = true;
        return builder.ToString();
    }

    private void EnsureWritable()
    {
        if (completed)
        {
            throw new InvalidOperationException("JavaScript writer is complete.");
        }
    }

    [InterpolatedStringHandler]
    internal ref struct JavaScriptLineInterpolatedStringHandler
    {
        private List<LinePart>? parts;

        public JavaScriptLineInterpolatedStringHandler(int literalLength, int formattedCount)
        {
            parts = new List<LinePart>(formattedCount + 1);
        }

        internal IReadOnlyList<LinePart> Parts => parts ?? [];

        public void AppendLiteral(string value) => AddText(value);

        public void AppendFormatted(JavaScriptBindingReference value)
        {
            EnsureParts().Add(new BindingPart(value));
        }

        public void AppendFormatted<T>(T value) => AddText(Format(value));

        public void AppendFormatted<T>(T value, string? format) => AddText(Format(value, format));

        public void AppendFormatted<T>(T value, int alignment) => AddText(Format(value));

        public void AppendFormatted<T>(T value, int alignment, string? format) => AddText(Format(value, format));

        private void AddText(string value)
        {
            List<LinePart> target = EnsureParts();
            if (target.LastOrDefault() is TextPart previous)
            {
                target[^1] = new TextPart(previous.Value + value);
                return;
            }

            target.Add(new TextPart(value));
        }

        private List<LinePart> EnsureParts() => parts ??= [];

        private static string Format<T>(T value, string? format = null)
        {
            if (value is IFormattable formattable)
            {
                return formattable.ToString(format, CultureInfo.InvariantCulture);
            }

            return value?.ToString() ?? string.Empty;
        }
    }

    private sealed record LineEvent(IReadOnlyList<LinePart> Parts, int Indent, JavaScriptScopeId Scope);

    internal abstract record LinePart;

    internal sealed record TextPart(string Value) : LinePart;

    internal sealed record BindingPart(JavaScriptBindingReference Reference) : LinePart;
}
