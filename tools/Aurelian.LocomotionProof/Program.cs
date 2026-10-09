using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aetheris.Humanoid;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Humanoid;
using InputMan.Core;

string bodyPath = Option("--body");
string bankPath = Option("--locomotion");
string output = Path.GetFullPath(Option("--output"));
Directory.CreateDirectory(output);
var profile = HumanoidPlayerOptions.Load(bodyPath);
var bank = HumanoidLocomotionBank.Load(bankPath, profile.Body);
profile = profile with { Locomotion = bank };
if (args.Contains("--diagnose", StringComparer.Ordinal))
{
    LocomotionDiagnostics.Run(profile.Body, bank, output);
    return;
}
bool gpuRays = args.Contains("--gpu-rays", StringComparer.Ordinal);
var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian Locomotion Qualification",
        EnableRayQueries: gpuRays));
if (!initialized.Success)
{
    throw new InvalidOperationException(string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
}
using var plant = initialized.Plant!;
using var gpu = new HumanoidGpuBody(plant, new GameAssets().ComputeShader("HumanoidSkinning.v.ts"), profile.Body);
var checks = new List<LocomotionPoseCheck>();
double maximumParity = 0;
float maximumLockResidual = 0;
int locks = 0;
int releases = 0;
foreach (var gait in Enum.GetValues<HumanoidGait>())
{
    var clip = bank.Clip(gait);
    for (int index = 0; index < 12; index++)
    {
        var sample = clip.Sample(clip.Duration * index / 12);
        var result = HumanoidKinematicSolver.SolveAuthored(profile.Body.Skeleton,
            new(gait + "." + index, profile.Body.Skeleton.SkeletonId, profile.Body.Skeleton.RestPoseId,
                sample.Rotations, sample.OffsetMm));
        Check(gait + "." + index, result.Pose!, Matrix4x4.CreateRotationY(.4f) * Matrix4x4.CreateTranslation(2, .2f, -3));
    }
    for (int index = 0; index < clip.Frames.Length; index++)
    {
        CheckSample("key." + gait + "." + index, clip.Frames[index].Seconds);
        if (index + 1 < clip.Frames.Length)
        {
            CheckSample("midpoint." + gait + "." + index,
                (clip.Frames[index].Seconds + clip.Frames[index + 1].Seconds) / 2);
        }
    }
    Console.WriteLine($"LOCOMOTION_CLIP_CHECKED gait={gait} cases={checks.Count}");

    void CheckSample(string id, double seconds)
    {
        var sample = clip.Sample(seconds);
        var result = HumanoidKinematicSolver.SolveAuthored(profile.Body.Skeleton,
            new(id, profile.Body.Skeleton.SkeletonId, profile.Body.Skeleton.RestPoseId,
                sample.Rotations, sample.OffsetMm));
        if (!result.IsSolved) throw new InvalidDataException("Qualification sample was not admitted: " + id);
        Check(id, result.Pose!, Matrix4x4.CreateRotationY(.4f) * Matrix4x4.CreateTranslation(2, .2f, -3));
    }
}
using var game = GameStarter.Create("locomotion-proof", [.. GamePresets.FirstPersonShooter, GameConcept.HumanoidPresentation],
    humanoid: profile, saveDirectory: Path.Combine(output, "saves"));
using var rayQueries = gpuRays ? GameRayQueries.Create(plant, game.SpatialWorld) : null;
if (rayQueries is not null) game.UseRayQueries(rayQueries);
game.Activate("start");
for (int frame = 0; frame < 600; frame++)
{
    game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), frame < 240);
    game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.LeftShift), frame >= 120);
    game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), frame >= 110 && frame < 160);
    game.Advance(TimeSpan.FromTicks(166667));
    var state = game.HumanoidPlayer!.State.Animation.Locomotion!;
    foreach (var foot in new[] { state.Left, state.Right })
    {
        if (foot.Locked && foot.Weight == 1 && !foot.ReachClamped)
        {
            locks++;
            maximumLockResidual = Math.Max(maximumLockResidual, foot.ResidualMm);
        }
    }
    if (state.Diagnostic is not null) releases++;
    Check("game." + frame + "." + state.Gait, game.CharacterPose!, game.HumanoidPlayer.WorldTransform);
}
var evidence = new LocomotionEvidence(checks.All(check => check.ScreenAdmissible) && maximumParity <= .02 &&
    locks > 0 && maximumLockResidual <= 1, plant.Facts.PhysicalDeviceName, bank.Identity,
    checks.Count, gpu.DispatchCount, maximumParity, locks, maximumLockResidual, releases,
    gpuRays, rayQueries?.DispatchCount ?? 0, checks);
File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(evidence, EvidenceJson.Default.LocomotionEvidence));
Console.WriteLine($"LOCOMOTION_QUALIFICATION accepted={evidence.Accepted} cases={checks.Count} parityMm={maximumParity:R} footResidualMm={maximumLockResidual:R}");
if (!evidence.Accepted) Environment.ExitCode = 1;

void Check(string id, SolvedHumanoidPose pose, Matrix4x4 world)
{
    var reference = profile.Body.Evaluate(pose);
    gpu.Present(pose, world);
    var actual = gpu.ReadVerticesForQualification();
    int corner = 0;
    foreach (var face in profile.Body.Surface.Faces)
    {
        foreach (int vertex in new[] { face.A, face.B, face.C })
        {
            var source = reference.Positions[vertex];
            Vector3 expected = Vector3.Transform(new((float)source.X, (float)source.Y, (float)source.Z), HumanoidSpace.SourceToWorld * world);
            double error = Vector3.Distance(expected, actual[corner].Position) * 1000;
            float normal = actual[corner++].Normal.LengthSquared();
            if (!double.IsFinite(error) || !float.IsFinite(normal) || MathF.Abs(normal - 1) > .001f)
            {
                throw new InvalidDataException("Nonfinite GPU deformation or non-unit normal.");
            }
            maximumParity = Math.Max(maximumParity, error);
        }
    }
    checks.Add(new(id, reference.Evidence.IsAdmissible,
        reference.Evidence.CollapsedTriangles, reference.Evidence.FlaggedFaceIds.Count,
        reference.Evidence.Regions.Max(region => region.Maximum),
        string.Join(",", reference.Evidence.Regions.Where(region => region.Maximum > 4 || region.OrientationReversalProxies > 0)
            .Select(region => region.Region + ":" + region.OrientationReversalProxies + ":" + region.Maximum.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))));
}

string Option(string name)
{
    int index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name + " requires a value.");
    return args[index + 1];
}

public sealed record LocomotionPoseCheck(string Id, bool ScreenAdmissible, int CollapsedTriangles,
    int OrientationProxies, double MaximumEdgeRatio, string FlaggedRegions);
public sealed record LocomotionEvidence(bool Accepted, string Device, string BankIdentity, int PoseCases,
    int GpuDispatches, double MaximumParityMm, int FootLockSamples, float MaximumFootResidualMm,
    int LockReleases, bool GpuRayQueries, int RayQueryDispatches, IReadOnlyList<LocomotionPoseCheck> Cases);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LocomotionEvidence))]
internal partial class EvidenceJson : JsonSerializerContext;
