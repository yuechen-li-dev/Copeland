using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>
/// Experimental single-bounce diffuse lighting for a fixed, bounded triangle scene.
/// Hardware visibility, surface shading and probe reconstruction remain GPU resident.
/// Batches synchronously complete on the existing graphics queue; this is not async compute.
/// </summary>
public sealed unsafe class VulkanDiffuseProbeVolume : IDisposable
{
    private const int RayCapacity = 65536;
    private const float RayRange = 32;
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly VulkanTextureUploader uploader;
    private readonly Stack<IDisposable> owned = new();
    private readonly VulkanRayQueryScene queries;
    private readonly DiffuseProbeTriangle[] triangles;
    private readonly Vector3[] samplePositions;
    private readonly AurelianVulkanTexture surfaces;
    private readonly AurelianVulkanTexture directions;
    private readonly AurelianVulkanTexture[] caches;
    private readonly AurelianVulkanTexture[] irradiance;
    private readonly AurelianVulkanTexture[] visibility;
    private readonly VulkanPostProcess3D[] cachePasses;
    private readonly VulkanPostProcess3D[] probePasses;
    private readonly AurelianVulkanBuffer fullscreen;
    private readonly Vulkan3DGpuTimings timings;
    private int cacheIndex;
    private int probeIndex;
    private int cacheCursor;
    private int probeCursor;
    private DiffuseProbeLight light;
    private bool disposed;

    public VulkanDiffuseProbeVolume(AurelianVulkanPlant plant, DiffuseProbeGrid grid,
        IReadOnlyList<DiffuseProbeTriangle> scene, DiffuseProbeLight light, byte[] rayQuerySpirv,
        CompiledGraphicsProgram cacheProgram, CompiledGraphicsProgram probeProgram)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(scene);
        grid.Validate();
        light.Validate();
        if (scene.Count is < 1 or > 64)
        {
            throw new ArgumentException("AUR-PROBE-004: This experiment admits 1 to 64 constant-material triangles.");
        }
        triangles = scene.ToArray();
        if (scene.Count * grid.SurfaceResolution * grid.SurfaceResolution > 16384)
        {
            throw new ArgumentException("AUR-PROBE-004: Surface atlas exceeds the bounded texture height.");
        }
        foreach (var triangle in triangles)
        {
            triangle.Validate();
        }
        this.plant = plant;
        this.light = light;
        Grid = grid;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        uploader = new(plant, allocator, commands, fences);
        try
        {
            queries = Own(new VulkanRayQueryScene(plant, rayQuerySpirv,
                triangles.Select(t => new RayQueryTriangle(t.A, t.B, t.C)).ToArray(), capacity: RayCapacity));
            int size = grid.SurfaceResolution;
            int cells = triangles.Length * size * size;
            samplePositions = new Vector3[cells];
            float[] surfaceWords = new float[cells * 16];
            for (int index = 0; index < cells; index++)
            {
                var triangle = triangles[index / (size * size)];
                float b = (float)(index % size) / (size - 1);
                float c = (float)(index / size % size) / (size - 1);
                if (b + c > 1)
                {
                    b = 1 - b;
                    c = 1 - c;
                }
                Vector3 point = triangle.A * (1 - b - c) + triangle.B * b + triangle.C * c;
                // Avoid shared triangle edges during direct-visibility queries.
                point = Vector3.Lerp(point, (triangle.A + triangle.B + triangle.C) / 3, .0001f);
                samplePositions[index] = point + triangle.Normal * .002f;
                Store(surfaceWords, index * 16, point);
                Store(surfaceWords, index * 16 + 4, triangle.Normal);
                Store(surfaceWords, index * 16 + 8, triangle.Albedo);
                Store(surfaceWords, index * 16 + 12, triangle.Emission);
            }
            surfaces = Data(4, cells, "probe.surfaces", surfaceWords);
            float[] directionWords = new float[grid.RaysPerProbe * 4];
            for (int index = 0; index < grid.RaysPerProbe; index++)
            {
                Store(directionWords, index * 4, Direction(index, grid.RaysPerProbe));
            }
            directions = Data(1, grid.RaysPerProbe, "probe.directions", directionWords);
            caches = [Target(size, size * triangles.Length, "probe.cache.0"), Target(size, size * triangles.Length, "probe.cache.1")];
            irradiance = [Target(8, grid.Count * 8, "probe.irradiance.0"), Target(8, grid.Count * 8, "probe.irradiance.1")];
            visibility = [Target(8, grid.Count * 8, "probe.visibility.0"), Target(8, grid.Count * 8, "probe.visibility.1")];
            cachePasses = new VulkanPostProcess3D[2];
            probePasses = new VulkanPostProcess3D[2];
            for (int index = 0; index < 2; index++)
            {
                cachePasses[index] = Own(new VulkanPostProcess3D(plant, allocator, cacheProgram, caches[index], 3, Filter.Nearest));
                probePasses[index] = Own(new VulkanPostProcess3D(plant, allocator, probeProgram, irradiance[index], 5,
                    Filter.Nearest, visibility[index]));
            }
            fullscreen = Own(VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 24,
                VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "probe.fullscreen"));
            float[] vertices = [-1, -1, 3, -1, -1, 3];
            Require(fullscreen.Write(MemoryMarshal.AsBytes(vertices.AsSpan())).Success, "Probe vertices upload failed.");
            timings = Own(new Vulkan3DGpuTimings(plant, ["diffuse-reconstruction"]));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public DiffuseProbeGrid Grid { get; }
    public int Generation { get; private set; } = 1;
    public int UpdateCount { get; private set; }
    public int SurfaceShadingCount { get; private set; }
    public int ProbeRayCount { get; private set; }
    public double LastGpuUpdateMilliseconds { get; private set; }
    public double LastGpuPassMilliseconds { get; private set; }
    public bool Ready => !disposed && cacheCursor == samplePositions.Length && probeCursor == Grid.Count;
    public AurelianVulkanTexture Irradiance => irradiance[probeIndex];
    public AurelianVulkanTexture Visibility => visibility[probeIndex];
    public AurelianVulkanTexture RadianceCache => caches[cacheIndex];
    public AurelianVulkanTexture SurfaceData => surfaces;

