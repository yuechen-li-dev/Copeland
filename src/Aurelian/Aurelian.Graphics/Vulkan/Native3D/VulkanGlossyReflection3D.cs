using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Reconstructs the Fresnel-weighted glossy signal before surface lighting consumes it.</summary>
internal sealed class VulkanGlossyReflection3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly Stack<IDisposable> owned = new();
    private readonly AurelianVulkanTexture[] history = new AurelianVulkanTexture[2];
    private readonly AurelianVulkanTexture[] guides = new AurelianVulkanTexture[2];
    private readonly VulkanPostProcess3D[] passes = new VulkanPostProcess3D[2];
    private Matrix4x4 previousInverse = Matrix4x4.Identity;
    private Vector3 previousEye;
    private bool initialized;
    private bool valid;
    private int writeIndex;

    public AurelianVulkanTexture Output => history[1 - writeIndex];

    public VulkanGlossyReflection3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanFrameInFlightSet frames, CompiledGraphicsProgram program, uint width, uint height)
    {
        this.plant = plant;
        try
        {
            for (int index = 0; index < history.Length; index++)
            {
                history[index] = Image("glossy-reflection.history", VulkanTextureFormat.Rgba16Float);
                guides[index] = Image("glossy-reflection.guide", VulkanTextureFormat.Rgba32Float);
                passes[index] = Own(new VulkanPostProcess3D(plant, allocator, program, history[index],
                    8, Filter.Nearest, guides[index], frames));
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        AurelianVulkanTexture Image(string name, VulkanTextureFormat format)
        {
            return Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource,
                VulkanMemoryUsage.GpuOnly, name, format));
        }
    }

    public void Reset() => valid = false;

    public void Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        VulkanSharpReflection3D trace, AurelianVulkanTexture normal, AurelianVulkanTexture motion,
        AurelianVulkanTexture surface, AurelianVulkanTexture identity, Matrix4x4 clip, Matrix4x4 inverse, Vector3 eye, bool halfRate)
    {
        if (!initialized)
        {
            // The first pass binds the opposite images but rejects all history.
            for (int index = 0; index < history.Length; index++)
            {
                Vulkan3DPass.SampleAfterRendering(plant, command, history[index]);
                Vulkan3DPass.SampleAfterRendering(plant, command, guides[index]);
            }
            initialized = true;
        }
        bool orthographic = clip.M14 == 0 && clip.M24 == 0 && clip.M34 == 0;
        var pass = passes[writeIndex];
        pass.Configure([.. Lighting3DUniforms.Rows(inverse), .. Lighting3DUniforms.Rows(previousInverse),
            eye.X, eye.Y, eye.Z, orthographic ? 0 : 1, previousEye.X, previousEye.Y, previousEye.Z, 0,
            1f / surface.Width, 1f / surface.Height, valid ? 1 : 0, halfRate ? trace.RecordedPhase : -1],
            trace.Output, normal, motion, surface, history[1 - writeIndex], guides[1 - writeIndex], trace.HitMetadata!, identity);
        pass.Record(command, triangle);
        previousEye = eye;
        previousInverse = inverse;
        writeIndex = 1 - writeIndex;
        valid = true;
    }

    private T Own<T>(T value) where T : IDisposable
    {
        owned.Push(value);
        return value;
    }

    public void Dispose()
    {
        while (owned.TryPop(out var resource))
            resource.Dispose();
    }
}
