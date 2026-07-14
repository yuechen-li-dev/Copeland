using Copeland.TS.Mir;

namespace Copeland.TS.TestSupport;

public static class TableMirValidationCases
{
    private static readonly MirNamedType Number = new("number");
    private static readonly MirNamedType String = new("string");
    private static readonly MirNamedType Void = new("void");
    private static readonly MirRecordTypeId RecordId = new("r1");
    private static readonly MirRecordFieldId NumberFieldId = new("r1.f0");
    private static readonly MirRecordFieldId StringFieldId = new("r1.f1");
    private static readonly MirRecordType RecordType = new(RecordId, "Pair");
    private static readonly MirNamedType Choice = new("Choice");

    public static IEnumerable<object[]> Cases()
    {
        yield return Case("primitive kind/type mismatch", Table(new MirTableLiteralConstant("wrong", Number), Number), "not a supported closed constant");
        yield return Case("unknown record identity", Table(new MirTableRecordConstant(new MirRecordTypeId("missing"), [], new MirRecordType(new MirRecordTypeId("missing"), "Missing")), new MirRecordType(new MirRecordTypeId("missing"), "Missing")), "unknown record identity");
        yield return Case("incomplete record field set", Table(Record([]), RecordType), "does not provide every record field");
        yield return Case("duplicate record field", Table(Record([Field(NumberFieldId, 1), Field(NumberFieldId, 2)]), RecordType), "duplicate field identity");
        yield return Case("wrong record field type", Table(Record([Field(NumberFieldId, "wrong"), Field(StringFieldId, "ok")]), RecordType), "does not match the column element type");
        yield return Case("unknown enum", Table(new MirTableEnumConstant("Missing", "None", [], new MirNamedType("Missing")), new MirNamedType("Missing")), "unknown enum or case");
        yield return Case("unknown enum case", Table(new MirTableEnumConstant("Choice", "Missing", [], Choice), Choice), "unknown enum or case");
        yield return Case("incorrect enum payload arity", Table(new MirTableEnumConstant("Choice", "Value", [], Choice), Choice), "invalid payload count");
        yield return Case("incorrect enum payload type", Table(new MirTableEnumConstant("Choice", "Value", [Literal("wrong")], Choice), Choice), "does not match the column element type");
        yield return Case("wrong Result success payload", Table(Result(true, Literal("wrong"), Number, String), new MirResultType(Number, String)), "does not match the column element type");
        yield return Case("wrong Result error payload", Table(Result(false, Literal(1), Number, String), new MirResultType(Number, String)), "does not match the column element type");
        yield return Case("invalid zero-payload Result", Table(Result(true, Literal(1), Void, String), new MirResultType(Void, String)), "cannot use a void success payload");
        yield return Case("unsupported mutable constant type", Table(new MirTableLiteralConstant(new object(), new MirArrayType(Number)), new MirArrayType(Number)), "not a supported closed constant");
        yield return Case("missing array element type", MissingArrayElementType(), "has no element type");
        yield return Case("heterogeneous closed array", Table(new MirTableArrayConstant(new MirArrayType(Number), [Literal(1), Literal("wrong")]), new MirArrayType(Number)), "heterogeneous element");
        var aliasedElement = Literal(1);
        yield return Case("aliased closed array element", Table(new MirTableArrayConstant(new MirArrayType(Number), [aliasedElement, aliasedElement]), new MirArrayType(Number)), "alias or cycle");
        yield return Case("column element type mismatch", Table(Literal("wrong"), Number), "does not match the column element type");
        yield return Case("row count mismatch", Table(Literal(1), Number, rowCount: 2), "has 1 constants but row count is 2");
    }

    private static object[] Case(string name, MirProgram program, string expectedMessage) => [name, program, expectedMessage];

    private static MirProgram Table(MirTableConstant constant, MirType elementType, int rowCount = 1)
    {
        var table = new MirTableDefinition(
            new MirTableId("t1"),
            "Values",
            "t1.row",
            [new MirTableColumnDefinition(new MirTableColumnId("t1.c0"), "value", elementType, [constant])],
            rowCount);
        return new MirProgram([BoundsError(), ChoiceDefinition()], [RecordDefinition()], [table], []);
    }

    private static MirTableRecordConstant Record(IReadOnlyList<MirTableRecordFieldConstant> fields) => new(RecordId, fields, RecordType);

    private static MirTableRecordFieldConstant Field(MirRecordFieldId id, object value) => new(id, Literal(value));

    private static MirTableLiteralConstant Literal(object value) => new(value, value is string ? String : Number);

    private static MirTableResultConstant Result(bool isOk, MirTableConstant payload, MirType success, MirType error) => new(isOk, payload, new MirResultType(success, error));

    private static MirProgram MissingArrayElementType()
    {
        var type = new MirArrayType(null!);
        return Table(new MirTableArrayConstant(type, []), type);
    }

    private static MirRecordDefinition RecordDefinition() => new(RecordId, "Pair", [
        new MirRecordFieldDefinition(NumberFieldId, "numberValue", Number),
        new MirRecordFieldDefinition(StringFieldId, "stringValue", String),
    ]);

    private static MirEnum ChoiceDefinition() => new("Choice", [new MirEnumCase("Value", [new MirEnumPayloadField("value", Number)])]);

    private static MirEnum BoundsError() => new("TableBoundsError", [
        new MirEnumCase("InvalidIndex", [new MirEnumPayloadField("index", Number)]),
        new MirEnumCase("OutOfBounds", [new MirEnumPayloadField("index", Number), new MirEnumPayloadField("rowCount", Number)]),
    ]);
}
