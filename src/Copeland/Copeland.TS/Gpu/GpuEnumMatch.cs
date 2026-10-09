using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

internal static class GpuEnumMatch
{
    // Subject evaluation belongs to the call site and occurs once. Each branch
    // contains its own payload binding and value, preserving lazy arm execution.
    public static VdMirExpression Bind(string path, MatchExpressionSyntax syntax,
        VdMirExpression subject, GpuEnums enums, string helperName,
        IReadOnlyDictionary<string, string> captures,
        Func<ExpressionSyntax, Dictionary<string, string>, VdMirExpression> bindExpression,
        Action<VdMirFunction> addFunction, Action<string, string, VdMirSourceSpan> error)
    {
        var source = new VdMirSourceSpan(path, syntax.MatchKeyword.Position, syntax.MatchKeyword.Text.Length);
        VdMirEnum? enumeration = enums.Find(subject.Type);
        if (enumeration is null)
        {
            error("COPE-GPU-MATCH-0001", "GPU match requires an enum subject.", source);
            return new("error", "error", source);
        }
        string subjectName = "vtsSubject";
        while (captures.ContainsKey(subjectName))
        {
            subjectName += "_";
        }
        var subjectValue = new VdMirExpression("name", enumeration.Name, source, subjectName);
        var tag = new VdMirExpression("field", "u32", source, "tag", [subjectValue]);
        var parameters = new List<VdMirParameter> { new(subjectName, enumeration.Name, null, source) };
        parameters.AddRange(captures.Select(item => new VdMirParameter(item.Key, item.Value, null, source)));
        var arguments = new List<VdMirExpression> { subject };
        arguments.AddRange(captures.Select(item => new VdMirExpression("name", item.Value, source, item.Key)));
        var statements = new List<VdMirStatement>();
        var seen = new HashSet<uint>();
        string? resultType = null;
        VdMirExpression? lastValue = null;
        foreach (MatchArmSyntax arm in syntax.Arms)
        {
            VdMirEnumCase? variant = enums.MatchCase(path, arm.Pattern, enumeration);
            if (variant is null)
            {
                continue;
            }
            if (!seen.Add(variant.Tag))
            {
                error("COPE-GPU-MATCH-0002", "Duplicate match arm: " + variant.Name, source);
            }
            var scope = new Dictionary<string, string>(captures, StringComparer.Ordinal);
            var body = new List<VdMirStatement>();
            int bindingCount = variant.Payload.Count == 0 ? 0 : 1;
            if (arm.Pattern.PayloadIdentifiers.Count != bindingCount)
            {
                error("COPE-GPU-MATCH-0003", "A payload arm requires one record binding; a payload-free arm has none.", source);
                continue;
            }
            if (bindingCount == 1)
            {
                string binding = arm.Pattern.PayloadIdentifiers[0].Text;
                if (scope.ContainsKey(binding) || binding == subjectName)
                {
                    error("COPE-GPU-MATCH-0003", "Payload bindings cannot shadow an existing shader value.", source);
                }
                scope[binding] = variant.PayloadType;
                body.Add(new("local", source, binding, variant.PayloadType, Expression: enums.Extract(enumeration, variant, subjectValue, addFunction)));
            }
            VdMirExpression value = bindExpression(arm.Expression, scope);
            resultType ??= value.Type;
            if (value.Type != resultType)
            {
                error("COPE-GPU-MATCH-0004", "Match arm result types must agree exactly.", value.Source);
            }
            body.Add(new("return", value.Source, Expression: value));
            var condition = new VdMirExpression("binary", "bool", source, "==", [tag,
                new("literal", "u32", source, variant.Tag.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
            statements.Add(new("if", source, Expression: condition, Body: body));
            lastValue = value;
        }
        if (seen.Count != enumeration.Cases.Count)
        {
            string missing = string.Join(", ", enumeration.Cases.Where(item => !seen.Contains(item.Tag)).Select(item => item.Name));
            error("COPE-GPU-MATCH-0005", "Non-exhaustive match; missing: " + missing, source);
        }
        if (resultType is null || lastValue is null)
        {
            return new("error", "error", source);
        }
        // The final certified case is unconditional. Only typed constructors
        // create tags, and values cannot enter through external buffers in this slice.
        if (statements.Count > 0)
        {
            VdMirStatement last = statements[^1];
            statements[^1] = new("block", last.Source, Body: last.Body);
        }
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        FindNames(statements, usedNames);
        var needed = parameters.Select((parameter, index) => (parameter, index))
            .Where(item => item.index == 0 || usedNames.Contains(item.parameter.Name)).ToArray();
        foreach (var item in needed.Where(item => item.parameter.Type is "storage-buffer<f32>" or "acceleration_structure"))
        {
            error("COPE-GPU-MATCH-0006", "Read storage values before matching; opaque resources cannot be captured into the local match helper ABI.", source);
        }
        addFunction(new(helperName, needed.Select(item => item.parameter).ToArray(), resultType, statements, source));
        return new("call", resultType, source, helperName, needed.Select(item => arguments[item.index]).ToArray());
    }

    private static void FindNames(IEnumerable<VdMirStatement> statements, HashSet<string> names)
    {
        foreach (VdMirStatement statement in statements)
        {
            if (statement.Expression is not null)
            {
                FindExpressionNames(statement.Expression, names);
            }
            FindNames(statement.Body ?? [], names);
            FindNames(statement.ElseBody ?? [], names);
        }
    }

    private static void FindExpressionNames(VdMirExpression expression, HashSet<string> names)
    {
        if (expression.Kind == "name")
        {
            names.Add(expression.Value!);
        }
        foreach (VdMirExpression operand in expression.Operands ?? [])
        {
            FindExpressionNames(operand, names);
        }
    }
}
