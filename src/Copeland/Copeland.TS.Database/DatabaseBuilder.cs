using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.TS.Database;

public static class DatabaseBuilder
{
    private static readonly byte[] RootMagic = "CTSROOT1"u8.ToArray();
    private static readonly byte[] LeafMagic = "CTSLEAF1"u8.ToArray();

    public static DatabaseBuildResult Build(DatabaseSchema schema, IEnumerable<DatabaseRow> rows)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        var stopwatch = Stopwatch.StartNew();
        DatabaseRow[] materializedRows = rows.ToArray();
        ValidateRows(schema, materializedRows);

        var partitions = new SortedDictionary<string, Partition>(StringComparer.Ordinal);
        foreach (DatabaseRow row in materializedRows)
        {
            byte[] encodedKey = EncodePartitionKey(schema, row);
            string keyIdentity = Convert.ToHexString(encodedKey);
            if (!partitions.TryGetValue(keyIdentity, out Partition? partition))
            {
                partition = new Partition(encodedKey, PartitionValues(schema, row));
                partitions.Add(keyIdentity, partition);
            }

            partition.Rows.Add(row);
        }

        var artifacts = new List<DatabaseArtifact>();
        var leafEntries = new List<LeafEntry>();
        foreach (Partition partition in partitions.Values)
        {
            byte[] leafIdentity = SHA256.HashData(partition.EncodedKey);
            string leafName = Convert.ToHexString(leafIdentity).ToLowerInvariant() + ".segment";
            string relativePath = "leaves/" + leafName;
            byte[] segment = WriteLeaf(schema, partition.Rows, leafIdentity);
            artifacts.Add(new DatabaseArtifact(relativePath, segment));
            leafEntries.Add(new LeafEntry(partition.Values, partition.EncodedKey, leafIdentity, relativePath));
        }

        byte[] root = WriteRoot(schema, leafEntries);
        artifacts.Insert(0, new DatabaseArtifact("root.index", root));
        string generatedSource = DatabaseCSharpGenerator.Generate(schema);
        stopwatch.Stop();

