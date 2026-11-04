using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Immutable resident descriptors and material/instance tables. The existing material owner owns images/samplers.</summary>
internal sealed unsafe class VulkanBindlessMaterials3D : IDisposable
{
    public const int TextureCapacity = 4096;
    public const int SamplerCapacity = 64;
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanFrameInFlightSet frames;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly AurelianVulkanBuffer[] uniforms = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly DescriptorSet[] sets = new DescriptorSet[VulkanFrameInFlightSet.Count];
    private DescriptorPool pool;
    public DescriptorSetLayout Layout { get; private set; }
    public DescriptorSet Set => sets[frames.Slot];
    public int TextureCount { get; private set; }
    public int SamplerCount { get; private set; }

    public VulkanBindlessMaterials3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, VulkanFrameInFlightSet frames,
        VulkanModel3DBatches owner, CompiledGraphicsProgram program, NativeInstanceSet3D source, bool visibility = false)
    {
        this.plant = plant;
        this.frames = frames;
        Validate(program, visibility);
        ValidateCapacity(source);
        try
        {
            var layoutProgram = visibility ? program with
            {
                Resources = program.Resources.Select(resource => resource with
                {
                    Visibility = [CompiledGraphicsStage.Vertex, CompiledGraphicsStage.Fragment],
                }).ToArray(),
            } : program;
            Layout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, layoutProgram);
            var textures = new List<AurelianVulkanTexture>();
            var samplers = new List<Sampler>();
            var textureIndices = new Dictionary<AurelianVulkanTexture, uint>();
            var samplerIndices = new Dictionary<Sampler, uint>();
            var materialIndices = source.Materials.Select((material, index) => (material, index))
                .ToDictionary(item => item.material, item => (uint)item.index);
            byte[] records = new byte[source.Materials.Length * 128];
            for (int index = 0; index < source.Materials.Length; index++)
            {
                ModelMaterial material = source.Materials[index];
                var channels = owner.GetChannels(material);
                Span<byte> record = records.AsSpan(index * 128, 128);
                Vector4[] values = [material.BaseColor,
                    new(material.Metallic, material.Roughness, material.NormalTexture is null ? 0 : material.NormalScale, material.OcclusionStrength),
                    new(material.Emissive, material.AlphaCutoff),
                    new(material.Unlit ? 1 : 0, material.AlphaMask ? 1 : 0, material.DoubleSided ? 1 : 0, 0),
                    new(material.SubsurfaceColor * material.SubsurfaceStrength, material.SubsurfaceStrength > 0 ? material.SubsurfaceRadius : 0)];
                MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(record);
                Span<uint> indices = MemoryMarshal.Cast<byte, uint>(record[80..]);
                for (int channel = 0; channel < 5; channel++)
                {
                    if (!textureIndices.TryGetValue(channels.Images[channel], out uint texture))
                    {
                        texture = (uint)textures.Count;
                        textureIndices.Add(channels.Images[channel], texture);
                        textures.Add(channels.Images[channel]);
                    }
                    if (!samplerIndices.TryGetValue(channels.Samplers[channel], out uint sampler))
                    {
                        sampler = (uint)samplers.Count;
                        samplerIndices.Add(channels.Samplers[channel], sampler);
                        samplers.Add(channels.Samplers[channel]);
                    }
                    indices[channel] = texture;
                    indices[5 + channel] = sampler;
                }
            }
            TextureCount = textures.Count;
            SamplerCount = samplers.Count;
            if (TextureCount > TextureCapacity || SamplerCount > SamplerCapacity)
                throw new InvalidDataException("AUR-BINDLESS-002: Resident texture or sampler table exceeds its declared capacity.");
            uint[] instanceMaterials = source.PackedInstances.Select(instance => materialIndices[instance.Material]).ToArray();
            var materialBuffer = Upload(records);
            var instanceBuffer = Upload(MemoryMarshal.AsBytes(instanceMaterials.AsSpan()).ToArray());
            DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[4]
            {
                new(DescriptorType.UniformBuffer, 2), new(DescriptorType.StorageBuffer, visibility ? 12u : 4u),
                new(DescriptorType.SampledImage, (uint)(TextureCapacity + (visibility ? 1 : 0)) * 2), new(DescriptorType.Sampler, SamplerCapacity * 2),
            };
            DescriptorPoolCreateInfo poolInfo = new() { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 2,
                PoolSizeCount = 4, PPoolSizes = sizes };
            Check(plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out pool));
            DescriptorImageInfo[] imageInfos = Enumerable.Range(0, TextureCapacity).Select(index =>
                new DescriptorImageInfo(default, textures[index < textures.Count ? index : 0].NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal)).ToArray();
            DescriptorImageInfo[] samplerInfos = Enumerable.Range(0, SamplerCapacity).Select(index =>
                new DescriptorImageInfo(samplers[index < samplers.Count ? index : 0], default, ImageLayout.Undefined)).ToArray();
            WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
            for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
            {
                uniforms[slot] = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 352,
                    VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "bindless.frame");
                buffers.Add(uniforms[slot]);
                DescriptorSetLayout layout = Layout;
                DescriptorSetAllocateInfo allocation = new() { SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &layout };
                DescriptorSet set;
                Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocation, &set));
                sets[slot] = set;
                WriteBuffer(set, 0, uniforms[slot], DescriptorType.UniformBuffer);
                WriteBuffer(set, 1, materialBuffer, DescriptorType.StorageBuffer);
                WriteBuffer(set, 2, instanceBuffer, DescriptorType.StorageBuffer);
                fixed (DescriptorImageInfo* images = imageInfos)
                fixed (DescriptorImageInfo* filters = samplerInfos)
                {
                    writes[0] = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 3,
                        DescriptorType = DescriptorType.SampledImage, DescriptorCount = TextureCapacity, PImageInfo = images };
                    writes[1] = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 4,
                        DescriptorType = DescriptorType.Sampler, DescriptorCount = SamplerCapacity, PImageInfo = filters };
                    plant.Vk.UpdateDescriptorSets(plant.Device, 2, writes, 0, null);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        AurelianVulkanBuffer Upload(byte[] bytes)
        {
            var result = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, (ulong)bytes.Length,
                VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly, "bindless.table"));
            if (!result.Success) throw new InvalidOperationException("AUR-BINDLESS-003: Table allocation failed.");
            var buffer = result.Buffer!;
            buffers.Add(buffer);
            using var uploader = new VulkanBufferUploader(plant, allocator, commands, fences);
            var upload = uploader.Upload(new(buffer, bytes, DebugName: "bindless.table"));
            if (!upload.Success) throw new InvalidOperationException("AUR-BINDLESS-003: Table upload failed.");
            return buffer;
        }
    }

    public void UpdateFrame(Matrix4x4 clip, Vector3 eye, float[] lighting, Matrix4x4 previousClip, Vector2 viewport = default)
    {
        float[] values = [.. Lighting3DUniforms.Rows(clip), eye.X, eye.Y, eye.Z, 1,
            .. new float[16], .. lighting, .. Lighting3DUniforms.Rows(previousClip)];
        values[20] = viewport.X;
        values[21] = viewport.Y;
        if (values.Length != 88 || !uniforms[frames.Slot].Write(MemoryMarshal.AsBytes(values.AsSpan())).Success)
            throw new InvalidOperationException("AUR-BINDLESS-003: Frame uniform upload failed.");
    }

    private void WriteBuffer(DescriptorSet set, uint binding, AurelianVulkanBuffer buffer, DescriptorType type)
    {
        DescriptorBufferInfo info = new(buffer.NativeBuffer, 0, buffer.SizeBytes);
        WriteDescriptorSet write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = binding,
            DescriptorType = type, DescriptorCount = 1, PBufferInfo = &info };
        plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
    }

    internal void BindVisibilityStorage(uint binding, AurelianVulkanBuffer buffer, int slot)
    {
        if (binding is < 5 or > 8) throw new ArgumentOutOfRangeException(nameof(binding));
        WriteBuffer(sets[slot], binding, buffer, DescriptorType.StorageBuffer);
    }

    internal void BindVisibilityImage(AurelianVulkanTexture image)
    {
        DescriptorImageInfo info = new(default, image.NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal);
        foreach (DescriptorSet set in sets)
        {
            WriteDescriptorSet write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 9,
                DescriptorType = DescriptorType.SampledImage, DescriptorCount = 1, PImageInfo = &info };
            plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
        }
    }

    private static void Validate(CompiledGraphicsProgram program, bool visibility)
    {
        var resources = program.Resources.OrderBy(resource => resource.Binding).ToArray();
        int resourceCount = visibility ? 10 : 5;
        if (program.Material?.Size != 352 || resources.Length != resourceCount || program.PixelTargets.Count != 5
            || !resources.Select(resource => resource.Binding).SequenceEqual(Enumerable.Range(0, resourceCount))
            || resources[1].StorageLayout is not { Stride: 128 } || resources[2].StorageLayout is not { Stride: 4 }
            || resources[3].DescriptorCount != TextureCapacity || resources[4].DescriptorCount != SamplerCapacity)
            throw new ArgumentException("AUR-BINDLESS-001: Program does not implement the admitted frame/material/table ABI.");
        if (visibility && (resources.Skip(5).Take(4).Any(resource => resource.Kind != CompiledGraphicsResourceKind.StorageBuffer)
            || resources[5].StorageLayout?.Stride != 64 || resources[6].StorageLayout?.Stride != 4
            || resources[7].StorageLayout?.Stride != 160 || resources[8].StorageLayout?.Stride != 4
            || resources[9].Type != "Texture2D<uint2>" || resources[9].Kind != CompiledGraphicsResourceKind.Texture2D))
            throw new ArgumentException("AUR-VBUFFER-001: Geometry, instance, command or identity resource ABI is invalid.");
        if (visibility) VulkanVisibilityBuffer3D.ValidateStorageLayouts(program);
        var expected = new List<CompiledStorageField>();
        foreach (string name in new[] { "baseColor", "factors", "emissiveAlpha", "flags", "subsurfaceProfile" })
        {
            foreach (char lane in "xyzw") expected.Add(new("surface." + name + "." + lane, "f32", expected.Count * 4));
        }
        foreach (string name in new[] { "baseTexture", "metalTexture", "normalTexture", "occlusionTexture", "emissiveTexture",
            "baseSampler", "metalSampler", "normalSampler", "occlusionSampler", "emissiveSampler", "reserved0", "reserved1" })
        {
            expected.Add(new(name, "u32", expected.Count * 4));
        }
        if (!resources[1].StorageLayout!.Fields.SequenceEqual(expected)
            || !resources[2].StorageLayout!.Fields.SequenceEqual([new CompiledStorageField("", "u32", 0)])
            || resources[0].Kind != CompiledGraphicsResourceKind.UniformBuffer
            || resources[1].Kind != CompiledGraphicsResourceKind.StorageBuffer || resources[2].Kind != CompiledGraphicsResourceKind.StorageBuffer
            || resources[3].Kind != CompiledGraphicsResourceKind.Texture2D || resources[4].Kind != CompiledGraphicsResourceKind.Sampler
            || resources.Any(resource => resource.Set != 0))
            throw new ArgumentException("AUR-BINDLESS-001: Material field order, transport types or resource kinds disagree with the native packer.");
        string[] frameFields = ["clipX", "clipY", "clipZ", "clipW", "eye", "baseColor", "factors", "emissiveAlpha", "flags",
            "light", "sun", "sky", "subsurfaceProfile", "shadowX", "shadowY", "shadowZ", "shadowW", "shadowParameters",
            "previousX", "previousY", "previousZ", "previousW"];
        if (!program.Material!.Fields.Select(field => field.Name).SequenceEqual(frameFields)
            || program.Material.Fields.Where((field, index) => field.PhysicalType != "float4" || field.Offset != index * 16 || field.Size != 16).Any())
            throw new ArgumentException("AUR-BINDLESS-001: Frame-uniform fields do not match the native frame packer.");
    }

    internal static void ValidateCapacity(NativeInstanceSet3D source)
    {
        var textures = new HashSet<(ModelTexture? Texture, bool Srgb, bool FlatNormal)>();
        var samplers = new HashSet<ModelSampler>();
        foreach (var material in source.Materials)
        {
            int channel = 0;
            foreach (var binding in material.Textures())
            {
                textures.Add((binding?.Texture, channel is 0 or 4, binding is null && channel == 2));
                samplers.Add(binding?.Sampler ?? new ModelSampler());
                channel++;
            }
        }
        if (textures.Count > TextureCapacity || samplers.Count > SamplerCapacity)
            throw new InvalidDataException("AUR-BINDLESS-002: Resident descriptor capacity is exceeded before GPU admission.");
    }

    private static void Check(Result result)
    {
        if (result != Result.Success) throw new InvalidOperationException("AUR-BINDLESS-003: " + result);
    }

    public void Dispose()
    {
        if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        if (Layout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, Layout, null);
        foreach (var buffer in buffers) buffer.Dispose();
    }
}
