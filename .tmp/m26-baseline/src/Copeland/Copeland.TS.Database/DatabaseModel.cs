using Copeland.TS.Diagnostics;

namespace Copeland.TS.Database;

public enum DatabaseScalarType
{
    Boolean,
    Int32,
    Float64,
    String,
}

public sealed record DatabaseField(string Name, DatabaseScalarType Type);

public sealed record DatabaseSchema(
    string DatabaseName,
    string SchemaAuthority,
    string RecordName,
    IReadOnlyList<DatabaseField> Fields,
    IReadOnlyList<string> PartitionFields,
    string SchemaIdentity,
    string IndexIdentity)
{
    public const int StorageFormatVersion = 1;

    public IReadOnlyList<DatabaseField> StoredFields { get; } = Fields
        .Where(field => !PartitionFields.Contains(field.Name, StringComparer.Ordinal))
        .ToArray();
}

public sealed class DatabaseDefinitionResult(
    DatabaseSchema? schema,
    IReadOnlyList<Diagnostic> diagnostics)
{
    public DatabaseSchema? Schema { get; } = schema;

    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;

    public bool Success => Schema is not null && Diagnostics.Count == 0;
}

public sealed class DatabaseRow
{
    private readonly IReadOnlyDictionary<string, object> _values;

    public DatabaseRow(IReadOnlyDictionary<string, object> values)
    {
        _values = new Dictionary<string, object>(values, StringComparer.Ordinal);
    }

    public object this[string fieldName] => _values[fieldName];

    public bool TryGetValue(string fieldName, out object? value)
        => _values.TryGetValue(fieldName, out value);

    internal IEnumerable<string> FieldNames => _values.Keys;
}

public sealed record DatabaseArtifact(string RelativePath, byte[] Contents);

public sealed record DatabaseBuildMetrics(
    int RowCount,
    int LeafCount,
    long BinaryBytes,
    TimeSpan BuildTime);

public sealed class DatabaseBuildResult(
    DatabaseSchema schema,
    IReadOnlyList<DatabaseArtifact> artifacts,
    string generatedSource,
    DatabaseBuildMetrics metrics)
{
    public DatabaseSchema Schema { get; } = schema;

    public IReadOnlyList<DatabaseArtifact> Artifacts { get; } = artifacts;

    public string GeneratedSource { get; } = generatedSource;

    public DatabaseBuildMetrics Metrics { get; } = metrics;

    public void WriteToDirectory(string outputPath)
    {
        string root = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(root);

        foreach (DatabaseArtifact artifact in Artifacts)
        {
            string path = Path.GetFullPath(Path.Combine(root, artifact.RelativePath));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A database artifact escaped the output directory.");
            }

            string? directory = Path.GetDirectoryName(path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, artifact.Contents);
        }
    }
}

public sealed class DatabaseBuildException(string message) : Exception(message);