        return new DatabaseBuildResult(
            schema,
            artifacts,
            generatedSource,
            new DatabaseBuildMetrics(
                materializedRows.Length,
                leafEntries.Count,
                artifacts.Sum(artifact => (long)artifact.Contents.Length),
                stopwatch.Elapsed));
    }

    private static void ValidateRows(DatabaseSchema schema, IReadOnlyList<DatabaseRow> rows)
    {
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            DatabaseRow row = rows[rowIndex];
            string? extraField = row.FieldNames.FirstOrDefault(name =>
                !schema.Fields.Any(field => field.Name == name));
            if (extraField is not null)
            {
                throw new DatabaseBuildException($"Row {rowIndex} has unknown field '{extraField}'.");
            }

            foreach (DatabaseField field in schema.Fields)
            {
                if (!row.TryGetValue(field.Name, out object? value))
                {
                    throw new DatabaseBuildException($"Row {rowIndex} is missing field '{field.Name}'.");
                }

                if (!TryNormalize(field.Type, value, out _))
                {
                    throw new DatabaseBuildException(
                        $"Row {rowIndex} field '{field.Name}' is not a valid {field.Type} value.");
                }
            }
        }
    }

    private static bool TryNormalize(DatabaseScalarType type, object? value, out object normalized)
    {
        normalized = false;
        try
        {
            switch (type)
            {
                case DatabaseScalarType.Boolean when value is bool boolean:
                    normalized = boolean;
                    return true;
                case DatabaseScalarType.String when value is string text:
                    _ = new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true).GetBytes(text);
                    normalized = text;
                    return true;
                case DatabaseScalarType.Int32:
                    long integer = value switch
                    {
                        int int32 => int32,
                        long int64 => int64,
                        _ => long.MinValue,
                    };
                    if (integer is >= int.MinValue and <= int.MaxValue)
                    {
                        normalized = (int)integer;
                        return true;
                    }

                    break;
                case DatabaseScalarType.Float64:
                    double number = value switch
                    {
                        double float64 => float64,
                        float float32 => float32,
                        int int32 => int32,
                        long int64 => int64,
                        _ => double.NaN,
                    };
                    if (double.IsFinite(number))
                    {
                        normalized = number;
                        return true;
                    }

                    break;
            }
        }
        catch (Exception exception) when (
            exception is InvalidCastException or OverflowException or EncoderFallbackException)
        {
            return false;
        }

        return false;
    }

    private static byte[] EncodePartitionKey(DatabaseSchema schema, DatabaseRow row)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (string fieldName in schema.PartitionFields)
        {
            DatabaseField field = schema.Fields.Single(candidate => candidate.Name == fieldName);
            TryNormalize(field.Type, row[fieldName], out object value);
            WriteKey(writer, field.Type, value);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static IReadOnlyList<object> PartitionValues(DatabaseSchema schema, DatabaseRow row)
    {
        var values = new List<object>();
        foreach (string fieldName in schema.PartitionFields)
        {
            DatabaseField field = schema.Fields.Single(candidate => candidate.Name == fieldName);
            TryNormalize(field.Type, row[fieldName], out object value);
            values.Add(value);
        }

        return values;
    }

    private static byte[] WriteLeaf(
        DatabaseSchema schema,
        IReadOnlyList<DatabaseRow> rows,
        byte[] leafIdentity)
    {
        List<ColumnPayload> columns = schema.StoredFields
            .Select(field => new ColumnPayload(field, EncodeColumn(field, rows)))
            .ToList();

        long headerLength = LeafMagic.Length + sizeof(int) + 32 + 32 + 32 + sizeof(int) + sizeof(int);
        foreach (ColumnPayload column in columns)
        {
            headerLength += EncodedStringLength(column.Field.Name) + sizeof(byte) + sizeof(long) + sizeof(long) + 32;
        }

        long nextOffset = headerLength;
        var descriptors = new List<ColumnDescriptor>();
        foreach (ColumnPayload column in columns)
        {
            descriptors.Add(new ColumnDescriptor(
                column.Field,
                nextOffset,
                column.Payload.Length,
                SHA256.HashData(column.Payload)));
            nextOffset = checked(nextOffset + column.Payload.Length);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(LeafMagic);
        writer.Write(DatabaseSchema.StorageFormatVersion);
        writer.Write(Convert.FromHexString(schema.SchemaIdentity));
        writer.Write(Convert.FromHexString(schema.IndexIdentity));
        writer.Write(leafIdentity);
        writer.Write(rows.Count);
        writer.Write(columns.Count);
        foreach (ColumnDescriptor descriptor in descriptors)
        {
            writer.Write(descriptor.Field.Name);
            writer.Write((byte)descriptor.Field.Type);
            writer.Write(descriptor.Offset);
            writer.Write(descriptor.Length);
            writer.Write(descriptor.Hash);
        }

        foreach (ColumnPayload column in columns)
        {
            writer.Write(column.Payload);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EncodeColumn(DatabaseField field, IReadOnlyList<DatabaseRow> rows)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        if (field.Type == DatabaseScalarType.String)
        {
            var encoded = new List<byte[]>(rows.Count);
            int offset = 0;
            writer.Write(offset);
            foreach (DatabaseRow row in rows)
            {
                TryNormalize(field.Type, row[field.Name], out object normalized);
                byte[] bytes = Encoding.UTF8.GetBytes((string)normalized);
                encoded.Add(bytes);
                offset = checked(offset + bytes.Length);
                writer.Write(offset);
            }

            foreach (byte[] bytes in encoded)
            {
                writer.Write(bytes);
            }

            return stream.ToArray();
        }

        foreach (DatabaseRow row in rows)
        {
            TryNormalize(field.Type, row[field.Name], out object normalized);
            switch (field.Type)
            {
                case DatabaseScalarType.Boolean:
                    writer.Write((bool)normalized);
                    break;
                case DatabaseScalarType.Int32:
                    writer.Write((int)normalized);
                    break;
                case DatabaseScalarType.Float64:
                    writer.Write((double)normalized);
                    break;
            }
        }

        return stream.ToArray();
    }

    private static byte[] WriteRoot(DatabaseSchema schema, IReadOnlyList<LeafEntry> leaves)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(RootMagic);
        writer.Write(DatabaseSchema.StorageFormatVersion);
        writer.Write(Convert.FromHexString(schema.SchemaIdentity));
        writer.Write(Convert.FromHexString(schema.IndexIdentity));
        writer.Write(schema.PartitionFields.Count);
        foreach (string fieldName in schema.PartitionFields)
        {
            DatabaseField field = schema.Fields.Single(candidate => candidate.Name == fieldName);
            writer.Write(field.Name);
            writer.Write((byte)field.Type);
        }

        writer.Write(leaves.Count);
        foreach (LeafEntry leaf in leaves.OrderBy(leaf => Convert.ToHexString(leaf.EncodedKey), StringComparer.Ordinal))
        {
            for (int index = 0; index < leaf.Values.Count; index++)
            {
                string fieldName = schema.PartitionFields[index];
                DatabaseField field = schema.Fields.Single(candidate => candidate.Name == fieldName);
                WriteKey(writer, field.Type, leaf.Values[index]);
            }

            writer.Write(leaf.LeafIdentity);
            writer.Write(leaf.RelativePath);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteKey(BinaryWriter writer, DatabaseScalarType type, object value)
    {
        switch (type)
        {
            case DatabaseScalarType.String:
                writer.Write((string)value);
                break;
            case DatabaseScalarType.Int32:
                writer.Write((int)value);
                break;
            default:
                throw new DatabaseBuildException($"M0 cannot encode partition key type '{type}'.");
        }
    }

    private static int EncodedStringLength(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        int prefixLength = byteCount switch
        {
            < 0x80 => 1,
            < 0x4000 => 2,
            < 0x20_0000 => 3,
            < 0x1000_0000 => 4,
            _ => 5,
        };
        return prefixLength + byteCount;
    }

    private sealed record ColumnPayload(DatabaseField Field, byte[] Payload);

    private sealed record ColumnDescriptor(
        DatabaseField Field,
        long Offset,
        long Length,
        byte[] Hash);

    private sealed class Partition(byte[] encodedKey, IReadOnlyList<object> values)
    {
        public byte[] EncodedKey { get; } = encodedKey;

        public IReadOnlyList<object> Values { get; } = values;

        public List<DatabaseRow> Rows { get; } = [];
    }

    private sealed record LeafEntry(
        IReadOnlyList<object> Values,
        byte[] EncodedKey,
        byte[] LeafIdentity,
        string RelativePath);
}
