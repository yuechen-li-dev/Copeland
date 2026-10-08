using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Aetheris;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.NativeComposition;
using Aurelian.Shaders.Compute;
using Aurelian.Spatial3D;
using Aurelian.Spatial3D.Vulkan;
using Aurelian.World.Scenes;
using Copeland.TS.Gpu;

if (args.Length != 2) throw new ArgumentException("Usage: Aurelian.Spatial3DProof room.aurelian.json output-directory");
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var asset = AetherisSceneAsset.Load(args[0]);
ScenePlan plan = SceneCompiler.Compile(asset.Compose());
var world = SceneSpatial3D.Build(plan);
string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "RayQuery.v.ts"));
var module = GpuComputeBinder.Compile(new([new("RayQuery.v.ts", source)]));
if (!module.Success) throw new InvalidOperationException(string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
var program = VdMirComputeBackend.Compile(module);
if (program.Spirv.Length == 0 || !program.SpirvValidated) throw new InvalidOperationException("Ray query shader failed: " + program.DxcOutput + program.SpirvValidationOutput);
File.WriteAllText(Path.Combine(output, "rayquery.hlsl"), program.Hlsl);
File.WriteAllBytes(Path.Combine(output, "rayquery.spv"), program.Spirv);
File.WriteAllText(Path.Combine(output, "rayquery.spvasm"), program.SpirvDisassembly);
var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new(EnableRayQueries: true, ApplicationName: "Aurelian Spatial3D proof"));
if (!initialized.Success) throw new NotSupportedException(string.Join("; ", initialized.Diagnostics.Select(diagnostic => diagnostic.Message)));
using var plant = initialized.Plant!;
using var gpu = new VulkanSpatialRayQueries3D(plant, program.Spirv, world);
var random = new Random(7619);
Ray3D[] rays = Enumerable.Range(0, 4096).Select(_ =>
{
    Vector3 direction = Vector3.Normalize(new Vector3((float)random.NextDouble() * 2 - 1,
        (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1));
    return new Ray3D(new(6, 1.6f, -6), direction, 30);
}).ToArray();
SpatialHit3D?[] expected = rays.Select(ray => world.Raycast(ray)).ToArray();
var actual = gpu.RaycastBatch(rays);
int mismatches = 0;
for (int index = 0; index < rays.Length; index++)
{
    if (expected[index] is null && actual[index] is null) continue;
    if (expected[index] is not { } cpu || actual[index] is not { } hardware
        || cpu.ColliderId != hardware.ColliderId || cpu.TriangleIndex != hardware.TriangleIndex
        || MathF.Abs(cpu.Distance - hardware.Distance) > 0.0001f
        || Vector3.Distance(cpu.Normal, hardware.Normal) > 0.0001f)
    {
        mismatches++;
        Console.Error.WriteLine($"ray {index}: cpu={expected[index]} gpu={actual[index]}");
    }
}
if (mismatches != 0) throw new InvalidOperationException($"CPU/GPU ray agreement failed: {mismatches} mismatches.");
if (gpu.RaycastBatch(rays, new(0, uint.MaxValue)).Any(hit => hit is not null)) throw new InvalidOperationException("GPU filtering failed.");
// Coincident surfaces exercise canonical identity ties; upper bits prove masks are not truncated to Vulkan's instance byte.
var filteredWorld = new SpatialWorld3D([
    new("z", CollisionMesh3D.Box(Vector3.One), Matrix4x4.CreateTranslation(0, 0, -3), 0x80000000, 0x40000000),
    new("a", CollisionMesh3D.Box(Vector3.One), Matrix4x4.CreateTranslation(0, 0, -3), 0x80000000, 0x40000000),
    new("far", CollisionMesh3D.Box(Vector3.One), Matrix4x4.CreateTranslation(0, 0, -6), 4, 8),
]);
using (var filteredGpu = new VulkanSpatialRayQueries3D(plant, program.Spirv, filteredWorld))
{
    var tieRay = new Ray3D(new(0.2f, 0.1f, 0), -Vector3.UnitZ, 20);
    QueryFilter3D[] filters = [QueryFilter3D.All, new(0x80000000, 0x40000000), new(uint.MaxValue, 8), new(0, uint.MaxValue)];
    foreach (QueryFilter3D filter in filters)
    {
        SpatialHit3D? cpu = filteredWorld.Raycast(tieRay, filter);
        SpatialHit3D? hardware = filteredGpu.Raycast(tieRay, filter);
        if (cpu is null && hardware is null) continue;
        if (cpu is not { } expectedHit || hardware is not { } actualHit
            || expectedHit.ColliderId != actualHit.ColliderId || expectedHit.TriangleIndex != actualHit.TriangleIndex
            || MathF.Abs(expectedHit.Distance - actualHit.Distance) > 0.0001f
            || Vector3.Distance(expectedHit.Normal, actualHit.Normal) > 0.0001f)
        {
            throw new InvalidOperationException($"GPU stable tie or full-width symmetric filter failed: filter={filter}, cpu={cpu}, gpu={hardware}");
        }
    }
    // Hardware triangle boundary ownership is permitted to choose either adjacent
    // triangle. The semantic collider, distance and geometric normal must agree.
    var edgeRay = new Ray3D(Vector3.Zero, -Vector3.UnitZ, 20);
    SpatialHit3D edgeCpu = filteredWorld.Raycast(edgeRay)!.Value;
    SpatialHit3D edgeGpu = filteredGpu.Raycast(edgeRay)!.Value;
    if (edgeCpu.ColliderId != edgeGpu.ColliderId || MathF.Abs(edgeCpu.Distance - edgeGpu.Distance) > 0.0001f
        || Vector3.Distance(edgeCpu.Normal, edgeGpu.Normal) > 0.0001f || edgeGpu.TriangleIndex is not (2 or 3))
        throw new InvalidOperationException($"GPU triangle-edge surface agreement failed: cpu={edgeCpu}, gpu={edgeGpu}");
}
var watch = Stopwatch.StartNew();
for (int repeat = 0; repeat < 10; repeat++) foreach (Ray3D ray in rays) world.Raycast(ray);
double cpuMilliseconds = watch.Elapsed.TotalMilliseconds;
watch.Restart();
for (int repeat = 0; repeat < 10; repeat++) gpu.RaycastBatch(rays);
double gpuMilliseconds = watch.Elapsed.TotalMilliseconds;
using (var exact = new VulkanRayQueryScene(plant, program.Spirv, [], [new(new(0, 0, 4), 1)]))
{
    var sphereHits = exact.Trace([new(Vector3.Zero, Vector3.UnitZ, 10), new(Vector3.Zero, Vector3.UnitX, 10)]);
    if (sphereHits[0].Kind != 2 || MathF.Abs(sphereHits[0].Distance - 3) > 0.00001f || sphereHits[1].Kind != 0)
        throw new InvalidOperationException("Procedural analytic sphere proof failed.");
}
// The authored south-wall opening occupies x=[2,4], height=[0,3], z=-2.
var motor = new CharacterMotor3D();
CharacterState3D state = new(new(3, 0, -3), 0);
for (int tick = 0; tick < 60; tick++) state = motor.Step(world, state, Vector3.UnitZ * 2, false, 1f / 60).State;
if (state.Feet.Z < -1.5f) throw new InvalidOperationException("Character could not traverse Aetheris's authored doorway.");
var blocked = motor.Step(world, new(new(5, 0, -2.4f), 0), Vector3.UnitZ * 20, false, 0.05f);
if (blocked.State.Feet.Z > -2.29f) throw new InvalidOperationException("Character passed through Aetheris's authored wall.");
var proof = new SpatialProof(plant.Facts.PhysicalDeviceName, asset.SourceSha256, world.Colliders.Length,
    rays.Length, mismatches, program.SpirvSha256!, gpu.BottomLevelAddress, gpu.TopLevelAddress,
    gpu.DispatchCount, cpuMilliseconds, gpuMilliseconds, state.Feet.Z, true, true, true,
    plant.Facts.EnabledValidationLayers.ToArray(), true);
string json = JsonSerializer.Serialize(proof, ProofJsonContext.Default.SpatialProof);
File.WriteAllText(Path.Combine(output, "proof.json"), json);
Console.WriteLine(json);

public sealed record SpatialProof(string Device, string SourceSha256, int ColliderCount, int RayCount,
    int Mismatches, string ShaderSha256, ulong BlasAddress, ulong TlasAddress, int DispatchCount,
    double CpuMilliseconds, double GpuMilliseconds, float DoorwayExitZ, bool WallBlocked, bool AnalyticSpherePassed,
    bool FullWidthFilteringAndStableTiesPassed, string[] ValidationLayers, bool SharedTriangleEdgeSurfaceAgreementPassed);
[JsonSerializable(typeof(SpatialProof))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class ProofJsonContext : JsonSerializerContext;
