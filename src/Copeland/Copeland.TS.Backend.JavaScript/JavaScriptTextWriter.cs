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
    private readonly JavaScriptGeneratedDefinitionGraph generatedDefinitions;
    private readonly bool enableGeneratedDefinitionReachability;
    private readonly JavaScriptEmissionProfile profile;
    private JavaScriptScopeId currentScope;
    private int indent;
    private bool completed;

    public JavaScriptTextWriter(
        JavaScriptEmissionDocument document,
        JavaScriptEmissionProfile profile = JavaScriptEmissionProfile.Diagnostic)
        : this(document, new JavaScriptGeneratedDefinitionGraph(), false, profile)
    {
    }

    public JavaScriptTextWriter(
        JavaScriptEmissionDocument document,
        JavaScriptGeneratedDefinitionGraph generatedDefinitions,
        bool enableGeneratedDefinitionReachability,
        JavaScriptEmissionProfile profile = JavaScriptEmissionProfile.Diagnostic)
    {
        this.document = document;
        this.generatedDefinitions = generatedDefinitions;
        this.enableGeneratedDefinitionReachability = enableGeneratedDefinitionReachability;
        this.profile = profile;
        currentScope = document.ProgramScope;
    }

    public JavaScriptReachabilityReport? ReachabilityReport { get; private set; }

    public void BeginGeneratedDefinition(string stableId)
    {
        EnsureWritable();
        generatedDefinitions.Begin(stableId);
    }

    public void EndGeneratedDefinition(string stableId)
    {
        EnsureWritable();
        generatedDefinitions.End(stableId);
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
        events.Add(new LineEvent([new TextPart(text)], indent, currentScope, generatedDefinitions.CurrentDefinition));
    }

    public void WriteLine(ref JavaScriptLineInterpolatedStringHandler line)
    {
        EnsureWritable();
        events.Add(new LineEvent(line.Parts, indent, currentScope, generatedDefinitions.CurrentDefinition));
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
        IReadOnlySet<string> reachable = generatedDefinitions.MarkReachable();
        var bytesByDefinition = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (LineEvent line in events)
        {
            bool hasContent = line.Parts.Any(part => part is BindingPart
                || part is TextPart { Value.Length: > 0 });
            if (profile == JavaScriptEmissionProfile.Symbolic && !hasContent)
            {
                continue;
            }

            int renderedBytes = RenderedByteCount(line, hasContent);
            if (line.GeneratedDefinition is not null)
            {
                bytesByDefinition[line.GeneratedDefinition] = bytesByDefinition.GetValueOrDefault(line.GeneratedDefinition) + renderedBytes;
                if (enableGeneratedDefinitionReachability && !reachable.Contains(line.GeneratedDefinition))
                {
                    continue;
                }
            }

            if (hasContent)
            {
                builder.Append(' ', line.Indent * (profile == JavaScriptEmissionProfile.Symbolic ? 2 : 4));
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
        ReachabilityReport = generatedDefinitions.CreateReport(
            enableGeneratedDefinitionReachability,
            reachable,
            bytesByDefinition);
        completed = true;
        return builder.ToString();
    }

    private int RenderedByteCount(LineEvent line, bool hasContent)
    {
        int characterCount = hasContent
            ? line.Indent * (profile == JavaScriptEmissionProfile.Symbolic ? 2 : 4)
            : 0;
        foreach (LinePart part in line.Parts)
        {
            characterCount += part switch
            {
                TextPart text => text.Value.Length,
                BindingPart binding => binding.Reference.Name.Length,
                _ => 0,
            };
        }
        // Generated identifiers and syntax are ASCII. String literals can be
        // non-ASCII, so count the rendered content exactly when necessary.
        if (line.Parts.Any(part => part is TextPart text && text.Value.Any(character => character > 127)))
        {
            var rendered = new System.Text.StringBuilder();
            if (hasContent)
            {
                rendered.Append(' ', line.Indent * (profile == JavaScriptEmissionProfile.Symbolic ? 2 : 4));
            }
            foreach (LinePart part in line.Parts)
            {
                rendered.Append(part is TextPart text ? text.Value : ((BindingPart)part).Reference.Name);
            }
            rendered.Append('\n');
            return System.Text.Encoding.UTF8.GetByteCount(rendered.ToString());
        }
        return characterCount + 1;
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

    private sealed record LineEvent(
        IReadOnlyList<LinePart> Parts,
        int Indent,
        JavaScriptScopeId Scope,
        string? GeneratedDefinition);

    internal abstract record LinePart;

    internal sealed record TextPart(string Value) : LinePart;

    internal sealed record BindingPart(JavaScriptBindingReference Reference) : LinePart;
}
