using System.Text;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// Closed CTS-JS-EMIT-M1 vocabulary. It is deliberately internal: generated
/// names are a backend representation, not a mutable public dictionary.
/// </summary>
internal static class SymbolicJavaScriptVocabulary
{
    public const string Version = "CTS-JS-EMIT-M1";
    private const string Stems = "甲乙丙丁戊己庚辛壬癸";
    private static readonly HashSet<char> Allowed = new(("表行列录枚项载组果成错流函接型值存符印造验取更编源纲识界律终助运串数布域序配传解槽收支临计写返附").Concat(Stems));

    public static string Name(JavaScriptSymbolicBindingRole role, int ordinal)
    {
        string compound = role switch
        {
            JavaScriptSymbolicBindingRole.Panic => "终",
            JavaScriptSymbolicBindingRole.UnwrapPanic => "终解",
            JavaScriptSymbolicBindingRole.ValueConstructor => "值造",
            JavaScriptSymbolicBindingRole.EnumType => "枚型",
            JavaScriptSymbolicBindingRole.EnumInstances => "枚印",
            JavaScriptSymbolicBindingRole.EnumValidator => "枚验",
            JavaScriptSymbolicBindingRole.ResultType => "果型",
            JavaScriptSymbolicBindingRole.ResultValidator => "果验",
            JavaScriptSymbolicBindingRole.FlowToken => "流符",
            JavaScriptSymbolicBindingRole.FlowValue => "流值",
            JavaScriptSymbolicBindingRole.FlowHandler => "流接",
            JavaScriptSymbolicBindingRole.FlowFunction => "流函",
            JavaScriptSymbolicBindingRole.FlowValidator => "流验",
            JavaScriptSymbolicBindingRole.RecordType => "录型",
            JavaScriptSymbolicBindingRole.RecordInstances => "录印",
            JavaScriptSymbolicBindingRole.RecordConstructor => "录造",
            JavaScriptSymbolicBindingRole.RecordValidator => "录验",
            JavaScriptSymbolicBindingRole.RecordField => "录域",
            JavaScriptSymbolicBindingRole.TableType => "表型",
            JavaScriptSymbolicBindingRole.TableInstances => "表印",
            JavaScriptSymbolicBindingRole.TableRowType => "表行型",
            JavaScriptSymbolicBindingRole.TableValidator => "表验",
            JavaScriptSymbolicBindingRole.TableRowValidator => "表行验",
            JavaScriptSymbolicBindingRole.TableConstructor => "表造",
            JavaScriptSymbolicBindingRole.TableRowConstructor => "表行造",
            JavaScriptSymbolicBindingRole.TableValue => "表值",
            JavaScriptSymbolicBindingRole.TableRowSlot => "表行槽",
            JavaScriptSymbolicBindingRole.TableColumnSlot => "表列槽",
            JavaScriptSymbolicBindingRole.TableColumnToken => "表列符",
            JavaScriptSymbolicBindingRole.TableStorage => "表列存",
            JavaScriptSymbolicBindingRole.TableColumnValue => "表列值",
            JavaScriptSymbolicBindingRole.ColumnType => "列型",
            JavaScriptSymbolicBindingRole.ColumnInstances => "列印",
            JavaScriptSymbolicBindingRole.ColumnRead => "列取",
            JavaScriptSymbolicBindingRole.ColumnValues => "列值",
            JavaScriptSymbolicBindingRole.ColumnValidator => "列验",
            JavaScriptSymbolicBindingRole.TableRowTable => "表行表",
            JavaScriptSymbolicBindingRole.TableRowIndex => "表行序",
            JavaScriptSymbolicBindingRole.MatchTemporary => "配临",
            JavaScriptSymbolicBindingRole.Temporary => "临",
            JavaScriptSymbolicBindingRole.TsonRuntime => "运编",
            JavaScriptSymbolicBindingRole.TsonBooleanWriter => "布写",
            JavaScriptSymbolicBindingRole.TsonNumberWriter => "数写",
            JavaScriptSymbolicBindingRole.TsonStringWriter => "串写",
            JavaScriptSymbolicBindingRole.TsonWriterFactory => "写造",
            JavaScriptSymbolicBindingRole.TsonWriterFail => "写错",
            JavaScriptSymbolicBindingRole.TsonWriterAppend => "写附",
            JavaScriptSymbolicBindingRole.TsonUnicodeEscape => "串编",
            JavaScriptSymbolicBindingRole.TsonRecordWriter => "录写",
            JavaScriptSymbolicBindingRole.TsonEnumWriter => "枚写",
            JavaScriptSymbolicBindingRole.TsonArrayWriter => "组写",
            JavaScriptSymbolicBindingRole.TsonEncoder => "编",
            JavaScriptSymbolicBindingRole.GenericHelper => "助",
            _ => throw new InvalidOperationException($"No Symbolic vocabulary mapping exists for role '{role}'."),
        };

        string name = "$" + compound + HeavenlyStemOrdinal(ordinal);
        ValidateIdentifier(name);
        return name;
    }

