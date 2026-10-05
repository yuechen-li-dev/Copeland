using System.Globalization;
using System.Text;

namespace Copeland.TS.Tson;

public static class TsonCanonicalPrinter
{
    public static string Print(TsonDocument document, TsonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        limits ??= TsonLimits.Default;
        var writer = new Writer(document.Catalog, limits.MaximumCanonicalUtf8ByteCount);
        return writer.Print(document);
    }

    public static byte[] PrintUtf8(TsonDocument document, TsonLimits? limits = null)
    {
        return Encoding.UTF8.GetBytes(Print(document, limits));
    }

    private sealed class Writer
    {
        private readonly TsonCatalog _catalog;
        private readonly Dictionary<string, TsonNominalDefinition> _definitionsByIdentity;
        private readonly CanonicalBuffer _builder;

        public Writer(TsonCatalog catalog, int maximumUtf8ByteCount)
        {
            _catalog = catalog;
            _builder = new CanonicalBuffer(maximumUtf8ByteCount);
            _definitionsByIdentity = catalog.Definitions.ToDictionary(
                definition => definition.Identity,
                StringComparer.Ordinal);
        }

        public string Print(TsonDocument document)
        {
            _builder.Append("const $schema: string = ");
            AppendString(_catalog.SchemaIdentity);
            AppendLine(";");

            foreach (var definition in _catalog.Definitions.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                AppendLine();
                if (definition is TsonRecordDefinition record)
                {
                    AppendRecordDefinition(record);
                }
                else if (definition is TsonEnumDefinition @enum)
                {
                    AppendEnumDefinition(@enum);
                }
                else if (definition is TsonTableSchema tableSchema)
                {
                    if (document.Root is not TsonTable table
                        || !table.Schema.IdentityValue.Equals(tableSchema.IdentityValue))
                    {
                        throw new ArgumentException(
                            "A TSON table definition requires the matching table document root.",
                            nameof(document));
                    }

                    AppendTableDefinition(tableSchema, table);
                }
            }

            AppendLine();
            _builder.Append("const $value = ");
            if (document.Root is TsonTable rootTable)
            {
                _builder.Append(rootTable.Schema.Name);
            }
            else
            {
                AppendValue(document.Root, 0);
            }
            AppendLine(";");
            return _builder.ToString();
        }

        private void AppendTableDefinition(TsonTableSchema schema, TsonTable table)
        {
            _builder.Append("record table ");
            _builder.Append(schema.Name);
            AppendLine(" {");
            for (var columnIndex = 0; columnIndex < schema.Columns.Count; columnIndex++)
            {
                var columnSchema = schema.Columns[columnIndex];
                var column = table.Columns[columnIndex];
                _builder.Append("    ");
                _builder.Append(columnSchema.Name);
                _builder.Append(": ");
                AppendType(columnSchema.ElementType);
                _builder.Append(" = ");
                AppendValues(column.Cells, indentation: 1);
                AppendLine(";");
            }

            AppendLine("}");
        }

        private void AppendRecordDefinition(TsonRecordDefinition definition)
        {
            _builder.Append("record ");
            _builder.Append(definition.Name);
            AppendLine(" {");
            foreach (var field in definition.Fields)
            {
                _builder.Append("    ");
                _builder.Append(field.Name);
                _builder.Append(": ");
                AppendType(field.Type);
                AppendLine(";");
            }

            AppendLine("}");
        }

        private void AppendEnumDefinition(TsonEnumDefinition definition)
        {
            _builder.Append("enum ");
            _builder.Append(definition.Name);
            AppendLine(" {");
            foreach (var item in definition.Cases)
            {
                _builder.Append("    ");
                _builder.Append(item.Name);
                if (item.Payloads.Count > 0)
                {
                    _builder.Append('(');
                    for (var index = 0; index < item.Payloads.Count; index++)
                    {
                        if (index > 0)
                        {
                            _builder.Append(", ");
                        }

                        var payload = item.Payloads[index];
                        _builder.Append(payload.Name);
                        _builder.Append(": ");
                        AppendType(payload.Type);
                    }

                    _builder.Append(')');
                }

                AppendLine(",");
            }

            AppendLine("}");
        }

        private void AppendType(TsonTypeReference type)
        {
            _builder.Append(type.Kind switch
            {
                TsonTypeKind.Boolean => "boolean",
                TsonTypeKind.Number => "number",
                TsonTypeKind.String => "string",
                TsonTypeKind.Object => "$object",
                TsonTypeKind.Record or TsonTypeKind.Enum => type.NominalName,
                TsonTypeKind.Array => FormatArrayType(type),
                _ => throw new InvalidOperationException("Unknown TSON type kind."),
            });
        }

