using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>Fixed value storage, shape and layout authority shared by both GPU binders.</summary>
internal sealed class GpuValues
{
    private readonly GpuModuleGraph modules;
    private readonly Action<string, string, VdMirSourceSpan> error;
    private readonly Func<string, string> physicalType;
    private readonly Dictionary<string, (string Path, RecordDeclarationSyntax Syntax)> records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VdMirValueType> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Path, TypeAliasDeclarationSyntax Syntax)> aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> aliasTargets = new(StringComparer.Ordinal);
    private readonly HashSet<string> active = new(StringComparer.Ordinal);
    private int helperSequence;

    public GpuValues(GpuModuleGraph modules, Action<string, string, VdMirSourceSpan> error, Func<string, string>? physicalType = null)
    {
        this.modules = modules;
        this.error = error;
        this.physicalType = physicalType ?? (type => type);
        foreach ((GpuSourceFile source, SyntaxTree tree) in modules.Sources)
        {
            foreach (RecordDeclarationSyntax record in tree.Root.Members.OfType<RecordDeclarationSyntax>())
            {
                if (!(record.Annotations ?? []).Any(annotation => annotation.NameToken.Text == "material"))
                {
                    records.TryAdd(modules.Declare(source.Path, record.Identifier.Text), (source.Path, record));
                }
            }
            foreach (TypeAliasDeclarationSyntax alias in tree.Root.Members.OfType<TypeAliasDeclarationSyntax>())
            {
                if (!(alias.Annotations ?? []).Any(annotation => annotation.NameToken.Text == "space"))
                {
                    aliases.TryAdd(modules.Declare(source.Path, alias.Identifier.Text), (source.Path, alias));
                }
            }
        }
    }

    // Dependency order is preserved: a record always follows its field types.
    public IReadOnlyList<VdMirValueType> Definitions => types.Values.ToArray();
    public VdMirValueType? Find(string type) => types.GetValueOrDefault(type);

    public string? BindType(string path, TypeSyntax? syntax, Func<string, TypeSyntax, string> bindOther)
    {
        if (active.Count >= 64 && syntax is not null)
        {
            return Fail("COPE-GPU-VALUE-0002", "Inline storage type dependencies exceed the 64-level budget.", Span(path, syntax)).Type;
        }
        if (syntax is IdentifierTypeSyntax identifier)
        {
            string identity = modules.Resolve(path, identifier.Identifier.Text);
            if (aliases.TryGetValue(identity, out var alias))
            {
                if (aliasTargets.TryGetValue(identity, out string? target))
                {
                    return target;
                }
                if (!active.Add(identity))
                {
                    return Fail("COPE-GPU-VALUE-0001", "Recursive type aliases cannot describe finite storage.", Span(path, syntax)).Type;
                }
                target = BindType(alias.Path, alias.Syntax.TargetType, bindOther) ?? bindOther(alias.Path, alias.Syntax.TargetType);
                active.Remove(identity);
                aliasTargets[identity] = target;
                return target;
            }
            if (!records.TryGetValue(identity, out var source))
            {
                return null;
            }
            if (types.ContainsKey(identity))
            {
                return identity;
            }
            if (!active.Add(identity))
            {
                return Fail("COPE-GPU-VALUE-0001", "Recursive value records cannot have finite inline storage.", Span(path, syntax)).Type;
            }
            var fields = new List<(string Name, string Type, VdMirSourceSpan Source)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecordFieldSyntax field in source.Syntax.Fields)
            {
                if (!seen.Add(field.Identifier.Text) || field.Identifier.Text.StartsWith("Vts", StringComparison.Ordinal))
                {
                    error("COPE-GPU-VALUE-0001", "Record fields must be unique and cannot use the Vts compiler prefix.", Span(source.Path, field));
                }
                string fieldType = BindType(source.Path, field.Type, bindOther) ?? bindOther(source.Path, field.Type);
                fields.Add((field.Identifier.Text, fieldType, Span(source.Path, field)));
            }
            active.Remove(identity);
            AddType(identity, "record", null, [], fields, Span(source.Path, source.Syntax));
            return identity;
        }
        if (syntax is not GenericTypeSyntax generic
            || generic.Identifier.Text is not ("Array" or "NDArray" or "Tensor" or "Vector" or "Matrix"))
        {
            return null;
        }
        VdMirSourceSpan span = Span(path, generic);
        int rank = generic.TypeArguments.Count - 1;
        if (rank is < 1 or > 4
            || generic.Identifier.Text is "Array" or "Vector" && rank != 1
            || generic.Identifier.Text == "Matrix" && rank != 2)
        {
            return Fail("COPE-GPU-SHAPE-0001", "Array/Vector require one extent, Matrix two, and NDArray/Tensor one to four fixed extents.", span).Type;
        }
        string element = BindType(path, generic.TypeArguments[0], bindOther) ?? bindOther(path, generic.TypeArguments[0]);
        var shape = new List<int>();
        long count = 1;
        foreach (TypeSyntax dimension in generic.TypeArguments.Skip(1))
        {
            if (dimension is not LiteralTypeSyntax { LiteralToken.Value: int extent } || extent <= 0 || extent > 256)
            {
                return Fail("COPE-GPU-SHAPE-0001", "Fixed extents must be positive integer literals, at most 256.", Span(path, dimension)).Type;
            }
            count *= extent;
            shape.Add(extent);
        }
        if (count > 256)
        {
            return Fail("COPE-GPU-SHAPE-0002", "This inline register-value profile admits at most 256 elements; use resource buffers for larger storage.", span).Type;
        }
        string kind = generic.Identifier.Text switch
        {
            "Array" => "array",
            "NDArray" => "ndarray",
            _ => "tensor",
        };
        if (kind == "tensor" && element != "f32")
        {
            return Fail("COPE-GPU-SHAPE-0003", "This tensor arithmetic profile requires f32 elements.", span).Type;
        }
        return Shaped(kind, element, shape, span);
    }

    private string Shaped(string kind, string element, IReadOnlyList<int> shape, VdMirSourceSpan span)
    {
        string key = kind + "<" + element + "," + string.Join(',', shape) + ">";
        string name = "VtsValue_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 8));
        if (!types.ContainsKey(name))
        {
            int count = shape.Aggregate(1, (product, extent) => product * extent);
            AddType(name, kind, element, shape,
                Enumerable.Range(0, count).Select(index => ("e" + index, element, span)).ToArray(), span);
        }
        return name;
    }

    private void AddType(string name, string kind, string? element, IReadOnlyList<int> shape,
        IEnumerable<(string Name, string Type, VdMirSourceSpan Source)> sourceFields, VdMirSourceSpan source)
    {
        var fields = new List<VdMirValueField>();
        int offset = 0;
        foreach (var field in sourceFields)
        {
            (int size, int alignment) = Layout(field.Type);
            if (size <= 0 || size > 65536)
            {
                error("COPE-GPU-VALUE-0002", "Inline fields require certified finite storage within the 64 KiB budget; resources and enums are excluded.", field.Source);
                continue;
            }
            offset = Align(offset, alignment);
            if (size <= 16 && offset / 16 != (offset + size - 1) / 16)
            {
                offset = Align(offset, 16);
            }
            if (offset > 65536 - size)
            {
                error("COPE-GPU-VALUE-0002", "Inline storage exceeds the 64 KiB layout budget.", field.Source);
                continue;
            }
            string physical = physicalType(field.Type);
            fields.Add(new(field.Name, field.Type, offset, size, alignment, field.Source, physical == field.Type ? null : physical));
            offset += size;
        }
        if (fields.Count == 0 || fields.Count > 256 || offset > 65536)
        {
            error("COPE-GPU-VALUE-0002", "Inline value records require 1–256 fields and at most 64 KiB of fixed storage.", source);
        }
        types.TryAdd(name, new(name, kind, element, shape, fields, Align(offset, 16), 16, "row-major", source));
    }

    public (int Size, int Alignment) Layout(string type)
    {
        if (types.TryGetValue(type, out VdMirValueType? value))
        {
            return (value.Size, value.Alignment);
        }
        return physicalType(type) switch
        {
            "f32" or "u32" or "bool" => (4, 4),
            "float2" => (8, 8),
            "float3" => (12, 16),
            "float4" => (16, 16),
            _ => (0, 0),
        };
    }

    public string? MemberType(string type, string member) => Find(type)?.Fields.FirstOrDefault(field => field.Name == member)?.Type;

    public bool ContainsBoolean(string type)
    {
        return type == "bool" || Find(type)?.Fields.Any(field => ContainsBoolean(field.Type)) == true;
    }

    public VdMirExpression UpdateRecord(VdMirExpression subject, WithExpressionSyntax syntax,
        Func<ExpressionSyntax, string, VdMirExpression> bind, Action<VdMirFunction> addFunction, VdMirSourceSpan span)
    {
        if (Find(subject.Type) is not { Kind: "record" } type)
        {
            return Fail("COPE-GPU-VALUE-0006", "with requires an admitted value record.", span);
        }
        var replacements = new Dictionary<string, VdMirExpression>(StringComparer.Ordinal);
        foreach (ObjectPropertySyntax property in syntax.Replacements.Properties)
        {
            VdMirValueField? field = type.Fields.FirstOrDefault(field => field.Name == property.NameToken.Text);
            if (field is null || replacements.ContainsKey(property.NameToken.Text))
            {
                return Fail("COPE-GPU-VALUE-0003", "with fields must be unique declared record fields.", span);
            }
            VdMirExpression value = bind(property.ValueExpression, field.Type);
            if (value.Type != field.Type)
            {
                return Fail("COPE-GPU-VALUE-0004", "with replacements require exact field types.", span);
            }
            replacements.Add(field.Name, value);
        }
        string helper = Fresh("With");
        var parameters = new List<VdMirParameter> { new("original", type.Name, null, span) };
        parameters.AddRange(replacements.Select((item, index) => new VdMirParameter("r" + index, item.Value.Type, null, span)));
        var original = Name("original", type.Name, span);
        var values = new List<VdMirExpression>();
        foreach (VdMirValueField field in type.Fields)
        {
            int index = replacements.Keys.ToList().IndexOf(field.Name);
            values.Add(index < 0 ? Field(original, field, span) : Name("r" + index, field.Type, span));
        }
        var result = new VdMirExpression("object", type.Name, span, type.Name, values, type.Fields.Select(field => field.Name).ToArray());
        addFunction(new(helper, parameters, type.Name, [new("return", span, Expression: result)], span));
        return new("call", type.Name, span, helper, [subject, .. replacements.Values]);
    }

    public VdMirExpression? BindLiteral(string expected, ExpressionSyntax syntax,
        Func<ExpressionSyntax, string, VdMirExpression> bind, Action<VdMirFunction> addFunction, VdMirSourceSpan span)
    {
        VdMirValueType? type = Find(expected);
        if (type is null)
        {
            return null;
        }
        var values = new List<VdMirExpression>();
        var fieldNames = new List<string>();
        if (type.Kind == "record" && syntax is ObjectLiteralExpressionSyntax record)
        {
            foreach (ObjectPropertySyntax property in record.Properties)
            {
                VdMirValueField? field = type.Fields.FirstOrDefault(field => field.Name == property.NameToken.Text);
                if (field is null || fieldNames.Contains(field.Name, StringComparer.Ordinal))
                {
                    return Fail("COPE-GPU-VALUE-0003", "Record initialization contains an unknown or duplicate field.", span);
                }
                fieldNames.Add(field.Name);
                values.Add(bind(property.ValueExpression, field.Type));
            }
            if (record.Properties.Count != type.Fields.Count)
            {
                return Fail("COPE-GPU-VALUE-0003", "Record initialization requires every declared field exactly once.", span);
            }
        }
        else if (type.Kind != "record" && syntax is ArrayLiteralExpressionSyntax array)
        {
            if (array.Elements.Count != type.Fields.Count)
            {
                return Fail("COPE-GPU-SHAPE-0004", "A shaped value requires a flat row-major initializer with exactly " + type.Fields.Count + " elements.", span);
            }
            values.AddRange(array.Elements.Select(element => bind(element, type.ElementType!)));
            fieldNames.AddRange(type.Fields.Select(field => field.Name));
        }
        else
        {
            return null;
        }
        if (values.Where((value, index) => value.Type != MemberType(type.Name, fieldNames[index])).Any())
        {
            return Fail("COPE-GPU-VALUE-0004", "Every initialized field must have its exact declared type.", span);
        }
        return Construct(type, values, addFunction, span, fieldNames);
    }

    private VdMirExpression Construct(VdMirValueType type, IReadOnlyList<VdMirExpression> values,
        Action<VdMirFunction> addFunction, VdMirSourceSpan span, IReadOnlyList<string>? fieldNames = null)
    {
        string helper = Fresh("Construct");
        VdMirParameter[] parameters = values.Select((value, index) => new VdMirParameter("v" + index, value.Type, null, span)).ToArray();
        string[] names = (fieldNames ?? type.Fields.Select(field => field.Name).ToArray()).ToArray();
        var fields = type.Fields.Select(field =>
        {
            VdMirParameter parameter = parameters[Array.IndexOf(names, field.Name)];
            return Name(parameter.Name, parameter.Type, span);
        }).ToArray();
        var result = new VdMirExpression("object", type.Name, span, type.Name,
            fields, type.Fields.Select(field => field.Name).ToArray());
        addFunction(new(helper, parameters, type.Name, [new("return", span, Expression: result)], span));
        return new("call", type.Name, span, helper, values);
    }

    public VdMirExpression? Read(VdMirExpression subject, IReadOnlyList<VdMirExpression> indices,
        Action<VdMirFunction> addFunction, VdMirSourceSpan span)
    {
        VdMirValueType? type = Find(subject.Type);
        if (type is null || type.Kind == "record")
        {
            return null;
        }
        if (!ValidIndices(type, indices, span))
        {
            return new("error", "error", span);
        }
        string helper = Fresh("Read");
        var parameters = new List<VdMirParameter> { new("value", type.Name, null, span) };
        parameters.AddRange(indices.Select((index, axis) => new VdMirParameter("i" + axis, "u32", null, span)));
        var statements = new List<VdMirStatement>();
        for (int linear = 0; linear < type.Fields.Count; linear++)
        {
            statements.Add(new("if", span, Expression: Coordinates(type, linear, span),
                Body: [new("return", span, Expression: Field(Name("value", type.Name, span), type.Fields[linear], span))]));
        }
        statements.Add(new("return", span, Expression: Zero(type.ElementType!, span, addFunction)));
        addFunction(new(helper, parameters, type.ElementType!, statements, span));
        return new("call", type.ElementType!, span, helper, [subject, .. indices]);
    }

    public VdMirExpression? Write(VdMirExpression subject, IReadOnlyList<VdMirExpression> indices, VdMirExpression value,
        Action<VdMirFunction> addFunction, VdMirSourceSpan span)
    {
        VdMirValueType? type = Find(subject.Type);
        if (type is null || type.Kind == "record")
        {
            return null;
        }
        if (!ValidIndices(type, indices, span) || value.Type != type.ElementType)
        {
            return Fail("COPE-GPU-VALUE-0004", "Shaped writes require exact element type and valid coordinates.", span);
        }
        if (type.Fields.Count > 64)
        {
            return Fail("COPE-GPU-SHAPE-0002", "Inline indexed mutation is bounded to 64 elements; use resource buffers for larger mutable storage.", span);
        }
        string helper = Fresh("Write");
        var parameters = new List<VdMirParameter> { new("value", type.Name, null, span) };
        parameters.AddRange(indices.Select((index, axis) => new VdMirParameter("i" + axis, "u32", null, span)));
        parameters.Add(new("replacement", value.Type, null, span));
        var statements = new List<VdMirStatement>();
        for (int linear = 0; linear < type.Fields.Count; linear++)
        {
            var original = Name("value", type.Name, span);
            VdMirExpression[] fields = type.Fields.Select((field, index) => index == linear
                ? Name("replacement", field.Type, span) : Field(original, field, span)).ToArray();
            statements.Add(new("if", span, Expression: Coordinates(type, linear, span),
                Body: [new("return", span, Expression: new("object", type.Name, span, type.Name, fields, type.Fields.Select(field => field.Name).ToArray()))]));
        }
        statements.Add(new("return", span, Expression: Name("value", type.Name, span)));
        addFunction(new(helper, parameters, type.Name, statements, span));
        return new("call", type.Name, span, helper, [subject, .. indices, value]);
    }

    private bool ValidIndices(VdMirValueType type, IReadOnlyList<VdMirExpression> indices, VdMirSourceSpan span)
    {
        if (indices.Count != type.Shape.Count || indices.Any(index => index.Type != "u32"))
        {
            error("COPE-GPU-INDEX-0002", "Shaped access requires one u32 coordinate per axis.", span);
            return false;
        }
        for (int axis = 0; axis < indices.Count; axis++)
        {
            if (indices[axis] is { Kind: "literal" } literal && uint.TryParse(literal.Value, out uint value) && value >= type.Shape[axis])
            {
                error("COPE-GPU-INDEX-0003", "Constant coordinate is outside axis " + axis + " extent " + type.Shape[axis] + ".", indices[axis].Source);
                return false;
            }
        }
        return true;
    }

    private static VdMirExpression Coordinates(VdMirValueType type, int linear, VdMirSourceSpan span)
    {
        VdMirExpression? condition = null;
        for (int axis = type.Shape.Count - 1; axis >= 0; axis--)
        {
            int coordinate = linear % type.Shape[axis];
            linear /= type.Shape[axis];
            var test = Binary("==", Name("i" + axis, "u32", span), UInt(coordinate, span), "bool", span);
            condition = condition is null ? test : Binary("&&", test, condition, "bool", span);
        }
        return condition!;
    }

    public VdMirExpression? Math(string operation, IReadOnlyList<VdMirExpression> arguments,
        Action<VdMirFunction> addFunction, VdMirSourceSpan span)
    {
        if (arguments.Count != 2 || Find(arguments[0].Type) is not { Kind: "tensor" } left
            || Find(arguments[1].Type) is not { Kind: "tensor" } right)
        {
            return null;
        }
        string helper = Fresh("Math");
        var a = Name("a", left.Name, span);
        var b = Name("b", right.Name, span);
        var values = new List<VdMirExpression>();
        VdMirExpression result;
        if (operation == "Dot" && left.Shape.Count == 1 && left.Name == right.Name)
        {
            result = Sum(Enumerable.Range(0, left.Fields.Count).Select(index => Binary("*", Field(a, left.Fields[index], span), Field(b, right.Fields[index], span), "f32", span)), span);
        }
        else if (operation == "MatMul" && left.Shape.Count == 2 && right.Shape.Count == 2 && left.Shape[1] == right.Shape[0])
        {
            if (left.Shape[0] * right.Shape[1] > 256 || left.Shape[0] * right.Shape[1] * left.Shape[1] > 4096)
            {
                return Fail("COPE-GPU-SHAPE-0002", "Fixed matrix multiplication exceeds the 4096 scalar-product budget.", span);
            }
            string resultType = Shaped("tensor", "f32", [left.Shape[0], right.Shape[1]], span);
            for (int row = 0; row < left.Shape[0]; row++)
            {
                for (int column = 0; column < right.Shape[1]; column++)
                {
                    values.Add(Sum(Enumerable.Range(0, left.Shape[1]).Select(inner => Binary("*",
                        Field(a, left.Fields[row * left.Shape[1] + inner], span),
                        Field(b, right.Fields[inner * right.Shape[1] + column], span), "f32", span)), span));
                }
            }
            result = new("object", resultType, span, resultType, values, Find(resultType)!.Fields.Select(field => field.Name).ToArray());
        }
        else if (operation is "+" or "-" or "*" && left.Name == right.Name)
        {
            values.AddRange(left.Fields.Select((field, index) => Binary(operation, Field(a, field, span), Field(b, right.Fields[index], span), "f32", span)));
            result = new("object", left.Name, span, left.Name, values, left.Fields.Select(field => field.Name).ToArray());
        }
        else
        {
            return Fail("COPE-GPU-TENSOR-0001", "Tensor shapes must agree. Dot takes equal vectors; MatMul takes compatible rank-two values. Broadcasting is unsupported.", span);
        }
        addFunction(new(helper, [new("a", left.Name, null, span), new("b", right.Name, null, span)], result.Type,
            [new("return", span, Expression: result)], span));
        return new("call", result.Type, span, helper, arguments);
    }

    public VdMirExpression? Query(VdMirExpression subject, string name, Action<VdMirFunction> addFunction, VdMirSourceSpan span)
    {
        VdMirValueType? type = Find(subject.Type);
        if (type is null || type.Kind == "record")
        {
            return null;
        }
        VdMirExpression result;
        if (name == "rank")
        {
            result = UInt(type.Shape.Count, span);
        }
        else if (name == "length")
        {
            result = UInt(type.Fields.Count, span);
        }
        else if (name == "shape")
        {
            string shapeType = Shaped("array", "u32", [type.Shape.Count], span);
            result = Construct(Find(shapeType)!, type.Shape.Select(extent => UInt(extent, span)).ToArray(), addFunction, span);
        }
        else
        {
            return null;
        }
        // A type-known result must not erase evaluation of an effectful subject.
        string helper = Fresh("Query");
        addFunction(new(helper, [new("subject", type.Name, null, span)], result.Type,
            [new("return", span, Expression: result)], span));
        return new("call", result.Type, span, helper, [subject]);
    }

    private VdMirExpression Zero(string type, VdMirSourceSpan span, Action<VdMirFunction> addFunction)
    {
        if (Find(type) is { } aggregate)
        {
            return Construct(aggregate, aggregate.Fields.Select(field => Zero(field.Type, span, addFunction)).ToArray(), addFunction, span);
        }
        string physical = physicalType(type);
        if (physical is "float2" or "float3" or "float4")
        {
            int count = physical[^1] - '0';
            return new("call", type, span, physical, Enumerable.Repeat(new VdMirExpression("literal", "f32", span, "0.0"), count).ToArray());
        }
        string value = type switch
        {
            "bool" => "false",
            "f32" => "0.0",
            _ => "0",
        };
        return new("literal", type, span, value);
    }

    private static VdMirExpression Sum(IEnumerable<VdMirExpression> terms, VdMirSourceSpan span)
        => terms.Aggregate((left, right) => Binary("+", left, right, "f32", span));
    private string Fresh(string kind) => "Vts" + kind + helperSequence++;
    private VdMirExpression Fail(string code, string message, VdMirSourceSpan span)
    {
        error(code, message, span);
        return new("error", "error", span);
    }
    private static VdMirExpression Name(string name, string type, VdMirSourceSpan span) => new("name", type, span, name);
    private static VdMirExpression Field(VdMirExpression owner, VdMirValueField field, VdMirSourceSpan span) => new("field", field.Type, span, field.Name, [owner]);
    private static VdMirExpression UInt(int value, VdMirSourceSpan span) => new("literal", "u32", span, value.ToString(CultureInfo.InvariantCulture));
    private static VdMirExpression Binary(string operation, VdMirExpression left, VdMirExpression right, string type, VdMirSourceSpan span)
        => new("binary", type, span, operation, [left, right]);
    internal static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
    private static VdMirSourceSpan Span(string path, SyntaxNode syntax)
    {
        SyntaxToken[] tokens = Tokens(syntax).ToArray();
        int start = tokens.Length == 0 ? 0 : tokens.Min(token => token.Position);
        int end = tokens.Length == 0 ? 1 : tokens.Max(token => token.Position + token.Text.Length);
        return new(path, start, System.Math.Max(1, end - start));
    }

    private static IEnumerable<SyntaxToken> Tokens(SyntaxNode syntax)
    {
        foreach (object child in syntax.GetChildren())
        {
            if (child is SyntaxToken token)
            {
                yield return token;
            }
            else if (child is SyntaxNode node)
            {
                foreach (SyntaxToken nested in Tokens(node))
                {
                    yield return nested;
                }
            }
        }
    }
}
