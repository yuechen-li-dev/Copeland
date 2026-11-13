using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aurelian.Audio;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/local/surface-response");
Directory.CreateDirectory(output);
string evidence = Path.Combine(output, "evidence.json");
File.WriteAllText(evidence, "{\"Accepted\":false}");
var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
    new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Surface response proof"));
if (!initialization.Success)
{
    throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
}
using var plant = initialization.Plant!;
var programs = new Dictionary<string, CompiledGraphicsProgram>();
var results = new Dictionary<string, int>();
foreach (bool shared in new[] { false, true })
{
    using var target = new VulkanNativeFrameTarget(plant, 128, 128);
    using var renderer = new VulkanSolid3DRenderer(plant, Shader(shared ? "SurfaceSolid3D.v.ts" : "Solid3D.v.ts"), target,
        modelProgram: Shader(shared ? "SurfaceModel3D.v.ts" : "StaticModel3D.v.ts"),
        shadowProgram: Shader("Shadow3D.v.ts"), outputProgram: Shader("ToneMap3D.v.ts"), temporalProgram: Shader("TemporalResolve3D.v.ts"),
        bloomProgram: Shader("Bloom3D.v.ts"), surfacePrograms: shared ? new(
            Shader("SurfaceResolve3D.v.ts"), Shader("AmbientOcclusion3D.v.ts"),
            Shader("AmbientDenoise3D.v.ts"), Shader("LightTiles3D.v.ts")) : null);
    renderer.Settings = Graphics3DSettings.Default with
    {
        AntiAliasing = AntiAliasing3D.None, Shadows = false, BloomIntensity = 0,
        SunDirection = Vector3.Normalize(new(.2f, .3f, 1)), SunIntensity = 2,
    };
    var dry = new ModelMaterial("stone")
    {
        BaseColor = new(.5f, .3f, .2f, 1), Metallic = 0, Roughness = .35f, DoubleSided = true,
        NormalTexture = new(new("averaged-normal", 1, 1, ImmutableArray.Create<byte>(128, 128, 200, 255)), new()),
    };
    var profile = new WetSurface3D();
    byte[] reference = Capture(dry, "dry");
    if (!reference.SequenceEqual(Capture(profile.Apply(dry, 0), "dry-zero")))
    {
        throw new InvalidOperationException("Zero wetness changed the dry image.");
    }
    results.Add(shared + ":wet", Changed(reference, Capture(profile.Apply(dry, 1), "wet")));
    results.Add(shared + ":specular-aa", Changed(reference, Capture(dry with { SpecularAntiAliasing = true }, "filtered")));
    var unmapped = dry with { NormalTexture = null };
    if (!Capture(unmapped, "unmapped").SequenceEqual(Capture(unmapped with { SpecularAntiAliasing = true }, "unmapped-filtered")))
    {
        throw new InvalidOperationException("Specular filtering changed a material without a normal map.");
    }

    byte[] Capture(ModelMaterial material, string name)
    {
        NativeModel3DVertex Vertex(float x, float y) => new(new(x, y, 0), Vector3.UnitZ,
            Vector4.One, Vector2.Zero, new(1, 0, 0, 1));
        NativeModel3DVertex[] vertices = [Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1),
            Vertex(-1, -1), Vertex(1, 1), Vertex(-1, 1)];
        var eye = new Vector3(0, 0, 3);
        var camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(1, 1, .1f, 10);
        byte[] pixels = renderer.Render(new([], [new(vertices, material)]), camera, eye,
            new NativeFrameClearColor(.01f, .01f, .01f, 1), capture: true).Pixels!;
        File.WriteAllBytes(Path.Combine(output, $"{shared}-{name}.rgba"), pixels);
        return pixels;
    }
}
if (results.Values.Any(count => count < 100))
{
    throw new InvalidOperationException("Surface response did not change enough native pixels: " + JsonSerializer.Serialize(results));
}
foreach (var kind in Enum.GetValues<ProceduralSoundKind>())
{
    var clip = new ProceduralSoundRecipe(kind).Prepare(new(kind.ToString()));
    using var writer = new BinaryWriter(File.Create(Path.Combine(output, kind + ".wav")));
    writer.Write("RIFF"u8);
    writer.Write(36 + clip.Samples.Length * 2);
    writer.Write("WAVEfmt "u8);
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(clip.SampleRateHz);
    writer.Write(clip.SampleRateHz * 2);
    writer.Write((short)2);
    writer.Write((short)16);
    writer.Write("data"u8);
    writer.Write(clip.Samples.Length * 2);
    foreach (float sample in clip.Samples)
    {
        writer.Write((short)Math.Round(Math.Clamp(sample, -1, 1) * 32767));
    }
}
File.WriteAllText(evidence, JsonSerializer.Serialize(new
{
    Accepted = true, Device = plant.Facts.PhysicalDeviceName, Width = 128, Height = 128,
    ChangedPixels = results, PixelFormat = "RGBA8", Audio = "mono PCM16 WAV",
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(File.ReadAllText(evidence));

CompiledGraphicsProgram Shader(string name)
{
    if (programs.TryGetValue(name, out var cached))
    {
        return cached;
    }
    var module = GpuGraphicsBinder.Compile(new(BuiltinShaderLibrary.LoadSources(name)));
    if (!module.Success)
    {
        throw new InvalidOperationException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
    }
    var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
    if (!backend.Vertex.SpirvValidated || !backend.Pixel.SpirvValidated)
    {
        throw new InvalidOperationException("SPIR-V validation failed for " + name);
    }
    var program = CompiledGraphicsProgramExporter.Export(module, backend);
    programs.Add(name, program);
    return program;
}

static int Changed(byte[] before, byte[] after)
{
    int count = 0;
    for (int offset = 0; offset < before.Length; offset += 4)
    {
        if (before[offset] != after[offset] || before[offset + 1] != after[offset + 1] || before[offset + 2] != after[offset + 2])
        {
            count++;
        }
    }
    return count;
}