        private static string FormatArrayType(TsonTypeReference type)
        {
            if (type.ElementType is null)
            {
                throw new InvalidOperationException("A TSON array type requires an element type.");
            }

            return FormatType(type.ElementType) + "[]";
        }

        private static string FormatType(TsonTypeReference type)
        {
            return type.Kind switch
            {
                TsonTypeKind.Boolean => "boolean",
                TsonTypeKind.Number => "number",
                TsonTypeKind.String => "string",
                TsonTypeKind.Object => "$object",
                TsonTypeKind.Record or TsonTypeKind.Enum => type.NominalName!,
                TsonTypeKind.Array => FormatArrayType(type),
                _ => throw new InvalidOperationException("Unknown TSON type kind."),
            };
        }

        private void AppendValue(TsonValue value, int indentation)
        {
            switch (value)
            {
                case TsonBoolean boolean:
                    _builder.Append(boolean.Value ? "true" : "false");
                    break;
                case TsonNumber number:
                    _builder.Append("$number(\"");
                    _builder.Append(number.Bits.ToString("X16", CultureInfo.InvariantCulture));
                    _builder.Append("\")");
                    break;
                case TsonString text:
                    AppendString(text.Value);
                    break;
                case TsonArray array:
                    AppendArray(array, indentation);
                    break;
                case TsonObject @object:
                    AppendFields(@object.Fields, indentation);
                    break;
                case TsonRecord record:
                    AppendRecord(record, indentation);
                    break;
                case TsonEnum @enum:
                    AppendEnum(@enum, indentation);
                    break;
                case TsonTable:
                    throw new InvalidOperationException("A TSON table can only be printed as the document root.");
                default:
                    throw new InvalidOperationException("Unknown TSON value variant.");
            }
        }

        private void AppendArray(TsonArray array, int indentation)
        {
            AppendValues(array.Elements, indentation);
        }

        private void AppendValues(IReadOnlyList<TsonValue> values, int indentation)
        {
            if (values.Count == 0)
            {
                _builder.Append("[]");
                return;
            }

            AppendLine("[");
            for (var index = 0; index < values.Count; index++)
            {
                AppendIndent(indentation + 1);
                AppendValue(values[index], indentation + 1);
                AppendLine(",");
            }

            AppendIndent(indentation);
            _builder.Append(']');
        }

        private void AppendRecord(TsonRecord record, int indentation)
        {
            if (!_definitionsByIdentity.TryGetValue(record.Identity, out var definition)
                || definition is not TsonRecordDefinition recordDefinition)
            {
                throw new ArgumentException(
                    $"Record identity '{record.Identity}' is absent from the TSON catalog.",
                    nameof(record));
            }

            ValidateFields(record.Fields, recordDefinition.Fields, "record");
            _builder.Append("$record.");
            _builder.Append(recordDefinition.Name);
            _builder.Append('(');
            AppendFields(record.Fields, indentation);
            _builder.Append(')');
        }

        private void AppendEnum(TsonEnum value, int indentation)
        {
            if (!_definitionsByIdentity.TryGetValue(value.EnumIdentity, out var definition)
                || definition is not TsonEnumDefinition enumDefinition)
            {
                throw new ArgumentException(
                    $"Enum identity '{value.EnumIdentity}' is absent from the TSON catalog.",
                    nameof(value));
            }

            var item = enumDefinition.Cases.FirstOrDefault(candidate => candidate.Identity == value.CaseIdentity);
            if (item is null || item.Name != value.CaseName)
            {
                throw new ArgumentException(
                    $"Enum case identity '{value.CaseIdentity}' is invalid for '{enumDefinition.Name}'.",
                    nameof(value));
            }

            ValidateFields(value.Payloads, item.Payloads, "enum payload");
            _builder.Append(enumDefinition.Name);
            _builder.Append('.');
            _builder.Append(item.Name);
            if (value.Payloads.Count == 0)
            {
                return;
            }

            AppendLine("(");
            for (var index = 0; index < value.Payloads.Count; index++)
            {
                AppendIndent(indentation + 1);
                AppendValue(value.Payloads[index].Value, indentation + 1);
                if (index + 1 < value.Payloads.Count)
                {
                    _builder.Append(',');
                }

                AppendLine();
            }

            AppendIndent(indentation);
            _builder.Append(')');
        }