    public static string HeavenlyStemOrdinal(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value <= Stems.Length)
        {
            return Stems[value - 1].ToString();
        }

        // CTS-JS-EMIT-M1 defines the continuation as the visible decimal
        // counter written in Heavenly Stems: 11 is 甲甲, 20 is 乙癸, and
        // 101 is 甲癸甲. The single-stem range remains 1 through 10.
        var builder = new StringBuilder();
        foreach (char digit in value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            int stemIndex = digit == '0' ? 9 : digit - '1';
            builder.Append(Stems[stemIndex]);
        }

        return builder.ToString();
    }

    public static void ValidateIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] != '$' || !name.IsNormalized(NormalizationForm.FormC))
        {
            throw new InvalidOperationException("Symbolic identifier must be NFC and begin with '$'.");
        }

        foreach (char character in name.AsSpan(1))
        {
            if (!Allowed.Contains(character))
            {
                throw new InvalidOperationException($"Symbolic identifier contains uncurated character U+{(int)character:X4}.");
            }
        }
    }

    public static void ValidateIdentifierFile(string sourceText)
    {
        if (!sourceText.IsNormalized(NormalizationForm.FormC) || !sourceText.EndsWith('\n'))
        {
            throw new InvalidOperationException("Symbolic JavaScript output must be NFC and LF terminated.");
        }

        foreach (char character in sourceText)
        {
            if (char.IsSurrogate(character) || character is '\u200B' or '\u200C' or '\u200D' or '\uFE0E' or '\uFE0F'
                || (character >= '\uE000' && character <= '\uF8FF')
                || (character >= '\u202A' && character <= '\u202E'))
            {
                throw new InvalidOperationException($"Symbolic JavaScript output contains forbidden character U+{(int)character:X4}.");
            }
        }
    }
}

internal enum JavaScriptSymbolicBindingRole
{
    Panic, UnwrapPanic, ValueConstructor, EnumType, EnumInstances, EnumValidator,
    ResultType, ResultValidator, FlowToken, FlowValue, FlowHandler, FlowFunction,
    FlowValidator, RecordType, RecordInstances, RecordConstructor, RecordValidator,
    RecordField, TableType, TableInstances, TableRowType, TableValidator,
    TableRowValidator, TableConstructor, TableRowConstructor, TableValue, TableRowSlot,
    TableColumnSlot, TableColumnToken, TableStorage, TableColumnValue, ColumnType,
    ColumnInstances, ColumnRead, ColumnValues, ColumnValidator, TableRowTable,
    TableRowIndex, MatchTemporary, Temporary, TsonRuntime, TsonBooleanWriter,
    TsonNumberWriter, TsonStringWriter, TsonWriterFactory, TsonWriterFail,
    TsonWriterAppend, TsonUnicodeEscape, TsonRecordWriter, TsonEnumWriter,
    TsonArrayWriter, TsonEncoder, GenericHelper,
}
