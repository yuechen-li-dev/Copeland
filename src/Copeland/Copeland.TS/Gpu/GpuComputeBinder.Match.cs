using System.Globalization;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private VdMirStatement BindStatementMatch(string path, RuntimeMatchStatementSyntax match,
            Dictionary<string, ValueBinding> scope, string returnType)
        {
            VdMirExpression subject = BindExpression(path, match.Expression, scope);
            var source = Span(path, match);
            var enumeration = _enums.Find(subject.Type);
            if (enumeration is null)
            {
                AddDiagnostic("COPE-GPU-MATCH-0001", "SDSL-V4200", "match",
                    "Statement match requires a typed enum subject.", source);
                return new("block", source, Body: []);
            }
            string subjectName;
            do
            {
                subjectName = "__vts_match_subject_" + _matchSequence++;
            }
            while (scope.ContainsKey(subjectName));
            var captured = new VdMirExpression("name", subject.Type, source, subjectName);
            var tag = new VdMirExpression("field", "u32", source, "tag", [captured]);
            var statements = new List<VdMirStatement>
            {
                new("local", source, subjectName, subject.Type, Expression: subject),
            };
            var seen = new HashSet<uint>();
            var cases = new List<VdMirStatement>();
            foreach (var arm in match.Arms)
            {
                var variant = _enums.MatchCase(path, arm.Pattern, enumeration);
                if (variant is null)
                {
                    continue;
                }
                if (!seen.Add(variant.Tag))
                {
                    AddDiagnostic("COPE-GPU-MATCH-0002", "SDSL-V4200", "match", "Duplicate match case.", Span(path, arm));
                }
                var armScope = CloneScope(scope);
                var body = new List<VdMirStatement>();
                int expected = variant.Payload.Count == 0 ? 0 : 1;
                if (arm.Pattern.PayloadIdentifiers.Count != expected)
                {
                    AddDiagnostic("COPE-GPU-MATCH-0003", "SDSL-V4200", "match",
                        "A payload case binds one record; a payload-free case binds none.", Span(path, arm));
                    continue;
                }
                if (expected == 1)
                {
                    string binding = arm.Pattern.PayloadIdentifiers[0].Text;
                    if (armScope.ContainsKey(binding))
                    {
                        AddDiagnostic("COPE-GPU-MATCH-0003", "SDSL-V4200", "match", "Payload bindings cannot shadow values.", Span(path, arm));
                    }
                    armScope[binding] = new(variant.PayloadType, false, null, false);
                    body.Add(new("local", source, binding, variant.PayloadType,
                        Expression: _enums.Extract(enumeration, variant, captured, AddEnumFunction)));
                }
                body.AddRange(BindNestedStatement(path, arm.Statement, armScope, returnType));
                cases.Add(new("case", source, Name: variant.Tag.ToString(CultureInfo.InvariantCulture), Body: body));
            }
            if (seen.Count != enumeration.Cases.Count)
            {
                AddDiagnostic("COPE-GPU-MATCH-0005", "SDSL-V4200", "match",
                    "Non-exhaustive statement match; cover every case.", source);
            }
            if (seen.Count == enumeration.Cases.Count && cases.Count == enumeration.Cases.Count)
            {
                // Enum tags originate only from typed constructors. The last
                // certified case can be the default, proving total return to DXC
                // without a synthetic fallback or a nested conditional chain.
                cases[^1] = cases[^1] with { Kind = "default" };
            }
            statements.Add(new("switch", source, Expression: tag, Body: cases));
            return new("block", source, Body: statements);
        }
    }
}
