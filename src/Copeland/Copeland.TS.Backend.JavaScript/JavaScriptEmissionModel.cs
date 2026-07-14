using System.Globalization;
using System.Text;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// Backend-private identity for a generated JavaScript lexical binding.  The
/// value is deliberately not its printed spelling: profiles assign spellings
/// after semantic emission has registered declarations and references.
/// </summary>
internal readonly record struct JavaScriptBindingId(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

internal readonly record struct JavaScriptScopeId(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

internal enum JavaScriptScopeKind
{
    Program,
    Function,
    Block,
}

internal enum JavaScriptBindingRole
{
    RuntimeHelper,
    TypeToken,
    ProvenanceSet,
    Validator,
    Constructor,
    Storage,
    SymbolSlot,
    Singleton,
    Temporary,
    Flow,
    UserVisible,
}

internal enum JavaScriptDeclarationKind
{
    Const,
    Let,
    Function,
    Parameter,
}

internal sealed record JavaScriptBinding(
    JavaScriptBindingId Id,
    JavaScriptScopeId Scope,
    JavaScriptBindingRole Role,
    string DiagnosticBaseName,
    JavaScriptDeclarationKind DeclarationKind,
    string? CompilerOrigin,
    bool IsUserVisible,
    bool MayBeMangled,
    int AllocationOrdinal)
{
    public string? AssignedName { get; set; }

    public int ReferenceCount { get; set; }
}

internal readonly record struct JavaScriptBindingReference(JavaScriptBindingId Id, string Name)
{
    public static JavaScriptBindingReference Empty { get; } = new(new JavaScriptBindingId(-1), string.Empty);

    public bool IsEmpty => Id.Value < 0;

    public override string ToString() => Name;

    public static implicit operator string(JavaScriptBindingReference reference) => reference.Name;
}

internal readonly record struct JavaScriptAllocatedBinding(JavaScriptBindingReference Reference)
{
    public JavaScriptBindingId Id => Reference.Id;

    public string Name => Reference.Name;
}

/// <summary>
/// Diagnostic-profile allocator.  Its allocation order deliberately matches
/// the pre-M0b <c>__cope_m3_&lt;purpose&gt;_&lt;ordinal&gt;</c> spelling, while callers
/// retain a typed identity for future profile-specific naming.
/// </summary>
internal sealed class JavaScriptNameAllocator
{
    private readonly JavaScriptEmissionDocument document;
    private readonly JavaScriptScopeId defaultScope;
    private readonly HashSet<string> occupied;
    private int nextIndex;

    public JavaScriptNameAllocator(
        JavaScriptEmissionDocument document,
        JavaScriptScopeId scope,
        IEnumerable<string> reservedNames)
    {
        this.document = document;
        defaultScope = scope;
        occupied = new HashSet<string>(reservedNames, StringComparer.Ordinal);
    }

    public JavaScriptAllocatedBinding Allocate(
        JavaScriptBindingRole role,
        string diagnosticBaseName,
        JavaScriptDeclarationKind declarationKind = JavaScriptDeclarationKind.Const,
        string? compilerOrigin = null)
    {
        return Allocate(defaultScope, role, diagnosticBaseName, declarationKind, compilerOrigin);
    }

    public JavaScriptAllocatedBinding Allocate(
        JavaScriptScopeId scope,
        JavaScriptBindingRole role,
        string diagnosticBaseName,
        JavaScriptDeclarationKind declarationKind = JavaScriptDeclarationKind.Const,
        string? compilerOrigin = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(diagnosticBaseName);
        while (true)
        {
            string candidate = $"__cope_m3_{diagnosticBaseName}_{nextIndex++}";
            if (!occupied.Add(candidate))
            {
                continue;
            }

            JavaScriptBindingId binding = document.RegisterBinding(
                scope,
                role,
                diagnosticBaseName,
                declarationKind,
                compilerOrigin ?? diagnosticBaseName,
                isUserVisible: false,
                mayBeMangled: true);
            document.Declare(binding);
            document.AssignName(binding, candidate);
            return new JavaScriptAllocatedBinding(new JavaScriptBindingReference(binding, candidate));
        }
    }
}

/// <summary>
/// The small backend-local lexical document used by JavaScript emission.  It
/// is intentionally not a JavaScript AST or compiler-wide IR.
/// </summary>
internal sealed class JavaScriptEmissionDocument
{
    private readonly List<Scope> scopes = [];
    private readonly List<JavaScriptBinding> bindings = [];
    private readonly HashSet<JavaScriptBindingId> declarations = [];

    public JavaScriptEmissionDocument()
    {
        ProgramScope = CreateScopeCore(JavaScriptScopeKind.Program, parent: null);
    }

    public JavaScriptScopeId ProgramScope { get; }

    public IReadOnlyList<JavaScriptBinding> Bindings => bindings;

    public JavaScriptScopeId CreateScope(JavaScriptScopeKind kind, JavaScriptScopeId parent)
    {
        GetScope(parent);
        return CreateScopeCore(kind, parent);
    }

    public void ValidateScope(JavaScriptScopeId scope)
    {
        GetScope(scope);
    }

    public JavaScriptBindingId RegisterBinding(
        JavaScriptScopeId scope,
        JavaScriptBindingRole role,
        string diagnosticBaseName,
        JavaScriptDeclarationKind declarationKind,
        string? compilerOrigin = null,
        bool isUserVisible = false,
        bool mayBeMangled = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(diagnosticBaseName);
        GetScope(scope);

        var id = new JavaScriptBindingId(bindings.Count);
        bindings.Add(new JavaScriptBinding(
            id,
            scope,
            role,
            diagnosticBaseName,
            declarationKind,
            compilerOrigin,
            isUserVisible,
            mayBeMangled,
            bindings.Count));
        return id;
    }

    public void Declare(JavaScriptBindingId binding)
    {
        JavaScriptBinding definition = GetBinding(binding);
        if (!declarations.Add(binding))
        {
            throw new InvalidOperationException($"JavaScript binding '{binding}' was declared more than once.");
        }

        Scope scope = GetScope(definition.Scope);
        if (!scope.Declarations.Add(binding))
        {
            throw new InvalidOperationException($"JavaScript scope '{definition.Scope}' contains a duplicate binding declaration.");
        }
    }

    public void Reference(JavaScriptScopeId fromScope, JavaScriptBindingId binding)
    {
        GetScope(fromScope);
        JavaScriptBinding definition = GetBinding(binding);
        if (!IsVisibleFrom(definition.Scope, fromScope))
        {
            throw new InvalidOperationException($"JavaScript binding '{binding}' is outside the legal lexical scope.");
        }

        definition.ReferenceCount += 1;
    }

    public void AssignDiagnosticNames(ISet<string> reservedNames)
    {
        ArgumentNullException.ThrowIfNull(reservedNames);
        var assigned = new HashSet<string>(reservedNames, StringComparer.Ordinal);
        foreach (JavaScriptBinding binding in bindings.OrderBy(binding => binding.AllocationOrdinal))
        {
            string baseName = binding.IsUserVisible
                ? binding.DiagnosticBaseName
                : "__cope_m3_" + binding.DiagnosticBaseName;
            string name = baseName;
            int suffix = 0;
            while (!assigned.Add(name))
            {
                name = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix += 1;
            }

            if (!JavaScriptIdentifierEncoder.IsValidGeneratedIdentifier(name))
            {
                throw new InvalidOperationException($"Generated JavaScript binding '{name}' is not a valid identifier.");
            }

            binding.AssignedName = name;
        }
    }

    public string GetAssignedName(JavaScriptBindingId binding)
    {
        return GetBinding(binding).AssignedName
            ?? throw new InvalidOperationException($"JavaScript binding '{binding}' has no assigned Diagnostic name.");
    }

    public void AssignName(JavaScriptBindingId binding, string name)
    {
        if (!JavaScriptIdentifierEncoder.IsValidGeneratedIdentifier(name))
        {
            throw new InvalidOperationException($"Generated JavaScript binding '{name}' is not a valid identifier.");
        }

        JavaScriptBinding definition = GetBinding(binding);
        if (definition.AssignedName is not null)
        {
            throw new InvalidOperationException($"JavaScript binding '{binding}' already has an assigned name.");
        }

        definition.AssignedName = name;
    }

    public void Validate()
    {
        foreach (JavaScriptBinding binding in bindings)
        {
            if (!declarations.Contains(binding.Id))
            {
                throw new InvalidOperationException($"JavaScript binding '{binding.Id}' was referenced or allocated without a declaration.");
            }

            if (binding.AssignedName is null || !JavaScriptIdentifierEncoder.IsValidGeneratedIdentifier(binding.AssignedName))
            {
                throw new InvalidOperationException($"JavaScript binding '{binding.Id}' has no valid assigned name.");
            }
        }

        foreach (Scope scope in scopes)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JavaScriptBindingId binding in scope.Declarations)
            {
                if (!names.Add(GetAssignedName(binding)))
                {
                    throw new InvalidOperationException($"JavaScript scope '{scope.Id}' has duplicate final names.");
                }
            }
        }
    }

    private JavaScriptScopeId CreateScopeCore(JavaScriptScopeKind kind, JavaScriptScopeId? parent)
    {
        var id = new JavaScriptScopeId(scopes.Count);
        scopes.Add(new Scope(id, kind, parent));
        return id;
    }

    private JavaScriptBinding GetBinding(JavaScriptBindingId binding)
    {
        if (binding.Value < 0 || binding.Value >= bindings.Count)
        {
            throw new InvalidOperationException($"JavaScript binding '{binding}' is unresolved.");
        }

        return bindings[binding.Value];
    }

    private Scope GetScope(JavaScriptScopeId scope)
    {
        if (scope.Value < 0 || scope.Value >= scopes.Count)
        {
            throw new InvalidOperationException($"JavaScript scope '{scope}' is unresolved.");
        }

        return scopes[scope.Value];
    }

    private bool IsVisibleFrom(JavaScriptScopeId declaredScope, JavaScriptScopeId fromScope)
    {
        JavaScriptScopeId? current = fromScope;
        while (current is { } scope)
        {
            if (scope == declaredScope)
            {
                return true;
            }

            current = GetScope(scope).Parent;
        }

        return false;
    }

    private sealed class Scope(JavaScriptScopeId id, JavaScriptScopeKind kind, JavaScriptScopeId? parent)
    {
        public JavaScriptScopeId Id { get; } = id;

        public JavaScriptScopeKind Kind { get; } = kind;

        public JavaScriptScopeId? Parent { get; } = parent;

        public HashSet<JavaScriptBindingId> Declarations { get; } = [];
    }
}