    /// <summary>Diagnostic input upload owned by this experiment. Runtime updates do not call this method.</summary>
    public AurelianVulkanTexture UploadInspectionData(int width, int height, float[] words)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (width < 1 || height < 1 || width > 512 || height > 4096 || words.Length != width * height * 4
            || words.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException("Invalid diagnostic texture.");
        }
        return Data(width, height, "probe.diagnostic", words);
    }

    public void SetLight(DiffuseProbeLight replacement)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        replacement.Validate();
        if (replacement == light)
        {
            return;
        }
        if (Generation >= 16_000_000)
        {
            throw new InvalidOperationException("AUR-PROBE-005: Generation exhausted; recreate the volume.");
        }
        light = replacement;
        Generation++;
        cacheCursor = 0;
        probeCursor = 0;
    }

    /// <summary>At most one cache or probe batch per tick. Stable completed scenes require no additional work.</summary>
    public void Update(int maximumSurfaceCells = 256, int maximumProbes = 32)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (maximumSurfaceCells < 1 || maximumSurfaceCells > RayCapacity || maximumProbes < 1
            || maximumProbes > RayCapacity / Grid.RaysPerProbe)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSurfaceCells));
        }
        if (Ready)
        {
            return;
        }
        if (cacheCursor < samplePositions.Length)
        {
            int count = Math.Min(maximumSurfaceCells, samplePositions.Length - cacheCursor);
            var rays = new RayQueryRequest[count];
            for (int index = 0; index < count; index++)
            {
                Vector3 position = samplePositions[cacheCursor + index];
                Vector3 delta = light.Position - position;
                float distance = delta.Length();
                if (distance < .004f)
                {
                    throw new ArgumentException("AUR-PROBE-006: A point light is too close to a sampled surface.");
                }
                rays[index] = new(position, delta / distance, Math.Max(.001f, distance - .002f));
            }
            var hitTexture = queries.TraceTexture(rays);
            int next = 1 - cacheIndex;
            cachePasses[next].Configure(Uniforms(cacheCursor, count), surfaces, hitTexture, caches[cacheIndex]);
            Run(cachePasses[next]);
            cacheIndex = next;
            cacheCursor += count;
            SurfaceShadingCount += count;
        }
        else
        {
            int count = Math.Min(maximumProbes, Grid.Count - probeCursor);
            var rays = new RayQueryRequest[count * Grid.RaysPerProbe];
            for (int probe = 0; probe < count; probe++)
            {
                for (int ray = 0; ray < Grid.RaysPerProbe; ray++)
                {
                    rays[probe * Grid.RaysPerProbe + ray] = new(Grid.Position(probeCursor + probe),
                        Direction(ray, Grid.RaysPerProbe), RayRange);
                }
            }
            var hitTexture = queries.TraceTexture(rays);
            int next = 1 - probeIndex;
            probePasses[next].Configure(Uniforms(probeCursor, count), caches[cacheIndex], hitTexture,
                directions, irradiance[probeIndex], visibility[probeIndex]);
            probePasses[next].SetLinearInput(0, caches[cacheIndex]);
            Run(probePasses[next]);
            probeIndex = next;
            probeCursor += count;
            ProbeRayCount += rays.Length;
        }
        LastGpuUpdateMilliseconds = queries.LastGpuMilliseconds + LastGpuPassMilliseconds;
        UpdateCount++;
    }

    public float[] Uniforms(int start = 0, int count = 0)
    {
        return [Grid.SurfaceResolution, triangles.Length, RayCapacity, Generation,
            start, count, Ready ? 1 : 0, 0,
            light.Position.X, light.Position.Y, light.Position.Z, light.Intensity,
            light.Color.X, light.Color.Y, light.Color.Z, 0,
            Grid.Minimum.X, Grid.Minimum.Y, Grid.Minimum.Z, 0,
            Grid.Maximum.X, Grid.Maximum.Y, Grid.Maximum.Z, 0,
            Grid.X, Grid.Y, Grid.Z, Grid.RaysPerProbe,
            0, 0, 0, RayRange];
    }

    public AurelianVulkanTexture TraceView(IReadOnlyList<RayQueryRequest> rays)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return queries.TraceTexture(rays);
    }

    /// <summary>Compile eight exact hardware visibility connections for authored receiver sites.
    /// The returned GPU mask is tied to these sites and this generation; it is not a general visibility field.</summary>
    public AurelianVulkanTexture CompileConnections(CompiledGraphicsProgram program,
        IReadOnlyList<(Vector3 Point, Vector3 Normal)> sites)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sites.Count is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(sites));
        }
        var rays = new List<RayQueryRequest>();
        foreach (var site in sites)
        {
            if (!DiffuseProbeGrid.Finite(site.Point) || !DiffuseProbeGrid.Finite(site.Normal)
                || MathF.Abs(site.Normal.LengthSquared() - 1) > .0001f)
            {
                throw new ArgumentException("Connection sites require finite points and unit normals.");
            }
            Vector3 origin = site.Point + site.Normal * .03f;
            foreach (int corner in Grid.Corners(site.Point, site.Normal))
            {
                Vector3 delta = Grid.Position(corner) - origin;
                float distance = delta.Length();
                if (distance < .004f)
                {
                    throw new ArgumentException("A connection site coincides with a probe.");
                }
                rays.Add(new(origin, delta / distance, distance - .002f));
            }
        }
        var hits = queries.TraceTexture(rays);
        var result = Target(1, sites.Count, "probe.connections");
        using var pass = new VulkanPostProcess3D(plant, allocator, program, result, 1, Filter.Nearest);
        pass.Configure(Uniforms(0, sites.Count), hits);
        Run(pass);
        return result;
    }

    /// <summary>Qualification readback only. Never called by Update or the lighting decoder.</summary>
    public float[] Inspect(AurelianVulkanTexture texture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!caches.Contains(texture) && !irradiance.Contains(texture) && !visibility.Contains(texture))
        {
            throw new ArgumentException("Only this volume's textures may be inspected.");
        }
        return ReadTexture(texture);
    }

    /// <summary>Diagnostic evaluation through the real reusable GPU decoder. Not a game presentation API.</summary>
    public float[] InspectResponse(CompiledGraphicsProgram program, int width, int height,
        float[] parameters, params AurelianVulkanTexture[] inputs)
    {
        return InspectResponseCore(program, width, height, parameters, null, inputs);
    }

    public float[] InspectResponseWithLinearInput(CompiledGraphicsProgram program, int width, int height,
        float[] parameters, int linearInput, params AurelianVulkanTexture[] inputs)
    {
        return InspectResponseCore(program, width, height, parameters, linearInput, inputs);
    }

    private float[] InspectResponseCore(CompiledGraphicsProgram program, int width, int height,
        float[] parameters, int? linearInput, AurelianVulkanTexture[] inputs)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (width < 1 || height < 1 || width > 512 || height > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        using var texture = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator,
            (uint)width, (uint)height, VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource
                | VulkanTextureUsage.TransferSource, VulkanMemoryUsage.GpuOnly, "probe.response", VulkanTextureFormat.Rgba32Float);
        using var pass = new VulkanPostProcess3D(plant, allocator, program, texture, inputs.Length, Filter.Nearest);
        pass.Configure(parameters, inputs);
        if (linearInput is { } index)
        {
            if (index < 0 || index >= inputs.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(linearInput));
            }
            pass.SetLinearInput(index, inputs[index]);
        }
        Run(pass);
        return ReadTexture(texture);
    }

    private float[] ReadTexture(AurelianVulkanTexture texture)
    {
        int bytes = checked((int)(texture.Width * texture.Height * 16));
        using var readback = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, (ulong)bytes,
            VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "probe.inspection");
        var command = Begin();
        Transition(command, texture, VulkanResourceLayout.TransferSource);
        BufferImageCopy region = new()
        {
            ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new(texture.Width, texture.Height, 1),
        };
        plant.Vk.CmdCopyImageToBuffer(command.CommandBuffer, texture.NativeImage, ImageLayout.TransferSrcOptimal,
            readback.NativeBuffer, 1, &region);
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit,
            0, 1, &barrier, 0, null, 0, null);
        Transition(command, texture, VulkanResourceLayout.ShaderResourceFragment);
        Submit(command);
        return MemoryMarshal.Cast<byte, float>(readback.ReadBytes(bytes)).ToArray();
    }

    public static Vector3 Direction(int index, int count)
    {
        float y = 1 - 2 * ((index + .5f) / count);
        float radius = MathF.Sqrt(MathF.Max(0, 1 - y * y));
        float angle = index * 2.39996323f;
        return new(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
    }

    private void Run(VulkanPostProcess3D pass)
    {
        var command = Begin();
        timings.Reset(command);
        timings.Mark(command, 0);
        pass.Record(command, fullscreen);
        timings.Mark(command, 1);
        Submit(command);
        LastGpuPassMilliseconds = timings.Read().Sum(time => time.Milliseconds);
    }

    private VulkanCommandBufferLease Begin()
    {
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Probe command begin failed.");
        return command;
    }

    private void Submit(VulkanCommandBufferLease command)
    {
        Require(command.End().Success, "Probe command end failed.");
        Require(submitter.Submit(new(command, WaitForCompletion: true, DebugName: "diffuse-probe")).Success,
            "Probe submit failed.");
    }

    private AurelianVulkanTexture Target(int width, int height, string name)
    {
        var texture = Data(width, height, name, new float[width * height * 4], renderTarget: true);
        return texture;
    }

    private AurelianVulkanTexture Data(int width, int height, string name, float[] words, bool renderTarget = false)
    {
        var usage = VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource;
        if (renderTarget)
        {
            usage |= VulkanTextureUsage.ColorAttachment;
        }
        var texture = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator,
            (uint)width, (uint)height, usage, VulkanMemoryUsage.GpuOnly, name, VulkanTextureFormat.Rgba32Float));
        Require(uploader.Upload(new(texture, MemoryMarshal.AsBytes(words.AsSpan()).ToArray(), name)).Success,
            "Probe data upload failed.");
        return texture;
    }

    private void Transition(VulkanCommandBufferLease command, AurelianVulkanTexture texture, VulkanResourceLayout layout)
    {
        var transition = texture.LayoutTracker.Transition("probe.inspection", 0, 0, layout);
        Require(transition.Success, "Probe texture transition failed.");
        if (transition.Plan is not null)
        {
            Require(VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(texture, transition.Plan)]).Success,
                "Probe barrier failed.");
        }
    }

    private T Own<T>(T value) where T : IDisposable
    {
        owned.Push(value);
        return value;
    }

    private static void Store(float[] words, int offset, Vector3 value)
    {
        words[offset] = value.X;
        words[offset + 1] = value.Y;
        words[offset + 2] = value.Z;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        _ = plant.Vk.QueueWaitIdle(plant.GraphicsQueue);
        foreach (var resource in owned)
        {
            resource.Dispose();
        }
        uploader.Dispose();
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
