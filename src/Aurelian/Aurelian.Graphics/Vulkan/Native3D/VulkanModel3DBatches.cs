using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Material draws inside the solid renderer's existing color/depth pass. Resources persist between frames.</summary>
internal sealed unsafe class VulkanModel3DBatches : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanTextureUploader uploader;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanFenceBundle fences;
    private readonly DescriptorSetLayout layout;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly Dictionary<(ModelTexture Texture, bool Srgb), AurelianVulkanTexture> textures = [];
    private readonly Dictionary<ModelSampler, Sampler> samplers = [];
    private readonly Dictionary<ModelMaterial, Surface> surfaces = [];
    private AurelianVulkanBuffer? vertices;
    private AurelianVulkanBuffer? previousVertices;
    private NativeModel3DVertex[] previousPacked = [];
    private string[] previousIdentities = [];
    private readonly bool temporal;
    private readonly int uniformBytes;
    private readonly AurelianVulkanTexture shadowMap;
    private readonly Sampler shadowSampler;
    private bool disposed;
    private static readonly ModelSampler DefaultSampler = new();
    private static readonly ModelTexture White = new("builtin-white", 1, 1, ImmutableArray.Create<byte>(255, 255, 255, 255));
    private static readonly ModelTexture FlatNormal = new("builtin-flat-normal", 1, 1, ImmutableArray.Create<byte>(128, 128, 255, 255));

    private sealed record Surface(AurelianVulkanBuffer Uniform, DescriptorPool Pool, DescriptorSet Set);

    public VulkanModel3DBatches(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool pool, VulkanFenceBundle fences, AurelianVulkanRenderPass pass, CompiledGraphicsProgram program, AurelianVulkanTexture shadowMap, Sampler shadowSampler)
    {
        Validate(program);
        temporal = program.VertexInputs.Count == 6;
        uniformBytes = program.Material!.Size;
        this.plant = plant;
        this.allocator = allocator;
        this.shadowMap = shadowMap;
        this.shadowSampler = shadowSampler;
        uploader = new(plant, allocator, pool, fences);
        commands = pool;
        this.fences = fences;
        try
        {
            layout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            var descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(program.Shaders,
                temporal ? [new(0, 64), new(1, 64)] : [new VulkanVertexBufferLayoutDescriptor(0, 64)],
                [new(0, 0, VulkanVertexAttributeFormat.Float3, 0), new(1, 0, VulkanVertexAttributeFormat.Float3, 12),
                    new(2, 0, VulkanVertexAttributeFormat.Float4, 24), new(3, 0, VulkanVertexAttributeFormat.Float2, 40),
                    new(4, 0, VulkanVertexAttributeFormat.Float4, 48),
                    .. temporal ? new VulkanVertexAttributeDescriptor[] { new(5, 1, VulkanVertexAttributeFormat.Float3, 0) } : []], enableDepthTest: true, enableDepthWrite: true);
            Require(descriptor.Success, "Static model pipeline descriptor rejected.");
            var result = VulkanGraphicsPipelineFactory.Create(plant, pass, descriptor.Descriptor!, [layout]);
            Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            pipeline = result.Pipeline!;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Prepare(StaticModel model)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (ModelMaterial material in model.Occurrences.Select(item => item.Primitive.Material).Distinct()) GetSurface(material);
    }

    public void Upload(IReadOnlyList<NativeModel3DBatch> batches, Matrix4x4 clip, Vector3 eye, float[] lighting, Matrix4x4 previousClip, bool resetHistory)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int count = batches.Sum(item => item.Vertices.Length);
        if (count == 0)
        {
            TrimResources([]);
            return;
        }
        if (count > 1_000_000 || batches.Any(item => item.Vertices.Length == 0 || item.Vertices.Length % 3 != 0))
            throw new InvalidDataException("Model draws need complete triangles, within the one-million vertex frame budget.");
        foreach (NativeModel3DBatch batch in batches)
        {
            foreach (NativeModel3DVertex vertex in batch.Vertices)
            {
                if (!Finite(vertex.Position) || !Finite(vertex.Normal) || !Finite(vertex.Color) || !Finite(vertex.Tangent)
                    || !float.IsFinite(vertex.Uv.X) || !float.IsFinite(vertex.Uv.Y))
                    throw new InvalidDataException("Model draw vertices must be finite.");
            }
            Surface surface = GetSurface(batch.Material);
            ModelMaterial m = batch.Material;
            float[] data = [clip.M11, clip.M21, clip.M31, clip.M41, clip.M12, clip.M22, clip.M32, clip.M42,
                clip.M13, clip.M23, clip.M33, clip.M43, clip.M14, clip.M24, clip.M34, clip.M44,
                eye.X, eye.Y, eye.Z, 1, m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z, m.BaseColor.W,
                m.Metallic, m.Roughness, m.NormalTexture is null ? 0 : m.NormalScale, m.OcclusionStrength,
                m.Emissive.X, m.Emissive.Y, m.Emissive.Z, m.AlphaCutoff,
                m.Unlit ? 1 : 0, m.AlphaMask ? 1 : 0, m.DoubleSided ? 1 : 0, 0, .. lighting,
                .. temporal ? Lighting3DUniforms.Rows(previousClip) : []];
            if (data.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Model camera and factors must be finite.");
            Require(surface.Uniform.Write(MemoryMarshal.AsBytes(data.AsSpan())).Success, "Model uniform upload failed.");
        }
        TrimResources(batches.Select(item => item.Material));
        ulong bytes = (ulong)count * 64;
        if (vertices is null || vertices.SizeBytes < bytes)
        {
            var candidate = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator,
                bytes, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "static-model.vertices");
            vertices?.Dispose();
            vertices = candidate;
        }
        NativeModel3DVertex[] packed = batches.SelectMany(item => item.Vertices).ToArray();
        Require(vertices.Write(MemoryMarshal.AsBytes(packed.AsSpan())).Success, "Model vertex upload failed.");
        if (temporal)
        {
            if (previousVertices is null || previousVertices.SizeBytes < bytes)
            {
                previousVertices?.Dispose();
                previousVertices = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator,
                    bytes, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "static-model.previous");
            }
            string[] identities = batches.Select(batch => batch.TemporalIdentity ?? (batch.Material.Slot + ":" + batch.Vertices.Length)).ToArray();
            bool correspondence = !resetHistory && previousPacked.Length == packed.Length && identities.SequenceEqual(previousIdentities);
            var previous = correspondence ? previousPacked : packed;
            Require(previousVertices.Write(MemoryMarshal.AsBytes(previous.AsSpan())).Success, "Previous model upload failed.");
            previousPacked = packed;
            previousIdentities = identities;
        }
    }

    public void Draw(VulkanCommandBufferLease command, VulkanRenderPassScope scope,
        AurelianVulkanFramebuffer framebuffer, IReadOnlyList<NativeModel3DBatch> batches)
    {
        uint first = 0;
        foreach (NativeModel3DBatch batch in batches)
        {
            DescriptorSet set = surfaces[batch.Material].Set;
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
                pipeline.NativePipelineLayout, 0, 1, &set, 0, null);
            var result = new VulkanDrawCommandEncoder().DrawVertices(plant, command, scope,
                new(pipeline, vertices!, (uint)batch.Vertices.Length, first, VulkanViewportScissor.FromFramebuffer(framebuffer))
                { PreviousVertexBuffer = temporal ? previousVertices : null });
            Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            first += (uint)batch.Vertices.Length;
        }
    }

    public void DrawShadows(VulkanCommandBufferLease command, VulkanRenderPassScope scope,
        Vulkan3DPass pass, IReadOnlyList<NativeModel3DBatch> batches)
    {
        uint first = 0;
        foreach (NativeModel3DBatch batch in batches)
        {
            // Cutout silhouettes need a material-aware shadow shader; do not cast an incorrect solid shadow.
            if (!batch.Material.AlphaMask)
            {
                pass.Draw(command, scope, vertices!, (uint)batch.Vertices.Length, model: true, first: first);
            }
            first += (uint)batch.Vertices.Length;
        }
    }

    private Surface GetSurface(ModelMaterial material)
    {
        if (surfaces.TryGetValue(material, out Surface? found)) return found;
        material.Validate();
        ModelTextureBinding?[] channels = material.Textures().ToArray();
        var images = new AurelianVulkanTexture[5];
        var filters = new Sampler[5];
        for (int index = 0; index < channels.Length; index++)
        {
            ModelTextureBinding? binding = channels[index];
            ModelTexture image = binding?.Texture ?? (index == 2 ? FlatNormal : White);
            bool srgb = index is 0 or 4;
            if (!textures.TryGetValue((image, srgb), out AurelianVulkanTexture? texture))
            {
                texture = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, (uint)image.Width, (uint)image.Height,
                    VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination | VulkanTextureUsage.TransferSource, VulkanMemoryUsage.GpuOnly,
                    "static-model.texture", srgb ? VulkanTextureFormat.Rgba8Srgb : VulkanTextureFormat.Rgba8Unorm,
                    (uint)(1 + System.Numerics.BitOperations.Log2((uint)Math.Max(image.Width, image.Height))));
                var upload = uploader.Upload(new(texture, image.Rgba.ToArray(), "static-model.upload"));
                if (!upload.Success)
                {
                    texture.Dispose();
                    throw new InvalidOperationException(string.Join("; ", upload.Diagnostics.Select(item => item.Message)));
                }
                try
                {
                    VulkanTextureMipGenerator.Generate(plant, commands, fences, texture);
                    textures.Add((image, srgb), texture);
                }
                catch
                {
                    texture.Dispose();
                    throw;
                }
            }
            images[index] = texture;
            filters[index] = GetSampler(binding?.Sampler ?? DefaultSampler);
        }
        var uniform = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, (ulong)uniformBytes,
            VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "static-model.material");
        DescriptorPool pool = default;
        try
        {
            DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3]
            {
                new(DescriptorType.UniformBuffer, 1),
                new(DescriptorType.SampledImage, 6),
                new(DescriptorType.Sampler, 6),
            };
            DescriptorPoolCreateInfo info = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 1,
                PoolSizeCount = 3,
                PPoolSizes = sizes,
            };
            Require(plant.Vk.CreateDescriptorPool(plant.Device, &info, null, out pool) == Result.Success, "Model descriptor pool failed.");
            DescriptorSetLayout setLayout = layout;
            DescriptorSetAllocateInfo allocate = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout,
            };
            Require(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, out DescriptorSet set) == Result.Success, "Model descriptor allocation failed.");
            DescriptorBufferInfo buffer = new(uniform.NativeBuffer, 0, (ulong)uniformBytes);
            WriteDescriptorSet uniformWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &buffer,
            };
            plant.Vk.UpdateDescriptorSets(plant.Device, 1, &uniformWrite, 0, null);
            WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
            for (uint index = 0; index < images.Length; index++)
            {
                DescriptorImageInfo image = new(default, images[index].NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal);
                DescriptorImageInfo sampler = new(filters[index], default, ImageLayout.Undefined);
                writes[0] = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set,
                    DstBinding = 1 + index * 2,
                    DescriptorType = DescriptorType.SampledImage,
                    DescriptorCount = 1,
                    PImageInfo = &image,
                };
                writes[1] = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set,
                    DstBinding = 2 + index * 2,
                    DescriptorType = DescriptorType.Sampler,
                    DescriptorCount = 1,
                    PImageInfo = &sampler,
                };
                plant.Vk.UpdateDescriptorSets(plant.Device, 2, writes, 0, null);
            }
            Vulkan3DPass.WriteImage(plant, set, 11, shadowMap, shadowSampler);
            var surface = new Surface(uniform, pool, set);
            surfaces.Add(material, surface);
            return surface;
        }
        catch
        {
            if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
            uniform.Dispose();
            throw;
        }
    }

    private Sampler GetSampler(ModelSampler definition)
    {
        if (samplers.TryGetValue(definition, out Sampler sampler)) return sampler;
        plant.Vk.GetPhysicalDeviceFeatures(plant.PhysicalDevice, out PhysicalDeviceFeatures features);
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        bool anisotropic = features.SamplerAnisotropy && definition.LinearMin && definition.LinearMag;
        SamplerCreateInfo info = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MinFilter = definition.LinearMin ? Filter.Linear : Filter.Nearest,
            MagFilter = definition.LinearMag ? Filter.Linear : Filter.Nearest,
            MipmapMode = definition.LinearMin ? SamplerMipmapMode.Linear : SamplerMipmapMode.Nearest,
            AddressModeU = Address(definition.WrapU),
            AddressModeV = Address(definition.WrapV),
            AddressModeW = SamplerAddressMode.Repeat,
            MaxLod = 13,
            AnisotropyEnable = anisotropic,
            MaxAnisotropy = anisotropic ? Math.Min(properties.Limits.MaxSamplerAnisotropy, 8) : 1,
        };
        Require(plant.Vk.CreateSampler(plant.Device, &info, null, out sampler) == Result.Success, "Model sampler creation failed.");
        samplers.Add(definition, sampler);
        return sampler;
    }

    private void TrimResources(IEnumerable<ModelMaterial> active)
    {
        var materials = active.ToHashSet();
        foreach (ModelMaterial key in surfaces.Keys.Where(key => !materials.Contains(key)).ToArray())
        {
            Surface surface = surfaces[key];
            plant.Vk.DestroyDescriptorPool(plant.Device, surface.Pool, null);
            surface.Uniform.Dispose();
            surfaces.Remove(key);
        }
        var images = new HashSet<(ModelTexture Texture, bool Srgb)>();
        var filters = new HashSet<ModelSampler>();
        foreach (ModelMaterial material in materials)
        {
            ModelTextureBinding?[] channels = material.Textures().ToArray();
            for (int index = 0; index < channels.Length; index++)
            {
                ModelTextureBinding? binding = channels[index];
                images.Add((binding?.Texture ?? (index == 2 ? FlatNormal : White), index is 0 or 4));
                filters.Add(binding?.Sampler ?? DefaultSampler);
            }
        }
        foreach (var key in textures.Keys.Where(key => !images.Contains(key)).ToArray())
        {
            textures[key].Dispose();
            textures.Remove(key);
        }
        foreach (ModelSampler key in samplers.Keys.Where(key => !filters.Contains(key)).ToArray())
        {
            plant.Vk.DestroySampler(plant.Device, samplers[key], null);
            samplers.Remove(key);
        }
    }

    private static SamplerAddressMode Address(TextureWrap wrap) => wrap switch
    {
        TextureWrap.Clamp => SamplerAddressMode.ClampToEdge,
        TextureWrap.Mirror => SamplerAddressMode.MirroredRepeat,
        _ => SamplerAddressMode.Repeat,
    };

    private static void Validate(CompiledGraphicsProgram program)
    {
        string[] fields = ["clipX", "clipY", "clipZ", "clipW", "eye", "baseColor", "factors", "emissiveAlpha", "flags", "light", "sun", "sky", "ground",
            "shadowX", "shadowY", "shadowZ", "shadowW", "shadowParameters"];
        string[] inputs = ["float3", "float3", "float4", "float2", "float4"];
        bool temporal = program.VertexInputs.Count == 6;
        if (temporal)
        {
            fields = [.. fields, "previousX", "previousY", "previousZ", "previousW"];
            inputs = [.. inputs, "float3"];
        }
        bool valid = program.Material is { Binding: 0, Set: 0 } material && material.Size == fields.Length * 16
            && material.Fields.Select(item => item.Name).SequenceEqual(fields)
            && material.Fields.All(item => item.PhysicalType == "float4")
            && material.Fields.Select(item => item.Offset).SequenceEqual(Enumerable.Range(0, fields.Length).Select(index => index * 16))
            && program.VertexInputs.OrderBy(item => item.Location).Select(item => item.PhysicalType).SequenceEqual(inputs)
            && program.VertexInputs.Select(item => item.Location).Order().SequenceEqual(Enumerable.Range(0, inputs.Length))
            && program.Resources.Count == 13;
        foreach (CompiledGraphicsResource resource in program.Resources)
        {
            CompiledGraphicsResourceKind kind = CompiledGraphicsResourceKind.Sampler;
            if (resource.Binding == 0)
                kind = CompiledGraphicsResourceKind.UniformBuffer;
            else if (resource.Binding % 2 == 1)
                kind = CompiledGraphicsResourceKind.Texture2D;
            valid &= resource.Set == 0 && resource.Binding is >= 0 and <= 12 && resource.Kind == kind;
            CompiledGraphicsStage[] visibility = resource.Binding == 0
                ? [CompiledGraphicsStage.Vertex, CompiledGraphicsStage.Fragment] : [CompiledGraphicsStage.Fragment];
            valid &= resource.Visibility.Order().SequenceEqual(visibility.Order());
        }
        valid &= program.Resources.Select(item => item.Binding).Order().SequenceEqual(Enumerable.Range(0, 13));
        if (!valid) throw new ArgumentException("StaticModel3D requires packed material uniforms (288 bytes, or 352 with previous positions), matching vertex streams and thirteen bindings.", nameof(program));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pipeline?.Dispose();
        foreach (Surface surface in surfaces.Values)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, surface.Pool, null);
            surface.Uniform.Dispose();
        }
        foreach (Sampler sampler in samplers.Values)
        {
            plant.Vk.DestroySampler(plant.Device, sampler, null);
        }
        foreach (AurelianVulkanTexture texture in textures.Values)
        {
            texture.Dispose();
        }
        previousVertices?.Dispose();
        vertices?.Dispose();
        if (layout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, layout, null);
        uploader.Dispose();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