/// <summary>
/// Token writer for the emitted JavaScript subset.  It owns token adjacency,
/// literal spelling, indentation and final-LF validation.  The legacy line
/// bridge remains only while M0b preserves the existing large emitter's exact
/// Diagnostic layout; generated names continue to be allocated centrally.
/// </summary>
internal sealed class JavaScriptTokenWriter
{
    private readonly StringBuilder builder = new();
    private TokenClass previous = TokenClass.None;
    private string? previousText;
    private int indentation;
    private bool atLineStart = true;
    private bool completed;

    public void Keyword(string value) => WriteToken(value, TokenClass.Word);

    public void ExternalIdentifier(string value) => WriteToken(value, TokenClass.Word);

    public void BindingReference(JavaScriptEmissionDocument document, JavaScriptBindingId binding)
    {
        document.Reference(document.ProgramScope, binding);
        WriteToken(document.GetAssignedName(binding), TokenClass.Word);
    }

    public void Number(object value) => WriteToken(JavaScriptLiteralWriter.WriteNumber(value), TokenClass.Number);

    public void String(string value) => WriteToken(JavaScriptLiteralWriter.WriteString(value), TokenClass.String);

    public void Punctuator(string value)
    {
        if (value is not ("(" or ")" or "[" or "]" or "{" or "}" or ";" or "," or "." or "+" or "-" or "*" or "%" or "++" or "--" or "/" or "=" or "===" or "!==" or "&&" or "||" or "!" or "<" or ">" or "<=" or ">=" or "=>" or "?" or ":"))
        {
            throw new InvalidOperationException($"Unsupported JavaScript punctuator '{value}'.");
        }

        WriteToken(value, TokenClass.Punctuator);
    }

