using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal sealed unsafe partial class VulkanRetainedModels3D
{
    private readonly uint visibilityWidth;
    private readonly uint visibilityHeight;
    private VulkanVisibilityBuffer3D? visibilityBuffer;
    public bool VisibilityBufferActive { get; private set; }
    public bool HierarchicalZActive { get; private set; }

    public bool CanUseVisibilityBuffer => plant.Facts.VisibilityBufferEnabled && programs.VisibilityBuffer is not null;
    public bool CanUseHierarchicalZ => plant.Facts.HierarchicalZEnabled && programs.Visibility is not null && programs.HierarchicalZ is not null;

    private void PrepareVisibilityBuffer()
    {
        visibilityBuffer = new(plant, allocator, commands, fences, frames, bindlessTable!, programs.VisibilityBuffer!,
            geometryPass, visibilityWidth, visibilityHeight, bindlessRuns.Select(run => run.Source).ToArray(), words,
            visibility, pass => CreatePipeline(pass, programs.VisibilityBuffer!.Raster.Shaders, bindlessTable!.Layout, shadow: false),
            HierarchicalZActive ? programs.HierarchicalZ!.Reduce : null);
    }

    public void RecordIdentityRaster(VulkanCommandBufferLease command)
    {
        visibilityBuffer!.RecordRaster(command, (scope, framebuffer, selected) =>
        {
            for (int index = 0; index < bindlessRuns.Count; index++)
            {
                DrawGroupIndexed(command, scope, framebuffer, selected, bindlessRuns[index].Draw,
                    HasVisibility ? visibility[index] : null);
            }
        });
    }

    public void DrawMaterialResolve(VulkanCommandBufferLease command, VulkanRenderPassScope scope,
        AurelianVulkanFramebuffer framebuffer, AurelianVulkanBuffer triangle)
    {
        visibilityBuffer!.DrawResolve(command, scope, framebuffer, triangle);
        Statistics = Statistics with { MaterialDescriptorBinds = 2 };
    }

    public NativeVisibilityBufferReadback ReadIdentitiesForQualification()
        => visibilityBuffer?.ReadForQualification() ?? throw new InvalidOperationException("AUR-VBUFFER-005: No visibility-buffer frame was rendered.");

    public void RecordHzbFinalCull(VulkanCommandBufferLease command, System.Numerics.Matrix4x4 clip,
        System.Numerics.Vector3 eye, ClusterVisibilityMode mode)
    {
        foreach (var group in visibility) group.RecordHzb(command, clip, eye, mode, seed: false);
    }
    public void RecordHzbPyramid(VulkanCommandBufferLease command, bool history) => visibilityBuffer!.RecordHzb(command, history);
    public HzbPyramidReadback ReadHzbForQualification() => visibilityBuffer!.ReadHzbForQualification();
}
