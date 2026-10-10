using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Security.Cryptography;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.World.Scenes;
using SkiaSharp;

internal static class LiveDiffuseExperiment
{
    private const int Width = 256;
    private const int Height = 192;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"accepted\":false}");
        var assets = new GameAssets();
        var cacheProgram = assets.Shader("DiffuseCache3D.v.ts");
        var probeProgram = assets.Shader("DiffuseProbes3D.v.ts");
        var queryProgram = assets.Shader("DiffuseProbeQuery3D.v.ts");
        var viewProgram = assets.Shader("DiffuseProbeView3D.v.ts");
        byte[] rayProgram = assets.ComputeShader("RayQuery.v.ts");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian live diffuse experiment", EnableRayQueries: true));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        DiffuseProbeGrid grid = new(new(-2.7f, .25f, -2.7f), new(2.7f, 2.7f, 2.7f), SurfaceResolution: 16);
        DiffuseProbeLight light = new(new(-1.3f, 2.3f, -.8f), new(1, .75f, .45f), 28);
        DiffuseProbeTriangle[] scene = Room();
        using var volume = new VulkanDiffuseProbeVolume(plant, grid, scene, light, rayProgram, cacheProgram, probeProgram);
        var ticks = new List<object>();
        Complete();
        float[] initial = volume.Inspect(volume.Irradiance);
        Require(initial.All(float.IsFinite) && initial.Where((_, i) => i % 4 == 3).All(value => value == 1), "Invalid probe generation.");
        object cacheComparison = DiffuseProbeReference.CompareAtlas(scene, light, grid, initial);
        object leakage = TestLeakage();
        object decoder = TestDecoder();
        object reflectionMath = TestRefit();
        var capturedRadiance = CaptureEnvironment();
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"),
            128, 64, capturedRadiance);
        var environmentTexture = volume.UploadInspectionData(64, 512, environment.Pixels.ToArray());
        Vector3 eye = new(7, 5, 8);
        RayQueryRequest[] cameraRays = CameraRays(eye);
        float[] direct = Capture("direct", 0);
        float[] gi = Capture("diffuse", 1);
        float[] rawReflection = Capture("reflection-raw", 2);
        float[] fittedReflection = Capture("reflection-refit", 3);
        int stableUpdates = volume.UpdateCount;
        for (int tick = 0; tick < 10; tick++)
        {
            volume.Update();
        }
        Require(volume.UpdateCount == stableUpdates, "Stable lighting dispatched unnecessary updates.");
        volume.SetLight(light with { Intensity = 0 });
        Require(!volume.Ready, "Lighting change did not immediately withdraw readiness.");
        float[] invalidatedParameters = volume.Uniforms();
        invalidatedParameters[6] = 1;
        invalidatedParameters[7] = 1;
        var point = volume.UploadInspectionData(2, 1, [1, .02f, -1, 0, 0, 1, 0, 0]);
        float[] stale = volume.InspectResponse(queryProgram, 1, 1, invalidatedParameters,
            point, volume.Irradiance, volume.Visibility, point);
        Require(stale.All(value => value == 0), "Stale generation leaked through the GPU decoder.");
        Complete();
        float[] off = volume.Inspect(volume.Irradiance);
        float offMaximum = off.Where((_, index) => index % 4 != 3).Max(MathF.Abs);
        Require(offMaximum == 0, "Old light energy survived invalidation.");
        float[] offRaw = Capture("off-raw-reflection", 2);
        float[] offFit = Capture("off-refit", 3);
        volume.SetLight(light with { Position = new(1.4f, 2.2f, .7f), Color = new(.4f, .6f, 1) });
        Complete();
        float[] moved = Capture("moved-light", 3);
        var replacementScene = Room(wallPosition: 1.1f);
        using var replaced = new VulkanDiffuseProbeVolume(plant, grid, replacementScene, light, rayProgram, cacheProgram, probeProgram);
        Require(!replaced.Ready, "Replacement geometry started with published lighting.");
        for (int tick = 0; tick < 100 && !replaced.Ready; tick++)
        {
            replaced.Update(1024, 32);
        }
        Require(replaced.Ready, "Replacement geometry did not complete.");
        float[] replacedValues = replaced.Inspect(replaced.Irradiance);
        Require(MaximumDifference(initial, replacedValues) > .01f, "Moving geometry did not change transport.");
        Capture("moved-wall", 1, replaced);
        var bodyScene = Room(blockPosition: new(.54f, .4f, .54f));
        using var body = new VulkanDiffuseProbeVolume(plant, grid, bodyScene,
            light, rayProgram, cacheProgram, probeProgram);
        for (int tick = 0; tick < 100 && !body.Ready; tick++)
        {
            body.Update(1024, 32);
        }
        Require(body.Ready, "Moving body did not complete.");
        float[] bodyAtlas = body.Inspect(body.Irradiance);
        int interiorProbes = Enumerable.Range(0, grid.Count).Count(index => bodyAtlas[index * 64 * 4 + 3] == 0);
        Require(interiorProbes > 0, "Probes inside the moved solid were not rejected.");
        Capture("moved-body", 1, body);
        int changed = Changed(direct, gi);
        Require(changed > 100, "Diffuse bounce did not materially change the room.");
        var result = new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Inputs = new { Grid = grid, Light = light, Scene = scene, ReplacementScene = replacementScene, BodyScene = bodyScene },
            ProgramKeys = new
            {
                Cache = LightingProgramIdentity.Compute(cacheProgram),
                Probes = LightingProgramIdentity.Compute(probeProgram),
                Query = LightingProgramIdentity.Compute(queryProgram),
                View = LightingProgramIdentity.Compute(viewProgram),
                Connections = LightingProgramIdentity.Compute(assets.Shader("DiffuseProbeConnections3D.v.ts")),
                Refit = LightingProgramIdentity.Compute(assets.Shader("DiffuseRefitQuery3D.v.ts")),
                HardwareQuery = Convert.ToHexString(SHA256.HashData(rayProgram)),
            },
            ProgramManifest = "programs/manifest.json",
            TriangleCount = scene.Length,
            ProbeCount = grid.Count,
            grid.RaysPerProbe,
            grid.SurfaceResolution,
            DiffuseChangedPixels = changed,
            OffMaximum = offMaximum,
            OffRawEnergy = Energy(offRaw),
            OffRefitEnergy = Energy(offFit),
            BaselineRefitLocalizationDifference = MaximumDifference(rawReflection, fittedReflection),
            MovedLightChangedPixels = Changed(gi, moved),
            GeometryMaximumResponseChange = MaximumDifference(initial, replacedValues),
            InteriorProbesRejected = interiorProbes,
            CacheComparison = cacheComparison,
            Leakage = leakage,
            Decoder = decoder,
            ReflectionMath = reflectionMath,
            volume.SurfaceShadingCount,
            volume.ProbeRayCount,
            Ticks = ticks,
            Scope = "GPU-resident bounded single-bounce diffuse experiment. Serial synchronous queue; no full-game integration or path-traced quality claim.",
        };
        WriteComparison(output);
        RetainPrograms();
        File.WriteAllText(evidence, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine("AURELIAN_LIVE_DIFFUSE_PROOF_PASSED " + plant.Facts.PhysicalDeviceName);

        void Complete()
        {
            for (int tick = 0; tick < 100 && !volume.Ready; tick++)
            {
                var watch = Stopwatch.StartNew();
                volume.Update(1024, 32);
                ticks.Add(new { volume.Generation, volume.UpdateCount, Milliseconds = watch.Elapsed.TotalMilliseconds,
                    GpuMilliseconds = volume.LastGpuUpdateMilliseconds });
            }
            Require(volume.Ready, "Bounded lighting updates did not complete.");
        }

        void RetainPrograms()
        {
            string directory = Path.Combine(output, "programs");
            Directory.CreateDirectory(directory);
            string[] files = ["DiffuseCache3D.v.ts", "DiffuseProbes3D.v.ts", "DiffuseProbeQuery3D.v.ts",
                "DiffuseProbeView3D.v.ts", "DiffuseProbeConnections3D.v.ts", "DiffuseRefitQuery3D.v.ts",
                "DiffuseProbeLighting.v.ts", "DiffuseRayHit.v.ts", "RayQuery.v.ts", "Lighting3D.v.ts", "SurfaceLighting.v.ts"];
            foreach (string name in files)
            {
                using var stream = typeof(GameAssets).Assembly.GetManifestResourceStream("Aurelian.Games." + name)
                    ?? throw new FileNotFoundException("Built-in proof source is unavailable: " + name);
                using var reader = new StreamReader(stream);
                File.WriteAllText(Path.Combine(directory, name), reader.ReadToEnd());
            }
            var manifest = new List<object>();
            var programs = new[]
            {
                (Name: "DiffuseCache3D", Program: cacheProgram),
                (Name: "DiffuseProbes3D", Program: probeProgram),
                (Name: "DiffuseProbeQuery3D", Program: queryProgram),
                (Name: "DiffuseProbeView3D", Program: viewProgram),
                (Name: "DiffuseProbeConnections3D", Program: assets.Shader("DiffuseProbeConnections3D.v.ts")),
                (Name: "DiffuseRefitQuery3D", Program: assets.Shader("DiffuseRefitQuery3D.v.ts")),
            };
            foreach (var item in programs)
            {
                foreach (var stage in item.Program.Shaders.Stages)
                {
                    string name = item.Name + "." + stage.Stage + ".spv";
                    string path = Path.Combine(directory, name);
                    File.WriteAllBytes(path, stage.SpirvBytes);
                    string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                    Require(hash.Equals(stage.SpirvSha256, StringComparison.OrdinalIgnoreCase), "Retained SPIR-V checksum differs.");
                    manifest.Add(new { File = name, Sha256 = hash, stage.EntryPoint, stage.Profile });
                }
            }
            File.WriteAllBytes(Path.Combine(directory, "RayQuery.spv"), rayProgram);
            manifest.Add(new { File = "RayQuery.spv", Sha256 = Convert.ToHexString(SHA256.HashData(rayProgram)),
                EntryPoint = "RayQueryMain", Profile = "cs_6_5" });
            File.WriteAllText(Path.Combine(directory, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        object TestLeakage()
        {
            var positions = new List<Vector3>();
            foreach (float x in new[] { .05f, .2f, .4f })
            {
                foreach (float z in new[] { -1.8f, -1f, -.2f })
                {
                    positions.Add(new(x, .005f, z));
                }
            }
            float[] words = new float[positions.Count * 8];
            for (int index = 0; index < positions.Count; index++)
            {
                words[index * 8] = positions[index].X;
                words[index * 8 + 1] = positions[index].Y;
                words[index * 8 + 2] = positions[index].Z;
                words[index * 8 + 5] = 1;
            }
            var sites = volume.UploadInspectionData(2, positions.Count, words);
            float[] uniforms = volume.Uniforms();
            uniforms[7] = 0;
            float[] plain = volume.InspectResponse(queryProgram, 1, positions.Count, uniforms,
                sites, volume.Irradiance, volume.Visibility, sites);
            uniforms[7] = 1;
            float[] filtered = volume.InspectResponse(queryProgram, 1, positions.Count, uniforms,
                sites, volume.Irradiance, volume.Visibility, sites);
            var connections = volume.CompileConnections(assets.Shader("DiffuseProbeConnections3D.v.ts"),
                positions.Select(position => (position, Vector3.UnitY)).ToArray());
            uniforms[7] = 2;
            float[] connected = volume.InspectResponse(queryProgram, 1, positions.Count, uniforms,
                sites, volume.Irradiance, volume.Visibility, connections);
            var rows = new List<object>();
            double plainSquared = 0;
            double filteredSquared = 0;
            double connectedSquared = 0;
            for (int index = 0; index < positions.Count; index++)
            {
                Vector3 reference = DiffuseProbeReference.At(scene, light, positions[index], Vector3.UnitY, 2048);
                Vector3 a = new(plain[index * 4], plain[index * 4 + 1], plain[index * 4 + 2]);
                Vector3 b = new(filtered[index * 4], filtered[index * 4 + 1], filtered[index * 4 + 2]);
                Vector3 c = new(connected[index * 4], connected[index * 4 + 1], connected[index * 4 + 2]);
                Require(connected[index * 4 + 3] == 1, "A tested connection site fell back instead of evaluating lighting.");
                plainSquared += (a - reference).LengthSquared();
                filteredSquared += (b - reference).LengthSquared();
                connectedSquared += (c - reference).LengthSquared();
                rows.Add(new { Position = positions[index].ToString(), Reference = reference.ToString(),
                    Unweighted = a.ToString(), VisibilityWeighted = b.ToString(), Connected = c.ToString(),
                    Valid = connected[index * 4 + 3] });
            }
            Require(connectedSquared < filteredSquared, "Compiled connectivity did not improve the thin-wall witness.");
            return new
            {
                UnweightedRmse = Math.Sqrt(plainSquared / (positions.Count * 3)),
                VisibilityWeightedRmse = Math.Sqrt(filteredSquared / (positions.Count * 3)),
                CompiledConnectionsRmse = Math.Sqrt(connectedSquared / (positions.Count * 3)),
                ConnectionRayCount = positions.Count * 8,
                SamplesPerReference = 2048,
                Sites = rows,
            };
        }

        object TestDecoder()
        {
            float[] constant = new float[grid.Count * 64 * 4];
            float[] moments = new float[constant.Length];
            for (int index = 0; index < constant.Length; index += 4)
            {
                constant[index] = 1;
                constant[index + 1] = .5f;
                constant[index + 2] = .25f;
                constant[index + 3] = 1;
                moments[index] = 32;
                moments[index + 1] = 1024;
                moments[index + 3] = 1;
            }
            var colors = volume.UploadInspectionData(8, grid.Count * 8, constant);
            var distances = volume.UploadInspectionData(8, grid.Count * 8, moments);
            var sites = volume.UploadInspectionData(2, 3, [0, 1, 0, 0, 0, 1, 0, 0,
                .3f, .4f, .2f, 0, 1, 0, 0, 0, 100, 1, 0, 0, 0, 1, 0, 0]);
            float[] uniforms = volume.Uniforms();
            uniforms[7] = 1;
            float[] values = volume.InspectResponse(queryProgram, 1, 3, uniforms, sites, colors, distances, sites);
            float error = MathF.Max(MathF.Abs(values[0] - 1), MathF.Abs(values[4] - 1));
            Require(error < .00001f && values[3] == 1 && values[7] == 1 && values[11] == 0,
                "Constant irradiance or out-of-domain fallback failed.");
            return new { ConstantMaximumError = error, OutsideValidity = values[11], StaleValidity = 0 };
        }

        object TestRefit()
        {
            var program = assets.Shader("DiffuseRefitQuery3D.v.ts");
            var reference = volume.UploadInspectionData(2, 4, [2, 3, 4, 0, .5f, .75f, 1, 0,
                2, 3, 4, 0, .5f, .75f, 1, 0, 2, 3, 4, 0, .5f, .75f, 1, 0,
                2, 3, 4, 0, 0, 0, 0, 0]);
            var current = volume.UploadInspectionData(1, 4, [.5f, .75f, 1, 0,
                .25f, 1.5f, .25f, 0, 0, 0, 0, 0, 100, 100, 100, 0]);
            float[] actual = volume.InspectResponse(program, 1, 4, volume.Uniforms(), reference, current);
            float[] expected = [2, 3, 4, 1, 1, 6, 1, 1, 0, 0, 0, 1, 8, 12, 16, 1];
            float error = MaximumDifference(actual, expected);
            Require(error < .00001f && actual.All(float.IsFinite), "Reflection normalization/guard failed.");
            return new { MaximumError = error, GainLimit = 4, DenominatorFloor = .01f,
                Scope = "Exact GPU checks of the stated heuristic, not physical reflection correctness." };
        }

        float[] Capture(string name, int mode, VulkanDiffuseProbeVolume? source = null)
        {
            var current = source ?? volume;
            var hits = current.TraceView(cameraRays);
            float[] parameters = current.Uniforms();
            parameters[4] = Width;
            parameters[5] = Height;
            parameters[7] = mode;
            parameters[28] = eye.X;
            parameters[29] = eye.Y;
            parameters[30] = eye.Z;
            float[] pixels = current.InspectResponseWithLinearInput(viewProgram, Width, Height, parameters, 2, hits, current.SurfaceData,
                current.RadianceCache, current.Irradiance, current.Visibility, environmentTexture);
            WriteImage(Path.Combine(output, name + ".png"), pixels);
            return pixels;
        }

        float[] CaptureEnvironment()
        {
            const int width = 128;
            const int height = 64;
            Vector3 origin = new(1.2f, 1.3f, .4f);
            var rays = new RayQueryRequest[width * height];
            for (int y = 0; y < height; y++)
            {
                float latitude = (y + .5f) / height * MathF.PI;
                for (int x = 0; x < width; x++)
                {
                    float longitude = ((x + .5f) / width - .5f) * MathF.Tau;
                    Vector3 direction = new(MathF.Cos(longitude) * MathF.Sin(latitude), MathF.Cos(latitude),
                        MathF.Sin(longitude) * MathF.Sin(latitude));
                    rays[y * width + x] = new(origin, direction, 32);
                }
            }
            var hits = volume.TraceView(rays);
            float[] uniforms = volume.Uniforms();
            uniforms[4] = width;
            uniforms[5] = height;
            uniforms[6] = 0;
            uniforms[7] = 0;
            uniforms[15] = 1;
            // A neutral atlas satisfies the unused environment binding during radiance acquisition.
            var neutral = volume.UploadInspectionData(64, 512, new float[64 * 512 * 4]);
            return volume.InspectResponseWithLinearInput(viewProgram, width, height, uniforms, 2, hits, volume.SurfaceData,
                volume.RadianceCache, volume.Irradiance, volume.Visibility, neutral);
        }
    }

    internal static DiffuseProbeTriangle[] Room(float wallPosition = 0, Vector3? blockPosition = null)
    {
        var plan = SceneCompiler.Compile(Scene.World("probe-room", [
            Scene.Box("floor", new(6, .2f, 6), new(.65f, .65f, .65f, 1), new(0, -.1f, 0)),
            Scene.Box("back", new(6, 3, .15f), new(.65f, .25f, .12f, 1), new(0, 1.5f, -3)),
            Scene.Box("side", new(.15f, 3, 6), new(.12f, .35f, .65f, 1), new(-3, 1.5f, 0)),
            Scene.Box("divider", new(.08f, 2.2f, 2.8f), new(.6f, .6f, .6f, 1), new(wallPosition, 1.1f, -.8f)),
            Scene.Box("block", new(.8f, .8f, .8f), new(.15f, .65f, .25f, 1), blockPosition ?? new(1.6f, .4f, 1.2f)),
        ]));
        using var mounted = plan.Mount();
        Native3DVertex[] vertices = SceneGeometry3D.Build(mounted.Project());
        var result = new List<DiffuseProbeTriangle>();
        for (int index = 0; index < vertices.Length; index += 3)
        {
            var a = vertices[index];
            var b = vertices[index + 1];
            var c = vertices[index + 2];
            if (Vector3.Dot(Vector3.Cross(b.Position - a.Position, c.Position - a.Position), a.Normal) < 0)
            {
                (b, c) = (c, b);
            }
            result.Add(new(a.Position, b.Position, c.Position, new(a.Color.X, a.Color.Y, a.Color.Z), Vector3.Zero));
        }
        return result.ToArray();
    }

    private static RayQueryRequest[] CameraRays(Vector3 eye)
    {
        Vector3 forward = Vector3.Normalize(new Vector3(0, .7f, 0) - eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);
        var rays = new RayQueryRequest[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float horizontal = ((x + .5f) / Width * 2 - 1) * Width / Height * .34f;
                float vertical = (1 - (y + .5f) / Height * 2) * .34f;
                rays[y * Width + x] = new(eye, Vector3.Normalize(forward + right * horizontal + up * vertical), 32);
            }
        }
        return rays;
    }

    private static void WriteImage(string path, float[] linear)
    {
        var pixels = new byte[linear.Length];
        for (int index = 0; index < linear.Length; index++)
        {
            float value = linear[index];
            if (index % 4 == 3)
            {
                pixels[index] = 255;
            }
            else
            {
                value = value / (1 + value);
                pixels[index] = (byte)Math.Clamp(MathF.Round(MathF.Pow(value, 1 / 2.2f) * 255), 0, 255);
            }
        }
        NativeGameGraphics.WritePng(path, Width, Height, pixels);
    }

    private static int Changed(float[] left, float[] right)
    {
        int count = 0;
        for (int index = 0; index < left.Length; index += 4)
        {
            if (MathF.Abs(left[index] - right[index]) + MathF.Abs(left[index + 1] - right[index + 1])
                + MathF.Abs(left[index + 2] - right[index + 2]) > .01f)
            {
                count++;
            }
        }
        return count;
    }

    private static double Energy(float[] values)
    {
        double sum = 0;
        int count = 0;
        for (int index = 0; index < values.Length; index += 4)
        {
            if (values[index + 3] > .5f)
            {
                sum += values[index] + values[index + 1] + values[index + 2];
                count += 3;
            }
        }
        return sum / Math.Max(1, count);
    }

    private static void WriteComparison(string output)
    {
        string[] names = ["direct", "diffuse", "reflection-refit", "off-raw-reflection", "off-refit", "moved-light"];
        string[] labels = ["Direct only", "Live single-bounce diffuse", "Local reflection refit",
            "Lamp off: captured reflections", "Lamp off: refit", "Moved blue lamp"];
        using var bitmap = new SKBitmap(Width * 3, (Height + 28) * 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(25, 28, 34));
        using var font = new SKFont(SKTypeface.Default, 14);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        for (int index = 0; index < names.Length; index++)
        {
            int x = index % 3 * Width;
            int y = index / 3 * (Height + 28);
            canvas.DrawText(labels[index], x + 8, y + 19, font, paint);
            using var panel = SKBitmap.Decode(Path.Combine(output, names[index] + ".png"));
            canvas.DrawBitmap(panel, x, y + 28);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(output, "comparison.png"));
        data.SaveTo(file);
    }

    private static float MaximumDifference(float[] left, float[] right)
    {
        return left.Zip(right, (a, b) => MathF.Abs(a - b)).Max();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