    public void Space()
    {
        EnsureWritable();
        if (!atLineStart && builder.Length > 0 && builder[^1] != ' ')
        {
            builder.Append(' ');
        }
    }

    public void LineBreak()
    {
        EnsureWritable();
        builder.Append('\n');
        previous = TokenClass.None;
        previousText = null;
        atLineStart = true;
    }

    public void Indent() => indentation += 1;

    public void Unindent()
    {
        if (indentation == 0)
        {
            throw new InvalidOperationException("JavaScript writer indentation cannot become negative.");
        }

        indentation -= 1;
    }

    public string Complete()
    {
        EnsureWritable();
        if (indentation != 0)
        {
            throw new InvalidOperationException("JavaScript writer has unfinished indentation.");
        }

        if (builder.Length == 0 || builder[^1] != '\n')
        {
            builder.Append('\n');
        }

        completed = true;
        return builder.ToString();
    }

    private void WriteToken(string value, TokenClass current)
    {
        EnsureWritable();
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (atLineStart)
        {
            builder.Append(' ', indentation * 4);
            atLineStart = false;
        }

        if (RequiresSeparator(previous, previousText, current, value))
        {
            builder.Append(' ');
        }

        builder.Append(value);
        previous = current;
        previousText = value;
    }

    private static bool RequiresSeparator(TokenClass previous, string? previousText, TokenClass current, string value)
    {
        if (previous == TokenClass.None)
        {
            return false;
        }

        if ((previous is TokenClass.Word or TokenClass.Number)
            && (current is TokenClass.Word or TokenClass.Number))
        {
            return true;
        }

        if (previous == TokenClass.Number && value == ".")
        {
            return true;
        }

        return previous == TokenClass.Punctuator
            && ((previousText == "+" && value is "+" or "++")
                || (previousText == "-" && value is "-" or "--")
                || (previousText == "/" && value is "/" or "*"));
    }

    private void EnsureWritable()
    {
        if (completed)
        {
            throw new InvalidOperationException("JavaScript writer is complete.");
        }
    }

    private enum TokenClass
    {
        None,
        Word,
        Number,
        String,
        Punctuator,
    }
}
