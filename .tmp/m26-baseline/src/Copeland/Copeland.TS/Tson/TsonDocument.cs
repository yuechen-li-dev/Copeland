using Copeland.TS.Diagnostics;

namespace Copeland.TS.Tson;

public enum TsonDocumentProfile
{
    ObjectTypeScript,
    CanonicalTson,
}

public sealed class TsonLimits
{
    public static TsonLimits Default { get; } = new();

    public TsonLimits(
        int maximumSourceLength = 1_048_576,
        int maximumNestingDepth = 64,
        int maximumDeclarationCount = 256,
        int maximumFieldsPerAggregate = 256,
        int maximumEnumCases = 256,
        int maximumPayloadsPerCase = 64,
        int maximumValueNodeCount = 100_000,
        int maximumStringLength = 262_144,
        int maximumArrayLength = 100_000,
        int maximumTableColumnCount = 256,
        int maximumTableRowCount = 100_000,
        int maximumTableCellCount = 100_000,
        int maximumCanonicalUtf8ByteCount = 1_048_576)
    {
        MaximumSourceLength = RequirePositive(maximumSourceLength, nameof(maximumSourceLength));
        MaximumNestingDepth = RequirePositive(maximumNestingDepth, nameof(maximumNestingDepth));
        MaximumDeclarationCount = RequirePositive(maximumDeclarationCount, nameof(maximumDeclarationCount));
        MaximumFieldsPerAggregate = RequirePositive(maximumFieldsPerAggregate, nameof(maximumFieldsPerAggregate));
        MaximumEnumCases = RequirePositive(maximumEnumCases, nameof(maximumEnumCases));
        MaximumPayloadsPerCase = RequirePositive(maximumPayloadsPerCase, nameof(maximumPayloadsPerCase));
        MaximumValueNodeCount = RequirePositive(maximumValueNodeCount, nameof(maximumValueNodeCount));
        MaximumStringLength = RequirePositive(maximumStringLength, nameof(maximumStringLength));
        MaximumArrayLength = RequirePositive(maximumArrayLength, nameof(maximumArrayLength));
        MaximumTableColumnCount = RequirePositive(maximumTableColumnCount, nameof(maximumTableColumnCount));
        MaximumTableRowCount = RequirePositive(maximumTableRowCount, nameof(maximumTableRowCount));
        MaximumTableCellCount = RequirePositive(maximumTableCellCount, nameof(maximumTableCellCount));
        MaximumCanonicalUtf8ByteCount = RequirePositive(maximumCanonicalUtf8ByteCount, nameof(maximumCanonicalUtf8ByteCount));
    }

    public int MaximumSourceLength { get; }

    public int MaximumNestingDepth { get; }

    public int MaximumDeclarationCount { get; }

    public int MaximumFieldsPerAggregate { get; }

    public int MaximumEnumCases { get; }

    public int MaximumPayloadsPerCase { get; }

    public int MaximumValueNodeCount { get; }

    public int MaximumStringLength { get; }

    public int MaximumArrayLength { get; }

    public int MaximumTableColumnCount { get; }

    public int MaximumTableRowCount { get; }

    public int MaximumTableCellCount { get; }

    public int MaximumCanonicalUtf8ByteCount { get; }

    private static int RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A TSON resource limit must be positive.");
        }

        return value;
    }
}

public sealed class TsonDiagnostic
{
    public TsonDiagnostic(string code, string message, int position, int length)
    {
        Code = code;
        Message = message;
        Position = position;
        Length = length;
    }

    public string Code { get; }

    public string Message { get; }

    public int Position { get; }

    public int Length { get; }
}

public sealed class TsonDocument
{
    public TsonDocument(TsonCatalog catalog, TsonValue root)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(root);
        Catalog = catalog;
        Root = root;
    }

    public TsonCatalog Catalog { get; }

    public TsonValue Root { get; }
}

public sealed class TsonReadResult
{
    internal TsonReadResult(
        TsonDocument? document,
        IEnumerable<Diagnostic> syntaxDiagnostics,
        IEnumerable<TsonDiagnostic> diagnostics)
    {
        Document = document;
        SyntaxDiagnostics = TsonCollection.Copy(syntaxDiagnostics, nameof(syntaxDiagnostics));
        Diagnostics = TsonCollection.Copy(diagnostics, nameof(diagnostics));
    }

    public bool Success => Document is not null && SyntaxDiagnostics.Count == 0 && Diagnostics.Count == 0;

    public TsonDocument? Document { get; }

    public IReadOnlyList<Diagnostic> SyntaxDiagnostics { get; }

    public IReadOnlyList<TsonDiagnostic> Diagnostics { get; }
}
