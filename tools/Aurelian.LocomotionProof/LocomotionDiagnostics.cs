using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aetheris.Humanoid;
using Aurelian.Humanoid;

internal static class LocomotionDiagnostics
{
    public static void Run(HumanoidGameplayBody body, HumanoidLocomotionBank bank, string output)
    {
        var cases = new List<PoseDiagnosis>();
        foreach (var selection in new[] { (HumanoidGait.Idle, 6), (HumanoidGait.Walk, 2),
            (HumanoidGait.Run, 0), (HumanoidGait.Run, 9) })
        {
            var clip = bank.Clip(selection.Item1);
            var sample = clip.Sample(clip.Duration * selection.Item2 / 12);
            var variants = new Dictionary<string, string[]>
            {
                ["original"] = [],
                ["head-neutral"] = ["Head", "Neck"],
                ["hands-neutral"] = ["LeftWrist", "RightWrist"],
                ["clavicles-neutral"] = ["LeftClavicle", "RightClavicle"],
                ["shoulders-neutral"] = ["LeftShoulder", "RightShoulder"],
                ["knees-neutral"] = ["LeftKnee", "RightKnee"],
            };
            foreach (var variant in variants)
            {
                var rotations = sample.Rotations.Select(item => variant.Value.Contains(item.Joint.ToString())
                    ? item with { LocalRotation = Quaternion.Identity } : item).ToImmutableArray();
                var solved = HumanoidKinematicSolver.SolveAuthored(body.Skeleton,
                    new(variant.Key, body.Skeleton.SkeletonId, body.Skeleton.RestPoseId, rotations, sample.OffsetMm));
                if (!solved.IsSolved) throw new InvalidDataException("Diagnostic pose was not admitted.");
                var evaluated = body.Evaluate(solved.Pose!);
                var flagged = evaluated.Evidence.FlaggedFaceIds.ToHashSet();
                var faces = body.Surface.Faces.Where(face => flagged.Contains(face.Id)).Take(16)
                    .Select(face => new FaceDiagnosis(face.Id, face.Region.ToString(),
                        new[] { face.A, face.B, face.C }.Select(Vertex).ToArray())).ToArray();
                var edges = new HashSet<(int, int)>();
                foreach (var face in body.Surface.Faces)
                {
                    Add(face.A, face.B);
                    Add(face.B, face.C);
                    Add(face.C, face.A);
                }
                var worst = edges.Select(edge =>
                {
                    double before = (body.Surface.Vertices[edge.Item1].Position - body.Surface.Vertices[edge.Item2].Position).Length;
                    double after = (evaluated.Positions[edge.Item1] - evaluated.Positions[edge.Item2]).Length;
                    return new EdgeDiagnosis(Vertex(edge.Item1), Vertex(edge.Item2), before, after,
                        Math.Max(before / after, after / before));
                }).OrderByDescending(edge => edge.Ratio).Take(8).ToArray();
                cases.Add(new(selection.Item1 + "." + selection.Item2, variant.Key,
                    evaluated.Evidence.IsAdmissible, evaluated.Evidence.FlaggedFaceIds.Count,
                    evaluated.Evidence.Regions.Max(region => region.Maximum), faces, worst));

                VertexDiagnosis Vertex(int index)
                {
                    var vertex = body.Surface.Vertices[index];
                    return new(vertex.Id, vertex.Region.ToString(), vertex.Position.X, vertex.Position.Y, vertex.Position.Z,
                        string.Join(", ", body.Surface.SkinWeights[index].Weights.Select(weight =>
                            body.Skeleton.Joints[weight.JointIndex].Kind + ":" + weight.Weight.ToString("F3"))));
                }
                void Add(int a, int b) => edges.Add(a < b ? (a, b) : (b, a));
            }
        }
        File.WriteAllText(Path.Combine(output, "diagnosis.json"),
            JsonSerializer.Serialize(cases, DiagnosisJson.Default.ListPoseDiagnosis));
        foreach (var item in cases)
        {
            Console.WriteLine($"{item.Pose} {item.Variant}: proxies={item.Proxies} edge={item.MaximumRatio:F3}");
        }
    }
}

internal sealed record VertexDiagnosis(string Id, string Region, double X, double Y, double Z, string Weights);
internal sealed record FaceDiagnosis(string Id, string Region, VertexDiagnosis[] Vertices);
internal sealed record EdgeDiagnosis(VertexDiagnosis A, VertexDiagnosis B, double RestMm, double PosedMm, double Ratio);
internal sealed record PoseDiagnosis(string Pose, string Variant, bool Admissible, int Proxies,
    double MaximumRatio, FaceDiagnosis[] Faces, EdgeDiagnosis[] Edges);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<PoseDiagnosis>))]
internal partial class DiagnosisJson : JsonSerializerContext;
