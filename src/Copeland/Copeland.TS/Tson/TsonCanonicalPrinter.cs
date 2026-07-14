using System.Globalization;
using System.Text;

namespace Copeland.TS.Tson;

public static class TsonCanonicalPrinter
{
    public static string Print(TsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var writer = new Writer(document.Catalog);
        return writer.Print(document);
    }

    public static byte[] PrintUtf8(TsonDocument document)
    {
        return Encoding.UTF8.GetBytes(Print(document));
    }

    private sealed class Writer
    {
        private readonly TsonCatalog _catalog;
        private readonly Dictionary<string, TsonNominalDefinition> _definitionsByIdentity;
        private readonly StringBuilder _builder = new();

        public Writer(TsonCatalog catalog)
        {
            _catalog = catalog;
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
            }

            AppendLine();
            _builder.Append("const $value = ");
            AppendValue(document.Root, 0);
            AppendLine(";");
            return _builder.ToString();
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
                default:
                    throw new InvalidOperationException("Unknown TSON value variant.");
            }
        }

        private void AppendArray(TsonArray array, int indentation)
        {
            if (array.Elements.Count == 0)
            {
                _builder.Append("[]");
                return;
            }

            AppendLine("[");
            for (var index = 0; index < array.Elements.Count; index++)
            {
                AppendIndent(indentation + 1);
                AppendValue(array.Elements[index], indentation + 1);
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
}
