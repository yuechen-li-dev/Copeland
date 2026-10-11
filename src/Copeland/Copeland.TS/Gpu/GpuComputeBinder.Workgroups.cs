using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private static bool IsWorkgroupArray(string type) => type.StartsWith("workgroup:", StringComparison.Ordinal);

        private string BindWorkgroupType(string path, GenericTypeSyntax type)
        {
            if (type.TypeArguments.Count == 2
                && type.TypeArguments[0] is IdentifierTypeSyntax { Identifier.Text: "f32" or "u32" } element
                && type.TypeArguments[1] is LiteralTypeSyntax { LiteralToken.Value: int count }
                && count is > 0 and <= 4096)
            {
                return $"workgroup:{element.Identifier.Text}:{count}";
            }
            AddWorkgroupDiagnostic(path, type, "WorkgroupArray requires f32/u32 and a fixed extent of 1–4096 words.");
            return "error";
        }

        private void BindWorkgroupParameter(string path, ParameterSyntax parameter, string type,
            AnnotationSyntax annotation, Dictionary<string, ValueBinding> scope)
        {
            if (!IsWorkgroupArray(type) || parameter.AccessToken?.Text != "readwrite"
                || annotation.Arguments.Count != 0 || parameter.Annotations?.Count != 1
                || scope.ContainsKey(parameter.Identifier.Text))
            {
                AddWorkgroupDiagnostic(path, parameter,
                    "Shared storage requires a unique @workgroup readwrite name: WorkgroupArray<f32|u32, extent>, without a descriptor binding.");
                return;
            }
            string[] parts = type.Split(':');
            int count = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            _workgroupArrays.Add(new(parameter.Identifier.Text, parts[1], count, Span(path, parameter)));
            if (_workgroupArrays.Sum(array => array.Count) > 4096)
            {
                AddWorkgroupDiagnostic(path, parameter, "This portable workgroup profile admits at most 16 KiB of shared storage per entry.");
            }
            scope.Add(parameter.Identifier.Text, new(type, true, null, false));
        }

        private void AddWorkgroupDiagnostic(string path, SyntaxNode syntax, string message) =>
            AddDiagnostic("COPE-GPU-WORKGROUP-0001", "SDSL-V4300", "workgroup", message, Span(path, syntax));

        private void ValidateWorkgroupSynchronization()
        {
            // Conservative portable profile: waits occur on the entry's straight-line
            // spine, including statically expanded blocks. No helper or branch can wait,
            // and an invocation cannot return before the last wait. This deliberately
            // does not guess whether a data-dependent condition is workgroup-uniform.
            foreach (var function in _boundFunctions)
            {
                bool entry = function.Name == _entryPoint?.Name;
                var spine = FlattenBlocks(function.Statements).ToArray();
                int lastBarrier = Array.FindLastIndex(spine, statement => statement.Kind is "workgroup-barrier" or "cooperative-matmul");
                for (int index = 0; index < spine.Length; index++)
                {
                    var statement = spine[index];
                    if (statement.Kind == "cooperative-matmul")
                    {
                        ValidateCooperativeUniformity(statement, spine.Take(index));
                    }
                    bool nestedBarrier = Descendants(statement.Body).Concat(Descendants(statement.ElseBody))
                        .Any(child => child.Kind is "workgroup-barrier" or "cooperative-matmul");
                    bool earlyReturn = index < lastBarrier && Descendants([statement]).Any(child => child.Kind == "return");
                    if (nestedBarrier || earlyReturn || !entry && statement.Kind is "workgroup-barrier" or "cooperative-matmul")
                    {
                        AddDiagnostic("COPE-GPU-WORKGROUP-0002", "SDSL-V4301", "synchronization",
                            "WorkgroupBarrier must be on the compute entry's straight-line spine, with no earlier conditional return; move masked work around an unconditional wait.",
                            statement.Source);
                    }
                }
            }
        }

        private static IEnumerable<VdMirStatement> FlattenBlocks(IEnumerable<VdMirStatement> statements)
        {
            foreach (var statement in statements)
            {
                if (statement.Kind == "block")
                {
                    foreach (var child in FlattenBlocks(statement.Body ?? []))
                    {
                        yield return child;
                    }
                }
                else
                {
                    yield return statement;
                }
            }
        }

        private static IEnumerable<VdMirStatement> Descendants(IEnumerable<VdMirStatement>? statements)
        {
            foreach (var statement in statements ?? [])
            {
                yield return statement;
                foreach (var child in Descendants(statement.Body).Concat(Descendants(statement.ElseBody)))
                {
                    yield return child;
                }
            }
        }
    }
}