        private void AppendFields(IReadOnlyList<TsonField> fields, int indentation)
        {
            if (fields.Count == 0)
            {
                _builder.Append("{}");
                return;
            }

            AppendLine("{");
            foreach (var field in fields)
            {
                AppendIndent(indentation + 1);
                AppendString(field.Name);
                _builder.Append(": ");
                AppendValue(field.Value, indentation + 1);
                AppendLine(",");
            }

            AppendIndent(indentation);
            _builder.Append('}');
        }

        private static void ValidateFields(
            IReadOnlyList<TsonField> values,
            IReadOnlyList<TsonFieldDefinition> definitions,
            string description)
        {
            if (values.Count != definitions.Count)
            {
                throw new ArgumentException($"TSON {description} field count does not match its catalog definition.");
            }

            for (var index = 0; index < values.Count; index++)
            {
                if (values[index].Name != definitions[index].Name
                    || values[index].Identity != definitions[index].Identity)
                {
                    throw new ArgumentException($"TSON {description} fields do not match declaration order and identity.");
                }
            }
        }

        private void AppendString(string value)
        {
            _builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"':
                        _builder.Append("\\\"");
                        break;
                    case '\\':
                        _builder.Append("\\\\");
                        break;
                    case '\b':
                        _builder.Append("\\b");
                        break;
                    case '\f':
                        _builder.Append("\\f");
                        break;
                    case '\n':
                        _builder.Append("\\n");
                        break;
                    case '\r':
                        _builder.Append("\\r");
                        break;
                    case '\t':
                        _builder.Append("\\t");
                        break;
                    case '\u2028':
                    case '\u2029':
                        AppendUnicodeEscape(character);
                        break;
                    default:
                        if (char.IsHighSurrogate(character)
                            && index + 1 < value.Length
                            && char.IsLowSurrogate(value[index + 1]))
                        {
                            _builder.Append(character);
                            _builder.Append(value[++index]);
                        }
                        else if (character < ' ' || char.IsSurrogate(character))
                        {
                            AppendUnicodeEscape(character);
                        }
                        else
                        {
                            _builder.Append(character);
                        }

                        break;
                }
            }

            _builder.Append('"');
        }

        private void AppendUnicodeEscape(char value)
        {
            _builder.Append("\\u");
            _builder.Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
        }

        private void AppendIndent(int indentation)
        {
            _builder.Append(' ', indentation * 4);
        }

        private void AppendLine(string text = "")
        {
            _builder.Append(text);
            _builder.Append('\n');
        }
    }

    private sealed class CanonicalBuffer
    {
        private readonly int _maximumUtf8ByteCount;
        private readonly StringBuilder _builder = new();
        private int _utf8ByteCount;
        private bool _pendingHighSurrogate;

        public CanonicalBuffer(int maximumUtf8ByteCount)
        {
            _maximumUtf8ByteCount = maximumUtf8ByteCount;
        }

        public void Append(string? text)
        {
            if (text is null)
            {
                return;
            }

            foreach (var character in text)
            {
                Append(character);
            }
        }

        public void Append(char character)
        {
            if (_pendingHighSurrogate)
            {
                if (char.IsLowSurrogate(character))
                {
                    AddBytes(4);
                    _pendingHighSurrogate = false;
                    _builder.Append(character);
                    return;
                }

                AddBytes(3);
                _pendingHighSurrogate = false;
            }

            if (char.IsHighSurrogate(character))
            {
                _pendingHighSurrogate = true;
            }
            else
            {
                AddBytes(character <= 0x7F ? 1 : character <= 0x7FF ? 2 : 3);
            }

            _builder.Append(character);
        }

        public void Append(char character, int repeatCount)
        {
            for (var index = 0; index < repeatCount; index++)
            {
                Append(character);
            }
        }

        public override string ToString()
        {
            if (_pendingHighSurrogate)
            {
                AddBytes(3);
                _pendingHighSurrogate = false;
            }

            return _builder.ToString();
        }

        private void AddBytes(int count)
        {
            if (_utf8ByteCount > _maximumUtf8ByteCount - count)
            {
                throw new TsonCanonicalLimitException(_maximumUtf8ByteCount);
            }

            _utf8ByteCount += count;
        }
    }
}

public sealed class TsonCanonicalLimitException : InvalidOperationException
{
    internal TsonCanonicalLimitException(int maximumUtf8ByteCount)
        : base($"Canonical TSON exceeds {maximumUtf8ByteCount} UTF-8 bytes.")
    {
    }
}
